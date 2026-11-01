using System.Text.Json.Nodes;
using ArcBox.Sdk;
using Tactics.Logic;
using static Tactics.Game.Tests.StageBattleTests;
using static Tactics.Game.Tests.TacticsGameTests;

namespace Tactics.Game.Tests;

/// <summary>What happens after a fight in the world (overworld 5c, Task 4): where the party stands, what the story keeps, and trying again.</summary>
public sealed class StageEndTests
{
    // ---- Ending a fight on demand ------------------------------------------------------------------------------------

    /// <summary>
    /// Makes the next command the engine applies end the battle with <paramref name="outcome"/>, after <paramref name="edit"/> has moved
    /// whoever it likes: the end goes through the game's own path, from a real command.
    /// </summary>
    private static void EndOnNextCommand(Session s, BattleOutcome outcome, Func<BattleState, BattleState>? edit = null)
    {
        var engine = s.Game.ApplyEngine;
        s.Game.ApplyEngine = (state, command, rules, ai) =>
        {
            s.Game.ApplyEngine = engine;
            var r = engine(state, command, rules, ai);
            return r with { State = (edit?.Invoke(r.State) ?? r.State) with { Outcome = outcome } };
        };
    }

    /// <summary>Plays on, every player ending their turn and the TV never answering, until the battle is over.</summary>
    private static void PlayOn(Session s)
    {
        for (var i = 0; i < 2_000 && s.Game.Phase == TacticsPhase.Battle; i++)
        {
            for (var p = 0; p < 4; p++)
                if (s.Screen(p) is { Screen: TacticsGame.Screens.Turn })
                    s.Send(p, "endTurn");
                else if (s.Screen(p) is { Screen: TacticsGame.Screens.React })
                    s.Send(p, "react", new { choice = "pass" });
            s.Harness.Advance(TimeSpan.FromSeconds(7));
        }
        Assert.NotEqual(TacticsPhase.Battle, s.Game.Phase);
    }

    /// <summary>The hero of <paramref name="member"/>.</summary>
    private static UnitId HeroOf(Session s, ProfileId member) => TacticsGame.UnitFor(s.Game.Document.Battle!.Members.IndexOf(member));

    private static ProfileId LeaderOf(Session s) => s.Game.Leader!.Profile!.Value;

    private static Func<BattleState, BattleState> Put(UnitId unit, Tile at, LifeState life = LifeState.Up)
        => state => state with { Units = state.Units.SetItem(unit, state.Units[unit] with { Pos = at, Life = life, Hp = life == LifeState.Up ? Math.Max(1, state.Units[unit].Hp) : 0 }) };

    /// <summary>The fight ended with <paramref name="outcome"/> on the next command, after <paramref name="edit"/>, at Results.</summary>
    private static Session Ended(Session s, BattleOutcome outcome, Func<BattleState, BattleState>? edit = null)
    {
        EndOnNextCommand(s, outcome, edit);
        PlayOn(s);
        Assert.Equal(TacticsPhase.Results, s.Game.Phase);
        return s;
    }

    /// <summary>A battle tile on the stamp's west bank that the world has walkable and reached, away from the party's spawn.</summary>
    private static readonly Tile WestBank = new(3, 3);

    private static Tile World(Tile battle) => Stage.ToWorld(battle);

    private static WorldSave Saved(Session s) => TacticsDocument.Read(s.Lease.Document).World!;

    /// <summary>The rule a resting place must keep: a tile the party could have walked to, outside the stamp that's armed.</summary>
    private static void Legal(Session s, Tile at, bool stampArmed)
    {
        Assert.True(Ground.Walkable(at), $"{at} can't be stood on");
        Assert.Contains(at, Reached);
        if (stampArmed)
            Assert.False(Stage.Holds(at), $"{at} is inside the armed stamp");
    }

    // ---- A win ------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_win_resumes_on_the_leaders_battle_tile()
    {
        var s = Fighting(Force);
        var epoch = s.Host.Explore!.Epoch;

        Ended(s, BattleOutcome.Won, Put(HeroOf(s, LeaderOf(s)), WestBank));

        var party = s.Host.Explore!;
        Assert.Equal(World(WestBank), party.At);
        Assert.Equal(epoch + 1, party.Epoch);
        Assert.Empty(party.Legs);
        Assert.Equal(TacticsGame.FellIn, party.Arrived);
        Assert.Equal(World(WestBank), Saved(s).At);
    }

