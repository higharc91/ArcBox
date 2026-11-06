using System.Collections.Immutable;

namespace Tactics.Logic.Tests;

public sealed class SceneRunTests
{
    private static LoreBook Book => Campaign.Book.Value;

    private static Arc Ford => Book.ArcFor("ashen_ford")!;

    private static ArcBeat BeatOf(string id) => Book.Arcs.SelectMany(a => a.Beats).Single(b => b.Id == id);

    private static SceneDef Scene(string id) => Book.Scenes.Single(s => s.Id == id);

    private static HashSet<string> Set(params string[] items) => [.. items];

    private static SceneDef Made(string id, string[]? requires = null, string[]? unless = null, bool after = false)
        => new(id, "beat", null, id, [.. requires ?? []], [.. unless ?? []], ["narrator"], "n",
            [new SceneNode("n", [new SceneLine($"{id}_1", "narrator", "t", "idle")], [], [], false, null, true)], after);

    private static PartyTags Tags(string[]? backgrounds = null, string[]? races = null, string[]? classes = null)
        => new([.. backgrounds ?? []], [.. races ?? []], [.. classes ?? []]);

    // ---- Which scene plays ----------------------------------------------------------------------------------------------------

    [Fact]
    public void A_scene_is_tested_as_it_starts()
    {
        ImmutableArray<SceneDef> scenes = [Made("first"), Made("needs_x", requires: ["x"]), Made("unless_x", unless: ["x"]), Made("last")];

        Assert.Equal("first", SceneRun.Next(scenes, Set(), null, aftermath: false)?.Id);
        Assert.Equal("unless_x", SceneRun.Next(scenes, Set(), "first", aftermath: false)?.Id);
        Assert.Equal("needs_x", SceneRun.Next(scenes, Set("x"), "first", aftermath: false)?.Id);
        Assert.Equal("last", SceneRun.Next(scenes, Set("x"), "needs_x", aftermath: false)?.Id);
        Assert.Null(SceneRun.Next(scenes, Set(), "last", aftermath: false));
        Assert.Null(SceneRun.Next([], Set(), null, aftermath: false));

        // A scene id that's gone after a content update starts the stage again.
        Assert.Equal("first", SceneRun.Next(scenes, Set(), "gone", aftermath: false)?.Id);
    }

    [Fact]
    public void A_vote_chooses_which_scene_plays_next()
    {
        var beat = BeatOf("ch1_the_ford_choice");
        var scenes = Book.ScenesOf(beat.Id);
        var vote = Scene("ch1_ford").Node("vote")!;
        var (force, parley) = (vote.Options.Single(o => o.Id == "force"), vote.Options.Single(o => o.Id == "parley"));

        Assert.Equal(["ch1_skarrow_slain"], SceneRun.Sets(force, beat));
        Assert.Equal(["ch1_skarrow_spared"], SceneRun.Sets(parley, beat));
        Assert.Null(SceneRun.Next(scenes, Set([.. SceneRun.Sets(force, beat)]), "ch1_ford", aftermath: false));
        Assert.Equal("ch1_parley", SceneRun.Next(scenes, Set([.. SceneRun.Sets(parley, beat)]), "ch1_ford", aftermath: false)?.Id);
        Assert.Empty(SceneRun.Sets(vote.Options.Single(o => o.Id == "pay"), beat));
    }

    [Fact]
    public void Lead_and_aftermath_are_told_apart()
    {
        var assault = Book.ScenesOf("ch1_ford_assault");
        var slain = Set("ch1_skarrow_slain");

        Assert.Equal("ch1_dawn", SceneRun.Next(assault, slain, null, aftermath: false)?.Id);
        Assert.Null(SceneRun.Next(assault, slain, "ch1_dawn", aftermath: false));
        Assert.Equal("ch1_bridge_cost", SceneRun.Next(assault, slain, null, aftermath: true)?.Id);
        Assert.Null(SceneRun.Next(assault, slain, "ch1_bridge_cost", aftermath: true));

        // The tower has only an aftermath; the bridge of Chapter III only lead-ins, one per branch.
        Assert.Null(SceneRun.Next(Book.ScenesOf("ch4_rime_tower"), Set(), null, aftermath: false));
        Assert.Equal("ch4_rime_tower_after", SceneRun.Next(Book.ScenesOf("ch4_rime_tower"), Set(), null, aftermath: true)?.Id);
        Assert.Equal("ch3_bridge_slain", SceneRun.Next(Book.ScenesOf("ch3_the_bridge"), slain, null, aftermath: false)?.Id);
        Assert.Null(SceneRun.Next(Book.ScenesOf("ch3_the_bridge"), slain, null, aftermath: true));

        ImmutableArray<SceneDef> made = [Made("lead"), Made("won", after: true)];
        Assert.Equal("won", SceneRun.Next(made, Set(), "lead", aftermath: true)?.Id);
        Assert.Null(SceneRun.Next(made, Set(), "lead", aftermath: false));
    }

