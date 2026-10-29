using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Campaigns;
using ArcBox.Sdk.Protocol;
using ArcBox.Server.Profiles;
using ArcBox.Server.Rooms;
using ArcBox.Server.Storage;
using ArcBox.Server.Tests.Storage;
using ArcBox.Server.Tests.Support;
using ArcBox.Server.Tests.TestGames;

namespace ArcBox.Server.Tests.Rooms;

/// <summary>Campaign rooms (spec §3.2): picking or creating a campaign, the roster, and a seat for every member.</summary>
public class CampaignRoomTests
{
    private const string Code = "TEST";

    private static LobbyState Lobby(RoomFixture f, string conn = "host") =>
        (LobbyState)f.Sender.Of(conn, ClientMethods.LobbyChanged).Last().Args[0]!;

    private static JsonElement HostView(RoomFixture f) =>
        (JsonElement)f.Sender.Of("host", ClientMethods.HostView).Last().Args[1]!;

    private static List<string?> Log(RoomFixture f) =>
        [.. HostView(f).GetProperty("log").EnumerateArray().Select(e => e.GetString())];

    private static JsonElement Json(object o) => JsonSerializer.SerializeToElement(o, ArcJson.Options);

    private static async Task<string> Fails(Task call) => (await Assert.ThrowsAsync<RoomException>(() => call)).Message;

    private static SeatCard Card(string name) => new($"Hero of {name}", "Lv 1");

    private static TakeSeatRequest NewName(string name) => new(Code, NewName: name);

    private static TakeSeatRequest Seat(SeatRef seat, params string[] keys) => new(Code, Seat: seat, ProfileKeys: keys);

    /// <summary>The host, Ann (c1, the VIP) and the ledger game; Ann has created "Embers" and is its owner.</summary>
    private static async Task<(CampaignInfo Campaign, ProfileKey AnnKey)> EmbersAsync(RoomFixture f)
    {
        await f.HostAsync();
        await f.JoinAsync("Ann", "c1");
        await f.Room.SelectGameAsync("host", "ledger");
        var key = await f.Room.CreateCampaignAsync("c1", "Embers");
        return (f.Campaigns.Find(Lobby(f).Campaign!.Id)!, key);
    }

    /// <summary>A member who has never been in this room.</summary>
    private static ProfileRecord AddMember(RoomFixture f, CampaignInfo campaign, string name)
    {
        var profile = f.Profiles.Create(name);
        f.Campaigns.AddMember(campaign.Id, profile);
        return profile;
    }

    [Fact]
    public async Task Lobby_lists_campaigns_for_selected_game()
    {
        await using var f = new RoomFixture();
        var dave = f.Profiles.Create("Dave");
        var embers = f.Campaigns.Create("ledger", "Embers", dave);
        f.Time.Advance(TimeSpan.FromMinutes(1));
        f.Campaigns.Create("ledger", "Frost", dave);
        await f.HostAsync();

        Assert.Null(Lobby(f).Campaigns);
        await f.Room.SelectGameAsync("host", "plain-ledger");
        Assert.Null(Lobby(f).Campaigns);

        await f.Room.SelectGameAsync("host", "ledger");
        Assert.Equal(["Frost", "Embers"], Lobby(f).Campaigns!.Select(c => c.Name));
        Assert.Null(Lobby(f).Campaign);

        await f.Room.SelectCampaignAsync("host", embers.Id);
        var selected = Lobby(f).Campaign!;
        Assert.Equal((embers.Id, embers.Code, "Embers"), (selected.Id, selected.Code, selected.Name));
        Assert.Equal([new RosterEntry(dave.Id, Card("Dave"), SeatedAs: null, Connected: false)], selected.Roster);

        // Selecting another game clears the campaign, and coming back doesn't restore it.
        await f.Room.SelectGameAsync("host", "plain-ledger");
        Assert.Null(Lobby(f).Campaign);
        Assert.Null(Lobby(f).Campaigns);
        await f.Room.SelectGameAsync("host", "ledger");
        Assert.Null(Lobby(f).Campaign);
        Assert.Equal(2, Lobby(f).Campaigns!.Count);
    }

