using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Campaigns;
using ArcBox.Sdk.Testing;
using Tactics.Logic;
using static Tactics.Game.Tests.TacticsGameTests;

namespace Tactics.Game.Tests;

/// <summary>
/// All of Chapter 1 (overworld 5d, Task 11), from a fresh campaign at the region's spawn to the chapter card and walking after it, on
/// either branch: the party walks to each "!" by the walking rule, taps Interact, plays every scene, fights each battle beat and goes on.
/// Nothing is seeded, so the order of the beats, the travel between places and every saved cursor are the game's own. The cases are
/// classes of their own, so xUnit runs them side by side.
/// </summary>
public abstract class ChapterOne
{
    private const string Ember = "ch1_the_ember";

    /// <summary>The scenes each branch plays, in order, and the battle between them.</summary>
    protected static readonly string[] ByForce =
        ["ch1_mayor", "ch1_ford", "ch1_dawn", "fight:ch1_ford_assault", "ch1_bridge_cost", TacticsGame.LootVoteId, "ch1_maud", "ch1_docks", "ch1_kindler", "ch1_four_more"];

    protected static readonly string[] ByParley =
    [
        "ch1_mayor", "ch1_ford", "ch1_parley", "ch1_young_ones", "fight:ch1_river_crossing", "ch1_road_east", TacticsGame.LootVoteId, "ch1_maud", "ch1_granary",
        "ch1_song", "ch1_docks", "ch1_kindler", "ch1_four_more",
    ];

    private static readonly string[] Classes = ["fighter", "cleric", "rogue", "wizard"];

    /// <summary>
    /// A campaign of <paramref name="players"/> members, played through its chapter by a party that answers as told. With
    /// <see cref="RestartEveryLine"/>, the server restarts as each new line starts, and everyone comes back.
    /// </summary>
    protected sealed class Play
    {
        private readonly int _players;
        private readonly LoreBook _lore;
        private readonly string? _branch;
        private readonly bool _answer;
        private int _toLose;

        private readonly TacticsSeed? _seed;

        public Play(int players = 4, string? branch = null, bool answer = true, LoreBook? lore = null, int lose = 0, TacticsSeed? seed = null)
        {
            (_players, _branch, _answer, _lore, _toLose, _seed) = (players, branch, answer, lore ?? StoryTests.Lore, lose, seed);
            Profiles = [.. Enumerable.Range(1, players).Select(i => (ProfileId?)new ProfileId($"pr_member{i}"))];
            var info = new CampaignInfo("c1", "ABCD", TacticsGame.Id, "Embers", Profiles[0]!.Value,
                [.. Profiles.Select((p, i) => new CampaignMember(p!.Value, $"Player{i + 1}", i == 0 ? CampaignRole.Owner : CampaignRole.Player,
                    DateTimeOffset.UnixEpoch))], 1);
            Lease = new FakeCampaignLease(info, new TacticsCampaignRules(Rules).NewDocument(info));
            (Game, Harness) = Open(seed: 1);
            Harness.StartCampaign(Lease);
        }

        public IReadOnlyList<ProfileId?> Profiles { get; }
        public FakeCampaignLease Lease { get; private set; }
        public TacticsGame Game { get; private set; }
        public GameHarness<TacticsGame> Harness { get; private set; }
        public bool RestartEveryLine { get; init; }

        /// <summary>
        /// Restart at every moment of every scene, once each: a new line, an open question, a tie-break, the pick of who rolls, a roll
        /// before its reveal, and the gap between scenes. A question still being answered reopens after a restart (votes aren't saved), so
        /// each moment is restarted at only once, and the party answers it the second time. A question already decided for a check never
        /// reopens: a restart while the party picks who rolls comes back to the pick.
        /// </summary>
        public bool RestartEveryMoment { get; init; }

        /// <summary>How many restarts came back to the pick of who rolls, as <see cref="RestartEveryMoment"/> checks.</summary>
        public int AfterPick { get; private set; }

        /// <summary>
        /// Party votes tie: the leader holds back and two others split between the branch's option and another, so the leader breaks the
        /// tie once the vote's clock runs out.
        /// </summary>
        public bool Ties { get; init; }

        /// <summary>The moments a restart hit, by state: <c>lines</c>, <c>asking</c>, <c>tieBreak</c>, <c>pickRoller</c>, <c>rolled</c>, <c>gap</c>.</summary>
        public Dictionary<string, int> RestartedAt { get; } = [];

