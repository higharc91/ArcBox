using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Campaigns;
using ArcBox.Sdk.Protocol;
using ArcBox.Server.Campaigns;
using ArcBox.Server.Profiles;
using ArcBox.Server.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ArcBox.Server.Tests.Campaigns;

/// <summary>The portal HTTP API (spec §3.4). Keys come from seating phones in a ledger campaign room through the hub.</summary>
public class PortalApiTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string SessionMessage = "A session is in progress. Use the in-game menu.";
    private const string StaleMessage = "Someone else changed this. Here's the latest.";

    /// <summary>
    /// A ledger room with the host, Ann (the VIP, who created "Embers" and owns it) and Bea (a member). Both hold the
    /// profile keys the hub issued them.
    /// </summary>
    private sealed class Table : IAsyncDisposable
    {
        private readonly ArcServerFactory _factory;
        private readonly TestClient _host;
        private readonly TestClient _ann;
        private readonly TestClient _bea;

        private Table(ArcServerFactory factory, HttpClient http, TestClient host, TestClient ann, TestClient bea,
            string room, CampaignInfo campaign, ProfileKey annKey, ProfileKey beaKey)
        {
            (_factory, Http, _host, _ann, _bea) = (factory, http, host, ann, bea);
            (Room, Campaign, Ann, Bea) = (room, campaign, annKey, beaKey);
        }

        public HttpClient Http { get; }
        public string Room { get; }
        public CampaignInfo Campaign { get; }
        public ProfileKey Ann { get; }
        public ProfileKey Bea { get; }
        public string Code => Campaign.Code;
        public CampaignService Campaigns => _factory.Services.GetRequiredService<CampaignService>();
        public Microsoft.Extensions.Time.Testing.FakeTimeProvider Time => _factory.Time;
        public ProfileService Profiles => _factory.Services.GetRequiredService<ProfileService>();

        public static async Task<Table> CreateAsync()
        {
            var factory = new ArcServerFactory();
            var host = await TestClient.ConnectAsync(factory);
            var ann = await TestClient.ConnectAsync(factory);
            var bea = await TestClient.ConnectAsync(factory);
            var room = await host.InvokeAsync<CreateRoomResult>("CreateRoom");
            await ann.TakeSeatAsync(new TakeSeatRequest(room.Code, NewName: "Ann"));
            await host.InvokeAsync("SelectGame", "ledger");
            var annKey = await ann.InvokeAsync<ProfileKey>("CreateCampaign", "Embers");
            var beaSeat = await bea.TakeSeatAsync(new TakeSeatRequest(room.Code, NewName: "Bea"));
            var beaKey = beaSeat.Profile ?? throw new InvalidOperationException("Bea got no profile key.");
            var campaign = factory.Services.GetRequiredService<CampaignService>().ListForGame("ledger").Single();
            var info = factory.Services.GetRequiredService<CampaignService>().Find(campaign.Id)!;
            return new Table(factory, factory.CreateClient(), host, ann, bea, room.Code, info, annKey, beaKey);
        }

        /// <summary>The room checks the campaign out, the way a real session start does.</summary>
        public Task StartSessionAsync() => _ann.InvokeAsync("StartGame");

        public ProfileKey Outsider()
        {
            var cy = Profiles.Create("Cy");
            return new ProfileKey(cy.Id, Profiles.IssueKey(cy.Id).Key, cy.DisplayName);
        }

        public async ValueTask DisposeAsync()
        {
            Http.Dispose();
            await _host.DisposeAsync();
            await _ann.DisposeAsync();
            await _bea.DisposeAsync();
            await _factory.DisposeAsync();
        }
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient http, HttpMethod method, string path, string? key,
        object? body = null, string? rawBody = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (key is not null) request.Headers.TryAddWithoutValidation("Authorization", "Profile " + key);
        if (rawBody is not null) request.Content = new StringContent(rawBody, Encoding.UTF8, "application/json");
        else if (body is not null) request.Content = JsonContent.Create(body, options: ArcJson.Options);
        return await http.SendAsync(request, Ct);
    }

    private static Task<HttpResponseMessage> Get(Table t, string path, string? key) => SendAsync(t.Http, HttpMethod.Get, path, key);

    private static Task<HttpResponseMessage> Note(Table t, string key, string text, long version, string? code = null) =>
        SendAsync(t.Http, HttpMethod.Post, $"/api/campaigns/{code ?? t.Code}/commands", key,
            new { type = "note", data = new { text }, version });

    private static async Task<JsonElement> Body(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(Ct);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static string[] Notes(JsonElement campaignResponse) =>
        [.. campaignResponse.GetProperty("view").GetProperty("notes").EnumerateArray().Select(n => n.GetString()!)];

    [Fact]
    public async Task Me_lists_my_campaigns()
    {
        await using var t = await Table.CreateAsync();
        t.Time.Advance(TimeSpan.FromMinutes(1));
        var other = t.Campaigns.Create("ledger", "Frost", t.Profiles.Find(t.Bea.Id)!);

        var response = await Get(t, "/api/me", t.Ann.Key);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await Body(response);
        Assert.Equal(t.Ann.Id.Value, me.GetProperty("id").GetString());
        Assert.Equal("Ann", me.GetProperty("displayName").GetString());
        var campaign = Assert.Single(me.GetProperty("campaigns").EnumerateArray());
        Assert.Equal(("Embers", t.Code, 2), (campaign.GetProperty("name").GetString(), campaign.GetProperty("code").GetString(), campaign.GetProperty("members").GetInt32()));

        // Bea is in both, newest first.
        var bea = await Body(await Get(t, "/api/me", t.Bea.Key));
        Assert.Equal(["Frost", "Embers"], bea.GetProperty("campaigns").EnumerateArray().Select(c => c.GetProperty("name").GetString()));
        Assert.Equal(other.Code, bea.GetProperty("campaigns")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task Missing_or_unknown_key_401()
    {
        await using var t = await Table.CreateAsync();
        string[] paths = ["/api/me", $"/api/campaigns/{t.Code}", $"/api/campaigns/{t.Code}/snapshots"];

        foreach (var path in paths)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await Get(t, path, null)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Get(t, path, "not-a-key")).StatusCode);
        }

        // Every write endpoint too, before it looks at the campaign or the body.
        var bad = "nope";
        Assert.Equal(HttpStatusCode.Unauthorized, (await Note(t, bad, "x", 0)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(t.Http, HttpMethod.Post, $"/api/campaigns/{t.Code}/rollback", bad, new { snapshotId = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(t.Http, HttpMethod.Post, $"/api/campaigns/{t.Code}/members/{t.Bea.Id}/forget-devices", bad)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(t.Http, HttpMethod.Delete, $"/api/campaigns/{t.Code}/members/{t.Bea.Id}", bad)).StatusCode);

        // The scheme is case-sensitive and the key is exactly what follows it (ProfileAuth_is_strict_about_the_header
        // covers the spacing, which an HttpClient would normalise).
        foreach (var header in new[] { $"profile {t.Ann.Key}", $"PROFILE {t.Ann.Key}", $"Bearer {t.Ann.Key}", t.Ann.Key, "Profile" })
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
            request.Headers.TryAddWithoutValidation("Authorization", header);
            var response = await t.Http.SendAsync(request, Ct);
            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"'{header}' gave {(int)response.StatusCode}");
        }

        // A 401 says nothing, and never echoes the key it was given.
        var denied = await Get(t, "/api/me", "secret-that-is-wrong");
        var text = await denied.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("secret-that-is-wrong", text);
        Assert.DoesNotContain("secret-that-is-wrong", string.Join(';', denied.Headers.SelectMany(h => h.Value)));
        Assert.True(text.Length == 0, text);

        // The same key does work with the right header.
        Assert.Equal(HttpStatusCode.OK, (await Get(t, "/api/me", t.Ann.Key)).StatusCode);
    }

    [Fact]
    public async Task ProfileAuth_is_strict_about_the_header()
    {
        await using var t = await Table.CreateAsync();
        var key = t.Ann.Key;

        ProfileRecord? Authenticate(params string[] headers)
        {
            var http = new DefaultHttpContext();
            if (headers.Length > 0) http.Request.Headers.Authorization = headers;
            return ProfileAuth.Authenticate(http, t.Profiles);
        }

        Assert.Equal(t.Ann.Id, Authenticate($"Profile {key}")!.Id);
        Assert.Null(Authenticate());
        Assert.Null(Authenticate(""));
        Assert.Null(Authenticate("Profile "));
        Assert.Null(Authenticate("Profile"));
        Assert.Null(Authenticate(key));
        Assert.Null(Authenticate($"profile {key}"));
        Assert.Null(Authenticate($"PROFILE {key}"));
        Assert.Null(Authenticate($"Bearer {key}"));
        Assert.Null(Authenticate($"Profile  {key}"));
        Assert.Null(Authenticate($"Profile {key} "));
        Assert.Null(Authenticate($" Profile {key}"));
        Assert.Null(Authenticate($"Profile\t{key}"));
        Assert.Null(Authenticate($"Profile {key}x"));
        Assert.Null(Authenticate($"Profile {key}", $"Profile {key}"));
        Assert.Null(Authenticate("Profile " + new string('a', 5000)));
    }

    [Fact]
    public async Task Non_member_403()
    {
        await using var t = await Table.CreateAsync();
        var cy = t.Outsider();

        Assert.Equal(HttpStatusCode.Forbidden, (await Get(t, $"/api/campaigns/{t.Code}", cy.Key)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Note(t, cy.Key, "hi", 0)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Get(t, $"/api/campaigns/{t.Code}/snapshots", cy.Key)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(t.Http, HttpMethod.Post, $"/api/campaigns/{t.Code}/rollback", cy.Key, new { snapshotId = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(t.Http, HttpMethod.Post, $"/api/campaigns/{t.Code}/members/{t.Bea.Id}/forget-devices", cy.Key)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(t.Http, HttpMethod.Delete, $"/api/campaigns/{t.Code}/members/{t.Bea.Id}", cy.Key)).StatusCode);

        // Nothing happened.
        Assert.Equal(0, t.Campaigns.Find(t.Campaign.Id)!.Version);
        Assert.Equal(2, t.Campaigns.Find(t.Campaign.Id)!.Members.Count);
        Assert.NotNull(t.Profiles.Resolve(t.Bea.Key));

        // Members who aren't the owner can read and command, but not manage.
        Assert.Equal(HttpStatusCode.OK, (await Get(t, $"/api/campaigns/{t.Code}", t.Bea.Key)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Get(t, $"/api/campaigns/{t.Code}/snapshots", t.Bea.Key)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(t.Http, HttpMethod.Post, $"/api/campaigns/{t.Code}/rollback", t.Bea.Key, new { snapshotId = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(t.Http, HttpMethod.Post, $"/api/campaigns/{t.Code}/members/{t.Ann.Id}/forget-devices", t.Bea.Key)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(t.Http, HttpMethod.Delete, $"/api/campaigns/{t.Code}/members/{t.Ann.Id}", t.Bea.Key)).StatusCode);
        Assert.NotNull(t.Profiles.Resolve(t.Ann.Key));
    }

    [Fact]
    public async Task Unknown_code_404()
    {
        await using var t = await Table.CreateAsync();
        var key = t.Ann.Key;

        Assert.Equal(HttpStatusCode.NotFound, (await Get(t, "/api/campaigns/ZZZZ", key)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Note(t, key, "x", 0, code: "ZZZZ")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Get(t, "/api/campaigns/ZZZZ/snapshots", key)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(t.Http, HttpMethod.Post, "/api/campaigns/ZZZZ/rollback", key, new { snapshotId = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(t.Http, HttpMethod.Post, $"/api/campaigns/ZZZZ/members/{t.Bea.Id}/forget-devices", key)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(t.Http, HttpMethod.Delete, $"/api/campaigns/ZZZZ/members/{t.Bea.Id}", key)).StatusCode);

        // The code is matched the way a room code is, so lowercase works.
        Assert.Equal(HttpStatusCode.OK, (await Get(t, $"/api/campaigns/{t.Code.ToLowerInvariant()}", key)).StatusCode);
        // An unknown snapshot on a real campaign is a 404 as well.
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(t.Http, HttpMethod.Post, $"/api/campaigns/{t.Code}/rollback", key, new { snapshotId = 999 })).StatusCode);
    }

    [Fact]
    public async Task Get_campaign_returns_info_and_view()
    {
        await using var t = await Table.CreateAsync();

        var response = await Get(t, $"/api/campaigns/{t.Code}", t.Bea.Key);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await Body(response);
        Assert.Equal("Embers", body.GetProperty("info").GetProperty("name").GetString());
        Assert.Equal(t.Ann.Id.Value, body.GetProperty("info").GetProperty("owner").GetString());
        Assert.Equal(0, body.GetProperty("info").GetProperty("version").GetInt64());
        Assert.Empty(Notes(body));
        Assert.Equal(JsonValueKind.Null, body.GetProperty("leasedBy").ValueKind);
    }

    [Fact]
    public async Task Command_ok_bumps_version()
    {
        await using var t = await Table.CreateAsync();

        var response = await Note(t, t.Bea.Key, "The bridge is out", 0);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await Body(response);
        Assert.Equal(1, body.GetProperty("info").GetProperty("version").GetInt64());
        Assert.Equal(["The bridge is out"], Notes(body));
        Assert.Equal(1, t.Campaigns.Find(t.Campaign.Id)!.Version);

        var next = await Note(t, t.Ann.Key, "Found a key", 1);
        Assert.Equal(["The bridge is out", "Found a key"], Notes(await Body(next)));
        Assert.Equal(2, t.Campaigns.Find(t.Campaign.Id)!.Version);
    }

    [Fact]
    public async Task Command_rules_error_400()
    {
        await using var t = await Table.CreateAsync();

        var response = await Note(t, t.Ann.Key, "", 0);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await Body(response);
        Assert.Equal("Write something.", body.GetProperty("error").GetString());
        Assert.Equal(0, t.Campaigns.Find(t.Campaign.Id)!.Version);
    }

    [Fact]
    public async Task Command_with_malformed_body_400()
    {
        await using var t = await Table.CreateAsync();
        var path = $"/api/campaigns/{t.Code}/commands";

        foreach (var raw in new[] { "{not json", "", "[]", "null", """{"type":"","data":{},"version":0}""", """{"data":{},"version":0}""", """{"type":"note","version":"x"}""" })
        {
            var response = await SendAsync(t.Http, HttpMethod.Post, path, t.Ann.Key, rawBody: raw);
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"'{raw}' gave {(int)response.StatusCode}");
        }

        var rollback = await SendAsync(t.Http, HttpMethod.Post, $"/api/campaigns/{t.Code}/rollback", t.Ann.Key, rawBody: "{oops");
        Assert.Equal(HttpStatusCode.BadRequest, rollback.StatusCode);
        Assert.Equal(0, t.Campaigns.Find(t.Campaign.Id)!.Version);
    }

    [Fact]
    public async Task Command_stale_version_409_with_current()
    {
        await using var t = await Table.CreateAsync();
        Assert.Equal(HttpStatusCode.OK, (await Note(t, t.Ann.Key, "first", 0)).StatusCode);

        var response = await Note(t, t.Bea.Key, "second", 0);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await Body(response);
        Assert.Equal(StaleMessage, body.GetProperty("error").GetString());
        var current = body.GetProperty("current");
        Assert.Equal(1, current.GetProperty("info").GetProperty("version").GetInt64());
        Assert.Equal(["first"], Notes(current));
        Assert.Equal(1, t.Campaigns.Find(t.Campaign.Id)!.Version);
    }

    [Fact]
    public async Task Command_while_leased_409()
    {
        await using var t = await Table.CreateAsync();
        var before = (await Body(await Get(t, $"/api/campaigns/{t.Code}", t.Bea.Key))).GetProperty("view").GetRawText();

        // The phone loaded the portal, then a room started a session before it submitted.
        await t.StartSessionAsync();
        Assert.Equal(t.Room, t.Campaigns.LeasedBy(t.Campaign.Id));
        var response = await Note(t, t.Bea.Key, "too late", 0);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await Body(response);
        Assert.Equal("A session is in progress. Use the in-game menu.", body.GetProperty("error").GetString());
        Assert.Equal(SessionMessage, body.GetProperty("error").GetString());
        Assert.Equal(before, body.GetProperty("current").GetProperty("view").GetRawText());
        Assert.Equal(t.Room, body.GetProperty("current").GetProperty("leasedBy").GetString());
        Assert.Equal(0, t.Campaigns.Find(t.Campaign.Id)!.Version);
        Assert.Empty(Notes(await Body(await Get(t, $"/api/campaigns/{t.Code}", t.Bea.Key)))); // reads keep working
    }

    [Fact]
    public async Task Forget_devices_makes_old_key_401()
    {
        await using var t = await Table.CreateAsync();
        Assert.Equal(HttpStatusCode.OK, (await Get(t, "/api/me", t.Bea.Key)).StatusCode);

        var response = await SendAsync(t.Http, HttpMethod.Post, $"/api/campaigns/{t.Code}/members/{t.Bea.Id}/forget-devices", t.Ann.Key);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Get(t, "/api/me", t.Bea.Key)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Get(t, "/api/me", t.Ann.Key)).StatusCode);
        Assert.Contains(t.Campaigns.Find(t.Campaign.Id)!.Members, m => m.Profile == t.Bea.Id); // still a member

        // The owner can't be used to revoke the keys of someone who isn't in the campaign.
        var cy = t.Outsider();
        var outsider = await SendAsync(t.Http, HttpMethod.Post, $"/api/campaigns/{t.Code}/members/{cy.Id}/forget-devices", t.Ann.Key);
        Assert.Equal(HttpStatusCode.NotFound, outsider.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Get(t, "/api/me", cy.Key)).StatusCode);
    }

    [Fact]
    public async Task Remove_member()
    {
        await using var t = await Table.CreateAsync();

        var response = await SendAsync(t.Http, HttpMethod.Delete, $"/api/campaigns/{t.Code}/members/{t.Bea.Id}", t.Ann.Key);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.DoesNotContain(t.Campaigns.Find(t.Campaign.Id)!.Members, m => m.Profile == t.Bea.Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await Get(t, $"/api/campaigns/{t.Code}", t.Bea.Key)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Get(t, "/api/me", t.Bea.Key)).StatusCode); // the profile and its key stay

        // R11: the owner can't be removed.
        var owner = await SendAsync(t.Http, HttpMethod.Delete, $"/api/campaigns/{t.Code}/members/{t.Ann.Id}", t.Ann.Key);
        Assert.Equal(HttpStatusCode.BadRequest, owner.StatusCode);
        Assert.Equal("The owner can't be removed.", (await Body(owner)).GetProperty("error").GetString());
        Assert.Equal(t.Ann.Id, t.Campaigns.Find(t.Campaign.Id)!.Owner);
    }

    [Fact]
    public async Task Remove_member_while_leased_409()
    {
        await using var t = await Table.CreateAsync();
        await t.StartSessionAsync();

        var response = await SendAsync(t.Http, HttpMethod.Delete, $"/api/campaigns/{t.Code}/members/{t.Bea.Id}", t.Ann.Key);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(SessionMessage, (await Body(response)).GetProperty("error").GetString());
        Assert.Contains(t.Campaigns.Find(t.Campaign.Id)!.Members, m => m.Profile == t.Bea.Id);
    }

    [Fact]
    public async Task Snapshots_and_rollback()
    {
        await using var t = await Table.CreateAsync();
        Assert.Equal(HttpStatusCode.OK, (await Note(t, t.Bea.Key, "one", 0)).StatusCode);
        var lease = t.Campaigns.CheckOut(t.Campaign.Id, "ZZZZ")!;
        lease.Checkpoint(JsonSerializer.SerializeToElement(new { notes = new[] { "one", "session 1" } }), "End of session 1");
        t.Campaigns.Release(t.Campaign.Id, "ZZZZ");
        Assert.Equal(HttpStatusCode.OK, (await Note(t, t.Bea.Key, "two", 2)).StatusCode);

        var list = await Get(t, $"/api/campaigns/{t.Code}/snapshots", t.Ann.Key);

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var snapshots = (await Body(list)).EnumerateArray().ToList();
        var snapshot = Assert.Single(snapshots);
        Assert.Equal("End of session 1", snapshot.GetProperty("label").GetString());
        Assert.Equal(2, snapshot.GetProperty("version").GetInt64());

        var rolled = await SendAsync(t.Http, HttpMethod.Post, $"/api/campaigns/{t.Code}/rollback", t.Ann.Key,
            new { snapshotId = snapshot.GetProperty("id").GetInt64() });

        Assert.Equal(HttpStatusCode.OK, rolled.StatusCode);
        var body = await Body(rolled);
        Assert.Equal(["one", "session 1"], Notes(body));
        Assert.Equal(4, body.GetProperty("info").GetProperty("version").GetInt64()); // a new version, so stale phones conflict
        Assert.Equal(HttpStatusCode.Conflict, (await Note(t, t.Bea.Key, "stale", 3)).StatusCode);
    }

    [Fact]
    public async Task Rollback_while_leased_409()
    {
        await using var t = await Table.CreateAsync();
        var lease = t.Campaigns.CheckOut(t.Campaign.Id, "ZZZZ")!;
        lease.Checkpoint(JsonSerializer.SerializeToElement(new { notes = new[] { "saved" } }), "Checkpoint");
        var id = t.Campaigns.Snapshots(t.Campaign.Id).Single().Id;

        var response = await SendAsync(t.Http, HttpMethod.Post, $"/api/campaigns/{t.Code}/rollback", t.Ann.Key, new { snapshotId = id });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(SessionMessage, (await Body(response)).GetProperty("error").GetString());
        Assert.Equal(1, t.Campaigns.Find(t.Campaign.Id)!.Version);
    }

    [Fact]
    public async Task Responses_are_not_cached_and_hold_no_keys()
    {
        await using var t = await Table.CreateAsync();

        var response = await Get(t, "/api/me", t.Ann.Key);
        var text = await response.Content.ReadAsStringAsync(Ct);

        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
        Assert.DoesNotContain(t.Ann.Key, text);
        Assert.DoesNotContain(t.Bea.Key, await (await Get(t, $"/api/campaigns/{t.Code}", t.Ann.Key)).Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Rate_limit_429_after_60()
    {
        // The default is 60 per minute per remote IP. The window is real time, so the whole run must stay inside it.
        using var f = new ArcServerFactory();
        using var http = f.CreateClient();
        for (var i = 0; i < 60; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(http, HttpMethod.Get, "/api/me", null)).StatusCode);

        var limited = await SendAsync(http, HttpMethod.Get, "/api/me", null);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Fact]
    public async Task Rate_limit_is_configurable()
    {
        using var f = new ArcServerFactory().WithWebHostBuilder(b => b.UseSetting("Portal:PermitsPerMinute", "3"));
        using var http = f.CreateClient();

        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++) codes.Add((await SendAsync(http, HttpMethod.Get, "/api/me", null)).StatusCode);

        Assert.Equal([HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized,
            HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests], codes);
        // Other endpoints aren't throttled by the portal's limiter.
        Assert.Equal("ok", await http.GetStringAsync("/healthz", Ct));
    }
}
