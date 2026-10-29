using System.Globalization;
using ArcBox.Sdk.Speech;
using ArcBox.Server.Rooms;
using Microsoft.Extensions.Options;

namespace ArcBox.Server.Speech;

/// <summary>
/// Host-only speech endpoints (spec §3.1). Only a room's host (room code + host token) can use them, so the
/// provider key is never exposed and a public URL cannot be used to run up the bill.
/// </summary>
/// <remarks>
/// <code>
/// POST /api/tts            {"code","hostToken","text","voice"?}  → 200 audio/mpeg
///   401 unknown room or wrong token · 400 empty/over-long text or unknown theme · 503 disabled, unconfigured or
///   provider failure (with Retry-After) · 429 over the room's per-minute limit (with Retry-After)
/// GET  /api/tts/voices     headers X-ArcBox-Room, X-ArcBox-Host-Token → 200 [{id, description, fallback:{rate, pitch}}]
///   401 as above. Never includes provider voice names or keys.
/// </code>
/// </remarks>
public static class SpeechEndpoints
{
    public const string RoomHeader = "X-ArcBox-Room";
    public const string HostTokenHeader = "X-ArcBox-Host-Token";

    public sealed record TtsRequest(string? Code, string? HostToken, string? Text, string? Voice);

    public sealed record VoiceInfo(string Id, string Description, FallbackVoice Fallback);

    public static IEndpointRouteBuilder MapSpeech(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/tts", SynthesizeAsync);
        app.MapGet("/api/tts/voices", ListVoices);
        return app;
    }

    private static async Task<IResult> SynthesizeAsync(
        TtsRequest request,
        HttpContext http,
        RoomManager rooms,
        ISpeechService speech,
        IVoiceThemeRegistry themes,
        SpeechRateLimiter limiter,
        IOptions<SpeechOptions> options)
    {
        NoStore(http);

        var room = Authenticate(rooms, request.Code, request.HostToken);
        if (room is null)
            return Problem(StatusCodes.Status401Unauthorized, "Unknown room or wrong host token.");

        var text = request.Text?.Trim() ?? "";
        var max = options.Value.MaxChars;
        if (text.Length == 0 || text.Length > max)
            return Problem(StatusCodes.Status400BadRequest, $"Text must be 1–{max} characters.");

        var themeId = string.IsNullOrWhiteSpace(request.Voice) ? VoiceThemes.Host : request.Voice.Trim();
        if (themes.Find(themeId) is null)
            return Problem(StatusCodes.Status400BadRequest, $"Unknown voice theme '{Shorten(themeId)}'.");

        if (!speech.IsAvailable)
            return Unavailable(http, "Speech is disabled or not configured.");

        if (!limiter.TryAcquire(room.Code, room, out var retryAfter))
        {
            http.Response.Headers.RetryAfter = Seconds(retryAfter);
            return Problem(StatusCodes.Status429TooManyRequests, "Too many speech requests for this room.");
        }

        byte[] audio;
        try
        {
            audio = await speech.SynthesizeAsync(themeId, text, http.RequestAborted);
        }
        catch (Exception e) when (e is SpeechSynthesisException or SpeechUnavailableException)
        {
            // Details are in the server log (redacted, rate-limited); the TV only needs to fall back.
            return Unavailable(http, "Speech synthesis failed.");
        }
        catch (OperationCanceledException) when (!http.RequestAborted.IsCancellationRequested)
        {
            // The server is shutting down and cancelled the shared synthesis.
            return Unavailable(http, "Speech synthesis was cancelled.");
        }

        http.Response.Headers.CacheControl = "private, max-age=86400";
        return Results.Bytes(audio, "audio/mpeg");
    }

    private static IResult ListVoices(HttpContext http, RoomManager rooms, IVoiceThemeRegistry themes)
    {
        NoStore(http);
        var room = Authenticate(rooms, http.Request.Headers[RoomHeader], http.Request.Headers[HostTokenHeader]);
        if (room is null)
            return Problem(StatusCodes.Status401Unauthorized, "Unknown room or wrong host token.");

        return Results.Ok(themes.All
            .Select(t => new VoiceInfo(t.Id, t.Description, t.Fallback ?? new FallbackVoice()))
            .ToList());
    }

    private static Room? Authenticate(RoomManager rooms, string? code, string? hostToken)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrEmpty(hostToken))
            return null;
        var room = rooms.Find(code);
        return room is not null && room.IsHostToken(hostToken) ? room : null;
    }

    private static IResult Unavailable(HttpContext http, string detail)
    {
        http.Response.Headers.RetryAfter = "60";
        return Problem(StatusCodes.Status503ServiceUnavailable, detail);
    }

    private static IResult Problem(int status, string detail) => Results.Problem(detail: detail, statusCode: status);

    private static void NoStore(HttpContext http) => http.Response.Headers.CacheControl = "no-store";

    private static string Seconds(TimeSpan t) =>
        Math.Max(1, (int)Math.Ceiling(t.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

    private static string Shorten(string s) => s.Length <= 64 ? s : s[..64];
}
