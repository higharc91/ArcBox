using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Campaigns;
using ArcBox.Server.Profiles;
using Microsoft.AspNetCore.Http.Features;

namespace ArcBox.Server.Campaigns;

public sealed record MeResponse(ProfileId Id, string DisplayName, IReadOnlyList<CampaignSummary> Campaigns);

/// <summary>A campaign as one member sees it: its info, the game's portal view, and the room that holds it, if any.</summary>
public sealed record CampaignResponse(CampaignInfo Info, JsonElement View, string? LeasedBy);

public sealed record CommandRequest(string Type, JsonElement Data, long Version);

/// <param name="Current">With a 409: the campaign as it is now, so the phone can show the latest.</param>
public sealed record ErrorResponse(string Error, CampaignResponse? Current = null);

public sealed record RollbackRequest(long SnapshotId);

/// <summary>
/// The between-sessions portal API (spec §3.4), authenticated with <c>Authorization: Profile &lt;key&gt;</c>. Every
/// route is on the <c>"portal"</c> rate limiter. A 401 has no body; 403 and 404 say nothing about the campaign.
/// </summary>
/// <remarks>
/// <code>
/// GET    /api/me                                              → 200 MeResponse
/// GET    /api/campaigns/{code}                                → 200 CampaignResponse · 403 not a member · 404
/// POST   /api/campaigns/{code}/commands {type,data,version}   → 200 CampaignResponse · 400 rules error or bad body
///                                                               · 409 stale version or session in progress (with Current)
/// POST   /api/campaigns/{code}/members/{profileId}/forget-devices → 204 · 403 not the owner · 404 not a member
/// DELETE /api/campaigns/{code}/members/{profileId}            → 204 · 400 the owner · 403 not the owner · 409 session in progress
/// GET    /api/campaigns/{code}/snapshots                      → 200 SnapshotInfo[] · 403 not the owner
/// POST   /api/campaigns/{code}/rollback {snapshotId}          → 200 CampaignResponse · 404 no such snapshot · 409 session in progress
/// </code>
/// </remarks>
public static class CampaignEndpoints
{
    public const string RateLimitPolicy = "portal";

    private const string SessionInProgress = "A session is in progress. Use the in-game menu.";
    private const string StaleVersion = "Someone else changed this. Here's the latest.";
    private const string NotFound = "Campaign not found.";
    private const int MaxBodyBytes = 256 * 1024;

    public static WebApplication MapCampaigns(this WebApplication app)
    {
        var api = app.MapGroup("/api")
            .RequireRateLimiting(RateLimitPolicy)
            .AddEndpointFilter(async (context, next) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store"; // personal data
                return await next(context);
            });