        /// <summary>Each roll as it was made, and as each restart showed it again: never made twice.</summary>
        public List<(string Line, int Die)> Rolls { get; } = [];

        private readonly HashSet<string> _moments = [];

        /// <summary>The scenes as they started, and each fight as <c>fight:&lt;beat&gt;</c>, in order.</summary>
        public List<string> Played { get; } = [];

        /// <summary>Each line as it came up, once (a line shown again after a restart isn't a new one).</summary>
        public List<string> Lines { get; } = [];

        public List<BattleOutcome> Fights { get; } = [];

        /// <summary>Who stood in the world (<see cref="ExploreView.Cast"/>) by <c>scene/node</c>, by character id: the same all through a node.</summary>
        public Dictionary<string, string[]> Casts { get; } = [];

        /// <summary>Where each of a scene's cast stood, by <c>scene/character</c>: one tile all through the scene, across restarts too.</summary>
        public Dictionary<string, Tile> CastTiles { get; } = [];

        /// <summary>How far each scene's cast stood from the leader at most, in tiles along x or y.</summary>
        public Dictionary<string, int> CastReach { get; } = [];
        public int Restarts { get; private set; }
        public int Steps { get; private set; }

        public TacticsHostView Host => Harness.HostView<TacticsHostView>();
        public WorldSave World => TacticsDocument.Read(Lease.Document).World!;

        private (TacticsGame, GameHarness<TacticsGame>) Open(int seed)
        {
            var game = new TacticsGame(Rules, lore: _lore, seed: _seed);
            return (game, new GameHarness<TacticsGame>(game, playerCount: _players, seed: seed, profiles: Profiles));
        }

        private int Lead => Harness.Players.ToList().FindIndex(p => p.Id == Game.Leader!.Id);

        private CustomView? Screen(int player) => Harness.PlayerView(player) as CustomView;

        private void Send(int player, string action, object? data = null) => Harness.Input(player, new { action, data });

        /// <summary>The same campaign on a new server, and everyone back.</summary>
        private void Restart()
        {
            Restarts++;
            Lease = new FakeCampaignLease(Lease.Info, Lease.Document);
            (Game, Harness) = Open(seed: 1 + Restarts);
            Harness.Resume(Lease);
            for (var p = 0; p < _players; p++)
                Harness.SetConnected(p, true);
        }

        /// <summary>Plays from wherever it is to the chapter card's end, and gives back the time it took.</summary>
        public TimeSpan ToTheEnd()
        {
            var start = Harness.Now;
            for (var guard = 0; guard < 200_000; guard++)
            {
                if (Host.Chapter is not null)
                    return Harness.Now - start;       // the card is up
                switch (Game.Phase)
                {
                    case TacticsPhase.Muster:
                        for (var p = 0; p < _players; p++)
                            if (Screen(p) is { Screen: TacticsGame.Screens.PickClass })
                                Send(p, "pickClass", new { @class = Classes[p % Classes.Length] });
                        break;
                    case TacticsPhase.Exploration:
                        Assert.DoesNotContain(Ember, World.Beats);      // never walking after the last beat without its card
                        Assert.Empty(Host.Explore!.Cast!.Value);         // the cast leaves with its scene
                        WalkToTheMarker();
                        break;
                    case TacticsPhase.Story:
                        Tell();
                        break;
                    case TacticsPhase.Battle:
                        Fight();
                        break;
                    case TacticsPhase.Results:
                        Send(0, "next");
                        break;
                    case TacticsPhase.Resuming:
                        throw new InvalidOperationException("Stuck at Resuming with everyone here.");
                    default:
                        throw new InvalidOperationException($"Unexpected phase {Game.Phase}.");
                }
            }
            throw new InvalidOperationException($"The chapter never ended: {string.Join(", ", Played)}.");
        }