    [Fact]
    public async Task VIP_creates_campaign_becomes_owner_and_gets_key()
    {
        await using var f = new RoomFixture();
        await f.HostAsync();
        await f.JoinAsync("Ann", "c1");
        await f.JoinAsync("Bea", "c2");
        await f.Room.SelectGameAsync("host", "ledger");

        var key = await f.Room.CreateCampaignAsync("c1", "  Embers ");

        Assert.Equal("Ann", key.Name);
        Assert.Equal(key.Id, f.Profiles.Resolve(key.Key)!.Id);
        var lobby = Lobby(f);
        var campaign = f.Campaigns.Find(lobby.Campaign!.Id)!;
        Assert.Equal("Embers", campaign.Name);
        Assert.Equal(key.Id, campaign.Owner);
        Assert.Equal(CampaignRole.Owner, Assert.Single(campaign.Members).Role);
        Assert.Equal(key.Id, lobby.Players[0].Profile);
        Assert.Null(lobby.Players[1].Profile);
        Assert.Equal([new RosterEntry(key.Id, Card("Ann"), new PlayerId("p1"), Connected: true)], lobby.Campaign.Roster);
        Assert.Equal(["Embers"], lobby.Campaigns!.Select(c => c.Name));

        // R3: every creation issues a fresh key, for the profile the VIP's seat already has.
        var again = await f.Room.CreateCampaignAsync("c1", "Frost");
        Assert.Equal(key.Id, again.Id);
        Assert.NotEqual(key.Key, again.Key);
        Assert.Equal("Frost", Lobby(f).Campaign!.Name);

        Assert.Equal("Only the VIP can create a campaign.", await Fails(f.Room.CreateCampaignAsync("c2", "Mine")));
        Assert.Equal("Campaign names are 1–32 characters.", await Fails(f.Room.CreateCampaignAsync("c1", "   ")));
        Assert.Equal("Frost", Lobby(f).Campaign!.Name);
    }

    [Fact]
    public async Task Host_cannot_create_campaign()
    {
        await using var f = new RoomFixture();
        await f.HostAsync();
        await f.Room.SelectGameAsync("host", "ledger");

        Assert.Equal("Only the VIP can create a campaign.", await Fails(f.Room.CreateCampaignAsync("host", "Embers")));

        await f.JoinAsync("Ann", "c1");
        Assert.Equal("Only the VIP can create a campaign.", await Fails(f.Room.CreateCampaignAsync("host", "Embers")));
        Assert.Empty(f.Campaigns.ListForGame("ledger"));
    }

    [Fact]
    public async Task Peek_offers_unseated_members_with_rules_cards()
    {
        await using var f = new RoomFixture();
        var (campaign, _) = await EmbersAsync(f);
        var bea = await f.Room.TakeSeatAsync("c2", NewName("Bea"));
        var cy = AddMember(f, campaign, "Cy");
        await f.Room.ConnectionLostAsync("c2");

        var peek = await f.Room.PeekAsync([]);

        // "I'm new here" in a campaign lobby made Bea a profile, a member and a seat.
        Assert.Equal("Bea", bea.Profile!.Name);
        Assert.Contains(f.Campaigns.Find(campaign.Id)!.Members, m => m.Profile == bea.Profile.Id && m.Role == CampaignRole.Player);
        Assert.Equal("Embers", peek.CampaignName);
        Assert.False(peek.InGame);
        Assert.True(peek.CanJoinNew);
        Assert.Null(peek.Suggested);
        // Ann is connected, so not on offer; Bea's seat is offline, and Cy has none yet. Member order.
        Assert.Equal(
            [
                new SeatOffer(new SeatRef(Slot: bea.PlayerId), Card("Bea"), Absent: false),
                new SeatOffer(new SeatRef(Member: cy.Id), Card("Cy"), Absent: false),
            ],
            peek.Offers);
    }