    /// <summary>The leader's tile wins over the first hero's in member order: here the leader isn't that hero.</summary>
    [Fact]
    public void A_win_follows_the_leader_even_when_others_come_first()
    {
        var (from, dir) = Entry();
        var s = Walking(Force, from);
        s.Harness.SetConnected(0, false);        // the first member's phone goes, so the second leads
        var leader = LeaderOf(s);
        var first = s.Harness.Players[0].Profile!.Value;
        Assert.NotEqual(first, leader);
        Assert.True(string.CompareOrdinal(first.Value, leader.Value) < 0);
        s.Send(Lead(s), "walk", new { dir = Name(dir) });
        Assert.Equal(TacticsPhase.Battle, s.Game.Phase);

        Ended(s, BattleOutcome.Won, state => Put(HeroOf(s, leader), WestBank)(Put(HeroOf(s, first), new Tile(2, 2))(state)));

        Assert.Equal(World(WestBank), s.Host.Explore!.At);
    }

    [Fact]
    public void A_win_with_the_leader_down_uses_the_first_hero_up()
    {
        var s = Fighting(Force);
        var leader = LeaderOf(s);
        var members = s.Game.Document.Battle!.Members.OrderBy(m => m.Value, StringComparer.Ordinal).ToArray();
        var first = members.First(m => m != leader);
        var tiles = new[] { new Tile(2, 2), new Tile(3, 4), new Tile(4, 2), new Tile(4, 4) };

        Ended(s, BattleOutcome.Won, state =>
        {
            for (var i = 0; i < members.Length; i++)
                state = Put(HeroOf(s, members[i]), tiles[i], members[i] == leader ? LifeState.Downed : LifeState.Up)(state);
            return state;
        });

        Assert.Equal(World(tiles[Array.IndexOf(members, first)]), s.Host.Explore!.At);
    }

    /// <summary>A leader in the river (swimming, as the engine allows after a revive) comes ashore at the nearest tile it can stand on.</summary>
    [Fact]
    public void A_win_on_a_tile_that_cant_be_stood_on_finds_one_that_can()
    {
        var s = Fighting(Force);
        var river = new Tile(6, 2);
        Assert.False(Ground.Walkable(World(river)));

        Ended(s, BattleOutcome.Won, Put(HeroOf(s, LeaderOf(s)), river));

        Assert.Equal(World(new Tile(5, 2)), s.Host.Explore!.At);
        Legal(s, s.Host.Explore.At, stampArmed: false);
    }

    /// <summary>
    /// Ashen Ford with the ford washed out, in the world and on the map alike, so the far bank can't be walked to from the start, and the
    /// beats over there moved to this side. The nearest bank to a swimmer in the far channel is the far one.
    /// </summary>
    private static readonly Lazy<RulesPack> NoFord = new(() => ExplorationTests.EditedRules(rules =>
    {
        var mapPath = Path.Combine(rules, "maps", "river_crossing.json");
        var map = JsonNode.Parse(File.ReadAllText(mapPath))!;
        var width = map["size"]![0]!.GetValue<int>();
        var tiles = Convert.FromBase64String(map["tiles"]!.GetValue<string>());
        var stage = Stage;
        var regionPath = Path.Combine(rules, "world", "ashen_ford.json");
        var region = JsonNode.Parse(File.ReadAllText(regionPath))!;
        byte[] Chunk(Tile at, out JsonNode chunk)
        {
            var (cx, cy) = (at.X / WorldRegion.ChunkSize, at.Y / WorldRegion.ChunkSize);
            chunk = region["chunks"]!.AsArray().Single(c => c!["x"]!.GetValue<int>() == cx && c["y"]!.GetValue<int>() == cy)!;
            return Convert.FromBase64String(chunk["tiles"]!.GetValue<string>());
        }
        int WorldIndex(Tile at) => (at.Y % WorldRegion.ChunkSize * WorldRegion.ChunkSize + at.X % WorldRegion.ChunkSize) * 3;

        var deep = new Tile(6, 6);     // the river beside the ford, on the map
        foreach (var ford in map["zones"]!["ford"]!.AsArray().Select(t => new Tile(t![0]!.GetValue<int>(), t[1]!.GetValue<int>())))
        {
            Array.Copy(tiles, (deep.Y * width + deep.X) * 3, tiles, (ford.Y * width + ford.X) * 3, 3);
            var source = Chunk(stage.ToWorld(deep), out _);
            var bytes = Chunk(stage.ToWorld(ford), out var chunk);
            Array.Copy(source, WorldIndex(stage.ToWorld(deep)), bytes, WorldIndex(stage.ToWorld(ford)), 3);
            chunk["tiles"] = Convert.ToBase64String(bytes);
        }
        map["tiles"] = Convert.ToBase64String(tiles);
        File.WriteAllText(mapPath, map.ToJsonString());

        var hither = region["spawns"]!["recovery"]!.DeepClone();
        foreach (var beat in region["beats"]!.AsArray())
            if (beat!["at"]![0]!.GetValue<int>() >= stage.Origin.X + 8)
                beat["at"] = hither.DeepClone();
        File.WriteAllText(regionPath, region.ToJsonString());
    }));