    // ---- What a node offers ---------------------------------------------------------------------------------------------------

    [Fact]
    public void Options_filter_on_flags_and_tags()
    {
        var open = Scene("ch1_maud").Node("open")!;
        string[] Ids(IReadOnlySet<string> flags, PartyTags tags) => [.. SceneRun.Offered(open, flags, tags, Set()).Select(o => o.Id)];

        Assert.Equal(["fell"], Ids(Set(), PartyTags.None));
        Assert.Equal(["fell", "slain"], Ids(Set("ch1_skarrow_slain"), PartyTags.None));
        Assert.Equal(["fell", "hermit", "spared"], Ids(Set("ch1_skarrow_spared"), Tags(backgrounds: ["hermit", "soldier"])));
        Assert.Equal(["fell", "hermit", "sage"], Ids(Set(), Tags(backgrounds: ["sage", "hermit"])));

        // Any one tag of any kind is enough: the reeds offer the wild way to a druid or a ranger.
        var reeds = Scene("ch2_reeds").Node("open")!;
        Assert.Equal(["go"], SceneRun.Offered(reeds, Set(), Tags(races: ["elf"], classes: ["fighter"]), Set()).Select(o => o.Id));
        Assert.Equal(["dwarf", "wild", "go"], SceneRun.Offered(reeds, Set(), Tags(races: ["dwarf"], classes: ["ranger"]), Set()).Select(o => o.Id));
        Assert.Equal(["wild", "go"], SceneRun.Offered(reeds, Set(), Tags(classes: ["druid"]), Set()).Select(o => o.Id));

        // An option tagged with two kinds needs only one of them.
        var either = new SceneNode("n", [new SceneLine("l", "narrator", "t", "idle")], [], [new SceneOption("o", "O", "n", null, [], [], new OptionTags(["sage"], [], ["wizard"]), null)],
            false, null, false);
        Assert.Single(SceneRun.Offered(either, Set(), Tags(classes: ["wizard"]), Set()));
        Assert.Single(SceneRun.Offered(either, Set(), Tags(backgrounds: ["sage"]), Set()));
        Assert.Empty(SceneRun.Offered(either, Set(), Tags(backgrounds: ["noble"], races: ["elf"], classes: ["fighter"]), Set()));
    }

    [Fact]
    public void A_tag_counts_an_absent_character()
    {
        var rules = BasePackTests.Rules;
        var here = SheetBuilder.DefaultCharacter("Here", "human", "fighter", null, 1, rules) with { Background = "soldier" };
        var away = SheetBuilder.DefaultCharacter("Away", "half_orc", "wizard", null, 1, rules) with { Background = "folk_hero" };

        var party = PartyTags.Of([here, away]);

        Assert.Equal(["soldier", "folk_hero"], party.Backgrounds.Order().Reverse());
        Assert.Contains("hill", SceneRun.Offered(Scene("ch1_parley").Node("open")!, Set(), party, Set()).Select(o => o.Id));
        Assert.Contains("wizard", SceneRun.Offered(Scene("ch4_caves_lead").Node("open")!, Set(), party, Set()).Select(o => o.Id));
        Assert.DoesNotContain("hill", SceneRun.Offered(Scene("ch1_parley").Node("open")!, Set(), PartyTags.Of([here]), Set()).Select(o => o.Id));
    }

    /// <summary>A hub's questions can each be asked once, so a scene nobody answers still reaches its way on.</summary>
    [Fact]
    public void An_option_already_asked_isnt_offered_again()
    {
        var willow = Scene("ch2_willow");
        var more = willow.Node("more")!;

        Assert.Equal(["origin", "kindled", "ember"], SceneRun.Offered(more, Set(), PartyTags.None, Set()).Select(o => o.Id));
        Assert.Equal(["kindled", "ember"], SceneRun.Offered(more, Set(), PartyTags.None, Set("open", "origin", "more")).Select(o => o.Id));
        Assert.Equal(["ember"], SceneRun.Offered(more, Set(), PartyTags.None, Set("open", "origin", "kindled", "more")).Select(o => o.Id));

        // A check, once tried, is gone too: the heartpool's hub doesn't offer it, and nor does its opening once either result played.
        var open = Scene("ch2_heartpool").Node("open")!;
        Assert.DoesNotContain("court", SceneRun.Offered(open, Set(), PartyTags.None, Set("court_fail")).Select(o => o.Id));
        Assert.Contains("court", SceneRun.Offered(open, Set(), PartyTags.None, Set("what")).Select(o => o.Id));
    }