    [Fact]
    public async Task TakeSeat_member_issues_key_once()
    {
        await using var f = new RoomFixture();
        var (campaign, _) = await EmbersAsync(f);
        var cy = AddMember(f, campaign, "Cy");

        var first = await f.Room.TakeSeatAsync("c2", Seat(new SeatRef(Member: cy.Id)));

        Assert.Equal((cy.Id, "Cy"), (first.Profile!.Id, first.Profile.Name));
        Assert.Equal(cy.Id, f.Profiles.Resolve(first.Profile.Key)!.Id);
        var seat = Lobby(f).Players.Single(p => p.Id == first.PlayerId);
        Assert.Equal(("Cy", cy.Id), (seat.Name, seat.Profile));
        Assert.Equal("Someone's already playing as Hero of Cy.",
            await Fails(f.Room.TakeSeatAsync("c3", Seat(new SeatRef(Member: cy.Id)))));

        // Back on a phone that holds Cy's key: same seat, no new key.
        await f.Room.ConnectionLostAsync("c2");
        var again = await f.Room.TakeSeatAsync("c3", Seat(new SeatRef(Slot: first.PlayerId), "junk", first.Profile.Key));
        Assert.Equal(first.PlayerId, again.PlayerId);
        Assert.Null(again.Profile);

        // A phone without it, picking Cy as a member, lands on Cy's offline seat and gets a key of its own.
        await f.Room.ConnectionLostAsync("c3");
        var third = await f.Room.TakeSeatAsync("c4", Seat(new SeatRef(Member: cy.Id)));
        Assert.Equal(first.PlayerId, third.PlayerId);
        Assert.NotEqual(first.Profile.Key, third.Profile!.Key);
        Assert.Equal(2, Lobby(f).Players.Count);
    }

    [Fact]
    public async Task Suggested_is_most_recently_used_matching_key()
    {
        await using var f = new RoomFixture();
        await EmbersAsync(f);
        var bea = await f.Room.TakeSeatAsync("c2", NewName("Bea"));
        f.Time.Advance(TimeSpan.FromMinutes(1));
        var cy = await f.Room.TakeSeatAsync("c3", NewName("Cy"));
        await f.Room.ConnectionLostAsync("c2");
        await f.Room.ConnectionLostAsync("c3");
        string[] tablet = [bea.Profile!.Key, cy.Profile!.Key];

        // A shared tablet holds both keys: Cy's was used last, and both stay listed.
        var peek = await f.Room.PeekAsync(tablet);
        Assert.Equal(new SeatRef(Slot: cy.PlayerId), peek.Suggested!.Ref);
        Assert.Equal(2, peek.Offers.Count);

        // Bea sits down on the tablet with its keys, which uses hers, then leaves again.
        f.Time.Advance(TimeSpan.FromMinutes(1));
        Assert.Null((await f.Room.TakeSeatAsync("c4", Seat(new SeatRef(Slot: bea.PlayerId), tablet))).Profile);
        await f.Room.ConnectionLostAsync("c4");
        Assert.Equal(new SeatRef(Slot: bea.PlayerId), (await f.Room.PeekAsync([.. tablet.Reverse()])).Suggested!.Ref);

        // A key for someone who isn't on offer suggests nothing.
        var zed = f.Profiles.IssueKey(f.Profiles.Create("Zed").Id);
        Assert.Null((await f.Room.PeekAsync([zed.Key])).Suggested);
    }