    /// <summary>
    /// The reviewers' case: a win in the ford with the leader swimming. The nearest tile it could stand on is the far bank, which the
    /// party could never walk back from; it must come ashore on the near one instead.
    /// </summary>
    [Fact]
    public void A_win_by_a_swimmer_never_strands_the_party_on_the_far_bank()
    {
        var rules = NoFord.Value;
        var ground = rules.GroundFor(Ford)!.Terrain;
        var reached = WorldWalk.Reach(ground, rules.RegionFor(Ford)!.Def.Spawns.Start, ClassDef.DefaultJump);
        var (swimming, farBank, nearBank) = (new Tile(7, 7), new Tile(8, 7), new Tile(5, 7));
        Assert.True(ground.Walkable(World(farBank)));
        Assert.DoesNotContain(World(farBank), reached);                 // the far bank is cut off
        Assert.Equal(World(farBank), BattleStage.Standable(ground, World(swimming), []));     // and walkability alone would pick it
        var s = Fighting(Force, rules: rules);

        Ended(s, BattleOutcome.Won, Put(HeroOf(s, LeaderOf(s)), swimming));

        Assert.Equal(World(nearBank), s.Host.Explore!.At);
        Assert.Contains(s.Host.Explore.At, reached);
        s.Send(0, "next");
        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
    }

    [Fact]
    public void A_win_marks_the_beat_done_and_merges_its_flags()
    {
        var s = Fighting(Force);

        Ended(s, BattleOutcome.Won, state => state with { Flags = state.Flags.Add("bridge_held") });

        var world = Saved(s);
        Assert.Equal([.. Force.Beats, "ch1_ford_assault"], world.Beats);
        Assert.Equal(["ch1_skarrow_slain", "bridge_held"], world.Flags);
        Assert.Contains("ford_assault", TacticsDocument.Read(s.Lease.Document).Completed);
    }

    [Fact]
    public void A_stamp_won_is_ordinary_ground()
    {
        var (from, dir) = Entry();
        var s = Fighting(Force);
        Ended(s, BattleOutcome.Won, Put(HeroOf(s, LeaderOf(s)), WestBank));
        s.Send(0, "next");
        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);

        // Out of the stamp and in again, at the place the fight began.
        var doc = TacticsDocument.Read(s.Lease.Document);
        var again = new Session(document: (doc with { World = doc.World! with { At = from } }).ToJson(), game: WithLore(null));
        again.Harness.StartCampaign(again.Lease);
        again.Send(Lead(again), "walk", new { dir = Name(dir) });