    [Fact]
    public void An_unanswered_vote_takes_the_first_option()
    {
        var vote = Scene("ch1_ford").Node("vote")!;
        var offered = SceneRun.Offered(vote, Set(), PartyTags.None, Set());

        Assert.Equal(["force", "parley", "pay"], offered.Select(o => o.Id));
        Assert.Equal("force", SceneRun.Unanswered(offered, vote: true).Id);
        Assert.Equal("ch1_force", SceneRun.Unanswered(offered, vote: true).Choice);

        // A check comes first only in a vote, where the first option is taken whatever it is.
        var pay = offered.Single(o => o.Id == "pay");
        Assert.Same(pay, SceneRun.Unanswered([pay, offered[0]], vote: true));
        Assert.Throws<ArgumentException>(() => SceneRun.Unanswered([], vote: true));
    }

    [Fact]
    public void An_unanswered_choice_skips_a_check()
    {
        var why = Scene("ch1_ford").Node("why")!;
        var offered = SceneRun.Offered(why, Set(), PartyTags.None, Set());

        Assert.Equal(["insight", "fair"], offered.Select(o => o.Id));
        Assert.Equal("fair", SceneRun.Unanswered(offered, vote: false).Id);

        // With nothing but checks, there's no way round one.
        var trick = SceneRun.Offered(Scene("ch2_heartpool").Node("trick")!, Set(), PartyTags.None, Set());
        Assert.Equal(["arcana", "deceive"], trick.Select(o => o.Id));
        Assert.Equal("arcana", SceneRun.Unanswered(trick, vote: false).Id);
        Assert.Throws<ArgumentException>(() => SceneRun.Unanswered([], vote: false));
    }

    [Fact]
    public void An_option_leads_to_its_node_or_its_checks_result()
    {
        var why = Scene("ch1_ford").Node("why")!;

        Assert.Equal("cases", SceneRun.Target(why.Options[1]));
        Assert.Equal("why_pass", SceneRun.Target(why.Options[0], passed: true));
        Assert.Equal("why_fail", SceneRun.Target(why.Options[0], passed: false));
        Assert.Throws<ArgumentException>(() => SceneRun.Target(why.Options[0]));
    }

    // ---- Places, chains and reach ---------------------------------------------------------------------------------------------

    [Fact]
    public void Parley_travels_to_the_hills()
    {
        var beat = BeatOf("ch1_the_ford_choice");
        var region = BasePackTests.Rules.RegionFor("ashen_ford")!.Def;

        Assert.Equal("ford_bridge", StoryRules.PlaceOf(Scene("ch1_ford"), beat));
        Assert.Equal("skarrow_hills", StoryRules.PlaceOf(Scene("ch1_parley"), beat));
        Assert.Equal(region.Beats.Single(s => s.Beat == "ch1_skarrow_song").At, StoryRules.SpotFor(Ford, region, "skarrow_hills"));
        Assert.Equal(region.Beats.Single(s => s.Beat == "ch1_mayors_request").At, StoryRules.SpotFor(Ford, region, "emberwick_town"));
        Assert.Null(StoryRules.SpotFor(Ford, region, "nowhere"));
        Assert.Null(StoryRules.PlaceOf(Scene("ch4_pip_remembers"), BeatOf("ch4_pip_remembers")));
    }

    [Fact]
    public void Force_chains_to_the_assault()
    {
        var flags = Set("ch1_skarrow_slain");
        var next = StoryRules.Available(Ford, flags, Set("ch1_mayors_request", "ch1_the_ford_choice"))!;

        Assert.Equal("ch1_ford_assault", next.Id);
        Assert.Null(SceneRun.Next(Book.ScenesOf("ch1_the_ford_choice"), flags, "ch1_ford", aftermath: false));
        Assert.True(StoryRules.Chains(next, StoryRules.PlaceOf(Scene("ch1_ford"), BeatOf("ch1_the_ford_choice"))));
    }