        /// <summary>Walks to the beat on offer, a tap a step by the walking rule, and taps Interact there.</summary>
        private void WalkToTheMarker()
        {
            var marker = Assert.Single(Host.Explore!.Markers!.Value);
            Assert.Equal("alert", marker.Emote);
            var ground = Rules.GroundFor(Host.Explore.Region)!.Terrain;
            // The chapter route deliberately walks around visible, optional fights.
            var danger = Rules.MobsIn(Host.Explore.Region).SelectMany(m => m.Patch).SelectMany(t => new[]
                { t, new Tile(t.X + 1, t.Y), new Tile(t.X - 1, t.Y), new Tile(t.X, t.Y + 1), new Tile(t.X, t.Y - 1) }).ToHashSet();
            ground = Rules.GroundFor(Host.Explore.Region)!.With(danger);
            var taps = WorldWalk.Route(ground, Host.Explore.At, t => StoryRules.InReach(ground, t, marker.At), ClassDef.DefaultJump)
                ?? throw new InvalidOperationException($"No way from {Host.Explore.At} to {marker.Beat} at {marker.At}.");
            foreach (var dir in taps)
            {
                var seq = Host.Explore.Seq;
                var lead = Lead;
                Send(lead, "walk", new { dir = dir.ToString().ToLowerInvariant() });
                Send(lead, "walk", new { dir = (string?)null });
                // A tap during the last step's leg is latched, and taken as it ends.
                for (var i = 0; i < 20 && Game.Phase == TacticsPhase.Exploration && Host.Explore.Seq == seq; i++)
                    Harness.Advance(TimeSpan.FromMilliseconds(100));
                if (Game.Phase != TacticsPhase.Exploration)
                    return;             // a step into an armed stamp: the beat's own way in
                Assert.True(Host.Explore.Seq > seq, $"The step {dir} from {Host.Explore.At} towards {marker.Beat} didn't go.");
                Steps++;
            }
            Harness.Advance(TimeSpan.FromMilliseconds(600));     // the last leg's end
            var leader = Lead;
            Assert.NotEqual(JsonValueKind.Null, Screen(leader)!.Data.GetProperty("interact").ValueKind);
            Send(leader, "interact");
            Assert.NotEqual(TacticsPhase.Exploration, Game.Phase);
        }

        /// <summary>A moment of a scene: what's new is noted, the party answers what it's asked, and time goes on.</summary>
        private void Tell()
        {
            if (Host.Story is { } story)
            {
                SeeCast(story);
                if (Played.Count == 0 || Played[^1] != story.Scene)
                    Played.Add(story.Scene);
                if (story.Line is { } line && (Lines.Count == 0 || Lines[^1] != line.Id))
                {
                    Lines.Add(line.Id);
                    if (RestartEveryLine)
                    {
                        Restart();
                        return;
                    }
                }
                if (World.Story?.Roll is { } roll)
                    Rolls.Add(($"{story.Scene} {roll.Option}", roll.Die));
                if (RestartEveryMoment && _moments.Add($"{story.Scene} {story.Line?.Id} {story.State}"))
                {
                    RestartedAt[story.State] = RestartedAt.GetValueOrDefault(story.State) + 1;
                    var checking = story.Check is { } check && story.State != TacticsGame.StoryStates.Rolled ? check.Skill : null;
                    Restart();
                    if (checking is not null)
                    {
                        // The vote for the check was decided: the party picks who rolls again, and the question never reopens (review).
                        var back = Host.Story!;
                        Assert.Equal(TacticsGame.StoryStates.PickRoller, back.State);
                        Assert.Null(back.Ask);
                        Assert.Equal(checking, back.Check!.Skill);
                        AfterPick++;
                    }
                    return;
                }
                if (_answer)
                    Answer();
            }
            Harness.Advance(TimeSpan.FromMilliseconds(250));
        }