        Assert.Equal(TacticsPhase.Exploration, again.Game.Phase);
        Assert.Equal(new Tile(from.X + 1, from.Y), again.Host.Explore!.At);
    }

    /// <summary>
    /// A story where winning one fight puts the next on offer in the same place, so the stamp is armed again the moment the party lands:
    /// it mustn't land inside it, or its first step would start the next fight with no way to walk out first.
    /// </summary>
    [Fact]
    public void A_win_that_arms_the_stamp_again_lands_outside_it()
    {
        var dir = Directory.CreateTempSubdirectory("tactics-two-fights-");
        try
        {
            Directory.CreateDirectory(Path.Combine(dir.FullName, "lore"));
            File.WriteAllText(Path.Combine(dir.FullName, "lore", "pack.json"), """{ "files": ["arcs.json"] }""");
            File.WriteAllText(Path.Combine(dir.FullName, "lore", "arcs.json"), """
                { "arcs": [ { "id": "two", "chapter": 1, "region": "ashen_ford", "title": "Two fights", "beats": [
                  { "id": "first", "kind": "battle", "place": "ford_bridge", "encounter": "ford_assault" },
                  { "id": "second", "kind": "battle", "place": "ford_bridge", "encounter": "river_crossing" } ] } ] }
                """);
            var lore = LoreBook.Load(dir.FullName);
            var (from, entry) = Entry();
            var s = new Session(game: new TacticsGame(Rules, lore: lore, seed: new TacticsSeed([], [], At: from)));
            s.ToWalking();
            s.Send(Lead(s), "walk", new { dir = Name(entry) });
            Assert.Equal("first", s.Game.Document.Battle!.Beat);

            Ended(s, BattleOutcome.Won, Put(HeroOf(s, LeaderOf(s)), WestBank));

            Legal(s, s.Host.Explore!.At, stampArmed: true);
            s.Send(0, "next");
            var back = s.Host.Explore!.At;
            Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
            Assert.Equal(from, back);       // where the fight began: nearer tiles are in the stamp, and so is its party spawn
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    // ---- A loss -----------------------------------------------------------------------------------------------------

    /// <summary>
    /// A fight in the world the engine can't finish is called off: nothing won or lost, and the party back where it stood when it began.
    /// The board goes from around it, so the TV is told as after any fight: a new epoch, falling in.
    /// </summary>
    [Fact]
    public void A_fight_called_off_in_the_world_snaps_the_party_back()
    {
        var (from, _) = Entry();
        var s = Fighting(Force);
        var epoch = s.Host.Explore!.Epoch;
        s.Game.ResultEngine = (_, _) => throw new InvalidOperationException("forced");

        EndOnNextCommand(s, BattleOutcome.Won);
        PlayOn(s);

        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
        Assert.Equal(TacticsGame.BattleCalledOff, s.Host.Notice);
        var party = s.Host.Explore!;
        Assert.Equal((from, epoch + 1, TacticsGame.FellIn), (party.At, party.Epoch, party.Arrived));
        Assert.Empty(party.Legs);
        Assert.Equal(Force.Beats, Saved(s).Beats);
    }

    /// <summary>A fight on a plain board, called off, leaves the world as it was: there was nothing round the party to take away.</summary>
    [Fact]
    public void A_fight_called_off_on_a_plain_board_leaves_the_epoch()
    {
        var unstamped = Rules.Encounters.Values.First(e => Rules.RegionFor(Ford)!.StampFor(e.Map) is null && e.Party.Max >= 4);
        var s = FromTheList(unstamped.Id);
        var party = s.Host.Explore!;
        s.Game.ResultEngine = (_, _) => throw new InvalidOperationException("forced");

        EndThrough(s, BattleOutcome.Won);

        Assert.Equal((party.At, party.Epoch), (s.Host.Explore!.At, s.Host.Explore.Epoch));
        Assert.Null(s.Host.Explore.Arrived);
    }

    private static void EndThrough(Session s, BattleOutcome outcome)
    {
        EndOnNextCommand(s, outcome);
        PlayOn(s);
    }

    /// <summary>
    /// What <see cref="ExploreView.Arrived"/> said after the last fight in the world goes as soon as another fight starts, even one from
    /// the list, and doesn't come back when that one ends on a plain board.
    /// </summary>
    [Fact]
    public void An_arrival_doesnt_outlive_the_next_fight()
    {
        var unstamped = Rules.Encounters.Values.First(e => Rules.RegionFor(Ford)!.StampFor(e.Map) is null && e.Party.Max >= 4);
        var s = new Session(game: WithLore(Force, Entry().From, battleList: true));
        s.ToWalking();
        s.Send(Lead(s), "walk", new { dir = Name(Entry().Dir) });
        Ended(s, BattleOutcome.Lost);
        s.Send(0, "next");
        Assert.Equal(TacticsGame.Woke, s.Host.Explore!.Arrived);

        s.Send(0, "battles");
        s.Send(0, "pickEncounter", new { encounter = unstamped.Id });
        Assert.Null(s.Host.Explore!.Arrived);
        Ended(s, BattleOutcome.Won);
        Assert.Null(s.Host.Explore!.Arrived);
    }

    [Fact]
    public void A_loss_wakes_the_party_at_the_recovery_spawn_with_a_new_epoch()
    {
        var s = Fighting(Force);
        var epoch = s.Host.Explore!.Epoch;

        Ended(s, BattleOutcome.Lost);

        var party = s.Host.Explore!;
        Assert.Equal(Region.Spawns.Recovery, party.At);
        Assert.Equal(epoch + 1, party.Epoch);
        Assert.Empty(party.Legs);
        Assert.Equal(TacticsGame.Woke, party.Arrived);
        Assert.Equal(Region.Spawns.Recovery, Saved(s).At);
    }

    /// <summary>XP and injuries after a loss are exactly what the battle result says, as they were before the world.</summary>
    [Fact]
    public void A_loss_keeps_xp_and_injuries_as_they_are_today()
    {
        var s = Fighting(Force);
        var before = s.Game.Document.Members;
        var members = s.Game.Document.Battle!.Members;
        BattleResult? result = null;
        var engine = s.Game.ResultEngine;
        s.Game.ResultEngine = (state, rules) => result = engine(state, rules);

        Ended(s, BattleOutcome.Lost);

        Assert.NotNull(result);
        for (var i = 0; i < members.Length; i++)
        {
            var (was, now) = (before[members[i].Value], s.Game.Document.Members[members[i].Value]);
            Assert.Equal(was.Xp + result.Xp.GetValueOrDefault(TacticsGame.UnitFor(i)), now.Xp);
            Assert.Equal([.. was.Character.Injuries, .. result.Injuries.GetValueOrDefault(TacticsGame.UnitFor(i), [])], now.Character.Injuries);
        }
        Assert.DoesNotContain("ford_assault", s.Game.Document.Completed);
    }

    [Fact]
    public void A_loss_sets_no_flags_and_completes_no_beat()
    {
        var s = Fighting(Force);

        Ended(s, BattleOutcome.Lost, state => state with { Flags = state.Flags.Add("bridge_held") });

        Assert.Equal(Force.Flags, Saved(s).Flags);
        Assert.Equal(Force.Beats, Saved(s).Beats);
    }

    /// <summary>Lose, wake at the recovery spawn, walk back, and the same fight starts again, on the same map, from a new seed.</summary>
    [Fact]
    public void A_lost_fight_can_be_retried_by_walking_back()
    {
        var s = Fighting(Force);
        var first = s.Game.Document.Battle!;
        Ended(s, BattleOutcome.Lost);
        s.Send(0, "next");
        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);

        // Walk from the recovery spawn to the stamp, as a player would: along a path the party can take, into the stamp's first tile.
        var path = PathInto(Region.Spawns.Recovery);
        var lead = Lead(s);
        foreach (var dir in path)
        {
            Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
            s.Send(lead, "walk", new { dir = Name(dir) });
            s.Send(lead, "walk", new { dir = (string?)null });
            s.Harness.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.Equal(TacticsPhase.Battle, s.Game.Phase);
        var retry = s.Game.Document.Battle!;
        Assert.Equal((first.Encounter, first.Beat, first.State.MapId), (retry.Encounter, retry.Beat, retry.State.MapId));
        Assert.Equal(first.Members, retry.Members);
        Assert.Equal(s.Host.Battle!.Stage!.Origin, Stage.Origin);
    }

    [Fact]
    public void A_retry_has_a_new_seed()
    {
        var s = Fighting(Force);
        var seed = s.Game.Document.Battle!.Seed;
        Ended(s, BattleOutcome.Lost);
        s.Send(0, "next");
        var (from, dir) = Entry();
        var doc = TacticsDocument.Read(s.Lease.Document);
        var again = new Session(seed: 2, document: (doc with { World = doc.World! with { At = from } }).ToJson(), game: WithLore(null));
        again.Harness.StartCampaign(again.Lease);

        again.Send(Lead(again), "walk", new { dir = Name(dir) });

        Assert.Equal(("ford_assault", "ch1_ford_assault"), (again.Game.Document.Battle!.Encounter, again.Game.Document.Battle.Beat));
        Assert.NotEqual(seed, again.Game.Document.Battle.Seed);
    }

    /// <summary>
    /// The directions of a shortest walk from <paramref name="from"/> to the first step into the stamp, by <see cref="WorldWalk.Next"/>
    /// with straight steps only, never through the stamp before that.
    /// </summary>
    private static List<WalkDir> PathInto(Tile from)
    {
        var (ground, stage) = (Ground, Stage);
        WalkDir[] straight = [WalkDir.NE, WalkDir.SE, WalkDir.SW, WalkDir.NW];
        var came = new Dictionary<Tile, (Tile From, WalkDir Dir)>();
        var open = new Queue<Tile>([from]);
        var seen = new HashSet<Tile> { from };
        while (open.TryDequeue(out var t))
            foreach (var dir in straight)
                if (WorldWalk.Next(ground, t, dir, ClassDef.DefaultJump, null) is { Diagonal: false } m && seen.Add(m.To))
                {
                    came[m.To] = (t, dir);
                    if (stage.Holds(m.To))
                    {
                        var path = new List<WalkDir>();
                        for (var at = m.To; at != from; at = came[at].From)
                            path.Insert(0, came[at].Dir);
                        return path;
                    }
                    open.Enqueue(m.To);
                }
        throw new InvalidOperationException("No way to the stamp.");
    }

    // ---- Fleeing ----------------------------------------------------------------------------------------------------

    /// <summary>A flight leaves the beat open and wakes nobody: the party stands by the stamp, which is still armed, so outside it.</summary>
    [Fact]
    public void A_flight_doesnt_wake_the_party()
    {
        var (from, _) = Entry();
        var s = Fighting(Force);

        Ended(s, BattleOutcome.Fled, Put(HeroOf(s, LeaderOf(s)), WestBank));

        var party = s.Host.Explore!;
        Assert.Equal(TacticsGame.FellIn, party.Arrived);
        Assert.NotEqual(Region.Spawns.Recovery, party.At);
        Legal(s, party.At, stampArmed: true);
        Assert.Equal(from, party.At);
        Assert.Equal(Force.Beats, Saved(s).Beats);
    }

    /// <summary>A leader near the stamp's edge after a flight steps off it, to the nearest tile outside.</summary>
    [Fact]
    public void A_flight_from_the_edge_of_the_stamp_steps_just_outside_it()
    {
        var s = Fighting(Force);
        var edge = new Tile(0, 3);

        Ended(s, BattleOutcome.Fled, Put(HeroOf(s, LeaderOf(s)), edge));

        Assert.Equal(new Tile(World(edge).X - 1, World(edge).Y), s.Host.Explore!.At);
    }

    // ---- Restarts ---------------------------------------------------------------------------------------------------

    [Fact]
    public void A_restart_at_results_resumes_on_the_new_tile()
    {
        var s = Fighting(Force);
        Ended(s, BattleOutcome.Won, Put(HeroOf(s, LeaderOf(s)), WestBank));

        var resumed = new Session(document: s.Lease.Document, game: WithLore(null));
        resumed.Harness.Resume(resumed.Lease);

        Assert.Equal(TacticsPhase.Exploration, resumed.Game.Phase);
        Assert.Equal(World(WestBank), resumed.Host.Explore!.At);
        Assert.Null(resumed.Host.Notice);
        Assert.Contains("ch1_ford_assault", Saved(resumed).Beats);
    }

    [Fact]
    public void A_restart_mid_fight_keeps_the_beat()
    {
        var s = Fighting(Force);

        var resumed = new Session(document: s.Lease.Document, game: WithLore(null));
        resumed.Harness.Resume(resumed.Lease);
        for (var i = 0; i < 4; i++)
            resumed.Harness.SetConnected(i, true);

        Assert.Equal(TacticsPhase.Battle, resumed.Game.Phase);
        Assert.Equal("ch1_ford_assault", resumed.Game.Document.Battle!.Beat);
        Assert.NotNull(resumed.Host.Battle!.Stage);
        Ended(resumed, BattleOutcome.Won);
        Assert.Contains("ch1_ford_assault", Saved(resumed).Beats);
    }

    // ---- The battle list --------------------------------------------------------------------------------------------

    private static Session FromTheList(string encounter, TacticsSeed? seed = null, Tile? at = null)
    {
        var s = new Session(game: WithLore(seed ?? new TacticsSeed([], []), at ?? Region.Spawns.Start, battleList: true));
        s.ToWalking();
        s.Send(0, "battles");
        s.Send(0, "pickEncounter", new { encounter });
        Assert.Equal(TacticsPhase.Battle, s.Game.Phase);
        return s;
    }

    /// <summary>A beat's fight won from the list counts as won, but it isn't the beat: the story waits for it to be fought where it is.</summary>
    [Fact]
    public void A_list_fight_completes_no_beat()
    {
        var s = FromTheList("ford_assault", Force);
        Assert.Null(s.Game.Document.Battle!.Beat);

        Ended(s, BattleOutcome.Won, state => state with { Flags = state.Flags.Add("bridge_held") });

        Assert.Contains("ford_assault", TacticsDocument.Read(s.Lease.Document).Completed);
        Assert.Equal(Force.Beats, Saved(s).Beats);
        Assert.Equal(Force.Flags, Saved(s).Flags);
        Legal(s, s.Host.Explore!.At, stampArmed: true);     // the beat is still on offer, so the stamp is still armed
    }

    [Fact]
    public void A_list_fight_far_away_stands_at_its_stamp()
    {
        var s = FromTheList("river_crossing");
        var epoch = s.Host.Explore!.Epoch;
        Assert.Equal(Stage.Origin, s.Host.Battle!.Stage!.Origin);

        Ended(s, BattleOutcome.Won, Put(HeroOf(s, LeaderOf(s)), WestBank));

        Assert.Equal(World(WestBank), s.Host.Explore!.At);
        Assert.Equal(epoch + 1, s.Host.Explore.Epoch);
    }

    [Fact]
    public void A_list_fight_on_an_unstamped_map_keeps_the_tile_and_epoch()
    {
        var unstamped = Rules.Encounters.Values.First(e => Rules.RegionFor(Ford)!.StampFor(e.Map) is null && e.Party.Max >= 4);
        foreach (var outcome in new[] { BattleOutcome.Won, BattleOutcome.Lost })
        {
            var s = FromTheList(unstamped.Id);
            var party = s.Host.Explore!;

            Ended(s, outcome);

            Assert.Equal((party.At, party.Epoch, party.Seq), (s.Host.Explore!.At, s.Host.Explore.Epoch, s.Host.Explore.Seq));
            Assert.Null(s.Host.Explore.Arrived);
        }
    }

    // ---- Saved state and walking on ---------------------------------------------------------------------------------

    [Fact]
    public void A_version_1_battle_with_no_beat_still_reads()
    {
        var s = new Session();
        s.ToBattle(everyoneAway: true);
        var node = JsonNode.Parse(s.Lease.Document.GetRawText())!;
        Assert.Null(node["battle"]!["beat"]);           // a list fight writes none, as every battle before 5c

        var doc = TacticsDocument.Read(s.Lease.Document);

        Assert.Equal(1, node["version"]!.GetValue<int>());
        Assert.Null(doc.Battle!.Beat);
        var walked = Fighting(Force);
        Assert.Equal("ch1_ford_assault", JsonNode.Parse(walked.Lease.Document.GetRawText())!["battle"]!["beat"]!.GetValue<string>());
    }

    [Fact]
    public void The_party_can_walk_again_after_results()
    {
        var s = Fighting(Force);
        Ended(s, BattleOutcome.Won, Put(HeroOf(s, LeaderOf(s)), WestBank));
        var at = s.Host.Explore!.At;
        s.Send(0, "next");
        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
        var lead = Lead(s);
        Assert.True(s.Screen(lead)!.Data.GetProperty("leader").GetBoolean());
        Assert.Contains(TacticsGame.Timers.Lead, s.Harness.ActiveTimers);

        var dir = new[] { WalkDir.SE, WalkDir.NW, WalkDir.NE, WalkDir.SW }.First(d => WorldWalk.Next(Ground, at, d, ClassDef.DefaultJump, null) is not null);
        s.Send(lead, "walk", new { dir = Name(dir) });

        Assert.NotEqual(at, s.Host.Explore!.At);
        Assert.Single(s.Host.Explore.Legs);
        Assert.Null(s.Host.Explore.Arrived);
        s.Send(lead, "walk", new { dir = (string?)null });
        s.Harness.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(s.Host.Explore.At, Saved(s).At);
    }
}
