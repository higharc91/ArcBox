using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Campaigns;
using ArcBox.Sdk.Testing;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Beats;
using Tactics.Presentation.Board;
using Tactics.Presentation.Hud;

namespace Tactics.Presentation.Tests;

/// <summary>
/// The TV reads real host views: every phase of a real session, written the way the server writes them and read the way
/// the scene reads them, turns into a screen.
/// </summary>
public sealed class HostViewContractTests
{
    private static readonly Lazy<RulesPack> Pack = new(() =>
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "ArcBox.slnx")))
                return RulesPack.Load([Path.Combine(dir.FullName, "content", "tactics", "base", "rules")]);
        throw new DirectoryNotFoundException("No ArcBox.slnx above the test binaries.");
    });

    private static string ContentRoot => Tactics.Presentation.Tests.Board.Pack.Root;

    private static TacticsHostView OverTheWire(TacticsGame game)
        => JsonSerializer.SerializeToElement(game.HostView(), ArcJson.Options).Deserialize<TacticsHostView>(ArcJson.Options)!;

    /// <summary>The Done-when check as a test: a real session's first view against the encounter and map files.</summary>
    [Fact]
    public void The_river_crossing_start_matches_the_encounter_and_the_map()
    {
        var rules = Pack.Value;
        ProfileId?[] profiles = [new ProfileId("pr_ana"), new ProfileId("pr_bo"), new ProfileId("pr_cy"), new ProfileId("pr_di")];
        string[] classes = ["fighter", "cleric", "wizard", "rogue"];
        var game = new TacticsGame(rules);
        var h = new GameHarness<TacticsGame>(game, playerCount: 4, seed: 3, profiles: profiles);
        var info = new CampaignInfo("c1", "ABCD", TacticsGame.Id, "Embers", profiles[0]!.Value,
            [.. profiles.Select((p, i) => new CampaignMember(p!.Value, $"Player{i + 1}", CampaignRole.Player, DateTimeOffset.UnixEpoch))], 1);
        h.StartCampaign(new FakeCampaignLease(info, new TacticsCampaignRules(rules).NewDocument(info)));
        for (var i = 0; i < classes.Length; i++)
            h.Input(i, new { action = "pickClass", data = new { @class = classes[i] } });
        h.Input(0, new { action = "pickEncounter", data = new { encounter = "river_crossing" } });

        var battle = OverTheWire(game).Battle!;
        var map = rules.MapFor("river_crossing")!;
        var units = UnitPlan.Build(battle, new BoardTerrain(Board.Pack.Map("river_crossing"), battle.Board), UnitArt.Load(Board.Pack.Root),
            BoardFit.For(new BoardTerrain(Board.Pack.Map("river_crossing"), null), Board.Pack.Art));

        Assert.Equal(10, units.Length);
        var party = battle.Units.Where(u => u.Side == "Party").ToArray();
        Assert.Equal([.. map.Zones["party_spawn"].Take(4)], party.Select(u => u.At));
        Assert.Equal(classes.Select(c => $"characters.class_{c}"), party.Select(u => u.Sprite));
        Assert.Equal(["goblin.1", "goblin.2", "goblin.3", "goblin.4", "goblin_archer.1", "goblin_archer.2"],
            battle.Units.Where(u => u.Side == "Enemy").Select(u => u.Id.Value));
        Assert.Equal([new Tile(9, 5), new Tile(10, 5), new Tile(9, 6), new Tile(10, 6)],
            battle.Units.Where(u => u.Sprite == "characters.goblin_cutthroat").Select(u => u.At));
        Assert.Equal([new Tile(11, 2), new Tile(12, 2)], battle.Units.Where(u => u.Sprite == "characters.goblin_archer").Select(u => u.At));
        Assert.All(units, u => Assert.DoesNotContain("presets/bandit", u.Sprite.Path));   // nobody needed a stand-in
    }

    [Fact]
    public void Every_phase_of_a_real_session_reaches_the_tv()
    {
        var rules = Pack.Value;
        ProfileId?[] profiles = [new ProfileId("pr_ana"), new ProfileId("pr_bo")];
        var game = new TacticsGame(rules);
        var h = new GameHarness<TacticsGame>(game, playerCount: 2, seed: 3, profiles: profiles);
        var info = new CampaignInfo("c1", "ABCD", TacticsGame.Id, "Embers", profiles[0]!.Value,
            [.. profiles.Select((p, i) => new CampaignMember(p!.Value, $"Player{i + 1}", CampaignRole.Player, DateTimeOffset.UnixEpoch))], 1);
        h.StartCampaign(new FakeCampaignLease(info, new TacticsCampaignRules(rules).NewDocument(info)));

        var muster = OverTheWire(game);
        Assert.Equal("Muster · 0 ready", PhaseScreen.Of(muster).Meta);

        h.Input(0, new { action = "pickClass", data = new { @class = "fighter" } });
        Assert.Equal(["Player1"], PhaseScreen.Of(OverTheWire(game)).Rows.Select(r => r.Title));
        h.Input(1, new { action = "pickClass", data = new { @class = "cleric" } });

        var pick = PhaseScreen.Of(OverTheWire(game));
        Assert.Contains(pick.Rows, r => r is { Title: "River Crossing", Detail: "4–6 players · +2 AI companions", Value: "Lv 1" });

        h.Input(0, new { action = "pickEncounter", data = new { encounter = "river_crossing" } });
        h.SetConnected(1, false);
        h.SetConnected(0, false);
        for (var step = 0; step < 3; step++)
            h.Advance(TimeSpan.FromSeconds(7));

        var fight = OverTheWire(game);
        Assert.NotNull(fight.Battle);
        Assert.NotEmpty(fight.Battle.Beats.SelectMany(b => b.Events));   // the polymorphic events survive the trip
        Assert.StartsWith("Battle · River Crossing · Round ", PhaseScreen.Of(fight).Meta);
        Assert.NotNull(fight.Battle.Board);                              // and so do the board's changes

        for (var step = 0; step < 20_000 && game.Phase == TacticsPhase.Battle; step++)
            h.Advance(TimeSpan.FromSeconds(7));
        h.SetConnected(0, true);

        var over = PhaseScreen.Of(OverTheWire(game));
        Assert.StartsWith("Results · ", over.Meta);
        Assert.Equal(2, over.Rows.Length);
        Assert.All(over.Rows, r => Assert.EndsWith(" XP", r.Value));
    }

    [Fact]
    public void The_warlords_objectives_reach_the_tv_and_read_as_the_encounter_says()
    {
        var rules = Pack.Value;
        ProfileId?[] profiles = [new ProfileId("pr_ana"), new ProfileId("pr_bo"), new ProfileId("pr_cy"), new ProfileId("pr_di")];
        string[] classes = ["fighter", "cleric", "wizard", "rogue"];
        var game = new TacticsGame(rules);
        var h = new GameHarness<TacticsGame>(game, playerCount: 4, seed: 3, profiles: profiles);
        var info = new CampaignInfo("c1", "ABCD", TacticsGame.Id, "Embers", profiles[0]!.Value,
            [.. profiles.Select((p, i) => new CampaignMember(p!.Value, $"Player{i + 1}", CampaignRole.Player, DateTimeOffset.UnixEpoch))], 1);
        h.StartCampaign(new FakeCampaignLease(info, new TacticsCampaignRules(rules).NewDocument(info)));
        for (var i = 0; i < classes.Length; i++)
            h.Input(i, new { action = "pickClass", data = new { @class = classes[i] } });
        h.Input(0, new { action = "pickEncounter", data = new { encounter = "warlord_bridge" } });

        var battle = OverTheWire(game).Battle!;

        Assert.Equal("Defeat the warlord, or survive 11 rounds", BattleHud.Of(battle, battle, "", null, null).Objective);
        Assert.Equal("The Warlord's Bridge", BattleHud.Of(battle, battle, "", null, null).Title);
    }

    /// <summary>
    /// Why the TV may animate from events alone: in a whole real battle, every beat applied to the battle as it stood gives
    /// exactly the next view, unit by unit and tile by tile.
    /// </summary>
    [Theory]
    [InlineData("river_crossing", 3)]
    [InlineData("warlord_bridge", 5)]
    [InlineData("crypt_hall", 7)]
    public void Beats_alone_rebuild_the_battle(string encounter, int seed)
    {
        var (game, h) = AiBattle(encounter, seed);

        var shown = OverTheWire(game).Battle!;
        var seen = shown.Beats[^1].Seq;
        var beats = 0;
        for (var step = 0; step < 20_000 && game.Phase == TacticsPhase.Battle; step++)
        {
            h.Advance(TimeSpan.FromSeconds(7));
            var view = OverTheWire(game);
            var live = view.Battle ?? view.LastBattle!;
            foreach (var beat in live.Beats.Where(b => b.Seq > seen))
            {
                Assert.Equal(seen + 1, beat.Seq);    // none missed
                seen = beat.Seq;
                beats++;
                foreach (var e in beat.Events)
                    shown = DisplayedBattle.Apply(shown, e, live);
            }
            AssertSame(live, shown);
        }

        Assert.Equal(TacticsPhase.Results, game.Phase);
        Assert.True(beats > 20, $"only {beats} beats");
    }

    /// <summary>What a catch-up relies on: taking a real beat back off a real view gives the view before it, for what's drawn of a unit.</summary>
    [Theory]
    [InlineData("river_crossing", 3)]
    [InlineData("warlord_bridge", 5)]
    [InlineData("crypt_hall", 7)]
    public void Undoing_a_beat_gives_the_view_before_it(string encounter, int seed)
    {
        var (game, h) = AiBattle(encounter, seed);

        var before = OverTheWire(game).Battle!;
        var undone = 0;
        for (var step = 0; step < 20_000 && game.Phase == TacticsPhase.Battle; step++)
        {
            h.Advance(TimeSpan.FromSeconds(7));
            var view = OverTheWire(game);
            var live = view.Battle ?? view.LastBattle!;
            var fresh = live.Beats.Where(b => b.Seq > before.Beats[^1].Seq).ToArray();
            if (fresh.Length > 0 && fresh[0].Seq == before.Beats[^1].Seq + 1)
            {
                var back = fresh.Reverse().Aggregate(live, DisplayedBattle.Before);
                foreach (var unit in back.Units.Where(u => before.Units.Any(b => b.Id == u.Id)))
                    Assert.Equal(Where(before.Units.Single(b => b.Id == unit.Id)), Where(unit));
                Assert.Equal(before.Active, back.Active);
                undone += fresh.Length;
            }
            before = live;
        }

        Assert.True(undone > 20, $"only {undone} beats");
    }

    private static string Where(UnitView u) => $"{u.Id} {u.At} {u.Life}";

    /// <summary>A TV joining a real fight half way plays one beat, says so to the game, and ends on the battle as it stands.</summary>
    [Fact]
    public void A_tv_joining_a_real_battle_half_way_plays_one_beat_and_lands_on_the_state()
    {
        var (game, h) = AiBattle("river_crossing", 3);
        for (var step = 0; step < 40; step++)
            h.Advance(TimeSpan.FromSeconds(7));
        var live = OverTheWire(game).Battle!;
        Assert.True(live.Beats[^1].Seq > 20);
        var player = new BeatPlayer(FxArt.Load(ContentRoot), UnitArt.Load(ContentRoot));

        player.Receive(live);
        var shown = new List<int>();
        for (var t = 0.0; t < 6; t += 1 / 60.0)
            shown.AddRange(player.Tick(1 / 60.0).Shown);

        Assert.Equal([live.Beats[^1].Seq], shown);
        AssertSame(live, player.Shown!);
    }

    [Fact]
    public void The_results_view_still_has_the_blow_that_ended_the_battle()
    {
        var (game, h) = AiBattle("river_crossing", 3);

        for (var step = 0; step < 20_000 && game.Phase == TacticsPhase.Battle; step++)
            h.Advance(TimeSpan.FromSeconds(7));

        var view = OverTheWire(game);
        Assert.Null(view.Battle);
        Assert.Contains(view.LastBattle!.Beats[^1].Events, e => e is BattleEnded);
    }

    private static (TacticsGame Game, GameHarness<TacticsGame> Harness) AiBattle(string encounter, int seed)
    {
        var rules = Pack.Value;
        ProfileId?[] profiles = [new ProfileId("pr_ana"), new ProfileId("pr_bo"), new ProfileId("pr_cy"), new ProfileId("pr_di")];
        string[] classes = ["fighter", "cleric", "wizard", "rogue"];
        var game = new TacticsGame(rules);
        var h = new GameHarness<TacticsGame>(game, playerCount: 4, seed: seed, profiles: profiles);
        var info = new CampaignInfo("c1", "ABCD", TacticsGame.Id, "Embers", profiles[0]!.Value,
            [.. profiles.Select((p, i) => new CampaignMember(p!.Value, $"Player{i + 1}", CampaignRole.Player, DateTimeOffset.UnixEpoch))], 1);
        h.StartCampaign(new FakeCampaignLease(info, new TacticsCampaignRules(rules).NewDocument(info)));
        for (var i = 0; i < classes.Length; i++)
            h.Input(i, new { action = "pickClass", data = new { @class = classes[i] } });
        h.Input(0, new { action = "pickEncounter", data = new { encounter } });
        for (var i = 0; i < classes.Length; i++)
            h.SetConnected(i, false);
        return (game, h);
    }

    private static void AssertSame(BattleView live, BattleView shown)
    {
        var drawn = live.Units.Where(u => u.Life is "Up" or "Downed").ToArray();
        Assert.Equal(drawn.OrderBy(u => u.Id.Value, StringComparer.Ordinal).Select(Facts),
            shown.Units.Where(u => u.Life is "Up" or "Downed").OrderBy(u => u.Id.Value, StringComparer.Ordinal).Select(Facts));
        Assert.True(DisplayedBattle.SameBoard(live.Board, shown.Board), "the board drifted");
        Assert.Equal(live.Active, shown.Active);
        Assert.Equal(live.Round, shown.Round);
    }

    private static string Facts(UnitView u)
        => $"{u.Id} {u.At} hp {u.Hp}+{u.TempHp} {u.Life} {u.Countdown} {string.Join(",", u.Conditions.Order(StringComparer.Ordinal))} {u.Sprite}";
}