    [Fact]
    public async Task ClaimMember_new_member_and_existing_member()
    {
        await using var f = new RoomFixture();
        await f.HostAsync();
        await f.JoinAsync("Ann", "c1");
        await f.JoinAsync("Bea", "c2");
        await f.JoinAsync("Cy", "c3");
        await f.JoinAsync("Dee", "c4");
        await f.Room.SelectGameAsync("host", "ledger");
        await f.Room.CreateCampaignAsync("c1", "Embers");
        var campaign = f.Campaigns.Find(Lobby(f).Campaign!.Id)!;

        // "I'm new here": a new member under the seat's name.
        var beaKey = await f.Room.ClaimMemberAsync("c2", new ClaimMemberRequest(null));
        Assert.Equal("Bea", beaKey!.Name);
        Assert.Contains(f.Campaigns.Find(campaign.Id)!.Members, m => m.Profile == beaKey.Id && m.Role == CampaignRole.Player);
        Assert.Equal(beaKey.Id, Lobby(f).Players[1].Profile);

        // A member with no seat: the caller's seat takes the profile and the name.
        var dan = AddMember(f, campaign, "Dan");
        var danKey = await f.Room.ClaimMemberAsync("c4", new ClaimMemberRequest(new SeatRef(Member: dan.Id)));
        Assert.Equal(dan.Id, danKey!.Id);
        var p4 = Lobby(f).Players.Single(p => p.Id == new PlayerId("p4"));
        Assert.Equal(("Dan", dan.Id), (p4.Name, p4.Profile));

        // A member seated offline: that seat goes, and the caller's seat becomes the member's. This phone has the key.
        var eve = await f.Room.TakeSeatAsync("c5", NewName("Eve"));
        await f.Room.ConnectionLostAsync("c5");
        var none = await f.Room.ClaimMemberAsync("c3",
            new ClaimMemberRequest(new SeatRef(Slot: eve.PlayerId), [eve.Profile!.Key]));
        Assert.Null(none);

        var lobby = Lobby(f);
        Assert.DoesNotContain(lobby.Players, p => p.Id == eve.PlayerId);
        var p3 = lobby.Players.Single(p => p.Id == new PlayerId("p3"));
        Assert.Equal(("Eve", eve.Profile.Id), (p3.Name, p3.Profile));
        Assert.Equal(["Ann", "Bea", "Dan", "Eve"], lobby.Campaign!.Roster.Select(r => r.Card.Title["Hero of ".Length..]));
        Assert.All(lobby.Campaign.Roster, r => Assert.True(r.Connected));
        Assert.Equal(["p1", "p2", "p4", "p3"], lobby.Campaign.Roster.Select(r => r.SeatedAs!.Value.Value));
        Assert.True(lobby.CanStart);
    }

    [Fact]
    public async Task ClaimMember_errors()
    {
        await using var f = new RoomFixture();
        await f.HostAsync();
        await f.JoinAsync("Ann", "c1");
        await f.JoinAsync("dan", "c2");
        var nobody = new ClaimMemberRequest(null);

        Assert.Equal("Pick a campaign first.", await Fails(f.Room.ClaimMemberAsync("c2", nobody)));
        await f.Room.SelectGameAsync("host", "ledger");
        Assert.Equal("Pick a campaign first.", await Fails(f.Room.ClaimMemberAsync("c2", nobody)));

        var key = await f.Room.CreateCampaignAsync("c1", "Embers");
        var campaign = f.Campaigns.Find(Lobby(f).Campaign!.Id)!;
        var dan = AddMember(f, campaign, "Dan");

        Assert.Equal("That name is taken.", await Fails(f.Room.ClaimMemberAsync("c2", nobody)));
        Assert.Equal("Pick a seat or enter a name.",
            await Fails(f.Room.ClaimMemberAsync("c2", new ClaimMemberRequest(new SeatRef()))));
        Assert.Equal("Pick a seat or enter a name.",
            await Fails(f.Room.ClaimMemberAsync("c2", new ClaimMemberRequest(new SeatRef(new PlayerId("p1"), dan.Id)))));
        Assert.Equal("Someone's already playing as Hero of Ann.",
            await Fails(f.Room.ClaimMemberAsync("c2", new ClaimMemberRequest(new SeatRef(Member: key.Id)))));
        Assert.Equal("Someone's already playing as Hero of Ann.",
            await Fails(f.Room.ClaimMemberAsync("c2", new ClaimMemberRequest(new SeatRef(Slot: new PlayerId("p1"))))));

        await f.Room.ClaimMemberAsync("c2", new ClaimMemberRequest(new SeatRef(Member: dan.Id)));
        await f.Room.StartGameAsync("host");
        Assert.Equal("You can only change who you are in the lobby.", await Fails(f.Room.ClaimMemberAsync("c2", nobody)));
    }