        /// <summary>
        /// The scene's cast as the TV is told it: the same all through a node, growing along the scene and never losing anyone, each keeping
        /// one tile all through the scene (after a restart too); each on a tile of their own the party could walk to, never the leader's or
        /// the trail's, facing the leader. Nobody shows before a node that can lead to one of their lines, and nobody in a gap or the card.
        /// A marker's figure never stands beside them.
        /// </summary>
        private void SeeCast(StoryView story)
        {
            var explore = Host.Explore!;
            var cast = explore.Cast!.Value;
            if (story.Scene == TacticsGame.LootVoteId)
            {
                Assert.Empty(cast); // reward allocation is narrated, with no lore actors placed in the world
                Assert.NotNull(World.Loot);
                if (story.Ask is { } ask)
                    Assert.Equal(Profiles.Select(p => p!.Value.Value).Order(StringComparer.Ordinal), ask.Options.Select(o => o.Id));
                return;
            }
            if (Host.Chapter is not null || story.State == TacticsGame.StoryStates.Gap || World.Story?.Node is not { } node)
            {
                Assert.Empty(cast);
                return;
            }
            var ids = cast.Select(c => c.Id).ToArray();
            var key = $"{story.Scene}/{node}";
            if (Casts.TryGetValue(key, out var seen))
                Assert.Equal(seen, ids);
            else
                Casts[key] = ids;
            var scene = _lore.Scenes.Single(sc => sc.Id == story.Scene);
            foreach (var id in ids)
                Assert.True(LeadsTo(scene, id, node), $"{id} stands in {key} before any node of theirs could have led there.");
            var ground = Rules.GroundFor(explore.Region)!.Terrain;
            var reached = Rules.ReachFor(explore.Region)!;
            Assert.Equal(cast.Length, cast.Select(c => c.At).Distinct().Count());
            foreach (var c in cast)
            {
                Assert.True(ground.Walkable(c.At) && reached.Contains(c.At), $"{c.Id} stands on {c.At} in {story.Scene}, which can't be stood on.");
                Assert.NotEqual(explore.At, c.At);
                Assert.DoesNotContain(c.At, explore.Legs.Select(l => l.To));
                Assert.Equal(explore.At, c.Facing);
                Assert.Equal($"characters.{_lore.Character(c.Id)!.Preset}", c.Sprite);
                if (CastTiles.TryGetValue($"{story.Scene}/{c.Id}", out var was))
                    Assert.Equal(was, c.At);
                else
                    CastTiles[$"{story.Scene}/{c.Id}"] = c.At;
                CastReach[story.Scene] = Math.Max(CastReach.GetValueOrDefault(story.Scene), Math.Max(Math.Abs(c.At.X - explore.At.X), Math.Abs(c.At.Y - explore.At.Y)));
            }
            if (!cast.IsEmpty)
                Assert.All(explore.Markers!.Value, m => Assert.Null(m.Sprite));
        }

        /// <summary>True when a node with a line of <paramref name="speaker"/> is <paramref name="node"/>, or can lead to it: walked forward from each.</summary>
        private static bool LeadsTo(SceneDef scene, string speaker, string node)
        {
            var seen = scene.Nodes.Where(n => n.Lines.Any(l => l.Speaker == speaker)).Select(n => n.Id).ToHashSet();
            var todo = new Stack<string>(seen);
            while (todo.TryPop(out var id))
            {
                if (id == node)
                    return true;
                foreach (var next in scene.Node(id)!.Targets)
                    if (seen.Add(next))
                        todo.Push(next);
            }
            return false;
        }

        /// <summary>Everyone who may answer does: the branch's option where it's offered, else the first; the best bonus to roll.</summary>
        private void Answer()
        {
            for (var p = 0; p < _players; p++)
            {
                if (Screen(p) is not { } view || !view.Data.TryGetProperty("canChoose", out var can) || !can.GetBoolean()
                    || view.Data.GetProperty("mine").ValueKind != JsonValueKind.Null)
                    continue;
                if (view.Screen == TacticsGame.Screens.Vote)
                {
                    var ids = view.Data.GetProperty("options").EnumerateArray().Select(o => o.GetProperty("id").GetString()!).ToList();
                    var mine = ids.Contains(_branch ?? "") ? _branch! : ids[0];
                    if (Ties && view.Data.GetProperty("party").GetBoolean() && !view.Data.GetProperty("tie").GetBoolean() && ids.Count > 1)
                    {
                        // The leader holds back; the first two others split, one for the branch's option and one for another.
                        var others = Enumerable.Range(0, _players).Where(o => o != Lead).ToList();
                        if (p == Lead || others.IndexOf(p) > 1)
                            continue;
                        mine = others.IndexOf(p) == 0 ? mine : ids.First(id => id != mine);
                    }
                    Send(p, "vote", new { option = mine });
                }
                else if (view.Screen == TacticsGame.Screens.Check)
                {
                    var best = view.Data.GetProperty("cards").EnumerateArray().MaxBy(c => c.GetProperty("bonus").GetInt32());
                    Send(p, "pickRoller", new { member = best.GetProperty("id").GetString() });
                }
            }
        }