    [Fact]
    public void Parley_doesnt_chain_to_the_crossing()
    {
        var flags = Set("ch1_skarrow_spared");
        var next = StoryRules.Available(Ford, flags, Set("ch1_mayors_request", "ch1_the_ford_choice"))!;
        var last = SceneRun.Next(Book.ScenesOf("ch1_the_ford_choice"), flags, "ch1_ford", aftermath: false)!;

        Assert.Equal(("ch1_river_crossing", "ch1_parley"), (next.Id, last.Id));
        Assert.False(StoryRules.Chains(next, StoryRules.PlaceOf(last, BeatOf("ch1_the_ford_choice"))));
        Assert.True(StoryRules.Chains(next, "ford_bridge"));

        // Only a battle chains, and never one set nowhere in particular.
        Assert.False(StoryRules.Chains(BeatOf("ch1_granary"), "emberwick_town"));
        Assert.False(StoryRules.Chains(next with { Place = null }, null));
    }

    private static Terrain TerrainOf(string ascii) => new(AsciiMap.Parse(ascii), ImmutableSortedDictionary<Tile, int>.Empty, ImmutableSortedSet<Tile>.Empty);

    [Fact]
    public void Reach_counts_steps_not_tiles_across_a_cliff()
    {
        // A cliff's top at (1, 0), five levels above its foot at (0, 0) and with no way up from below.
        var cliff = TerrainOf("""
            0. 5. 5.
            0. 0# 0#
            """);

        Assert.False(StoryRules.InReach(cliff, new Tile(0, 0), new Tile(1, 0)));
        Assert.False(StoryRules.InReach(cliff, new Tile(0, 1), new Tile(1, 0)));
        Assert.True(StoryRules.InReach(cliff, new Tile(2, 0), new Tile(1, 0)));
        Assert.True(StoryRules.InReach(cliff, new Tile(1, 0), new Tile(0, 0)), "Jumping down is a step.");

        var flat = TerrainOf("""
            0. 0. 0. 0.
            0. 0. 0. 0.
            0. 0. 0. 0.
            """);
        Assert.True(StoryRules.InReach(flat, new Tile(0, 0), new Tile(0, 0)));
        Assert.True(StoryRules.InReach(flat, new Tile(0, 0), new Tile(2, 0)));
        Assert.True(StoryRules.InReach(flat, new Tile(0, 0), new Tile(2, 2)), "Two diagonals are two steps.");
        Assert.False(StoryRules.InReach(flat, new Tile(0, 0), new Tile(3, 0)));
        Assert.False(StoryRules.InReach(flat, new Tile(0, 0), new Tile(3, 2)));
    }

    // ---- Every way through every real scene -----------------------------------------------------------------------------------

    /// <summary>Every tag the book's options name: a party with all of them sees every tagged option.</summary>
    private static PartyTags Everything()
    {
        var tags = Book.Scenes.SelectMany(s => s.Nodes).SelectMany(n => n.Options).Select(o => o.Tags).ToList();
        return new([.. tags.SelectMany(t => t.Background)], [.. tags.SelectMany(t => t.Race)], [.. tags.SelectMany(t => t.Class)]);
    }

    /// <summary>
    /// Walks every way through <paramref name="scene"/> from <paramref name="flags"/>: every option offered, both results of every check,
    /// with each node's and option's flags set as they play. Reports a node that leads nowhere, offers nothing, or a way round that
    /// never ends, and gives every set of flags the scene can end with. States are (node, nodes played, flags), and each is walked
    /// once: <paramref name="memo"/> keeps what each ends with.
    /// </summary>
    private static List<ImmutableHashSet<string>> Explore(SceneDef scene, ArcBeat beat, ImmutableHashSet<string> flags, PartyTags tags, ISet<string> problems,
        Dictionary<string, List<ImmutableHashSet<string>>> memo)
    {
        var onPath = new HashSet<string>();

        List<ImmutableHashSet<string>> Visit(string id, ImmutableHashSet<string> played, ImmutableHashSet<string> flags)
        {
            var key = $"{scene.Id}|{id}|{KeyOf(played)}|{KeyOf(flags)}";
            if (onPath.Contains(key))
            {
                problems.Add($"{scene.Id}: a way round through '{id}' never ends");
                return [];
            }
            if (memo.TryGetValue(key, out var known))
                return known;
            if (scene.Node(id) is not { } node)
            {
                problems.Add($"{scene.Id}: '{id}' isn't a node");
                return [];
            }
            onPath.Add(key);
            (played, flags) = (played.Add(id), flags.Union(node.Sets));
            var ends = new List<ImmutableHashSet<string>>();
            if (node.End)
                ends.Add(flags);
            else if (node.Next is { } next)
                ends.AddRange(Visit(next, played, flags));
            else
            {
                var offered = SceneRun.Offered(node, flags, tags, played);
                if (offered.IsEmpty)
                    problems.Add($"{scene.Id}: '{id}' offers nothing with [{string.Join(", ", flags.Order())}]");
                foreach (var option in offered)
                    foreach (var passed in option.Check is null ? [(bool?)null] : new bool?[] { true, false })
                        ends.AddRange(Visit(SceneRun.Target(option, passed), played, flags.Union(SceneRun.Sets(option, beat))));
            }
            onPath.Remove(key);
            return memo[key] = [.. ends.DistinctBy(KeyOf)];
        }

        return Visit(scene.Start, [], flags);
    }

