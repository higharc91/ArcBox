using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Campaigns;
using ArcBox.Sdk.Testing;
using Tactics.Logic;

namespace Tactics.Game.Tests;

/// <summary><see cref="TacticsGame"/> on the platform harness: muster, encounter pick, the battle loop, results and resume.</summary>
public sealed class TacticsGameTests
{
    private static readonly Lazy<RulesPack> Pack = new(() =>
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "ArcBox.slnx")))
                return RulesPack.Load([Path.Combine(dir.FullName, "content", "tactics", "base", "rules")]);
        throw new DirectoryNotFoundException("No ArcBox.slnx above the test binaries.");
    });

    private static RulesPack Rules => Pack.Value;

    private static readonly string[] FourClasses = ["fighter", "cleric", "rogue", "wizard"];

    /// <summary>A four-member campaign session: the harness, its lease and the game.</summary>
    private sealed class Session
    {
        /// <param name="reseated">Members sit in the opposite order (another night, other phones).</param>
        public Session(int seed = 1, JsonElement? document = null, TacticsGame? game = null, bool reseated = false)
        {
            var order = reseated ? new[] { 4, 3, 2, 1 } : [1, 2, 3, 4];
            Profiles = [.. order.Select(i => (ProfileId?)new ProfileId($"pr_member{i}"))];
            Game = game ?? new TacticsGame(Rules);
            Harness = new GameHarness<TacticsGame>(Game, playerCount: 4, seed: seed, profiles: Profiles);
            var info = new CampaignInfo("c1", "ABCD", TacticsGame.Id, "Embers", Profiles[0]!.Value,
                [.. Profiles.Select((p, i) => new CampaignMember(p!.Value, $"Player{i + 1}", i == 0 ? CampaignRole.Owner : CampaignRole.Player,
                    DateTimeOffset.UnixEpoch))], 1);
            Lease = new FakeCampaignLease(info, document ?? new TacticsCampaignRules(Rules).NewDocument(info));
        }

        public IReadOnlyList<ProfileId?> Profiles { get; }
        public TacticsGame Game { get; }
        public GameHarness<TacticsGame> Harness { get; }
        public FakeCampaignLease Lease { get; }

        public TacticsHostView Host => Harness.HostView<TacticsHostView>();

        public void Send(int player, string action, object? data = null)
            => Harness.Input(player, new { action, data });

        /// <summary>Starts, everyone picks a class, and the VIP picks <paramref name="encounter"/>.</summary>
        public void ToBattle(string encounter = "river_crossing", bool everyoneAway = false)
        {
            Harness.StartCampaign(Lease);
            Assert.Equal(TacticsPhase.Muster, Game.Phase);
            for (var i = 0; i < 4; i++)
                Send(i, "pickClass", new { @class = FourClasses[i] });
            Assert.Equal(TacticsPhase.EncounterPick, Game.Phase);
            if (everyoneAway)
                for (var i = 1; i < 4; i++)
                    Harness.SetConnected(i, false);
            Send(0, "pickEncounter", new { encounter });
            Assert.Equal(TacticsPhase.Battle, Game.Phase);
            if (everyoneAway)
                Harness.SetConnected(0, false);
        }

        /// <summary>Lets time pass in fallback-timer steps until the battle is over (or the step budget runs out).</summary>
        public void PlayOut(int steps = 20_000)
        {
            for (var i = 0; i < steps && Game.Phase == TacticsPhase.Battle; i++)
                Harness.Advance(TimeSpan.FromSeconds(7));
        }

        public CustomView? Screen(int player) => Harness.PlayerView(player) as CustomView;
    }

    [Fact]
    public void A_new_campaign_reads_back_and_shows_who_has_a_character()
    {
        var rules = new TacticsCampaignRules(Rules);
        var info = new CampaignInfo("c1", "ABCD", TacticsGame.Id, "Embers", new ProfileId("pr_a"),
            [new CampaignMember(new ProfileId("pr_a"), "Ana", CampaignRole.Owner, DateTimeOffset.UnixEpoch)], 1);
        var doc = TacticsDocument.Read(rules.NewDocument(info));
        Assert.Equal(1, doc.PartyLevel);
        Assert.Empty(doc.Members);
        Assert.Equal("No character yet", rules.CardFor(info, doc.ToJson(), new ProfileId("pr_a")).Subtitle);

        var character = SheetBuilder.DefaultCharacter("Ana", "dwarf", "cleric", null, 1, Rules);
        doc = doc with { Members = doc.Members.SetItem("pr_a", new MemberRecord(character, 0)) };
        var again = TacticsDocument.Read(doc.ToJson());
        Assert.Equal(doc.ToJson().GetRawText(), again.ToJson().GetRawText());
        Assert.Equal("Dwarf Cleric · Lv 1", rules.CardFor(info, again.ToJson(), new ProfileId("pr_a")).Subtitle);
    }

    [Fact]
    public void An_all_ai_battle_plays_out_and_writes_its_result_to_the_campaign()
    {
        var s = new Session();
        s.ToBattle(everyoneAway: true);

        s.PlayOut();

        Assert.Equal(TacticsPhase.Results, s.Game.Phase);
        var doc = TacticsDocument.Read(s.Lease.Document);
        Assert.Null(doc.Battle);
        Assert.Equal(4, doc.Members.Count);
        Assert.All(doc.Members.Values, m => Assert.True(m.Xp > 0));
        var (_, label) = Assert.Single(s.Lease.Checkpoints);
        Assert.Equal("After River Crossing", label);
        Assert.NotNull(s.Host.Results);
    }

    [Fact]
    public void A_players_turn_takes_their_commands_and_shows_a_refusal()
    {
        var s = new Session();
        s.ToBattle();
        var (player, screen) = WaitForTurn(s);

        // Nonsense is refused with the engine's reason, and the turn is still theirs.
        s.Send(player, "use", new { ability = "wizard.fireball", tile = new[] { 0, 0 } });
        var refused = s.Screen(player);
        Assert.Equal(TacticsGame.Screens.Turn, refused?.Screen);
        Assert.False(string.IsNullOrEmpty(refused?.Header));

        // A legal move from the choices applies.
        var moves = screen.Data.GetProperty("choices").GetProperty("moves");
        if (moves.GetArrayLength() > 0)
        {
            var path = moves[0].GetProperty("path");
            s.Send(player, "move", new { path });   // tiles are [x, y] on the wire, as the phone sends them back
            Assert.Contains(s.Game.Document.Battle!.Log[^1].Events, e => e is Moved);
        }

        s.Send(player, "endTurn");
        Assert.Contains(s.Game.Document.Battle!.Log.SelectMany(b => b.Events), e => e is TurnEnded);
    }

    [Fact]
    public void A_turn_that_times_out_is_finished_by_the_ai_with_the_badge()
    {
        var s = new Session();
        s.ToBattle();
        WaitForTurn(s);

        s.Harness.Advance(TimeSpan.FromSeconds(61));

        Assert.Contains(s.Game.Document.Battle!.Log.SelectMany(b => b.Events), e => e.AiFinished);
    }

    [Fact]
    public void The_tvs_shown_signal_moves_the_ai_on_and_stale_ones_are_ignored()
    {
        var s = new Session();
        s.ToBattle(everyoneAway: true);
        var seq = s.Game.Document.Battle!.Log[^1].Seq;

        s.Harness.HostSignal("shown", new { seq = seq - 1 });
        Assert.Equal(seq, s.Game.Document.Battle!.Log[^1].Seq);

        s.Harness.HostSignal("shown", new { seq });
        Assert.True(s.Game.Document.Battle!.Log[^1].Seq > seq);
    }

    [Fact]
    public void A_battle_resumed_after_a_restart_ends_as_if_it_had_never_stopped()
    {
        var straight = new Session();
        straight.ToBattle(everyoneAway: true);
        straight.PlayOut();

        var interrupted = new Session();
        interrupted.ToBattle(everyoneAway: true);
        for (var i = 0; i < 40; i++)
            interrupted.Harness.Advance(TimeSpan.FromSeconds(7));
        Assert.Equal(TacticsPhase.Battle, interrupted.Game.Phase);

        // The server restarts: a new game resumes from the saved document, with nobody connected.
        var resumed = new Session(document: interrupted.Lease.Document);
        resumed.Harness.Resume(resumed.Lease);
        resumed.PlayOut();

        Assert.Equal(TacticsPhase.Results, resumed.Game.Phase);
        Assert.Equal(straight.Lease.Document.GetRawText(), resumed.Lease.Document.GetRawText());
    }

    [Fact]
    public void A_battle_saved_under_other_rules_starts_again()
    {
        var s = new Session();
        s.ToBattle(everyoneAway: true);
        for (var i = 0; i < 20; i++)
            s.Harness.Advance(TimeSpan.FromSeconds(7));
        var doc = TacticsDocument.Read(s.Lease.Document);
        var stale = doc with { Battle = doc.Battle! with { State = doc.Battle.State with { ContentHash = "old" } } };

        var resumed = new Session(document: stale.ToJson());
        resumed.Harness.Resume(resumed.Lease);

        Assert.NotNull(resumed.Host.Notice);
        Assert.Equal(Rules.Hash, resumed.Game.Document.Battle!.State.ContentHash);
        Assert.Equal(1, resumed.Game.Document.Battle.Log[0].Seq);
    }

    [Fact]
    public void A_seat_that_drops_mid_turn_is_finished_by_the_ai_and_gets_its_character_back()
    {
        var s = new Session();
        s.ToBattle();
        var (player, _) = WaitForTurn(s);
        var unit = s.Game.Document.Battle!.State.Active!.Value;

        s.Harness.SetConnected(player, false);
        Assert.IsType<AiControlled>(s.Game.Document.Battle!.State.Units[unit].Controller);
        for (var i = 0; i < 20 && s.Game.Document.Battle!.State.Active == unit; i++)
            s.Harness.Advance(TimeSpan.FromSeconds(7));
        Assert.NotEqual(unit, s.Game.Document.Battle!.State.Active);

        s.Harness.SetConnected(player, true);
        Assert.Equal(new PlayerControlled(s.Profiles[player]!.Value), s.Game.Document.Battle!.State.Units[unit].Controller);
    }

    [Fact]
    public void A_reaction_left_unanswered_is_decided_by_the_ai_rule()
    {
        var s = new Session(seed: 3);
        s.ToBattle();
        for (var step = 0; step < 5_000; step++)
        {
            for (var p = 0; p < 4; p++)
            {
                if (s.Screen(p) is { Screen: TacticsGame.Screens.React } react)
                {
                    Assert.NotNull(react.DeadlineUtc);
                    var pending = s.Game.Document.Battle!.State.Pending;
                    s.Harness.Advance(TimeSpan.FromSeconds(11));
                    Assert.NotSame(pending, s.Game.Document.Battle!.State.Pending);
                    return;
                }
                if (s.Screen(p) is { Screen: TacticsGame.Screens.Turn })
                    s.Send(p, "endTurn");
            }
            s.Harness.Advance(TimeSpan.FromSeconds(7));
        }
        Assert.Fail("No reaction was offered to a player.");
    }

    [Fact]
    public void Malformed_input_from_a_phone_is_ignored_not_fatal()
    {
        var s = new Session();
        s.Harness.StartCampaign(s.Lease);

        s.Send(1, "pickClass", new { @class = 1 });
        s.Send(1, "pickClass", new[] { 1, 2 });
        s.Harness.Input(1, 42);
        s.Harness.Input(1, new { data = "no action" });

        Assert.Equal(TacticsPhase.Muster, s.Game.Phase);
        Assert.Equal("Pick one of the classes.", s.Screen(1)?.Header);
        s.Send(1, "pickClass", new { @class = "cleric" });
        Assert.NotNull(s.Game.Document.For(s.Profiles[1]!.Value));
    }

    [Fact]
    public void After_a_restart_the_first_player_back_can_continue_without_the_others()
    {
        var s = new Session();
        s.ToBattle();
        for (var i = 0; i < 10; i++)
            s.Harness.Advance(TimeSpan.FromSeconds(7));

        var resumed = new Session(document: s.Lease.Document);
        resumed.Harness.Resume(resumed.Lease);
        Assert.Equal(TacticsPhase.Resuming, resumed.Game.Phase);

        // The platform's VIP (player 1) never comes back; player 3 does, and may go on without the rest.
        resumed.Harness.SetConnected(2, true);
        Assert.IsType<ButtonsView>(resumed.Harness.PlayerView(2));
        resumed.Send(2, "continue");
        Assert.Equal(TacticsPhase.Battle, resumed.Game.Phase);
    }

    [Fact]
    public void Results_name_each_member_however_the_room_is_seated()
    {
        var s = new Session();
        s.ToBattle(everyoneAway: true);
        for (var i = 0; i < 10; i++)
            s.Harness.Advance(TimeSpan.FromSeconds(7));

        // Another night, in another order: the saved battle carries on and finishes.
        var other = new Session(document: s.Lease.Document, reseated: true);
        other.Harness.StartCampaign(other.Lease);
        for (var p = 0; p < 4; p++)
            other.Harness.SetConnected(p, false);
        other.PlayOut();

        Assert.Equal(TacticsPhase.Results, other.Game.Phase);
        var lines = other.Host.Results!.Value;
        // Party order is the first night's: member 1 first, who now sits in seat 4 (Player4).
        Assert.Equal(["Player4", "Player3", "Player2", "Player1"], lines.Select(l => l.Name));
    }

    /// <summary>Advances until some player's phone shows their turn; returns who, and the screen.</summary>
    private static (int Player, CustomView Screen) WaitForTurn(Session s)
    {
        for (var step = 0; step < 2_000; step++)
        {
            for (var p = 0; p < 4; p++)
                if (s.Screen(p) is { Screen: TacticsGame.Screens.Turn } screen)
                    return (p, screen);
            s.Harness.Advance(TimeSpan.FromSeconds(1));
        }
        throw new Xunit.Sdk.XunitException("No player's turn came up.");
    }
}