        /// <summary>The fight, as the TV shows it, won (or lost, while there are losses to take) on the next command.</summary>
        private void Fight()
        {
            Played.Add($"fight:{Game.Document.Battle!.Beat}");
            if (Host.Battle!.Stage is not null)
                Harness.HostSignal("shown", new { seq = Host.Battle.Beats[^1].Seq });
            var outcome = _toLose-- > 0 ? BattleOutcome.Lost : BattleOutcome.Won;
            var engine = Game.ApplyEngine;
            var game = Game;
            game.ApplyEngine = (state, command, rules, ai) =>
            {
                game.ApplyEngine = engine;
                var r = engine(state, command, rules, ai);
                return r with { State = r.State with { Outcome = outcome } };
            };
            for (var i = 0; i < 2_000 && Game.Phase == TacticsPhase.Battle; i++)
            {
                for (var p = 0; p < _players; p++)
                    if (Screen(p) is { Screen: TacticsGame.Screens.Turn })
                        Send(p, "endTurn");
                    else if (Screen(p) is { Screen: TacticsGame.Screens.React })
                        Send(p, "react", new { choice = "pass" });
                Harness.Advance(TimeSpan.FromSeconds(7));
            }
            Assert.Equal(TacticsPhase.Results, Game.Phase);
            Fights.Add(outcome);
        }

        /// <summary>After the card: no markers, the objective says what's next, and the party walks.</summary>
        public void WalksAfter()
        {
            Assert.Equal(TacticsPhase.Exploration, Game.Phase);
            Assert.Null(Host.Chapter);
            Assert.Empty(Host.Explore!.Markers!.Value);
            var lead = Lead;
            Assert.Equal("Chapter II isn't here yet", Screen(lead)!.Data.GetProperty("objective").GetString());
            var seq = Host.Explore.Seq;
            foreach (var dir in new[] { "n", "e", "s", "w", "ne", "se", "sw", "nw" })
            {
                Send(lead, "walk", new { dir });
                Send(lead, "walk", new { dir = (string?)null });
                Harness.Advance(TimeSpan.FromMilliseconds(400));
                if (Host.Explore.Seq > seq)
                    return;
            }
            Assert.Fail("The party couldn't walk after the chapter.");
        }
    }

    /// <summary>The casts both branches share, and every cast within sight of the leader: never further from them than the cast may stand from its spot.</summary>
    protected static void AssertCommonCasts(Play play)
    {
        // The mayor's: Pip and Bree at first, Halden and Dagna as he speaks, and the Stranger only at the close (no reveal before then).
        Assert.Equal(["pip_ashdown", "bree_tallow"], play.Casts["ch1_mayor/open"]);
        Assert.Equal(["pip_ashdown", "bree_tallow", "halden_varrow", "dagna_holt"], play.Casts["ch1_mayor/halden"]);
        Assert.Equal(["pip_ashdown", "bree_tallow", "halden_varrow", "dagna_holt", "hooded_stranger"], play.Casts["ch1_mayor/close"]);
        Assert.Equal(["halden_varrow", "dagna_holt", "bree_tallow"], play.Casts["ch1_ford/open"]);
        Assert.Equal(["old_maud"], play.Casts["ch1_maud/open"]);                                   // alone until she says what fell
        Assert.Equal(["old_maud", "hooded_stranger"], play.Casts["ch1_maud/fell"]);
        Assert.Equal(["pip_ashdown"], play.Casts["ch1_docks/open"]);
        Assert.Equal(["pip_ashdown", "rook"], play.Casts["ch1_docks/rook"]);
        Assert.Equal(["maren_sorrel", "mother_aubrine"], play.Casts["ch1_kindler/open"]);
        Assert.Equal(["hooded_stranger"], play.Casts["ch1_four_more/open"]);
        Assert.All(play.CastReach, r => Assert.True(r.Value <= SceneCast.Reach, $"{r.Key}'s cast stood {r.Value} tiles from the leader."));
    }