        api.MapGet("/me", Me);
        api.MapGet("/campaigns/{code}", GetCampaign);
        api.MapPost("/campaigns/{code}/commands", RunCommand);
        api.MapPost("/campaigns/{code}/members/{profileId}/forget-devices", ForgetDevices);
        api.MapDelete("/campaigns/{code}/members/{profileId}", RemoveMember);
        api.MapGet("/campaigns/{code}/snapshots", Snapshots);
        api.MapPost("/campaigns/{code}/rollback", Rollback);
        return app;
    }

    private static IResult Me(HttpContext http, ProfileService profiles, CampaignService campaigns) =>
        ProfileAuth.Authenticate(http, profiles) is { } me
            ? Json(new MeResponse(me.Id, me.DisplayName, campaigns.ForMember(me.Id)))
            : Results.Unauthorized();

    private static IResult GetCampaign(string code, HttpContext http, ProfileService profiles, CampaignService campaigns)
    {
        if (Member(http, profiles, campaigns, code, out var me, out var info) is { } denied) return denied;
        try
        {
            return Json(new CampaignResponse(info, campaigns.PortalView(info.Id, me.Id), campaigns.LeasedBy(info.Id)));
        }
        catch (CampaignException ex)
        {
            return Failed(ex);
        }
    }

    private static async Task<IResult> RunCommand(string code, HttpContext http, ProfileService profiles, CampaignService campaigns)
    {
        if (Member(http, profiles, campaigns, code, out var me, out _) is { } denied) return denied;
        if (await ReadBody<CommandRequest>(http) is not { Type: { Length: > 0 } type } request)
            return Error(StatusCodes.Status400BadRequest, "Invalid request.");

        try
        {
            var data = request.Data.ValueKind == JsonValueKind.Undefined ? JsonSerializer.SerializeToElement<object?>(null) : request.Data;
            if (campaigns.RunCommand(code, me.Id, type, data, request.Version) is not { } outcome)
                return Error(StatusCodes.Status404NotFound, NotFound);

            var current = new CampaignResponse(outcome.Info, outcome.View, campaigns.LeasedBy(outcome.Info.Id));
            return outcome.Status switch
            {
                CommandStatus.Ok => Json(current),
                CommandStatus.NotMember => Results.StatusCode(StatusCodes.Status403Forbidden),
                CommandStatus.VersionConflict => Error(StatusCodes.Status409Conflict, StaleVersion, current),
                CommandStatus.SessionInProgress => Error(StatusCodes.Status409Conflict, SessionInProgress, current),
                _ => Error(StatusCodes.Status400BadRequest, outcome.Error ?? "Invalid request."),
            };
        }
        catch (CampaignException ex)
        {
            return Failed(ex);
        }
    }

    private static IResult ForgetDevices(string code, string profileId, HttpContext http, ProfileService profiles, CampaignService campaigns)
    {
        if (Owner(http, profiles, campaigns, code, out _, out var info) is { } denied) return denied;

        // Only a member's keys: the owner can't use this to lock out people who aren't in the campaign.
        var target = new ProfileId(profileId);
        if (info.Members.All(m => m.Profile != target)) return Error(StatusCodes.Status404NotFound, NotFound);

        profiles.ForgetKeys(target);
        return Results.NoContent();
    }

    private static IResult RemoveMember(string code, string profileId, HttpContext http, ProfileService profiles, CampaignService campaigns)
    {
        if (Owner(http, profiles, campaigns, code, out _, out var info) is { } denied) return denied;
        if (campaigns.LeasedBy(info.Id) is not null) return Error(StatusCodes.Status409Conflict, SessionInProgress);

        try
        {
            campaigns.RemoveMember(info.Id, new ProfileId(profileId));
            return Results.NoContent();
        }
        catch (CampaignException ex)
        {
            return Failed(ex);
        }
    }

    private static IResult Snapshots(string code, HttpContext http, ProfileService profiles, CampaignService campaigns) =>
        Owner(http, profiles, campaigns, code, out _, out var info) ?? Json(campaigns.Snapshots(info.Id));

    private static async Task<IResult> Rollback(string code, HttpContext http, ProfileService profiles, CampaignService campaigns)
    {
        if (Owner(http, profiles, campaigns, code, out var me, out var info) is { } denied) return denied;
        if (await ReadBody<RollbackRequest>(http) is not { } request)
            return Error(StatusCodes.Status400BadRequest, "Invalid request.");

        if (!campaigns.Rollback(info.Id, request.SnapshotId))
        {
            // Rollback says no for a held campaign and for an unknown snapshot alike; the lease is what the phone can act on.
            return campaigns.LeasedBy(info.Id) is not null
                ? Error(StatusCodes.Status409Conflict, SessionInProgress)
                : Error(StatusCodes.Status404NotFound, "Snapshot not found.");
        }

        return GetCampaign(code, http, profiles, campaigns);
    }

    /// <summary>Null when the caller is a member; otherwise the 401, 404 or 403 to send.</summary>
    private static IResult? Member(HttpContext http, ProfileService profiles, CampaignService campaigns, string code,
        out ProfileRecord me, out CampaignInfo info)
    {
        me = null!;
        info = null!;
        if (ProfileAuth.Authenticate(http, profiles) is not { } profile) return Results.Unauthorized();
        if (campaigns.FindByCode(code) is not { } found) return Error(StatusCodes.Status404NotFound, NotFound);
        if (found.Members.All(m => m.Profile != profile.Id)) return Results.StatusCode(StatusCodes.Status403Forbidden);

        (me, info) = (profile, found);
        return null;
    }

    /// <summary>Null when the caller owns the campaign; otherwise the 401, 404 or 403 to send.</summary>
    private static IResult? Owner(HttpContext http, ProfileService profiles, CampaignService campaigns, string code,
        out ProfileRecord me, out CampaignInfo info)
    {
        if (Member(http, profiles, campaigns, code, out me, out info) is { } denied) return denied;
        return info.Owner == me.Id ? null : Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    /// <summary>The body as <typeparamref name="T"/>, or null when it is missing, malformed or too big.</summary>
    private static async Task<T?> ReadBody<T>(HttpContext http) where T : class
    {
        if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit) limit.MaxRequestBodySize = MaxBodyBytes;
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(http.Request.Body, ArcJson.Options, http.RequestAborted);
        }
        catch (Exception ex) when (ex is JsonException or BadHttpRequestException)
        {
            return null;
        }
    }

    private static IResult Json<T>(T value) => Results.Json(value, ArcJson.Options);

    private static IResult Error(int status, string message, CampaignResponse? current = null) =>
        Results.Json(new ErrorResponse(message, current), ArcJson.Options, statusCode: status);

    private static IResult Failed(CampaignException ex) =>
        ex.Message == NotFound ? Error(StatusCodes.Status404NotFound, NotFound) : Error(StatusCodes.Status400BadRequest, ex.Message);
}
