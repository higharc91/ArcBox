using Tactics.Presentation.Story;
using static Tactics.Presentation.Tests.Story.StoryFeed;
using States = Tactics.Game.TacticsGame.StoryStates;

namespace Tactics.Presentation.Tests.Story;

/// <summary><see cref="VoteClock"/>: the countdown on the vote panel, read from the server's deadline and counted on the TV's frames.</summary>
public sealed class VoteClockTests
{
    [Theory]
    [InlineData(-300)]      // the TV's clock five minutes slow
    [InlineData(300)]       // and five minutes fast
    [InlineData(-7)]
    [InlineData(4)]
    public void The_vote_clock_survives_a_skewed_tv_clock(int skewSeconds)
    {
        var tv = T0.AddSeconds(skewSeconds);       // the server's T0, on the TV's clock
        var clock = new VoteClock();

        clock.Read(T0.AddSeconds(45), tv, 45, opened: true);     // a vote opens: 45 s from the server's now
        Assert.Equal("0:45", clock.Text);
        clock.Tick(20);
        Assert.Equal("0:25", clock.Text);

        clock.Read(T0.AddSeconds(22), tv.AddSeconds(20), 45);   // everyone's in: the settle closes it 2 s from now
        Assert.Equal("0:02", clock.Text);
    }

    [Fact]
    public void Joining_with_a_skewed_clock_is_kept_inside_the_question()
    {
        var clock = new VoteClock();

        clock.Read(T0.AddSeconds(30), T0.AddSeconds(-600), 45);   // nothing learned yet, and ten minutes slow
        Assert.Equal("0:45", clock.Text);

        clock.Read(T0.AddSeconds(10), T0.AddSeconds(600), 10);    // ten minutes fast
        Assert.Equal("0:00", clock.Text);
    }

    [Fact]
    public void A_question_that_waits_on_its_line_doesnt_teach_the_skew()
    {
        var track = new StoryTrack(Clips, Art);
        track.Receive(Showing(1, Line("ch1_mayor_open_1")), T0);
        var line = T0.AddSeconds(50);                             // a line longer than the vote: the vote waits for it to be heard
        track.Receive(Showing(2, Line("ch1_mayor_halden_4"), States.Asking,
            Ask(true, 0, 2, [Option("a", "Yes.")], deadline: line), until: line), T0);

        Assert.Equal("0:45", track.Screen!.Vote!.Clock);          // clamped to the vote's length, and no skew learned from it
        track.Receive(Showing(2, Line("ch1_mayor_halden_4"), States.Asking,
            Ask(true, 2, 2, [Option("a", "Yes.", "A", "B")], deadline: T0.AddSeconds(3)), until: line), T0.AddSeconds(1));
        Assert.Equal("0:02", track.Screen.Vote.Clock);
    }

    [Fact]
    public void A_track_learns_the_skew_when_it_sees_a_vote_open()
    {
        var track = new StoryTrack(Clips, Art);
        var tv = T0.AddSeconds(-90);                              // the TV runs 90 s slow
        track.Receive(Showing(1, Line("ch1_mayor_open_1")), tv);
        track.Receive(Showing(2, Line("ch1_mayor_halden_4"), States.Asking,
            Ask(true, 0, 2, [Option("a", "Yes."), Option("b", "No.")], deadline: T0.AddSeconds(45))), tv);
        Assert.Equal("0:45", track.Screen!.Vote!.Clock);

        track.Tick(30);
        track.Receive(Showing(2, Line("ch1_mayor_halden_4"), States.TieBreak,
            Ask(true, 2, 2, [Option("a", "Yes.", "A"), Option("b", "No.", "B")], tie: true, decider: "A", deadline: T0.AddSeconds(40))), tv.AddSeconds(30));

        Assert.Equal("0:10", track.Screen.Vote.Clock);
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(0.2, "0:01")]
    [InlineData(14, "0:14")]
    [InlineData(13.01, "0:14")]
    [InlineData(65, "1:05")]
    public void The_clock_rounds_up(double seconds, string text) => Assert.Equal(text, VoteClock.Format(seconds));

    [Fact]
    public void No_deadline_no_clock()
    {
        var clock = new VoteClock();
        clock.Read(T0, T0, 10);
        clock.Read(null, T0, 10);

        Assert.Null(clock.Text);
        clock.Tick(1);
        Assert.Null(clock.Remaining);
    }
}
