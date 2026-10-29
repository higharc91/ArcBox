using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Campaigns;
using ArcBox.Sdk.Protocol;
using ArcBox.Sdk.Testing;
using Xunit;

namespace ArcBox.Sdk.Tests;

public class SerializationTests
{
    public static TheoryData<ControllerView> AllKinds => new()
    {
        new WaitView("Waiting for players...") { ViewId = "v1", Header = "Round 1" },
        new TextEntryView("Your answer", 50, "type here") { ViewId = "v2" },
        new ChoiceView("Pick", [new Option("a", "A"), new Option("b", "B")], true, 2) { ViewId = "v3" },
        new ButtonsView("Tap a button", [new Option("btn1", "Button 1")]) { ViewId = "v4" },
        new DrawView("Draw a cat", ["#ff0000", "#00ff00"]) { ViewId = "v5" },
        new ShakeView("Shake to build pressure!", "#ff8000", CanFire: true, InitialCharge: 0.35) { ViewId = "v6" },
    };

    [Fact]
    public void PlayerId_serializes_as_plain_string()
        => Assert.Equal("\"p1\"", JsonSerializer.Serialize(new PlayerId("p1"), ArcJson.Options));

    [Fact]
    public void Option_hint_serializes_camelCase_and_round_trips()
    {
        ControllerView v = new ButtonsView(null, [new Option("redline", "REDLINE", "+4 if right · spin out if wrong")]) { ViewId = "v" };
        var json = JsonSerializer.SerializeToElement(v, ArcJson.Options);
        Assert.Equal("+4 if right · spin out if wrong", json.GetProperty("buttons")[0].GetProperty("hint").GetString());
        Assert.Equal(json.GetRawText(), JsonSerializer.SerializeToElement(JsonSerializer.Deserialize<ControllerView>(json, ArcJson.Options), ArcJson.Options).GetRawText());
    }

    [Fact]
    public void Option_without_hint_has_no_hint_property()
    {
        ControllerView v = new ButtonsView(null, [new Option("go", "GO")]) { ViewId = "v" };
        var json = JsonSerializer.SerializeToElement(v, ArcJson.Options);
        Assert.False(json.GetProperty("buttons")[0].TryGetProperty("hint", out _));
    }

    [Fact]
    public void ControllerView_includes_kind_discriminator_and_camelCase()
    {
        ControllerView v = new ChoiceView("Pick", [new Option("a", "A")]) { ViewId = "v3" };
        var json = JsonSerializer.SerializeToElement(v, ArcJson.Options);
        Assert.Equal("choice", json.GetProperty("kind").GetString());
        Assert.Equal("v3", json.GetProperty("viewId").GetString());
        Assert.Equal("A", json.GetProperty("options")[0].GetProperty("label").GetString());
    }

    [Theory]
    [MemberData(nameof(AllKinds))]
    public void Every_widget_round_trips(ControllerView view)
    {
        var json = JsonSerializer.Serialize(view, ArcJson.Options);
        var back = JsonSerializer.Deserialize<ControllerView>(json, ArcJson.Options);
        Assert.Equal(json, JsonSerializer.Serialize(back, ArcJson.Options));
    }

    [Fact]
    public void ShakeView_serializes_kind_shake_with_camelCase_fields()
    {
        ControllerView v = new ShakeView("Pop the cork!", "#dc0000", CanFire: true, InitialCharge: 0.4) { ViewId = "v7" };
        var json = JsonSerializer.SerializeToElement(v, ArcJson.Options);
        Assert.Equal("shake", json.GetProperty("kind").GetString());
        Assert.Equal("Pop the cork!", json.GetProperty("prompt").GetString());
        Assert.Equal("#dc0000", json.GetProperty("color").GetString());
        Assert.True(json.GetProperty("canFire").GetBoolean());
        Assert.Equal(0.4, json.GetProperty("initialCharge").GetDouble());
    }

    [Fact]
    public void ShakeView_defaults_to_no_color_no_fire_and_empty_charge()
    {
        var v = new ShakeView("Shake!");
        Assert.Null(v.Color);
        Assert.False(v.CanFire);
        Assert.Equal(0, v.InitialCharge);
    }

    [Fact]
    public void ShakeInput_reads_charge_and_fire()
    {
        var input = new PlayerInput(JsonSerializer.SerializeToElement(new { charge = 0.75, fire = true }));
        Assert.Equal(new ShakeInput(0.75, true), input.Read<ShakeInput>());
        var chargeOnly = new PlayerInput(JsonSerializer.SerializeToElement(new { charge = 0.25 }));
        Assert.Equal(new ShakeInput(0.25, false), chargeOnly.Read<ShakeInput>());
    }