    /// <summary>Plays the chapter, and checks its end: every beat done, the card shown once, its checkpoint, and walking after.</summary>
    protected static Play Played(Play play, string[] scenes)
    {
        play.ToTheEnd();
        Assert.Equal(scenes, play.Played.Distinct());      // a lost fight comes again
        var world = play.World;
        var arc = StoryTests.Arc;
        var wanted = arc.Beats.Where(b => b.Requires.All(f => world.Flags.Contains(f))).Select(b => b.Id);
        Assert.Equal(wanted.Order(StringComparer.Ordinal), world.Beats.Order(StringComparer.Ordinal));
        Assert.Contains("ch1_ember_found", world.Flags);
        Assert.Null(world.Story);

        // The card is up now, for its while, and then the party walks.
        Assert.Equal(TacticsPhase.Story, play.Game.Phase);
        Assert.Equal("Chapter I · The Bridge at the Ford", play.Host.Chapter);
        Assert.Equal("End of Chapter I", play.Lease.Checkpoints[^1].Label);
        play.Harness.Advance(StoryTiming.Default.Card);
        play.WalksAfter();
        // Each line came up once, in an order the scenes allow: never one from a scene that wasn't played.
        Assert.Equal(play.Lines.Count, play.Lines.Distinct().Count());
        var sceneOf = StoryTests.Lore.Scenes.SelectMany(sc => sc.Lines.Select(l => (l.Id, sc.Id))).ToDictionary(x => x.Item1, x => x.Item2);
        var rewardLines = play.Lines.Where(l => l.StartsWith(TacticsGame.LootVoteId + ".", StringComparison.Ordinal)).ToArray();
        foreach (var line in rewardLines)
        {
            Assert.Matches("^loot_vote\\.[0-9]+$", line);
            sceneOf.Add(line, TacticsGame.LootVoteId);
        }
        var encounter = play.Fights.Contains(BattleOutcome.Won) && play.Profiles.Count > 1
            ? Rules.Encounters[play.Game.Document.Completed.Contains("ford_assault") ? "ford_assault" : "river_crossing"] : null;
        var rewardCount = encounter is null ? 0 : encounter.ItemLoot.Length + encounter.Loot.Sum(l => Rules.LootTables[l.Value].Items.Length);
        Assert.Equal(rewardCount, rewardLines.Length);
        Assert.Null(play.World.Loot);
        Assert.Equal(scenes.Where(s => !s.StartsWith("fight:", StringComparison.Ordinal)), play.Lines.Select(l => sceneOf[l]).Distinct());
        return play;
    }
}

[Trait("Category", "Integration")]
public sealed class ChapterOneTests : ChapterOne
{
    [Fact]
    public void Chapter_1_plays_by_force()
    {
        var play = Played(new Play(branch: "force"), ByForce);

        // Mother Skarrow at the bridge, at dawn with Dagna, and where she fell before Dagna speaks (item 21), and every other scene's speakers.
        Assert.Equal(["mother_skarrow", "dagna_holt"], play.Casts["ch1_dawn/open"]);
        Assert.Equal(["mother_skarrow"], play.Casts["ch1_bridge_cost/open"]);                      // where she fell, before Dagna speaks
        Assert.Equal(["mother_skarrow", "dagna_holt"], play.Casts["ch1_bridge_cost/over"]);
        AssertCommonCasts(play);

        Assert.Contains("ch1_skarrow_slain", play.World.Flags);
        Assert.DoesNotContain("ch1_skarrow_spared", play.World.Flags);
        Assert.Equal([BattleOutcome.Won], play.Fights);
        Assert.True(play.Steps > 100, $"The party walked only {play.Steps} steps.");
    }

    [Fact]
    public void Chapter_1_plays_by_parley()
    {
        var play = Played(new Play(branch: "parley"), ByParley);

        // Mother Skarrow stands at her camp for the parley, and the speakers of every other scene at theirs (item 21).
        Assert.Equal(["mother_skarrow"], play.Casts["ch1_parley/open"]);
        Assert.Equal(["mother_skarrow", "dagna_holt"], play.Casts["ch1_young_ones/open"]);
        Assert.Equal(["mother_skarrow", "dagna_holt"], play.Casts["ch1_road_east/open"]);
        Assert.Equal(["halden_varrow", "dagna_holt", "celandine_marrowby", "mother_aubrine"], play.Casts["ch1_granary/open"]);
        Assert.Equal(["mother_skarrow"], play.Casts["ch1_song/open"]);
        AssertCommonCasts(play);

        Assert.Contains("ch1_skarrow_spared", play.World.Flags);
        Assert.Contains("ch1_halden_confessed", play.World.Flags);
        Assert.Equal([BattleOutcome.Won], play.Fights);
    }
}