    private static string KeyOf(IEnumerable<string> flags) => string.Join(',', flags.Order());

    /// <summary>
    /// Plays a branch's beat as the game will: from the flags set before it (not the branch's choice, which only its vote sets), each
    /// scene in turn, tested as it starts with the flags the scene before it ended with, through every way <paramref name="run"/> gives
    /// it to end. A battle's own flags are set at the win, before its aftermath. Adds every scene played to <paramref name="played"/>.
    /// </summary>
    private static void PlayBeat(BeatStep step, Func<SceneDef, ImmutableHashSet<string>, IEnumerable<ImmutableHashSet<string>>> run, ISet<string> played)
    {
        var scenes = Book.ScenesOf(step.Beat.Id);

        void Stage(ImmutableHashSet<string> flags, string? after, bool aftermath, int depth)
        {
            Assert.True(depth <= scenes.Length, $"'{step.Beat.Id}' plays more scenes than it has.");
            if (SceneRun.Next(scenes, flags, after, aftermath) is not { } scene)
            {
                if (!aftermath && step.Beat.Kind == ArcBeat.Battle)
                    Stage(flags.Union(step.Beat.Sets), null, aftermath: true, 0);
                return;
            }
            played.Add(scene.Id);
            foreach (var end in run(scene, flags).DistinctBy(KeyOf).ToList())
                Stage(end, scene.Id, aftermath, depth + 1);
        }

        Stage(step.Before, null, aftermath: false, 0);
    }

    private static IEnumerable<BeatStep> EveryStep() => Campaign.Branches(Book).SelectMany(b => b).DistinctBy(s => (s.Beat.Id, KeyOf(s.Before)));

    /// <summary>
    /// The 5d plan's "no option offered can't happen": on both branches of Chapter I, and every branch of the campaign after it, every
    /// node anyone can reach offers something, with no tags in the party and with all of them, and every way round a hub ends. Each
    /// scene starts with the flags the one before it ended with, and a vote's flags are set only when it's voted.
    /// </summary>
    [Fact]
    public void Every_reachable_node_offers_something_on_both_branches()
    {
        var problems = new SortedSet<string>(StringComparer.Ordinal);
        var played = new HashSet<string>();
        foreach (var tags in new[] { PartyTags.None, Everything() })
        {
            var memo = new Dictionary<string, List<ImmutableHashSet<string>>>();
            foreach (var step in EveryStep())
                PlayBeat(step, (scene, flags) => Explore(scene, step.Beat, flags, tags, problems, memo), played);
        }
        Assert.Empty(problems);
        Assert.Equal(Book.Scenes.Where(s => !s.Camp && s.Beat is not null).Select(s => s.Id).Order(), played.Order());
    }

    /// <summary>A <c>next</c> can't lead round in a circle: only an option can go back, and options are each taken once.</summary>
    [Fact]
    public void Every_path_through_every_scene_ends()
    {
        foreach (var scene in Book.Scenes)
            foreach (var start in scene.Nodes)
            {
                var steps = 0;
                for (var node = start; node.Next is { } next; node = scene.Node(next)!)
                    Assert.True(++steps <= scene.Nodes.Length, $"{scene.Id}: '{start.Id}' goes round by 'next' alone.");
            }
    }