    [Fact]
    public void ShakeInput_without_a_numeric_charge_is_rejected()
    {
        Assert.Throws<JsonException>(() => new PlayerInput(JsonSerializer.SerializeToElement(new { fire = true })).Read<ShakeInput>());
        Assert.Throws<JsonException>(() => new PlayerInput(JsonSerializer.SerializeToElement(new { pressed = "go" })).Read<ShakeInput>());
        Assert.Throws<JsonException>(() => new PlayerInput(JsonSerializer.SerializeToElement(new { charge = "lots" })).Read<ShakeInput>());
    }

    [Fact]
    public void PlayerInput_reads_typed_payload()
    {
        var input = new PlayerInput(JsonSerializer.SerializeToElement(new { pressed = "go" }));
        Assert.Equal("go", input.Read<ButtonsInput>().Pressed);
    }

    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement;

    [Fact]
    public void CustomView_round_trips_with_kind_custom()
    {
        ControllerView v = new CustomView("tactics", "targetPicker", Json("""{"cursor":{"x":2,"y":3},"targets":[1,2,3]}""")) { ViewId = "v8", Header = "Your turn" };
        var json = JsonSerializer.SerializeToElement(v, ArcJson.Options);
        Assert.Equal("custom", json.GetProperty("kind").GetString());
        Assert.Equal("tactics", json.GetProperty("module").GetString());
        Assert.Equal("targetPicker", json.GetProperty("screen").GetString());
        Assert.Equal("""{"cursor":{"x":2,"y":3},"targets":[1,2,3]}""", json.GetProperty("data").GetRawText());

        var back = Assert.IsType<CustomView>(JsonSerializer.Deserialize<ControllerView>(json.GetRawText(), ArcJson.Options));
        Assert.Equal("tactics", back.Module);
        Assert.Equal("targetPicker", back.Screen);
        Assert.Equal("""{"cursor":{"x":2,"y":3},"targets":[1,2,3]}""", back.Data.GetRawText());
        Assert.Equal("v8", back.ViewId);
    }

    [Fact]
    public void CustomInput_reads_action_and_optional_data()
    {
        var withData = new PlayerInput(Json("""{"action":"move","data":{"x":1}}""")).Read<CustomInput>();
        Assert.Equal("move", withData.Action);
        Assert.Equal("""{"x":1}""", withData.Data!.Value.GetRawText());
        Assert.Null(new PlayerInput(Json("""{"action":"endTurn"}""")).Read<CustomInput>().Data);
    }

    [Fact]
    public void ProfileId_serializes_as_plain_string()
    {
        Assert.Equal("\"pr_abc\"", JsonSerializer.Serialize(new ProfileId("pr_abc"), ArcJson.Options));
        Assert.Equal(new ProfileId("pr_abc"), JsonSerializer.Deserialize<ProfileId>("\"pr_abc\"", ArcJson.Options));
    }

    [Fact]
    public void Player_without_profile_serializes_without_it()
    {
        var json = JsonSerializer.SerializeToElement(new Player(new PlayerId("p1"), "ALEX", IsVip: true), ArcJson.Options);
        Assert.False(json.TryGetProperty("profile", out _));
        Assert.Null(new Player(new PlayerId("p1"), "ALEX", true).Profile);
    }

    [Fact]
    public void Player_with_profile_serializes_it_as_a_plain_string()
    {
        var player = new Player(new PlayerId("p1"), "ALEX", true, new ProfileId("pr_AbCdEfGhIjKlMnOp"));
        var json = JsonSerializer.SerializeToElement(player, ArcJson.Options);
        Assert.Equal("pr_AbCdEfGhIjKlMnOp", json.GetProperty("profile").GetString());
        Assert.Equal(player, JsonSerializer.Deserialize<Player>(json.GetRawText(), ArcJson.Options));
    }

    [Fact]
    public void GameManifest_flags_default_false()
    {
        var manifest = new GameManifest("counter", "Counter", "Tap to count", 1, 8);
        Assert.False(manifest.UsesCampaigns);
        Assert.False(manifest.ResumesAfterRestart);
        var json = JsonSerializer.SerializeToElement(manifest, ArcJson.Options);
        Assert.False(json.GetProperty("usesCampaigns").GetBoolean());
        Assert.False(json.GetProperty("resumesAfterRestart").GetBoolean());
    }

    [Fact]
    public void Lobby_additions_are_omitted_when_null()
    {
        var lobby = new LobbyState("BCDF", [new LobbyPlayer(new PlayerId("p1"), "ALEX", true, true)], null, [], false, false, null);
        var json = JsonSerializer.SerializeToElement(lobby, ArcJson.Options);
        Assert.False(json.TryGetProperty("campaign", out _));
        Assert.False(json.TryGetProperty("campaigns", out _));
        Assert.False(json.GetProperty("players")[0].TryGetProperty("profile", out _));
    }

    [Fact]
    public void ClientMethods_has_ContentRequired()
        => Assert.Equal("ContentRequired", ClientMethods.ContentRequired);

