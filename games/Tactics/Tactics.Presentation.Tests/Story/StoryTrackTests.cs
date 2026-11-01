using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Story;
using static Tactics.Presentation.Tests.Story.StoryFeed;
using States = Tactics.Game.TacticsGame.StoryStates;

namespace Tactics.Presentation.Tests.Story;

/// <summary>
/// <see cref="StoryTrack"/>: which line the TV speaks and when (overworld 5d §10). The server owns every clock and the TV sends nothing back,
/// so a TV that joins late, misses views or outlives a server restart never speaks a line that isn't the one showing, nor one twice.
/// </summary>
public sealed class StoryTrackTests
{
    private static StoryTrack Track(SpeechFallback fallback = SpeechFallback.Captions) => new(Clips, Art, fallback);

    /// <summary>A track that has seen the story under way: the first view only ever shows.</summary>
    private static StoryTrack Running(int seq = 1)
    {
        var track = Track();
        track.Receive(Showing(seq, Line("ch1_mayor_open_1")), T0);
        return track;
    }

    [Fact]
    public void A_new_line_speaks_its_clip()
    {
        var track = Running();

        var tick = track.Receive(Showing(2, Line("ch1_mayor_open_2")), T0);

        Assert.Equal("ch1_mayor_open_2", tick.Say!.Line);
        Assert.Equal(Clips.ForLine("ch1_mayor_open_2")!.Path, tick.Say.Clip);
        Assert.Equal("It used to be warmer. Nobody in town will say so.", tick.Say.Text);
        Assert.True(tick.Cut);                                             // whatever was still speaking stops for it
        Assert.Equal("ch1_mayor_open_2", tick.Screen!.Caption!.Line);
    }

    [Fact]
    public void Every_chapter_1_line_finds_its_clip()
    {
        var lines = AllLines().Where(l => l.Scene.Id.StartsWith("ch1_", StringComparison.Ordinal)).Select(l => l.Line).ToArray();

        Assert.Equal(208, lines.Length);       // when this moves, check the new lines were rendered
        Assert.All(lines, line =>
        {
            var clip = Clips.ForLine(line.Id);
            Assert.True(clip is not null, line.Id);
            Assert.True(File.Exists(Path.Combine(Tactics.Presentation.Tests.Board.Pack.Root, clip.Path)), clip.Path);
        });
    }

    [Fact]
    public void Every_line_of_every_chapter_finds_its_clip()
        => Assert.All(AllLines(), l => Assert.NotNull(Clips.ForLine(l.Line.Id)));

    [Fact]
    public void A_bark_isnt_a_scene_line_but_has_its_own_clip()
    {
        Assert.Null(Clips.ForLine("no_such_line"));
        Assert.NotNull(Clips.ForLine("bark_bree_tallow_camp_1"));
    }

    [Fact]
    public void A_line_with_no_clip_shows_a_caption_and_cuts()
    {
        var track = Running();

        var tick = track.Receive(Showing(2, Unvoiced()), T0);

        Assert.Null(tick.Say);                                             // captions only: the user's decision 1, StoryVoice.Fallback
        Assert.True(tick.Cut);
        Assert.Equal("The wind drops.", Assert.Single(tick.Screen!.Caption!.Rows).Single().Text);
    }

    [Fact]
    public void With_speech_as_the_fallback_a_line_with_no_clip_is_read_out()
    {
        var track = Track(SpeechFallback.Speech);
        track.Receive(Showing(1, Line("ch1_mayor_open_1")), T0);

        var say = track.Receive(Showing(2, Unvoiced()), T0).Say!;

        Assert.Null(say.Clip);
        Assert.Equal("The wind drops.", say.Text);
        Assert.Equal(StoryVoice.Theme, say.Theme);
    }

    [Fact]
    public void The_fallback_is_captions_until_the_user_decides()
        => Assert.Equal(SpeechFallback.Captions, StoryVoice.Fallback);