    [Fact]
    public async Task Start_refused_until_campaign_and_everyone_matched()
    {
        await using var f = new RoomFixture();
        await f.HostAsync();
        await f.JoinAsync("Ann", "c1");
        await f.JoinAsync("Bea", "c2");
        await f.JoinAsync("Cy", "c3");
        await f.Room.SelectGameAsync("host", "ledger");

        Assert.False(Lobby(f).CanStart);
        Assert.Equal("Pick a campaign first.", await Fails(f.Room.StartGameAsync("host")));

        await f.Room.CreateCampaignAsync("c1", "Embers");
        Assert.False(Lobby(f).CanStart);
        Assert.Equal("Everyone needs to pick who they are first.", await Fails(f.Room.StartGameAsync("host")));

        await f.Room.ClaimMemberAsync("c2", new ClaimMemberRequest(null));
        Assert.False(Lobby(f).CanStart);

        // Only connected seats must be matched: Cy leaves without picking, and the game goes ahead without that seat.
        await f.Room.ConnectionLostAsync("c3");
        Assert.True(Lobby(f).CanStart);
        await f.Room.StartGameAsync("c1");

        Assert.True(Lobby(f).InGame);
        Assert.Equal(["Ann", "Bea"], HostView(f).GetProperty("seats").EnumerateArray().Select(s => s.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task Start_seats_absent_members_disconnected_and_hands_over_lease()
    {
        await using var f = new RoomFixture();
        var (campaign, annKey) = await EmbersAsync(f);
        var cy = AddMember(f, campaign, "Cy");
        var dan = AddMember(f, campaign, "Dan");

        await f.Room.StartGameAsync("host");

        var view = HostView(f);
        var seats = view.GetProperty("seats").EnumerateArray()
            .Select(s => (s.GetProperty("id").GetString(), s.GetProperty("name").GetString(),
                s.GetProperty("connected").GetBoolean(), s.GetProperty("profile").GetString()))
            .ToList();
        Assert.Equal(
            [("p1", "Ann", true, annKey.Id.Value), ("p2", "Cy", false, cy.Id.Value), ("p3", "Dan", false, dan.Id.Value)],
            seats);
        Assert.Equal(["connected:p2:false", "connected:p3:false"], Log(f));
        Assert.Equal(campaign.Id, view.GetProperty("campaignId").GetString());
        Assert.Equal(0, view.GetProperty("version").GetInt64());
        Assert.Equal(Code, f.Campaigns.LeasedBy(campaign.Id));

        var roster = Lobby(f).Campaign!.Roster;
        Assert.Equal(new RosterEntry(cy.Id, Card("Cy"), new PlayerId("p2"), Connected: false), roster[1]);
    }

    [Fact]
    public async Task Late_arrival_takes_absent_seat()
    {
        await using var f = new RoomFixture();
        var (campaign, _) = await EmbersAsync(f);
        var cy = AddMember(f, campaign, "Cy");
        await f.Room.StartGameAsync("host");

        var peek = await f.Room.PeekAsync([]);

        Assert.True(peek.InGame);
        Assert.False(peek.CanJoinNew);
        Assert.Equal("Embers", peek.CampaignName);
        var offer = Assert.Single(peek.Offers);
        Assert.Equal(new SeatOffer(new SeatRef(Slot: new PlayerId("p2")), Card("Cy"), Absent: true), offer);

        var taken = await f.Room.TakeSeatAsync("c2", Seat(offer.Ref));

        Assert.Equal(cy.Id, taken.Profile!.Id);
        Assert.Equal("connected:p2:true", Log(f)[^1]);
        Assert.Equal("buttons", ((JsonElement)f.Sender.Of("c2", ClientMethods.ControllerView).Last().Args[0]!)
            .GetProperty("kind").GetString());

        // Absent is about the start: once Cy has sat down, and for Ann who was there all along, a dropped seat isn't absent.
        await f.Room.ConnectionLostAsync("c2");
        await f.Room.ConnectionLostAsync("c1");
        var later = await f.Room.PeekAsync([]);
        Assert.Equal(["p1", "p2"], later.Offers.Select(o => o.Ref.Slot!.Value.Value));
        Assert.All(later.Offers, o => Assert.False(o.Absent));
    }

    [Fact]
    public async Task Select_refused_when_leased_elsewhere_or_over_8_members()
    {
        await using var f = new RoomFixture(seed: db => InsertCampaign(db, "cm_crowd", "BBBB", "Crowd", members: 9));
        var embers = f.Campaigns.Create("ledger", "Embers", f.Profiles.Create("Dave"));
        await f.HostAsync();

        Assert.Equal("Pick a game first.", await Fails(f.Room.SelectCampaignAsync("host", embers.Id)));
        await f.Room.SelectGameAsync("host", "plain-ledger");
        Assert.Equal("This game doesn't use campaigns.", await Fails(f.Room.SelectCampaignAsync("host", embers.Id)));
        await f.Room.SelectGameAsync("host", "ledger");
        Assert.Equal("Campaign not found.", await Fails(f.Room.SelectCampaignAsync("host", "cm_nope")));

        f.Campaigns.CheckOut(embers.Id, "KQZT");
        Assert.Equal("Embers is being played in room KQZT.", await Fails(f.Room.SelectCampaignAsync("host", embers.Id)));
        Assert.Equal("This campaign has 9 members; rooms hold 8.", await Fails(f.Room.SelectCampaignAsync("host", "cm_crowd")));
        Assert.Null(Lobby(f).Campaign);

        f.Campaigns.Release(embers.Id, "KQZT");
        await f.Room.SelectCampaignAsync("host", embers.Id);
        Assert.Equal(embers.Id, Lobby(f).Campaign!.Id);
    }

    [Fact]
    public async Task Lease_released_on_game_end_and_on_crash()
    {
        await using var f = new RoomFixture();
        var (campaign, _) = await EmbersAsync(f);
        var other = await f.NewRoomAsync("OTHR");
        await other.SelectGameAsync("host-OTHR", "ledger");

        await f.Room.StartGameAsync("host");
        Assert.Equal("Embers is being played in room TEST.", await Fails(other.SelectCampaignAsync("host-OTHR", campaign.Id)));
        await f.Room.ReturnToLobbyAsync("host");
        Assert.Null(f.Campaigns.LeasedBy(campaign.Id));

        await f.Room.StartGameAsync("host");
        Assert.Equal(Code, f.Campaigns.LeasedBy(campaign.Id));
        await f.Room.SendInputAsync("c1", f.LastViewId("c1"), Json(new { crash = true }));
        Assert.False(Lobby(f).InGame);
        Assert.Null(f.Campaigns.LeasedBy(campaign.Id));

        await other.SelectCampaignAsync("host-OTHR", campaign.Id);
        Assert.Equal(campaign.Id, Lobby(f, "host-OTHR").Campaign!.Id);
    }

    [Fact]
    public async Task Lease_released_on_force_close_and_when_the_room_stops()
    {
        await using var f = new RoomFixture();
        var (embers, _) = await EmbersAsync(f);
        await f.Room.StartGameAsync("host");
        f.Room.ForceClose("game stopped responding");
        Assert.Null(f.Campaigns.LeasedBy(embers.Id));

        var other = await f.NewRoomAsync("OTHR");
        await other.JoinAsync("c9", "Zed");
        await other.SelectGameAsync("host-OTHR", "ledger");
        await other.CreateCampaignAsync("c9", "Frost");
        await other.StartGameAsync("c9");
        var frost = f.Campaigns.ListForGame("ledger").Single(c => c.Name == "Frost");
        Assert.Equal("OTHR", f.Campaigns.LeasedBy(frost.Id));

        await f.StopAsync(other);
        Assert.Null(f.Campaigns.LeasedBy(frost.Id));
    }

    [Fact]
    public async Task Ledger_save_reaches_campaign_service()
    {
        await using var f = new RoomFixture();
        var (campaign, _) = await EmbersAsync(f);
        await f.Room.StartGameAsync("host");

        await f.Room.SendInputAsync("c1", f.LastViewId("c1"), Json(new { save = "x" }));

        Assert.Equal(1, f.Campaigns.Find(campaign.Id)!.Version);
        Assert.Equal("""{"notes":["x"]}""", f.Campaigns.DocumentOf(campaign.Id).GetRawText());
        Assert.Equal(1, HostView(f).GetProperty("version").GetInt64());
    }

    [Fact]
    public async Task Min_players_counts_absent_seats()
    {
        await using var f = new RoomFixture(ledger: LedgerGame.Campaign with { MinPlayers = 3 });
        var (campaign, _) = await EmbersAsync(f);
        AddMember(f, campaign, "Cy");
        Assert.Equal("This game needs 3–8 players.", await Fails(f.Room.StartGameAsync("host")));

        AddMember(f, campaign, "Dan");
        await f.Room.SelectCampaignAsync("host", campaign.Id); // refreshes the lobby
        Assert.True(Lobby(f).CanStart);
        await f.Room.StartGameAsync("c1");

        Assert.Equal(3, HostView(f).GetProperty("seats").GetArrayLength());
    }

    [Fact]
    public async Task Hub_exposes_the_campaign_calls()
    {
        using var f = new ArcServerFactory();
        await using var host = await TestClient.ConnectAsync(f);
        await using var ann = await TestClient.ConnectAsync(f);
        await using var bea = await TestClient.ConnectAsync(f);
        var code = (await host.InvokeAsync<CreateRoomResult>("CreateRoom")).Code;
        await ann.InvokeAsync<JoinResult>("Join", code, "Ann");
        await bea.InvokeAsync<JoinResult>("Join", code, "Bea");
        await ann.InvokeAsync("SelectGame", "ledger");

        var annKey = await ann.InvokeAsync<ProfileKey>("CreateCampaign", "Embers");
        var beaKey = await bea.InvokeAsync<ProfileKey?>("ClaimMember", new ClaimMemberRequest(null));

        Assert.Equal("Ann", annKey.Name);
        Assert.Equal("Bea", beaKey!.Name);
        var lobby = await host.WaitForAsync(ClientMethods.LobbyChanged,
            l => l.GetProperty("campaign").GetProperty("roster").GetArrayLength() == 2);
        var id = lobby.GetProperty("campaign").GetProperty("id").GetString();
        Assert.Equal("Embers", lobby.GetProperty("campaigns")[0].GetProperty("name").GetString());
        await host.InvokeAsync("SelectCampaign", id);
        Assert.Equal("Only the VIP can create a campaign.", await host.ThrowsHubAsync("CreateCampaign", "Frost"));

        await ann.InvokeAsync("StartGame");
        await host.WaitForAsync(ClientMethods.HostView, v => v.GetProperty("campaignId").GetString() == id);
        Assert.Equal("You can only change who you are in the lobby.",
            await bea.ThrowsHubAsync("ClaimMember", new ClaimMemberRequest(null)));
    }

    private static void InsertCampaign(ArcDb db, string id, string code, string name, int members)
    {
        var list = Enumerable.Range(1, members)
            .Select(i => new CampaignMember(new ProfileId($"pr_m{i}"), $"M{i}", i == 1 ? CampaignRole.Owner : CampaignRole.Player,
                DateTimeOffset.UnixEpoch))
            .ToList();
        var membersJson = JsonSerializer.Serialize(list, ArcJson.Options).Replace("'", "''");
        db.Execute($$"""
            INSERT INTO campaigns (id, code, game_id, name, owner, members_json, document, version, updated_utc)
            VALUES ('{{id}}', '{{code}}', 'ledger', '{{name}}', 'pr_m1', '{{membersJson}}', '{"notes":[]}', 0, '2026-01-01T00:00:00Z')
            """);
    }
}