[Trait("Category", "Integration")]
public sealed class ChapterOneSkippedAssaultTests : ChapterOne
{
    /// <summary>
    /// The force branch with the Ford Assault already done (a Development seed: tv-e2e's ARCBOX_BRANCH=skip-assault): the ford choice goes
    /// straight on to Old Maud, there's no fight, and the chapter still ends with its card.
    /// </summary>
    [Fact]
    public void Chapter_1_plays_by_force_with_the_assault_done()
    {
        var play = Played(new Play(branch: "force", seed: new TacticsSeed([], ["ch1_ford_assault"])),
            ["ch1_mayor", "ch1_ford", "ch1_maud", "ch1_docks", "ch1_kindler", "ch1_four_more"]);

        Assert.Empty(play.Fights);
        Assert.Contains("ch1_skarrow_slain", play.World.Flags);
    }
}

[Trait("Category", "Integration")]
public sealed class ChapterOneUnansweredTests : ChapterOne
{
    /// <summary>Nobody touches a phone from the mayor to the ember: every vote and choice runs out, which takes the first option (decision 2).</summary>
    [Fact]
    public void Chapter_1_plays_with_every_vote_unanswered()
    {
        var play = Played(new Play(answer: false), ByForce);

        Assert.Contains("ch1_skarrow_slain", play.World.Flags);
    }
}

[Trait("Category", "Integration")]
public sealed class ChapterOneCompanionTests : ChapterOne
{
    [Fact]
    public void Chapter_1_plays_with_one_player_and_companions()
    {
        var play = Played(new Play(players: 1, branch: "parley"), ByParley.Where(s => s != TacticsGame.LootVoteId).ToArray());

        Assert.Single(TacticsDocument.Read(play.Lease.Document).Members);
    }
}

[Trait("Category", "Integration")]
public sealed class ChapterOneRestartLineTests : ChapterOne
{
    [Theory]
    [InlineData("force")]
    [InlineData("parley")]
    public void Chapter_1_plays_with_a_restart_at_every_line(string branch)
    {
        var play = Played(new Play(branch: branch) { RestartEveryLine = true }, branch == "force" ? ByForce : ByParley);

        Assert.Equal(play.Lines.Count, play.Restarts);
        Assert.NotEmpty(play.CastTiles);        // every restart was checked against the tiles the cast had before it
    }
}

[Trait("Category", "Integration")]
public sealed class ChapterOneRestartMomentTests : ChapterOne
{
    /// <summary>
    /// Review: a restart at every moment, not only every line: mid-vote, on a tie-break, at the pick of who rolls, between a roll and its
    /// reveal, and in the gap between scenes. Each comes back: no question is lost, no decided vote reopens, no roll is made twice, no beat
    /// or flag repeats. With the roll's save taken out of <c>Roll</c>, both cases fail: the restart rolls again.
    /// </summary>
    [Theory]
    [InlineData("force")]
    [InlineData("parley")]
    public void Chapter_1_plays_with_a_restart_at_every_moment(string branch)
    {
        var play = Played(new Play(branch: branch) { RestartEveryMoment = true, Ties = true }, branch == "force" ? ByForce : ByParley);

        foreach (var state in new[] { "lines", "asking", "tieBreak", "pickRoller", "rolled", "gap" })
            Assert.True(play.RestartedAt.GetValueOrDefault(state) > 0, $"No restart at {state}: {string.Join(", ", play.RestartedAt)}");
        Assert.True(play.AfterPick > 0, "No restart while the party picked who rolls.");
        foreach (var option in play.Rolls.GroupBy(r => r.Line))
            Assert.Single(option.Select(r => r.Die).Distinct());       // the same die before and after every restart: never rolled again
        Assert.NotEmpty(play.Rolls);
    }
}

[Trait("Category", "Integration")]
public sealed class ChapterOneNoClipsTests : ChapterOne
{
    [Fact]
    public void Chapter_1_plays_with_no_clips()
        => Played(new Play(branch: "parley", lore: StoryTests.NoClips.Value), ByParley);
}

[Trait("Category", "Integration")]
public sealed class ChapterOneDefeatTests : ChapterOne
{
    /// <summary>The assault lost twice: the party wakes at the recovery spawn, walks back, and the retry goes straight to the fight.</summary>
    [Fact]
    public void Chapter_1_plays_with_a_defeat_in_its_battle()
    {
        var play = Played(new Play(branch: "force", lose: 2), ByForce);

        Assert.Equal([BattleOutcome.Lost, BattleOutcome.Lost, BattleOutcome.Won], play.Fights);
        Assert.Equal(["ch1_dawn", "fight:ch1_ford_assault", "fight:ch1_ford_assault", "fight:ch1_ford_assault", "ch1_bridge_cost"],
            play.Played.Skip(2).Take(5));
    }
}