    [Fact]
    public void A_fresh_track_shows_the_line_and_doesnt_speak()
    {
        var tick = Track().Receive(Showing(7, Line("ch1_mayor_halden_2")), T0);

        Assert.Null(tick.Say);
        Assert.False(tick.Cut);
        Assert.Equal("Halden Varrow", tick.Screen!.Caption!.Speaker);
    }

    [Fact]
    public void The_next_line_after_joining_speaks()
    {
        var track = Track();
        track.Receive(Showing(7, Line("ch1_mayor_halden_2")), T0);

        Assert.Equal("ch1_mayor_halden_3", track.Receive(Showing(8, Line("ch1_mayor_halden_3")), T0).Say!.Line);
    }

    [Fact]
    public void The_same_line_again_isnt_said_again()
    {
        var track = Running();
        track.Receive(Showing(2, Line("ch1_mayor_open_2")), T0);

        var again = track.Receive(Showing(2, Line("ch1_mayor_open_2"), until: T0.AddSeconds(9)), T0);

        Assert.Null(again.Say);
        Assert.False(again.Cut);                                          // and its voice goes on
    }

    [Fact]
    public void A_jump_in_seq_speaks_only_the_newest()
    {
        var track = Running(3);

        var tick = track.Receive(Showing(6, Line("ch1_mayor_halden_1")), T0);

        Assert.Equal("ch1_mayor_halden_1", tick.Say!.Line);
        Assert.Null(track.Tick(1).Say);                                   // nothing missed is caught up on
    }

    [Fact]
    public void A_lower_seq_after_a_restart_is_a_new_track()
    {
        var track = Running(40);
        track.Receive(Showing(41, Line("ch1_mayor_open_2")), T0);

        var fresh = track.Receive(Showing(1, Line("ch1_mayor_open_1")), T0);    // another campaign, from its start

        Assert.Null(fresh.Say);                                           // a new track shows its first line
        Assert.True(fresh.Cut);                                           // and the old one's voice stops
        Assert.Equal(1, track.Seq);
        Assert.Equal("ch1_mayor_open_2", track.Receive(Showing(2, Line("ch1_mayor_open_2")), T0).Say!.Line);   // heard on the old track, new here
    }

    [Fact]
    public void A_server_restart_replays_its_line_silently()
    {
        var track = Running(4);
        Assert.NotNull(track.Receive(Showing(5, Line("ch1_mayor_open_3")), T0).Say);
        track.Receive(null, T0);                                          // Resuming: no scene

        var replay = track.Receive(Showing(6, Line("ch1_mayor_open_3")), T0);   // the saved line, with a new Seq

        Assert.Null(replay.Say);
        Assert.Equal("ch1_mayor_open_3", replay.Screen!.Caption!.Line);
        Assert.Equal("ch1_mayor_open_4", track.Receive(Showing(7, Line("ch1_mayor_open_4")), T0).Say!.Line);
    }

    [Fact]
    public void A_line_the_tv_joined_on_isnt_said_when_a_restart_shows_it_again()
    {
        var track = Track();
        track.Receive(Showing(5, Line("ch1_mayor_open_3")), T0);          // joined mid-line: shown, not said
        track.Receive(null, T0);

        Assert.Null(track.Receive(Showing(6, Line("ch1_mayor_open_3")), T0).Say);
    }

    [Fact]
    public void The_story_ending_cuts_the_voice()
    {
        var track = Running();
        track.Receive(Showing(2, Line("ch1_mayor_open_2")), T0);

        var end = track.Receive(null, T0);

        Assert.True(end.Cut);
        Assert.Null(end.Screen);
        Assert.False(track.Receive(null, T0).Cut);                        // once
    }

    [Fact]
    public void A_skip_into_the_gap_cuts_the_voice()
    {
        var track = Running();
        track.Receive(Showing(2, Line("ch1_mayor_open_2")), T0);

        var gap = track.Receive(Showing(2, null, States.Gap, title: "What the Bridge Cost"), T0);

        Assert.True(gap.Cut);
        Assert.Equal("What the Bridge Cost", gap.Screen!.Interlude);
    }

