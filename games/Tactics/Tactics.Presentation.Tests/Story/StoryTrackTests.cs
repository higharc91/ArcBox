using System.Collections.Immutable;
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
        var lines = AllLines().Where(l => !l.Scene.Camp).Where(l => l.Scene.Id.StartsWith("ch1_", StringComparison.Ordinal)).Select(l => l.Line).ToArray();

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
        => Assert.All(AllLines().Where(l => !l.Scene.Camp), l => Assert.NotNull(Clips.ForLine(l.Line.Id)));

    [Fact]
    public void Camp_reflections_use_existing_portraits_and_the_completed_voice_batch()
    {
        var lines = AllLines().Where(l => l.Scene.Camp).ToArray();
        Assert.Equal(8, lines.Length);
        foreach (var (_, line) in lines)
        {
            Assert.NotNull(Clips.ForLine(line.Id));
            var track = Running();
            var tick = track.Receive(Showing(2, LineOf(line)), T0);
            Assert.Equal(line.Id, tick.Say!.Line);
            Assert.Equal(line.Id, tick.Screen!.Caption!.Line);
        }
    }

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
        Assert.Equal(TacticsVoices.Narrator, say.Theme);
    }

    [Fact]
    public void Named_and_townsfolk_live_fallbacks_use_their_registered_cast()
    {
        foreach (var speaker in new[] { "halden_varrow", "townsfolk_farmer", "townsfolk_goblin_spear" })
        {
            var line = new StoryLineView("missing_clip", speaker, speaker, null, "idle", "Hello.");
            Assert.Equal(TacticsVoices.Npc(speaker), StoryVoice.For(line, null, SpeechFallback.Speech)!.Theme);
        }
    }

    [Fact]
    public void The_npc_slice_uses_live_speech_for_an_unavailable_clip()
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
    public void A_tv_that_watched_the_walk_up_says_the_first_line()
    {
        var track = Track();
        track.Receive(null, T0, watching: true);                        // walking up to the "!"

        var first = track.Receive(Showing(1, Line("ch1_mayor_open_1")), T0);

        Assert.Equal("ch1_mayor_open_1", first.Say!.Line);
    }

    [Fact]
    public void A_tv_that_came_in_on_a_story_waiting_to_resume_says_nothing_of_its_saved_line()
    {
        var track = Track();
        track.Receive(null, T0, watching: false);                       // Resuming: the saved line comes back with a new Seq

        Assert.Null(track.Receive(Showing(9, Line("ch1_mayor_open_3")), T0).Say);
        Assert.Equal("ch1_mayor_open_4", track.Receive(Showing(10, Line("ch1_mayor_open_4")), T0).Say!.Line);
    }

    [Fact]
    public void A_host_view_says_whether_the_tv_is_watching()
    {
        var walking = new TacticsHostView(nameof(TacticsPhase.Exploration), "", null, 1, [], null, null, null, null);
        var resuming = walking with { Phase = nameof(TacticsPhase.Resuming) };

        var watched = Track();
        watched.Receive(walking, T0);
        var waited = Track();
        waited.Receive(resuming, T0);

        Assert.NotNull(watched.Receive(walking with { Phase = nameof(TacticsPhase.Story), Story = Showing(1, Line("ch1_mayor_open_1")) }, T0).Say);
        Assert.Null(waited.Receive(walking with { Phase = nameof(TacticsPhase.Story), Story = Showing(1, Line("ch1_mayor_open_1")) }, T0).Say);
    }

    [Fact]
    public void A_new_campaign_at_a_higher_seq_is_a_new_story()
    {
        var track = Track();
        track.Receive(null, T0, watching: true);
        Assert.NotNull(track.Receive(Showing(1, Line("ch1_mayor_open_1"), campaign: "embers"), T0).Say);
        Assert.NotNull(track.Receive(Showing(2, Line("ch1_mayor_open_2"), campaign: "embers"), T0).Say);
        track.Receive(null, T0, watching: true);                        // another party starts its own campaign, from the start

        var again = track.Receive(Showing(30, Line("ch1_mayor_open_1"), campaign: "ashes"), T0);

        Assert.Equal("ch1_mayor_open_1", again.Say!.Line);              // heard in the other story: new in this one
        Assert.Equal("ch1_mayor_open_2", track.Receive(Showing(31, Line("ch1_mayor_open_2"), campaign: "ashes"), T0).Say!.Line);
    }

    /// <summary>
    /// A crash that lost the server's last writes: it resumes on the line it saved, under a lower Seq. That's the server's truth, so it's shown,
    /// and the question its line opens shows with its clock, rather than the TV sitting on a stale line for the vote's 45 s.
    /// </summary>
    [Fact]
    public void A_lower_seq_after_a_crash_is_shown_with_its_question()
    {
        var track = Track();
        track.Receive(null, T0, watching: true);
        track.Receive(Showing(10, Line("ch1_mayor_halden_4"), campaign: "embers"), T0);
        track.Receive(Showing(11, Line("ch1_mayor_goblins_1"), campaign: "embers"), T0);
        track.Receive(Showing(12, Line("ch1_mayor_goblins_2"), campaign: "embers"), T0);

        var back = track.Receive(Showing(11, Line("ch1_mayor_halden_4"), States.Asking,
            Ask(false, 0, 1, [Option("goblins", "Who are these goblins?")], decider: "Ana", deadline: T0.AddSeconds(30), span: 30), campaign: "embers"), T0);

        Assert.Equal(11, track.Seq);
        Assert.Equal("ch1_mayor_halden_4", back.Screen!.Caption!.Line);
        Assert.Equal("0:30", back.Screen.Vote!.Clock);
        Assert.Null(back.Say);                                            // heard before the crash: shown, not said again
        Assert.True(back.Cut);                                            // and the line it went back from stops
        Assert.Equal("ch1_mayor_goblins_1", track.Receive(Showing(12, Line("ch1_mayor_goblins_1"), campaign: "embers"), T0).Screen!.Caption!.Line);
    }

    [Fact]
    public void A_lower_seq_with_a_line_not_yet_heard_says_it()
    {
        var track = Running(20);
        track.Receive(Showing(21, Line("ch1_mayor_open_2")), T0);

        Assert.Equal("ch1_mayor_open_3", track.Receive(Showing(19, Line("ch1_mayor_open_3")), T0).Say!.Line);
    }

    [Fact]
    public void After_the_connection_drops_the_first_view_only_shows()
    {
        var track = Running(3);
        track.Rejoin();

        var back = track.Receive(Showing(6, Line("ch1_mayor_halden_1")), T0);

        Assert.Null(back.Say);                                            // it can't be started partway
        Assert.Equal("ch1_mayor_halden_1", back.Screen!.Caption!.Line);
        Assert.Equal("ch1_mayor_halden_2", track.Receive(Showing(7, Line("ch1_mayor_halden_2")), T0).Say!.Line);
    }

    [Fact]
    public void A_cut_names_the_story_line_it_stops()
    {
        var track = Running();
        var said = track.Receive(Showing(2, Line("ch1_mayor_open_2")), T0).Say!;

        var gap = track.Receive(Showing(2, null, States.Gap), T0);

        Assert.True(gap.Cut);
        Assert.Equal(said.Text, gap.Stopping);
    }

    [Theory]
    [InlineData(true, "It used to be warmer.", "It used to be warmer.", true)]
    [InlineData(true, " It used to be warmer. ", "It used to be warmer.", true)]
    [InlineData(false, "It used to be warmer.", "It used to be warmer.", false)]      // the clip has ended
    [InlineData(true, "First blood!", "It used to be warmer.", false)]               // commentary since: not the story's
    [InlineData(true, "It used to be warmer.", null, false)]
    public void A_cut_stops_only_the_storys_own_line(bool speaking, string playing, string? story, bool stops)
        => Assert.Equal(stops, StoryVoice.Owns(speaking, playing, story));
    [Fact]
    public void The_server_says_how_long_its_votes_run()
    {
        var track = new StoryTrack(Clips, Art);                           // the TV's default is a 45 s vote
        var tv = T0.AddSeconds(-8);                                       // and its clock is 8 s slow
        track.Receive(Showing(1, Line("ch1_mayor_open_1")), tv);
        track.Receive(Showing(2, Line("ch1_mayor_halden_4"), States.Asking,
            Ask(true, 0, 2, [Option("a", "Yes."), Option("b", "No.")], deadline: T0.AddSeconds(60), span: 60)), tv);

        Assert.Equal("1:00", track.Screen!.Vote!.Clock);
        track.Tick(10);
        track.Receive(Showing(2, Line("ch1_mayor_halden_4"), States.Asking,
            Ask(true, 2, 2, [Option("a", "Yes.", "A", "B"), Option("b", "No.")], deadline: T0.AddSeconds(12), span: 60)), tv.AddSeconds(10));
        Assert.Equal("0:02", track.Screen.Vote.Clock);                    // the settle, on the server's time
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
    public void Another_campaign_is_a_new_track()
    {
        var track = Track();
        track.Receive(Showing(40, Line("ch1_mayor_open_1"), campaign: "embers"), T0);
        track.Receive(Showing(41, Line("ch1_mayor_open_2"), campaign: "embers"), T0);

        var fresh = track.Receive(Showing(1, Line("ch1_mayor_open_1"), campaign: "ashes"), T0);   // another campaign, mid-line

        Assert.Null(fresh.Say);                                           // a new track shows its first view
        Assert.True(fresh.Cut);                                           // and the old one's voice stops
        Assert.Equal(1, track.Seq);
        Assert.Equal("ch1_mayor_open_2", track.Receive(Showing(2, Line("ch1_mayor_open_2"), campaign: "ashes"), T0).Say!.Line);   // heard in the other
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

    /// <summary>
    /// Task 11's chapter run, at line 3 of Old Maud's scene: the server moved on to line 4 and saved it, and stopped before the TV heard of it.
    /// The TV's connection dropped, it came back to Resuming, and once the party was back the saved line started again with a new Seq. That
    /// line was never said, and it starts from its beginning in front of a TV that watched the story wait: it's said, as a new line is. Before
    /// the fix the reconnect's "first view only shows" swallowed it, and Maud's question was never heard.
    /// </summary>
    [Fact]
    public void A_line_the_tv_never_heard_is_said_when_a_restart_starts_it_again()
    {
        var resuming = new TacticsHostView(nameof(TacticsPhase.Resuming), "", null, 1, [], null, null, null, null);
        var track = Running(4);
        Assert.NotNull(track.Receive(Showing(5, Line("ch1_mayor_open_3")), T0).Say);
        track.Rejoin();                                                   // the server went away; line 4 started (seq 6) unseen
        track.Receive(resuming, T0);

        var replay = track.Receive(resuming with { Phase = nameof(TacticsPhase.Story), Story = Showing(7, Line("ch1_mayor_open_4")) }, T0);

        Assert.Equal("ch1_mayor_open_4", replay.Say!.Line);
    }

    /// <summary>
    /// Review: the TV saw the story wait at Resuming, then dropped again while it waited. The party came back without it, and its line was
    /// 3 s under way when the TV returned: that's a TV joining mid-question, so nothing is said, and the clock shows the time left, not the
    /// question's whole span as if the TV had watched it open.
    /// </summary>
    [Fact]
    public void A_tv_that_dropped_while_the_story_waited_to_resume_joins_its_line_silently()
    {
        var resuming = new TacticsHostView(nameof(TacticsPhase.Resuming), "", null, 1, [], null, null, null, null);
        var track = Running(4);
        Assert.NotNull(track.Receive(Showing(5, Line("ch1_mayor_open_3")), T0).Say);
        track.Rejoin();
        track.Receive(resuming, T0);
        track.Rejoin();                                                   // gone again, while the story waits

        var ask = Ask(true, 0, 4, [Option("a", "Yes."), Option("b", "No.")], deadline: T0.AddSeconds(12), span: 45);
        var back = track.Receive(resuming with { Phase = nameof(TacticsPhase.Story), Story = Showing(7, Line("ch1_mayor_open_4"), States.Asking, ask) },
            T0.AddSeconds(3));

        Assert.Null(back.Say);
        Assert.Equal("0:09", back.Screen!.Vote!.Clock);
    }

    /// <summary>The same restart, on a line the TV had said: the replay is shown, never said twice.</summary>
    [Fact]
    public void A_line_the_tv_heard_isnt_said_again_when_a_restart_starts_it_again()
    {
        var resuming = new TacticsHostView(nameof(TacticsPhase.Resuming), "", null, 1, [], null, null, null, null);
        var track = Running(4);
        Assert.NotNull(track.Receive(Showing(5, Line("ch1_mayor_open_3")), T0).Say);
        track.Rejoin();
        track.Receive(resuming, T0);

        var replay = track.Receive(resuming with { Phase = nameof(TacticsPhase.Story), Story = Showing(6, Line("ch1_mayor_open_3")) }, T0);

        Assert.Null(replay.Say);
        Assert.Equal("ch1_mayor_open_3", replay.Screen!.Caption!.Line);
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
    /// The property: whatever views a TV sees, from whenever it joins, through dropped and late views, server restarts and new campaigns, every
    /// line it speaks is the one showing in the view it came with, and it never speaks a line twice in one campaign. And it isn't silent for
    /// nothing: a TV that watched the party walk up says the scene's first line.
    /// </summary>
    [Fact]
    public void A_tv_joining_anywhere_never_speaks_a_line_out_of_turn_or_twice()
    {
        var lines = AllLines().Select(l => LineOf(l.Line)).ToArray();
        var firstLinesSaid = 0;
        for (var seed = 0; seed < 400; seed++)
        {
            var random = new Random(seed);
            var feed = ServerStory(random, lines);
            var join = random.Next(feed.Count);
            var fallback = random.Next(4) == 0 ? SpeechFallback.Speech : SpeechFallback.Captions;
            var track = Track(fallback);
            var heard = new HashSet<(string?, string)>();
            for (var i = 0; i + 1 < feed.Count; i++)
                if (random.Next(12) == 0)
                    (feed[i], feed[i + 1]) = (feed[i + 1], feed[i]);                  // now and then two come the wrong way round
            var shown = feed.Skip(join).Where(_ => random.Next(5) > 0).ToList();    // and a slow TV misses a fifth of them
            var watched = false;
            foreach (var (view, watching) in shown)
            {
                var tick = track.Receive(view, T0, watching);
                if (tick.Say is { } say)
                {
                    Assert.True(view?.Line?.Id == say.Line, $"seed {seed}: spoke {say.Line} over {view?.Line?.Id}");
                    Assert.True(heard.Add((view!.Campaign, say.Line)), $"seed {seed}: spoke {say.Line} twice");
                }
                if (watched && view is { Seq: 1, State: States.Lines, Line: { } first })
                {
                    if (Clips.ForLine(first.Id) is null && fallback == SpeechFallback.Captions)
                    {
                        Assert.Null(tick.Say);
                        Assert.Equal(first.Id, tick.Screen!.Caption!.Line);
                        continue;
                    }
                    Assert.True(tick.Say?.Line == first.Id, $"seed {seed}: the walk-up's first line went unsaid");
                    firstLinesSaid++;
                }
                watched = view is null && watching;
                Assert.Null(track.Tick(random.NextDouble()).Say);
            }
        }
        Assert.True(firstLinesSaid > 20, $"only {firstLinesSaid} walk-ups seen");
    }

    /// <summary>
    /// A server's story, as the views it sends, each with whether the TV sees the party then: lines one after another with a new Seq each,
    /// questions on a node's last line, rolls, gaps, walking between beats, a restart that waits to resume and replays its saved line, and now
    /// and then a new campaign, which walks up to its first line at Seq 1 or goes on from its own saved Seq.
    /// </summary>
    private static List<(StoryView? View, bool Watching)> ServerStory(Random random, StoryLineView[] lines)
    {
        var views = new List<(StoryView?, bool)>();
        var campaign = 0;
        var seq = random.Next(0, 50);
        var at = random.Next(lines.Length);
        StoryView Say(StoryLineView? line, int n, string state = States.Lines, StoryAskView? ask = null, StoryCheckView? check = null)
            => Showing(n, line, state, ask, check, campaign: $"c{campaign}");
        void Add(StoryView? view, bool watching = false) => views.Add((view, watching));
        for (var step = 0; step < 80; step++)
        {
            var line = lines[at % lines.Length];
            switch (random.Next(11))
            {
                case 0:     // a restart: no scene while it waits to resume, then the saved line again
                    Add(null, watching: false);
                    Add(Say(line, ++seq));
                    break;
                case 1:     // a question, a tie-break, maybe a roll
                    var ask = Ask(true, 1, 2, [Option("a", "Yes.", "Ana"), Option("b", "No.")], deadline: T0.AddSeconds(45), span: 45);
                    Add(Say(line, seq, States.Asking, ask));
                    Add(Say(line, seq, States.TieBreak, ask with { Tie = true, Decider = "Ana", Span = 10 }));
                    if (random.Next(2) == 0)
                    {
                        Add(Say(line, seq, States.PickRoller, check: new StoryCheckView("insight", 12, "Yes.", Cards(), null, null, Deadline: T0.AddSeconds(20), Span: 20)));
                        Add(Say(null, seq, States.Rolled, check: new StoryCheckView("persuasion", 15, "Yes.", Cards(), "pr_b", Rolls.Flat(random.Next(1, 21), 5, 15))));
                    }
                    at++;
                    Add(Say(lines[at % lines.Length], ++seq));
                    break;
                case 2:     // the scene ends: a gap, then the next one
                    Add(Say(null, seq, States.Gap));
                    at++;
                    Add(Say(lines[at % lines.Length], ++seq));
                    break;
                case 3:     // the beat ends: the party walks to the next, and it starts
                    Add(null, watching: true);
                    Add(null, watching: true);
                    at++;
                    Add(Say(lines[at % lines.Length], ++seq));
                    break;
                case 4 when step > 30:     // another campaign on this TV, from its start or from where it was saved
                    campaign++;
                    seq = random.Next(2) == 0 ? 0 : random.Next(0, 200);
                    Add(null, watching: true);
                    at = random.Next(lines.Length);
                    Add(Say(lines[at % lines.Length], ++seq));
                    break;
                default:    // the next line; sometimes the same line twice in a row, as a node that says it again would
                    at += random.Next(3) == 0 ? 0 : 1;
                    Add(Say(lines[at % lines.Length], ++seq));
                    break;
            }
        }
        return views;
    }

    // ---- The no-rebuild contract: the TV's boards redraw only when what they show changes ------------------------------------------

    /// <summary>
    /// The TV ticks the track every frame, and its boards skip a screen equal (<c>==</c>) to the one up. So two frames with nothing new must
    /// give equal parts, or every word label is torn down and made again each frame.
    /// </summary>
    [Fact]
    public void Two_frames_of_a_line_give_an_equal_caption()
    {
        var track = Running();
        track.Receive(Showing(2, Line("ch1_mayor_open_2")), T0);

        var a = track.Tick(1 / 60.0).Screen!;
        var b = track.Tick(1 / 60.0).Screen!;

        Assert.True(a.Caption == b.Caption);
        Assert.True(a == b);
        Assert.Same(a, b);                                                // nothing new: not even wrapped again
    }

    [Fact]
    public void Two_frames_of_a_question_give_an_equal_caption_and_vote_until_the_clock_moves()
    {
        var track = Running();
        var ask = Ask(true, 1, 3, [Option("a", "Yes, gladly.", "Jess"), Check("b", "Pay him, then.", "persuasion", 15, "Priya", "Sam")],
            away: ["Dave"], deadline: T0.AddSeconds(20));
        track.Receive(Showing(2, Line("ch1_mayor_halden_4"), States.Asking, ask), T0);

        var a = track.Tick(0.1).Screen!;
        var b = track.Tick(0.1).Screen!;
        Assert.True(a.Caption == b.Caption);
        Assert.True(a.Vote == b.Vote);

        var later = track.Tick(1).Screen!;                                // a second on: only the clock's words change
        Assert.False(b.Vote == later.Vote);
        Assert.True(b.Vote! with { Clock = later.Vote!.Clock } == later.Vote);
        Assert.True(b.Caption == later.Caption);
    }

    [Fact]
    public void Two_frames_of_picking_who_rolls_give_an_equal_check()
    {
        var track = Running();
        track.Receive(Showing(2, Line("ch1_mayor_halden_4"), States.PickRoller,
            check: new StoryCheckView("persuasion", 15, "First, what's it worth to you?", Cards(), null, null, Deadline: T0.AddSeconds(20))), T0);

        var a = track.Tick(0.1).Screen!;
        var b = track.Tick(0.1).Screen!;

        Assert.True(a.Check == b.Check);
    }

    [Fact]
    public void Two_frames_of_a_landed_roll_give_an_equal_check()
    {
        var track = Running();
        track.Receive(Rolled(2, Cards(), die: 14), T0);                   // joined mid-roll: shown landed

        var a = track.Tick(1 / 60.0).Screen!;
        var b = track.Tick(1 / 60.0).Screen!;

        Assert.True(a.Check == b.Check);
    }

    [Fact]
    public void The_same_view_drawn_twice_gives_equal_parts_with_equal_hashes()
    {
        var ask = Ask(true, 1, 3, [Option("a", "Yes, gladly.", "Jess"), Option("b", "No.")], away: ["Dave"], deadline: T0.AddSeconds(20));
        var asking = Showing(2, Line("ch1_mayor_halden_4"), States.Asking, ask);
        var picking = Showing(3, Line("ch1_mayor_halden_4"), States.PickRoller,
            check: new StoryCheckView("persuasion", 15, "First, what's it worth to you?", Cards(), null, null, Deadline: T0.AddSeconds(20)));

        foreach (var view in new[] { asking, picking, Rolled(4, Cards(), die: 7) })
        {
            var (a, b) = (StoryScreen.Of(view, Art, "0:12"), StoryScreen.Of(view, Art, "0:12"));
            Assert.Equal(a, b);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
            Assert.Equal(a.Caption?.GetHashCode(), b.Caption?.GetHashCode());
            Assert.Equal(a.Vote?.GetHashCode(), b.Vote?.GetHashCode());
            Assert.Equal(a.Check?.GetHashCode(), b.Check?.GetHashCode());
        }
    }

    [Fact]
    public void What_changes_on_a_board_still_tells_it_apart()
    {
        var options = ImmutableArray.Create(Option("a", "Yes.", "Jess"), Option("b", "No."));
        var view = Showing(2, Line("ch1_mayor_halden_4"), States.Asking, Ask(true, 1, 2, options, deadline: T0.AddSeconds(20)));
        var screen = StoryScreen.Of(view, Art, "0:12");

        var voted = StoryScreen.Of(view with { Ask = view.Ask! with { Options = [Option("a", "Yes.", "Jess"), Option("b", "No.", "Sam")] } }, Art, "0:12");
        Assert.NotEqual(screen.Vote, voted.Vote);                          // another voter's pip
        var relabelled = StoryScreen.Of(view with { Ask = view.Ask! with { Options = [Option("a", "Yes.", "Jess"), Option("b", "Never.")] } }, Art, "0:12");
        Assert.NotEqual(screen.Vote, relabelled.Vote);                     // another option's words
        var reworded = StoryScreen.Of(view with { Line = view.Line! with { Text = "Will you help?" } }, Art, "0:12");
        Assert.NotEqual(screen.Caption, reworded.Caption);                 // another line's words, same id
        var told = StoryScreen.Of(view with { Line = view.Line! with { Text = "Will you help?" }, Ask = null, State = States.Lines }, Art);
        Assert.NotEqual(reworded.Caption, told.Caption);                    // the same words, but the question no longer lit

        var cards = Cards();
        var check = StoryScreen.Of(Rolled(4, cards, die: 7), Art);
        var revoted = StoryScreen.Of(Rolled(4, [cards[0], cards[1] with { Votes = 3 }], die: 7), Art);
        Assert.NotEqual(check.Check, revoted.Check);                       // a card's votes
    }

    /// <summary>These compare by hand (their arrays by what they hold): a field added to one must be added to its Equals and GetHashCode.</summary>
    [Theory]
    [InlineData(typeof(StoryCaption), "Line Speaker Portrait Rows Narrator")]
    [InlineData(typeof(VoteRow), "Id Letter Label Detail Voters")]
    [InlineData(typeof(VotePanel), "Heading Party Tie Clock Rows Tally Away Scale Height")]
    [InlineData(typeof(CheckPanel), "Heading Skill Dc Title Cards Clock Footer Roll")]
    public void A_board_compared_by_hand_has_the_fields_its_equality_reads(Type type, string fields)
        => Assert.Equal(fields, string.Join(" ", type.GetProperties().Where(p => p.DeclaringType == type && p.Name != "EqualityContract").Select(p => p.Name)));

    private static System.Collections.Immutable.ImmutableArray<StoryCardView> Cards()
        => [Card("pr_a", "Kara", "Jess", 1), Card("pr_b", "Wren", "Priya", 5, "Proficient · CHA 14", votes: 2)];

    private static StoryView Rolled(int seq, System.Collections.Immutable.ImmutableArray<StoryCardView> cards, int die)
        => Showing(seq, null, States.Rolled,
            check: new StoryCheckView("persuasion", 15, "First, what's it worth to you?", cards, "pr_b", Rolls.Flat(die, 5, 15)));
}
