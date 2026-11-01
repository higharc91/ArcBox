using System.Collections.Immutable;
using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Campaigns;
using ArcBox.Sdk.Testing;
using Tactics.Logic;
using static Tactics.Game.Tests.TacticsGameTests;

namespace Tactics.Game.Tests;

/// <summary>A fight that starts where the party is (overworld 5c, Task 3): stepping into the stamp of the battle beat on offer.</summary>
public sealed class StageBattleTests
{
    // ---- Fixtures, shared with StageEndTests ---------------------------------------------------------------------------

    internal const string Ford = "ashen_ford";

    /// <summary>The base pack's lore, as the server loads it.</summary>
    internal static readonly Lazy<LoreBook> Lore = new(() => LoreBook.Load(ExplorationTests.ContentRules()));

    /// <summary>The ford chosen by force: the assault is the beat on offer.</summary>
    internal static readonly TacticsSeed Force = new(["ch1_skarrow_slain"], ["ch1_mayors_request", "ch1_the_ford_choice"]);

    /// <summary>The parley: the crossing is the beat on offer, on the same map.</summary>
    internal static readonly TacticsSeed Parley = new(["ch1_skarrow_spared"], ["ch1_mayors_request", "ch1_the_ford_choice"]);

    internal static Terrain Ground => Rules.GroundFor(Ford)!.Terrain;

    internal static RegionDef Region => Rules.RegionFor(Ford)!.Def;

    /// <summary>Where River Crossing's map, which both ford fights use, stands in Ashen Ford.</summary>
    internal static BattleStage Stage => BattleStage.Of(Rules.RegionFor(Ford)!, Rules.MapFor("river_crossing")!)!.Value;

    internal static HashSet<Tile> Reached => WorldWalk.Reach(Ground, Region.Spawns.Start, ClassDef.DefaultJump);

    internal static string Name(WalkDir dir) => dir.ToString().ToLowerInvariant();

    /// <summary>
    /// A tile just west of the stamp the party can reach, and the way into the stamp from it: the step lands on the stamp's first column.
    /// <paramref name="back"/> tiles further west, with the step in still along +x (screen south-east).
    /// </summary>
    internal static (Tile From, WalkDir Dir) Entry(int back = 0)
    {
        var (stage, ground, reached) = (Stage, Ground, Reached);
        for (var dy = 0; dy < stage.Depth; dy++)
            foreach (var y in new[] { stage.Origin.Y + stage.Depth / 2 + dy, stage.Origin.Y + stage.Depth / 2 - dy })
            {
                var path = Enumerable.Range(0, back + 2).Select(i => new Tile(stage.Origin.X - 1 - back + i, y)).ToArray();
                var steps = path.Zip(path.Skip(1));
                if (reached.Contains(path[0]) && steps.All(s => WorldWalk.Next(ground, s.First, WalkDir.SE, ClassDef.DefaultJump, null)?.To == s.Second))
                    return (path[0], WalkDir.SE);
            }
        throw new InvalidOperationException("No way into the stamp from the west.");
    }

    /// <summary>A tile inside the stamp the party can reach, with a step east to another tile inside it.</summary>
    internal static (Tile From, WalkDir Dir) InsideStep()
    {
        var (stage, ground, reached) = (Stage, Ground, Reached);
        var from = reached.Where(stage.Holds).Order().First(t =>
            WorldWalk.Next(ground, t, WalkDir.SE, ClassDef.DefaultJump, null) is { } m && stage.Holds(m.To) && !m.Diagonal);
        return (from, WalkDir.SE);
    }

    /// <summary>A game of the base pack with its lore, seeded with <paramref name="seed"/> on <paramref name="at"/>.</summary>
    internal static TacticsGame WithLore(TacticsSeed? seed, Tile? at = null, RulesPack? rules = null, TacticsTiming? timing = null, bool battleList = false)
        => new(rules ?? Rules, timing, Lore.Value, seed is null ? null : seed with { At = at }, battleList);