    [Fact]
    public void A_question_opening_on_its_line_doesnt_cut_it()
    {
        var track = Running();
        track.Receive(Showing(2, Line("ch1_mayor_halden_4")), T0);

        var asking = track.Receive(Showing(2, Line("ch1_mayor_halden_4"), States.Asking,
            Ask(true, 0, 2, [Option("goblins", "Who are these goblins?")], deadline: T0.AddSeconds(45))), T0);

        Assert.False(asking.Cut);
        Assert.Null(asking.Say);
        Assert.NotNull(asking.Screen!.Vote);
    }

    [Fact]
    public void Joining_mid_question_shows_the_panel_and_the_time_left_without_a_word()
    {
        var ask = Ask(true, 1, 2, [Option("a", "Yes.", "Jess"), Option("b", "No.")], deadline: T0.AddSeconds(12));

        var tick = Track().Receive(Showing(4, Line("ch1_mayor_halden_4"), States.Asking, ask), T0);

        Assert.Null(tick.Say);
        Assert.Equal("0:12", tick.Screen!.Vote!.Clock);
        Assert.Equal("1 of 2 voted", tick.Screen.Vote.Tally);
        Assert.Equal("ch1_mayor_halden_4", tick.Screen.Caption!.Line);
    }

    [Fact]
    public void A_roll_seen_being_picked_tumbles_then_lands()
    {
        var track = Running();
        var cards = Cards();
        track.Receive(Showing(2, Line("ch1_mayor_halden_4"), States.PickRoller,
            check: new StoryCheckView("persuasion", 15, "First, what's it worth to you?", cards, null, null, Deadline: T0.AddSeconds(20))), T0);

        var rolled = track.Receive(Rolled(2, cards, die: 14), T0);

        Assert.True(rolled.Tumbled);
        Assert.False(rolled.Screen!.Check!.Roll!.Landed);
        var landed = Enumerable.Range(0, 200).Select(_ => track.Tick(1 / 60.0)).First(t => t.Landed);
        Assert.Equal(14, landed.Screen!.Check!.Roll!.Face);
        Assert.Equal("14 + 5 = 19 vs DC 15", landed.Screen.Check.Roll.Sum);
        Assert.False(track.Tick(1).Landed);                               // once
    }

    [Fact]
    public void Joining_mid_roll_shows_it_landed()
    {
        var tick = Track().Receive(Rolled(9, Cards(), die: 3), T0);

        Assert.False(tick.Tumbled);
        Assert.True(tick.Screen!.Check!.Roll!.Landed);
        Assert.Equal(3, tick.Screen.Check.Roll.Face);
    }

    [Fact]
    public void A_roll_shown_again_after_a_restart_doesnt_tumble_again()
    {
        var track = Running();
        track.Receive(Rolled(2, Cards(), die: 11), T0);
        track.Receive(null, T0);

        var again = track.Receive(Rolled(3, Cards(), die: 11), T0);       // never rolled again: shown again

        Assert.False(again.Tumbled);
        Assert.True(again.Screen!.Check!.Roll!.Landed);
    }

    [Fact]
    public void The_vote_clock_counts_down_on_frame_time()
    {
        var track = Running();
        var ask = Ask(true, 0, 2, [Option("a", "Yes."), Option("b", "No.")], deadline: T0.AddSeconds(45));
        track.Receive(Showing(2, Line("ch1_mayor_halden_4"), States.Asking, ask), T0);

        Assert.Equal("0:45", track.Screen!.Vote!.Clock);
        track.Tick(30.5);
        Assert.Equal("0:15", track.Screen.Vote.Clock);
        track.Receive(Showing(2, Line("ch1_mayor_halden_4"), States.Asking, ask with { Voted = 1 }), T0.AddSeconds(40));   // a vote, same deadline
        Assert.Equal("0:15", track.Screen.Vote.Clock);                    // the frame clock holds: no jump from the wall clock
    }

