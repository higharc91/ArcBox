using System.Collections.Immutable;

namespace Tactics.Logic.Tests;

public sealed class StoryRulesTests
{
    private static readonly Lazy<LoreBook> Book = new(() => LoreBook.Load(ContentPaths.ContentBase));

    private static Arc Ford => Book.Value.ArcFor("ashen_ford")!;

    private static ArcBeat Beat(string id, string[]? requires = null, string[]? sets = null, string kind = "story")
        => new(id, kind, null, null, [.. requires ?? []], [.. sets ?? []], []);

    private static Arc ArcOf(params ArcBeat[] beats) => new("arc", 1, "r", "Arc", [.. beats]);

    private static HashSet<string> Set(params string[] items) => [.. items];

    /// <summary>
    /// Plays an arc to its end as the beats are offered, each choice taking the option <paramref name="choose"/> picks, and gives the
    /// beats in the order they were offered, with the flags and beats done when each was.
    /// </summary>
    internal static List<(ArcBeat Beat, HashSet<string> Flags, HashSet<string> Done)> Play(Arc arc, Func<ArcBeat, ArcChoice> choose)
    {
        var (flags, done, offered) = (new HashSet<string>(), new HashSet<string>(), new List<(ArcBeat, HashSet<string>, HashSet<string>)>());
        if (arc.SideAdventure && !arc.Beats.IsEmpty) flags.UnionWith(arc.Beats[0].Requires);
        while (StoryRules.Available(arc, flags, done) is { } beat)
        {
            offered.Add((beat, [.. flags], [.. done]));
            Assert.True(offered.Count <= arc.Beats.Length, $"The arc '{arc.Id}' offered more beats than it has.");
            done.Add(beat.Id);
            flags.UnionWith(beat.Sets);
            if (beat.Choices is [_, ..])
                flags.UnionWith(choose(beat).Sets);
        }
        return offered;
    }

    private static List<string> Branch(Arc arc, string choice)
        => [.. Play(arc, beat => beat.Choices.FirstOrDefault(c => c.Id == choice) ?? beat.Choices[0]).Select(o => o.Beat.Id)];

    [Fact]
    public void A_new_campaign_starts_at_the_first_beat()
        => Assert.Equal("ch1_mayors_request", StoryRules.Available(Ford, Set(), Set())?.Id);

    [Fact]
    public void A_beat_waits_for_the_beats_before_it()
    {
        var arc = ArcOf(Beat("first"), Beat("second"), Beat("third"));

        Assert.Equal("first", StoryRules.Available(arc, Set(), Set())?.Id);
        Assert.Equal("second", StoryRules.Available(arc, Set(), Set("first"))?.Id);
        Assert.Equal("third", StoryRules.Available(arc, Set(), Set("first", "second"))?.Id);
        Assert.Equal("ch1_the_ford_choice", StoryRules.Available(Ford, Set(), Set("ch1_mayors_request"))?.Id);
    }

    [Fact]
    public void Unmet_requires_are_passed_over()
    {
        var arc = ArcOf(Beat("a"), Beat("needs_x", requires: ["x"]), Beat("needs_x_and_y", requires: ["x", "y"]), Beat("b"));

        Assert.Equal("b", StoryRules.Available(arc, Set(), Set("a"))?.Id);
        Assert.Equal("b", StoryRules.Available(arc, Set("y"), Set("a"))?.Id);
        Assert.Equal("needs_x", StoryRules.Available(arc, Set("x"), Set("a"))?.Id);
        Assert.Equal("b", StoryRules.Available(arc, Set("x"), Set("a", "needs_x"))?.Id);
        Assert.Equal("needs_x_and_y", StoryRules.Available(arc, Set("x", "y"), Set("a", "needs_x"))?.Id);
    }

    /// <summary>A beat passed over isn't lost: once a later beat sets its flag, it's the next on offer.</summary>
    [Fact]
    public void A_beat_passed_over_comes_back_when_its_flag_is_set()
    {
        var arc = ArcOf(Beat("later", requires: ["x"]), Beat("sets_x", sets: ["x"]), Beat("last"));

        Assert.Equal(["sets_x", "later", "last"], Play(arc, b => b.Choices[0]).Select(o => o.Beat.Id));
    }

    [Fact]
    public void Force_makes_the_assault_available_and_never_the_crossing()
    {
        var beats = Branch(Ford, "ch1_force");

        Assert.Equal(["ch1_mayors_request", "ch1_the_ford_choice", "ch1_ford_assault", "ch1_old_maud", "ch1_granary", "ch1_skarrow_song", "ch1_the_ember"], beats);
    }

    [Fact]
    public void Parley_makes_the_crossing_available_and_never_the_assault()
    {
        var beats = Branch(Ford, "ch1_parley");

        Assert.Equal(["ch1_mayors_request", "ch1_the_ford_choice", "ch1_river_crossing", "ch1_old_maud", "ch1_granary", "ch1_skarrow_song", "ch1_the_ember"], beats);
    }

    [Fact]
    public void A_done_beat_isnt_available()
    {
        var arc = ArcOf(Beat("a"), Beat("b"));

        Assert.Equal("b", StoryRules.Available(arc, Set(), Set("a"))?.Id);
        Assert.Null(StoryRules.Available(arc, Set(), Set("a", "b")));
        // Done out of order, as a hand-edited save can be: the first not done is still the one.
        Assert.Equal("a", StoryRules.Available(arc, Set(), Set("b"))?.Id);
    }

    [Fact]
    public void The_chapter_ends_with_its_last_beat()
    {
        var arc = ArcOf(Beat("a"), Beat("b", requires: ["never"]), Beat("c"));

        Assert.False(StoryRules.ChapterDone(arc, Set()));
        Assert.False(StoryRules.ChapterDone(arc, Set("a", "b")));
        Assert.True(StoryRules.ChapterDone(arc, Set("a", "c")));
        Assert.False(StoryRules.ChapterDone(ArcOf(), Set("a")));
        Assert.True(StoryRules.ChapterDone(Ford, Set("ch1_the_ember")));
    }

    /// <summary>
    /// Every arc in the lore, played with random choices many times: each beat offered is new and has what it requires, every arc
    /// reaches its end, and the chapter is done there and not before.
    /// </summary>
    [Fact]
    public void Every_arc_plays_to_its_end_whatever_is_chosen()
    {
        var random = new Random(11);
        Assert.NotEmpty(Book.Value.Arcs);
        foreach (var arc in Book.Value.Arcs)
            for (var run = 0; run < 50; run++)
            {
                var offered = Play(arc, beat => beat.Choices[random.Next(beat.Choices.Length)]);
                foreach (var (beat, flags, done) in offered)
                {
                    Assert.DoesNotContain(beat.Id, done);
                    Assert.All(beat.Requires, flag => Assert.Contains(flag, flags));
                    Assert.False(StoryRules.ChapterDone(arc, done));
                }
                Assert.True(StoryRules.ChapterDone(arc, offered.Select(o => o.Beat.Id).ToHashSet()), $"The arc '{arc.Id}' never ends.");
            }
    }

    [Fact]
    public void An_arc_with_nothing_left_has_no_beat()
        => Assert.Null(StoryRules.Available(ArcOf(Beat("a", requires: ["x"])), ImmutableHashSet<string>.Empty, ImmutableHashSet<string>.Empty));
}