    [Fact]
    public void Harness_StartCampaign_passes_the_lease_and_the_profiles()
    {
        var lease = new FakeCampaignLease(Info(), Json("""{"gold":5}"""));
        ProfileId?[] profiles = [new ProfileId("pr_one"), null, new ProfileId("pr_three")];
        var harness = new GameHarness<StubCampaignGame>(new StubCampaignGame(), playerCount: 3, profiles: profiles);

        harness.StartCampaign(lease);

        Assert.Same(lease, harness.Game.Lease);
        Assert.Equal(["pr_one", null, "pr_three"], harness.Game.Players.Select(p => p.Profile?.Value));
        Assert.Equal(["pr_one", null, "pr_three"], harness.Players.Select(p => p.Profile?.Value));
        Assert.Equal(0, harness.Game.PlainStarts);
    }

    [Fact]
    public void Harness_without_profiles_leaves_them_null()
    {
        var harness = new GameHarness<StubCampaignGame>(new StubCampaignGame(), playerCount: 2);
        Assert.All(harness.Players, p => Assert.Null(p.Profile));
    }

    [Fact]
    public void Harness_rejects_more_profiles_than_players()
        => Assert.Throws<ArgumentException>(() => new GameHarness<StubCampaignGame>(new StubCampaignGame(), playerCount: 1, profiles: [null, null]));

    [Fact]
    public void Harness_Resume_and_HostSignal_reach_the_game()
    {
        var harness = new GameHarness<StubCampaignGame>(new StubCampaignGame(), playerCount: 2);
        var lease = new FakeCampaignLease(Info(), Json("{}"));

        harness.Resume(lease);
        harness.Resume(null);
        harness.HostSignal("anim.done", new { seq = 41 });

        Assert.Equal([lease, null], harness.Game.Resumes);
        var (kind, payload) = Assert.Single(harness.Game.Signals);
        Assert.Equal("anim.done", kind);
        Assert.Equal(41, payload.Read<SeqPayload>().Seq);
    }

    [Fact]
    public void Harness_methods_throw_when_the_game_lacks_the_interface()
    {
        var harness = new GameHarness<PlainGame>(new PlainGame());
        Assert.Throws<InvalidOperationException>(() => harness.StartCampaign(new FakeCampaignLease(Info(), Json("{}"))));
        Assert.Throws<InvalidOperationException>(() => harness.Resume(null));
        Assert.Throws<InvalidOperationException>(() => harness.HostSignal("x", new { }));
    }

    [Fact]
    public void FakeCampaignLease_records_saves_and_checkpoints_and_bumps_the_version()
    {
        var lease = new FakeCampaignLease(Info(version: 3), Json("""{"n":0}"""));
        Assert.Equal(3, lease.Info.Version);

        lease.Save(Json("""{"n":1}"""));
        lease.Checkpoint(Json("""{"n":2}"""), "End of session 7");

        Assert.Equal("""{"n":2}""", lease.Document.GetRawText());
        Assert.Equal(5, lease.Info.Version);
        Assert.Equal(["""{"n":1}""", """{"n":2}"""], lease.Saves.Select(d => d.GetRawText()));
        var (doc, label) = Assert.Single(lease.Checkpoints);
        Assert.Equal("""{"n":2}""", doc.GetRawText());
        Assert.Equal("End of session 7", label);
    }

    private static CampaignInfo Info(long version = 1) => new("c1", "BCDF", "stub", "Embers of the Vale",
        new ProfileId("pr_one"), [new CampaignMember(new ProfileId("pr_one"), "Dave", CampaignRole.Owner, DateTimeOffset.UnixEpoch)], version);

    private sealed record SeqPayload(int Seq);

    private class PlainGame : IGame
    {
        public GameManifest Manifest => new("plain", "Plain", "", 1, 8);
        public void Start(IGameContext ctx, IReadOnlyList<Player> players) { }
        public void OnInput(PlayerId from, PlayerInput input) { }
        public void OnTimer(string key) { }
        public void OnPlayerConnectionChanged(PlayerId id, bool connected) { }
        public object HostView() => new { };
        public ControllerView PlayerView(PlayerId id) => new WaitView("");
    }

    private sealed class StubCampaignGame : PlainGame, ICampaignGame, IResumableGame, IHostAware
    {
        public ICampaignLease? Lease { get; private set; }
        public IReadOnlyList<Player> Players { get; private set; } = [];
        public int PlainStarts { get; private set; }
        public List<ICampaignLease?> Resumes { get; } = [];
        public List<(string Kind, PlayerInput Payload)> Signals { get; } = [];

        void ICampaignGame.Start(IGameContext ctx, IReadOnlyList<Player> players, ICampaignLease campaign)
        {
            Lease = campaign;
            Players = players;
        }

        public new void Start(IGameContext ctx, IReadOnlyList<Player> players) => PlainStarts++;
        public void Resume(IGameContext ctx, IReadOnlyList<Player> players, ICampaignLease? campaign) => Resumes.Add(campaign);
        public void OnHostSignal(string kind, PlayerInput payload) => Signals.Add((kind, payload));
    }
}