    /// <summary>
    /// The property: whatever views a TV sees, from whenever it joins, through dropped views, server restarts and new campaigns, every line
    /// it speaks is the one showing in the view it came with, and it never speaks a line twice on one track.
    /// </summary>
    [Fact]
    public void A_tv_joining_anywhere_never_speaks_a_line_out_of_turn_or_twice()
    {
        var lines = AllLines().Select(l => LineOf(l.Line)).ToArray();
        for (var seed = 0; seed < 400; seed++)
        {
            var random = new Random(seed);
            var views = ServerStory(random, lines);
            var join = random.Next(views.Count);
            var track = Track(random.Next(4) == 0 ? SpeechFallback.Speech : SpeechFallback.Captions);
            var heard = new HashSet<string>();
            int? seq = null;
            foreach (var view in views.Skip(join).Where(_ => random.Next(5) > 0))    // a slow TV misses a fifth of them
            {
                if (view is not null && seq is { } s && view.Seq < s)
                    heard.Clear();                                        // a new campaign: a new track
                if (view is not null)
                    seq = view.Seq;
                var tick = track.Receive(view, T0);
                if (tick.Say is { } say)
                {
                    Assert.True(view?.Line?.Id == say.Line, $"seed {seed}: spoke {say.Line} over {view?.Line?.Id}");
                    Assert.True(heard.Add(say.Line), $"seed {seed}: spoke {say.Line} twice");
                }
                Assert.Null(track.Tick(random.NextDouble()).Say);
            }
        }
    }

    /// <summary>
    /// A server's story, as the views it sends: lines one after another with a new Seq each, questions on a node's last line, rolls, gaps,
    /// a restart that replays its saved line, and now and then a new campaign starting its Seq again.
    /// </summary>
    private static List<StoryView?> ServerStory(Random random, StoryLineView[] lines)
    {
        var views = new List<StoryView?>();
        var seq = random.Next(0, 50);
        var at = random.Next(lines.Length);
        for (var step = 0; step < 80; step++)
        {
            var line = lines[at % lines.Length];
            switch (random.Next(10))
            {
                case 0:     // a restart: no scene while it resumes, then the saved line again
                    views.Add(null);
                    views.Add(Showing(++seq, line));
                    break;
                case 1:     // a question, a tie-break, maybe a roll
                    var ask = Ask(true, 1, 2, [Option("a", "Yes.", "Ana"), Option("b", "No.")], deadline: T0.AddSeconds(45));
                    views.Add(Showing(seq, line, States.Asking, ask));
                    views.Add(Showing(seq, line, States.TieBreak, ask with { Tie = true, Decider = "Ana" }));
                    if (random.Next(2) == 0)
                    {
                        views.Add(Showing(seq, line, States.PickRoller,
                            check: new StoryCheckView("insight", 12, "Yes.", Cards(), null, null, Deadline: T0.AddSeconds(20))));
                        views.Add(Rolled(seq, Cards(), random.Next(1, 21)));
                    }
                    at++;
                    views.Add(Showing(++seq, lines[at % lines.Length]));
                    break;
                case 2:     // the scene ends: a gap, then the next one
                    views.Add(Showing(seq, null, States.Gap));
                    at++;
                    views.Add(Showing(++seq, lines[at % lines.Length]));
                    break;
                case 3 when step > 40:     // another campaign on this TV
                    seq = random.Next(0, 3);
                    views.Add(Showing(++seq, line));
                    break;
                default:    // the next line; sometimes the same line twice in a row, as a node that says it again would
                    at += random.Next(3) == 0 ? 0 : 1;
                    views.Add(Showing(++seq, lines[at % lines.Length]));
                    break;
            }
        }
        return views;
    }

    private static System.Collections.Immutable.ImmutableArray<StoryCardView> Cards()
        => [Card("pr_a", "Kara", "Jess", 1), Card("pr_b", "Wren", "Priya", 5, "Proficient · CHA 14", votes: 2)];

    private static StoryView Rolled(int seq, System.Collections.Immutable.ImmutableArray<StoryCardView> cards, int die)
        => Showing(seq, null, States.Rolled,
            check: new StoryCheckView("persuasion", 15, "First, what's it worth to you?", cards, "pr_b", Rolls.Flat(die, 5, 15)));
}