    /// <summary>
    /// Nobody touches a phone for a whole campaign: every question takes what <see cref="SceneRun.Unanswered"/> gives, on every branch,
    /// and every check both passes and fails. Every scene still reaches an end, and never comes up with nothing to pick.
    /// </summary>
    [Fact]
    public void A_scene_nobody_answers_still_ends()
    {
        var problems = new SortedSet<string>(StringComparer.Ordinal);
        var scenes = new HashSet<string>();

        IEnumerable<ImmutableHashSet<string>> Unanswered(SceneDef scene, ArcBeat beat, ImmutableHashSet<string> start)
        {
            var ends = new List<ImmutableHashSet<string>>();
            foreach (var passed in new[] { true, false })
            {
                var (id, flags, played, steps) = (scene.Start, start, ImmutableHashSet<string>.Empty, 0);
                while (scene.Node(id) is { End: false } node && steps++ <= scene.Nodes.Length * scene.Nodes.Length)
                {
                    (played, flags) = (played.Add(id), flags.Union(node.Sets));
                    if (node.Next is { } next)
                    {
                        id = next;
                        continue;
                    }
                    var offered = SceneRun.Offered(node, flags, PartyTags.None, played);
                    if (offered.IsEmpty)
                    {
                        problems.Add($"{scene.Id}: '{id}' offers nothing");
                        break;
                    }
                    var option = SceneRun.Unanswered(offered, node.Vote);
                    (id, flags) = (SceneRun.Target(option, option.Check is null ? null : passed), flags.Union(SceneRun.Sets(option, beat)));
                }
                if (scene.Node(id) is { End: true } end)
                    ends.Add(flags.Union(end.Sets));
                else
                    problems.Add($"{scene.Id}: unanswered, it stops at '{id}' after {steps} steps");
            }
            return ends;
        }

        foreach (var step in EveryStep())
            PlayBeat(step, (scene, flags) => Unanswered(scene, step.Beat, flags), scenes);
        Assert.Empty(problems);
        Assert.NotEmpty(scenes);
    }

    /// <summary>
    /// The model test: random flags, random tags and random picks (an unanswered one a third of the time) through every scene of the
    /// pack, a few hundred times each. Every walk ends, at an end or at a node with nothing to offer (which these random flags can
    /// make, and the game ends the scene on), within one pick per node; and it never steps onto a node the scene doesn't have.
    /// </summary>
    [Fact]
    public void Random_walks_through_every_scene_always_end()
    {
        var flagPool = Book.Arcs.SelectMany(a => a.Beats).SelectMany(b => b.Sets.Concat(b.Choices.SelectMany(c => c.Sets))).Distinct().Order().ToArray();
        var everything = Everything();
        var (ended, emptied) = (0, 0);
        foreach (var (scene, index) in Book.Scenes.Select((s, i) => (s, i)))
        {
            var beat = scene.Beat is { } beatId ? BeatOf(beatId) : new ArcBeat(scene.Id, "story", null, null, [], [], []);
            for (var walk = 0; walk < 300; walk++)
            {
                var rng = new Random(index * 1000 + walk);
                var flags = flagPool.Where(_ => rng.Next(2) == 0).ToImmutableHashSet();
                var tags = new PartyTags([.. everything.Backgrounds.Where(_ => rng.Next(3) == 0)], [.. everything.Races.Where(_ => rng.Next(3) == 0)],
                    [.. everything.Classes.Where(_ => rng.Next(3) == 0)]);
                var (id, played, picks) = (scene.Start, ImmutableHashSet<string>.Empty, 0);
                while (true)
                {
                    var node = scene.Node(id);
                    Assert.True(node is not null, $"{scene.Id}, walk {walk}: stepped onto '{id}', which isn't a node.");
                    (played, flags) = (played.Add(id), flags.Union(node.Sets));
                    if (node.End)
                    {
                        ended++;
                        break;
                    }
                    if (node.Next is { } next)
                    {
                        id = next;
                        continue;
                    }
                    var offered = SceneRun.Offered(node, flags, tags, played);
                    if (offered.IsEmpty)
                    {
                        emptied++;
                        break;
                    }
                    Assert.True(++picks <= scene.Nodes.Length, $"{scene.Id}, walk {walk}: {picks} picks in a scene of {scene.Nodes.Length} nodes.");
                    var option = rng.Next(3) == 0 ? SceneRun.Unanswered(offered, node.Vote) : offered[rng.Next(offered.Length)];
                    Assert.Contains(option, node.Options);
                    id = SceneRun.Target(option, option.Check is null ? null : rng.Next(2) == 0);
                    flags = flags.Union(SceneRun.Sets(option, beat));
                }
            }
        }
        Assert.Equal(Book.Scenes.Length * 300, ended + emptied);
        Assert.True(ended > emptied * 10, $"{ended} walks ended and {emptied} found nothing to pick.");
    }
}