    /// <summary>A session walking, with the party at <paramref name="at"/> and <paramref name="seed"/>'s story.</summary>
    internal static Session Walking(TacticsSeed? seed, Tile at, int harnessSeed = 1, RulesPack? rules = null, bool battleList = false)
    {
        var s = new Session(seed: harnessSeed, game: WithLore(seed, at, rules, battleList: battleList));
        s.ToWalking();
        Assert.Equal(at, s.Host.Explore!.At);
        return s;
    }

    /// <summary>The seat of whoever leads.</summary>
    internal static int Lead(Session s) => s.Harness.Players.ToList().FindIndex(p => p.Id == s.Game.Leader!.Id);

    /// <summary>Walks into the stamp from just outside it, as the leader.</summary>
    internal static Session Fighting(TacticsSeed seed, int harnessSeed = 1, RulesPack? rules = null)
    {
        var (from, dir) = Entry();
        var s = Walking(seed, from, harnessSeed, rules);
        s.Send(Lead(s), "walk", new { dir = Name(dir) });
        Assert.Equal(TacticsPhase.Battle, s.Game.Phase);
        return s;
    }

    internal static readonly string[] WalkingTimers = [TacticsGame.Timers.Step, TacticsGame.Timers.Lead, TacticsGame.Timers.Save];

    private static string Json(object view) => JsonSerializer.Serialize(view, ArcJson.Options);

    // ---- Starting -----------------------------------------------------------------------------------------------------

    [Fact]
    public void Stepping_into_an_armed_stamp_starts_its_fight()
    {
        var s = Fighting(Force);

        var battle = s.Host.Battle!;
        Assert.Equal("ford_assault", battle.Encounter);
        Assert.Equal("river_crossing", battle.Map);
        Assert.Equal("ch1_ford_assault", s.Game.Document.Battle!.Beat);
        Assert.Equal("ch1_ford_assault", TacticsDocument.Read(s.Lease.Document).Battle!.Beat);
    }

    [Fact]
    public void The_party_stops_one_tile_short()
    {
        var (from, dir) = Entry();
        var s = Walking(Force, from);
        var seq = s.Host.Explore!.Seq;

        s.Send(Lead(s), "walk", new { dir = Name(dir) });

        var party = s.Host.Explore!;
        Assert.Equal(from, party.At);
        Assert.Equal(seq, party.Seq);
        Assert.Equal(new Tile(from.X + 1, from.Y), party.Facing);     // it faces the stamp it was stepping into
        Assert.False(party.Held);
        Assert.False(party.Blocked);
        Assert.Equal(from, TacticsDocument.Read(s.Lease.Document).World!.At);
    }

    /// <summary>A walk held from a few tiles back starts the fight on the step in, not before, and stops one short.</summary>
    [Fact]
    public void A_held_walk_starts_the_fight_on_the_step_in()
    {
        var (from, dir) = Entry(back: 3);
        var s = Walking(Force, from);
        var lead = Lead(s);

        s.Send(lead, "walk", new { dir = Name(dir) });
        for (var i = 0; i < 40 && s.Game.Phase == TacticsPhase.Exploration; i++)
        {
            s.Harness.Advance(TimeSpan.FromMilliseconds(250));
            if (s.Game.Phase == TacticsPhase.Exploration)
                s.Send(lead, "walk", new { dir = Name(dir) });
        }

        Assert.Equal(TacticsPhase.Battle, s.Game.Phase);
        Assert.Equal(new Tile(Stage.Origin.X - 1, from.Y), s.Host.Explore!.At);
        Assert.Empty(s.Harness.ActiveTimers.Intersect(WalkingTimers));
    }

