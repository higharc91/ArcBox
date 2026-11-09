using System.Text.Json;
using ArcBox.Sdk;
using Tactics.Game;
using Tactics.Presentation.Story;
using static Tactics.Presentation.Tests.Story.StoryFeed;

namespace Tactics.Presentation.Tests.Story;

/// <summary><see cref="StoryDemo"/>, <see cref="ChapterCard"/> and the tick's sounds: what <c>--story-demo</c> and the TV's story layer use.</summary>
public sealed class StoryDemoTests
{
    private static readonly Lazy<System.Collections.Immutable.ImmutableArray<StoryDemoStep>> Demo = new(() => StoryDemo.Steps(Lore, T0));

    /// <summary>The demo through a track as the TV takes it, over the wire, frame by frame: what it says and shows.</summary>
    private static (List<string> Said, List<StoryTick> Ticks) Play()
    {
        var track = new StoryTrack(Clips, Art);
        var (said, ticks) = (new List<string>(), new List<StoryTick>());
        var steps = Demo.Value;
        for (var i = 0; i < steps.Length; i++)
        {
            var wire = JsonSerializer.SerializeToElement(steps[i].View, ArcJson.Options).Deserialize<TacticsHostView>(ArcJson.Options)!;
            var tick = track.Receive(wire, T0.AddSeconds(steps[i].At));
            ticks.Add(tick);
            if (tick.Say is { } say)
                said.Add(say.Line);
            var next = i + 1 < steps.Length ? steps[i + 1].At : steps[i].At + 1;
            for (var at = steps[i].At; at < next; at += 1 / 30.0)
                ticks.Add(track.Tick(1 / 30.0));
        }
        return (said, ticks);
    }

    [Fact]
    public void The_demo_starts_at_each_of_its_steps_in_order()
    {
        var labels = Demo.Value.Select(s => s.Label).Where(l => l.Length > 0).ToArray();

        Assert.Equal(StoryDemo.Labels, labels);
        Assert.Equal(Demo.Value.Select(s => s.At).Order(), Demo.Value.Select(s => s.At));
        Assert.Equal(0, Demo.Value[0].At);
    }

    [Fact]
    public void The_demo_says_each_line_once_from_the_walk_up()
    {
        var (said, _) = Play();

        Assert.Equal("ch1_mayor_open_1", said[0]);
        Assert.Equal(said.Distinct(), said);
        Assert.Contains("ch1_ford_pay_fail_2", said);
    }

    [Fact]
    public void The_demo_shows_every_panel()
    {
        var (_, ticks) = Play();
        var screens = ticks.Select(t => t.Screen).OfType<StoryScreen>().ToArray();

        Assert.Contains(screens, s => s.Vote is { Party: false, Tally: "Jess chooses for the party" });
        Assert.Contains(screens, s => s.Vote is { Tie: true, Tally: "4 of 4 voted · 2–2 tie, Sam decides" });
        Assert.Contains(screens, s => s.Vote is { Rows.Length: 11 } vote && vote.Scale == 1 && VoteLayout.ColumnCount(vote.Rows) > 1);
        Assert.Contains(screens, s => s.Check is { Cards.Length: 6, Roll: null });
        Assert.Contains(screens, s => s.Check?.Roll is { Landed: true, Success: true, Sum: "14 + 5 = 19 vs DC 15" });
        Assert.Contains(screens, s => s.Check?.Roll is { Landed: true, Success: false, Sum: "3 + 2 = 5 vs DC 15" });
        Assert.Contains(screens, s => s.Interlude == "The Ford Choice");
        Assert.Equal([StoryTick.DiceSound, StoryTick.PassSound, StoryTick.DiceSound, StoryTick.FailSound], ticks.Select(t => t.Sound).OfType<string>());
    }

    [Fact]
    public void The_demo_ends_on_the_chapters_card()
    {
        var card = ChapterCard.Of(Demo.Value.Single(s => s.Label == "card").View.Chapter)!;

        Assert.Equal(new ChapterCard("The end of", "Chapter I", "The Bridge at the Ford"), card);
        Assert.Null(ChapterCard.Of(null));
        Assert.Equal(new ChapterCard("The end of", "Chapter II", ""), ChapterCard.Of("Chapter II"));
    }

    [Fact]
    public void The_marker_shows_until_its_scene_starts()
    {
        Assert.Equal("alert", Demo.Value[0].View.Explore!.Markers!.Value[0].Emote);
        Assert.Null(Demo.Value[1].View.Explore!.Markers!.Value[0].Emote);
    }
}