    /// <summary>A quick tap during a step is latched, and goes when that step ends: into the stamp, it starts the fight from the timer.</summary>
    [Fact]
    public void A_latched_tap_into_the_stamp_starts_the_fight()
    {
        var (from, dir) = Entry(back: 1);
        var s = Walking(Force, from);
        var lead = Lead(s);

        s.Send(lead, "walk", new { dir = Name(dir) });          // the step to the edge
        s.Send(lead, "walk", new { dir = (string?)null });
        s.Harness.Advance(TimeSpan.FromMilliseconds(50));
        s.Send(lead, "walk", new { dir = Name(dir) });          // a tap during it, let go before it ends
        s.Send(lead, "walk", new { dir = (string?)null });
        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);

        s.Harness.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(TacticsPhase.Battle, s.Game.Phase);
        Assert.Equal(new Tile(Stage.Origin.X - 1, from.Y), s.Host.Explore!.At);
        Assert.Empty(s.Harness.ActiveTimers.Intersect(WalkingTimers));
    }

    [Fact]
    public void The_arc_beat_decides_between_two_encounters_on_a_map()
    {
        var assault = Fighting(Force);
        var crossing = Fighting(Parley);

        Assert.Equal(("ford_assault", "ch1_ford_assault"), (assault.Host.Battle!.Encounter, assault.Game.Document.Battle!.Beat));
        Assert.Equal(("river_crossing", "ch1_river_crossing"), (crossing.Host.Battle!.Encounter, crossing.Game.Document.Battle!.Beat));
        Assert.Equal(assault.Host.Battle.Map, crossing.Host.Battle.Map);
        Assert.Equal(assault.Host.Battle.Stage!.Origin, crossing.Host.Battle.Stage!.Origin);
    }

    public static TheoryData<string> Unarmed() => new() { "new campaign", "no lore", "assault done" };

    /// <summary>
    /// The stamp is ground like any other unless its fight is the beat on offer: in a new campaign that's the mayor's request, a game with
    /// no lore offers none, and once the assault is done it's another beat.
    /// </summary>
    [Theory]
    [MemberData(nameof(Unarmed))]
    public void An_unarmed_stamp_is_ordinary_ground(string story)
    {
        var (from, dir) = Entry();
        var game = story switch
        {
            "new campaign" => WithLore(new TacticsSeed([], []), from),
            "no lore" => new TacticsGame(Rules, seed: Force with { At = from }),
            _ => WithLore(Force with { Beats = [.. Force.Beats, "ch1_ford_assault"] }, from),
        };
        var s = new Session(game: game);
        s.ToWalking();

        s.Send(Lead(s), "walk", new { dir = Name(dir) });

        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
        Assert.Equal(new Tile(from.X + 1, from.Y), s.Host.Explore!.At);
        Assert.Null(s.Game.Document.Battle);
    }

    [Fact]
    public void Walking_inside_an_armed_stamp_doesnt_start_it()
    {
        var (from, dir) = InsideStep();
        var s = Walking(Force, from);

        s.Send(Lead(s), "walk", new { dir = Name(dir) });

        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
        Assert.NotEqual(from, s.Host.Explore!.At);
        Assert.True(Stage.Holds(s.Host.Explore.At));
    }

    // ---- Leaving the walk behind --------------------------------------------------------------------------------------

    [Fact]
    public void No_walking_timer_survives_the_start()
    {
        var (from, dir) = Entry(back: 1);
        var s = Walking(Force, from);
        var lead = Lead(s);
        s.Send(lead, "walk", new { dir = Name(dir) });          // a step to the edge, so there's a walk to save and a lease running
        s.Harness.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Superset(new HashSet<string>(WalkingTimers), new HashSet<string>(s.Harness.ActiveTimers));
        var edge = new Tile(from.X + 1, from.Y);
        Assert.Equal(edge, s.Host.Explore!.At);

        for (var i = 0; i < 10 && s.Game.Phase == TacticsPhase.Exploration; i++)
        {
            s.Send(lead, "walk", new { dir = Name(dir) });      // the pad's ping, holding it
            s.Harness.Advance(TimeSpan.FromMilliseconds(100));
        }

        Assert.Equal(TacticsPhase.Battle, s.Game.Phase);
        Assert.Empty(s.Harness.ActiveTimers.Intersect(WalkingTimers));
        Assert.Equal(edge, TacticsDocument.Read(s.Lease.Document).World!.At);     // the place is saved at once, not by a timer
        s.Harness.Advance(TimeSpan.FromSeconds(20));                              // and nothing of the walk fires later
        Assert.Empty(s.Harness.ActiveTimers.Intersect(WalkingTimers));
        Assert.Equal(edge, s.Host.Explore!.At);
    }

    /// <summary>The leader's phone has no pad in a battle, so it sends nothing for a whole fight: that's no reason to take the lead away.</summary>
    [Fact]
    public void The_lead_doesnt_change_during_a_battle()
    {
        var s = Fighting(Force);
        var leader = s.Game.Leader;

        for (var i = 0; i < 12 && s.Game.Phase == TacticsPhase.Battle; i++)
        {
            s.Harness.Advance(TimeSpan.FromSeconds(7));
            Assert.Equal(leader, s.Game.Leader);
            Assert.DoesNotContain(TacticsGame.Timers.Lead, s.Harness.ActiveTimers);
        }
        Assert.Equal(leader!.Name, s.Host.Explore!.Leader);
    }

    [Fact]
    public void A_late_walk_in_battle_is_ignored_with_no_toast()
    {
        var s = Fighting(Force);
        var lead = Lead(s);
        string[] Phones() => [.. Enumerable.Range(0, 4).Select(i => Json(s.Harness.PlayerView(i)))];
        var (phones, host) = (Phones(), Json(s.Host));

        foreach (var dir in new[] { "e", "e", "w", null, "n", null })
            s.Send(lead, "walk", new { dir });
        s.Send((lead + 1) % 4, "walk", new { dir = "e" });

        Assert.Equal(phones, Phones());
        Assert.Equal(host, Json(s.Host));
        Assert.Empty(s.Harness.ActiveTimers.Intersect(WalkingTimers));
    }

    /// <summary>
    /// The worst case of a late walk: from the player whose turn it is, where a battle command that doesn't parse would toast "That didn't
    /// make sense to the game".
    /// </summary>
    [Fact]
    public void A_late_walk_on_your_own_turn_raises_no_toast()
    {
        var (s, seat) = HeroFirst();
        var (turn, host) = (Json(s.Harness.PlayerView(seat)), Json(s.Host));

        s.Send(seat, "walk", new { dir = "se" });
        s.Send(seat, "walk", new { dir = (string?)null });

        Assert.Equal(turn, Json(s.Harness.PlayerView(seat)));
        Assert.Null(s.Screen(seat)!.Header);
        Assert.Equal(host, Json(s.Host));
    }

    /// <summary>A fight walked into whose first turn is a present player's, and that player's seat.</summary>
    private static (Session S, int Seat) HeroFirst()
    {
        for (var seed = 1; seed < 40; seed++)
        {
            var s = Fighting(Force, harnessSeed: seed);
            var state = s.Game.Document.Battle!.State;
            if (state.Active is { } active && state.Units[active].Controller is PlayerControlled { Owner: var owner })
                return (s, s.Harness.Players.ToList().FindIndex(p => p.Profile == owner));
        }
        throw new InvalidOperationException("No seed in 40 gave a hero the first turn.");
    }

    // ---- What the TV is told ------------------------------------------------------------------------------------------

    [Fact]
    public void The_host_view_has_the_stage_and_the_region()
    {
        var s = Fighting(Force);

        var view = s.Host;
        var stage = view.Battle!.Stage!;
        Assert.Equal(Ford, stage.Region);
        Assert.Equal(Stage.Origin, stage.Origin);
        Assert.Equal(Stage.Height, stage.Height);
        Assert.Equal(Ford, view.Explore!.Region);
        Assert.Equal([.. s.Game.Document.Battle!.Members.Select(m => m.Value)], stage.Heroes);
    }

    [Fact]
    public void Heroes_map_to_walkers_even_when_ai_played()
    {
        var (from, dir) = Entry();
        var s = Walking(Force, from);
        var lead = Lead(s);
        var away = (lead + 2) % 4;
        s.Harness.SetConnected(away, false);

        s.Send(lead, "walk", new { dir = Name(dir) });

        var view = s.Host;
        var members = s.Game.Document.Battle!.Members;
        var awayProfile = s.Harness.Players[away].Profile!.Value;
        var awayUnit = view.Battle!.Units.Single(u => u.Id == TacticsGame.UnitFor(members.IndexOf(awayProfile)));
        Assert.Null(awayUnit.Player);
        for (var i = 0; i < members.Length; i++)
        {
            Assert.Equal(members[i].Value, view.Battle.Stage!.Heroes[i]);
            Assert.Contains(view.Explore!.Party, w => w.Id == view.Battle.Stage.Heroes[i]);
        }
    }

    [Fact]
    public void An_absent_member_is_in_the_fight_with_a_character()
    {
        var (from, dir) = Entry();
        var s = new Session(game: WithLore(Force, from));
        s.Harness.StartCampaign(s.Lease);
        for (var i = 0; i < 3; i++)
            s.Send(i, "pickClass", new { @class = "fighter" });
        s.Harness.SetConnected(3, false);
        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
        var absent = s.Harness.Players[3].Profile!.Value;
        Assert.Null(s.Game.Document.For(absent));

        s.Send(Lead(s), "walk", new { dir = Name(dir) });

        Assert.Equal(TacticsPhase.Battle, s.Game.Phase);
        Assert.NotNull(s.Game.Document.For(absent));
        Assert.Contains(absent, s.Game.Document.Battle!.Members);
        Assert.Equal(4, s.Game.Document.Battle.Members.Length);
    }

    [Fact]
    public void A_small_party_gets_its_companions()
    {
        var (from, dir) = Entry();
        ProfileId?[] profiles = [new ProfileId("pr_a"), new ProfileId("pr_b")];
        var game = WithLore(Force, from);
        var h = new GameHarness<TacticsGame>(game, playerCount: 2, profiles: profiles);
        var info = new CampaignInfo("c1", "ABCD", TacticsGame.Id, "Embers", profiles[0]!.Value,
            [.. profiles.Select((p, i) => new CampaignMember(p!.Value, $"Player{i + 1}", CampaignRole.Player, DateTimeOffset.UnixEpoch))], 1);
        h.StartCampaign(new FakeCampaignLease(info, new TacticsCampaignRules(Rules).NewDocument(info)));
        h.Input(0, new { action = "pickClass", data = new { @class = "cleric" } });
        h.Input(1, new { action = "pickClass", data = new { @class = "fighter" } });

        h.Input(h.Players.ToList().FindIndex(p => p.Id == game.Leader!.Id), new { action = "walk", data = new { dir = Name(dir) } });

        Assert.Equal(TacticsPhase.Battle, game.Phase);
        Assert.Equal(Rules.Encounters["ford_assault"].Party.Min - 2, game.Document.Battle!.Companions.Length);
        Assert.Equal(2, game.Document.Battle.Members.Length);
    }

    // ---- The walk-in --------------------------------------------------------------------------------------------------

    /// <summary>Everyone's phone goes, so the AI plays every unit, and only the TV's pacing holds it.</summary>
    private static Session FightingWithNobodyHere()
    {
        var s = Fighting(Force);
        for (var i = 0; i < 4; i++)
            s.Harness.SetConnected(i, false);
        return s;
    }

    [Fact]
    public void The_opening_beat_holds_the_ai_until_shown()
    {
        var s = FightingWithNobodyHere();
        var opening = s.Host.Battle!.Beats.Single().Seq;

        s.Harness.Advance(TacticsTiming.Default.FormUp - TimeSpan.FromMilliseconds(1));
        Assert.Single(s.Host.Battle!.Beats);

        s.Harness.HostSignal("shown", new { seq = opening });
        Assert.True(s.Host.Battle!.Beats.Length > 1);
    }

    [Fact]
    public void The_opening_beat_holds_only_as_long_as_the_fallback()
    {
        var s = FightingWithNobodyHere();

        s.Harness.Advance(TacticsTiming.Default.FormUp);

        Assert.True(s.Host.Battle!.Beats.Length > 1);
    }

    /// <summary>A battle the list starts on a map the region has no stamp for plays as it always has: no stage, and no wait for a walk-in.</summary>
    [Fact]
    public void A_battle_on_an_unstamped_map_has_no_stage()
    {
        var unstamped = Rules.Encounters.Values.First(e => Rules.RegionFor(Ford)!.StampFor(e.Map) is null && e.Party.Max >= 4);
        var s = new Session(game: WithLore(null, battleList: true));
        s.ToWalking();
        for (var i = 1; i < 4; i++)
            s.Harness.SetConnected(i, false);
        s.Send(0, "battles");
        s.Send(0, "pickEncounter", new { encounter = unstamped.Id });
        Assert.Equal(TacticsPhase.Battle, s.Game.Phase);

        Assert.Null(s.Host.Battle!.Stage);
        Assert.Null(s.Game.Document.Battle!.Beat);

        // No walk-in to wait for: with nobody left to play, the AI goes at once, as it always has.
        s.Harness.SetConnected(0, false);
        Assert.True(s.Host.Battle!.Beats.Length > 1);
    }

    /// <summary>
    /// The review's case: a stand-in leads, the session's own leader is back inside the takeback window, and the stand-in walks into the
    /// stamp. Giving out characters as the fight starts settles the lead, which must not leave the lead's timer running into the fight.
    /// </summary>
    [Fact]
    public void A_stand_in_waiting_on_the_takeback_leaves_no_walking_timer_in_the_fight()
    {
        var (from, dir) = Entry();
        var s = new Session(game: WithLore(Force, from));
        s.Harness.StartCampaign(s.Lease);
        for (var i = 0; i < 3; i++)
            s.Send(i, "pickClass", new { @class = "fighter" });
        s.Harness.SetConnected(3, false);             // no character yet, so the fight gives one out
        var own = Lead(s);
        s.Harness.SetConnected(own, false);           // a stand-in takes the lead
        var standIn = Lead(s);
        Assert.NotEqual(own, standIn);
        s.Harness.SetConnected(own, true);            // back, but the stand-in keeps it for the takeback window
        s.Harness.Advance(TimeSpan.FromMilliseconds(500));
        Assert.Equal(standIn, Lead(s));

        s.Send(standIn, "walk", new { dir = Name(dir) });

        Assert.Equal(TacticsPhase.Battle, s.Game.Phase);
        Assert.Empty(s.Harness.ActiveTimers.Intersect(WalkingTimers));
        s.Harness.Advance(TimeSpan.FromSeconds(10));
        Assert.Empty(s.Harness.ActiveTimers.Intersect(WalkingTimers));
    }

    /// <summary>
    /// A player who acts during the walk-in asks the TV to show their beat, with a short wait of its own: the AI must still wait for the
    /// walk-in, until the TV shows it or <see cref="TacticsTiming.FormUp"/> is up, whichever comes first.
    /// </summary>
    [Fact]
    public void A_player_acting_during_the_walk_in_doesnt_cut_it_short()
    {
        var (s, seat) = HeroFirst();
        for (var i = 0; i < 4; i++)
            if (i != seat)
                s.Harness.SetConnected(i, false);     // everyone else is played by the AI, which only the hold keeps waiting
        s.Send(seat, "endTurn");
        var beats = s.Host.Battle!.Beats.Length;

        s.Harness.Advance(TacticsTiming.Default.FormUp - TimeSpan.FromMilliseconds(1));
        Assert.Equal(beats, s.Host.Battle!.Beats.Length);
        s.Harness.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(s.Host.Battle!.Beats.Length > beats);
    }

    [Fact]
    public void Once_the_walk_in_is_shown_the_ai_goes_on_the_usual_wait()
    {
        var (s, seat) = HeroFirst();
        for (var i = 0; i < 4; i++)
            if (i != seat)
                s.Harness.SetConnected(i, false);
        var opening = s.Host.Battle!.Beats[0].Seq;
        s.Send(seat, "endTurn");
        var beats = s.Host.Battle!.Beats.Length;

        var events = s.Host.Battle.Beats[^1].Events.Length;
        Assert.True(TacticsTiming.Default.Pace(events) < TacticsTiming.Default.FormUp);

        s.Harness.HostSignal("shown", new { seq = opening });      // the walk-in is over; the player's beat isn't shown yet
        Assert.Equal(beats, s.Host.Battle!.Beats.Length);
        s.Harness.Advance(TacticsTiming.Default.Pace(events));     // its own wait, not the rest of the walk-in's

        Assert.True(s.Host.Battle!.Beats.Length > beats);
    }

    /// <summary>Ashen Ford's assault for at most three: a party of four is too big, which the checks never let a real arc battle be.</summary>
    private static readonly Lazy<RulesPack> SmallAssault = new(() => ExplorationTests.EditedRules(rules =>
    {
        var path = Path.Combine(rules, "encounters", "ford_assault.json");
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        node["party"]!["min"] = 1;
        node["party"]!["max"] = 3;
        File.WriteAllText(path, node.ToJsonString());
    }));

    [Fact]
    public void A_party_too_big_for_the_fight_is_told_so_and_walks_on()
    {
        var (from, dir) = Entry();
        var s = Walking(Force, from, rules: SmallAssault.Value);

        s.Send(Lead(s), "walk", new { dir = Name(dir) });

        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
        Assert.Equal(TacticsGame.FightTooBig(SmallAssault.Value.Encounters["ford_assault"]), s.Host.Notice);
        Assert.NotEqual(TacticsGame.FightWontStart, s.Host.Notice);
        Assert.Equal(from, s.Host.Explore!.At);
        Assert.Contains(TacticsGame.Timers.Lead, s.Harness.ActiveTimers);
    }

    [Fact]
    public void A_fight_the_engine_wont_start_says_so_and_walks_on()
    {
        var (from, dir) = Entry();
        var s = Walking(Force, from);
        s.Game.StartEngine = (_, _, _) => throw new InvalidOperationException("forced");

        s.Send(Lead(s), "walk", new { dir = Name(dir) });

        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
        Assert.Equal(TacticsGame.FightWontStart, s.Host.Notice);
        Assert.Null(s.Game.Document.Battle);
    }

    /// <summary>The walk-in holds the AI, not the players: a hero whose turn comes first can act at once.</summary>
    [Fact]
    public void A_player_can_act_during_the_walk_in()
    {
        for (var seed = 1; seed < 40; seed++)
        {
            var s = Fighting(Force, harnessSeed: seed);
            var state = s.Game.Document.Battle!.State;
            if (state.Active is not { } active || state.Units[active].Controller is not PlayerControlled { Owner: var owner })
                continue;
            var seat = s.Harness.Players.ToList().FindIndex(p => p.Profile == owner);
            Assert.Equal(TacticsGame.Screens.Turn, s.Screen(seat)!.Screen);

            s.Send(seat, "endTurn");

            Assert.True(s.Host.Battle!.Beats.Length > 1);
            Assert.NotEqual(active, s.Game.Document.Battle!.State.Active);
            return;
        }
        Assert.Fail("No seed in 40 gave a hero the first turn.");
    }
}
