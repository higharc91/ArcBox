using ArcBox.Host.Core.Audio;

namespace ArcBox.Host.Core.Tests.Audio;

public class NarrationQueueTests
{
    private static Narration Line(string text, double at, NarrationPriority p = NarrationPriority.Commentary) =>
        new(text, "redline.announcer", p, at);

    private static Narration Question(string text, double at) =>
        new(text, "redline.questions", NarrationPriority.Question, at);

    [Fact]
    public void Plays_one_at_a_time_in_order()
    {
        var q = new NarrationQueue();
        Assert.False(q.Enqueue(Line("one", 0)));
        Assert.False(q.Enqueue(Line("two", 0)));

        Assert.Equal("one", q.Next(0)?.Text);
        Assert.Null(q.Next(0.5));            // still speaking "one"
        q.Finish();
        Assert.Equal("two", q.Next(1)?.Text);
        q.Finish();
        Assert.Null(q.Next(2));
    }

    [Fact]
    public void Question_preempts_commentary()
    {
        var q = new NarrationQueue();
        q.Enqueue(Line("Round 4. Geography, medium.", 0));
        q.Enqueue(Line("A later line", 0));
        Assert.Equal("Round 4. Geography, medium.", q.Next(0)?.Text);

        // The question cuts the commentary off and goes ahead of the line still waiting.
        Assert.True(q.Enqueue(Question("Which U.S. state has the longest coastline?", 1)));
        q.Finish();
        Assert.Equal(NarrationPriority.Question, q.Next(1)?.Priority);
        q.Finish();
        Assert.Equal("A later line", q.Next(2)?.Text);
    }

    [Fact]
    public void Commentary_never_cuts_off_the_question()
    {
        var q = new NarrationQueue();
        q.Enqueue(Question("The question", 0));
        Assert.Equal("The question", q.Next(0)?.Text);

        Assert.False(q.Enqueue(Line("The answer is Alaska.", 1)));
        Assert.Null(q.Next(1));
        q.Finish();
        Assert.Equal("The answer is Alaska.", q.Next(3)?.Text);
    }

    [Fact]
    public void Stale_commentary_dropped()
    {
        var q = new NarrationQueue();
        q.Enqueue(Question("A long question", 0));
        q.Next(0);
        q.Enqueue(Line("Too late", 1));
        q.Enqueue(Line("Just in time", 3));
        q.Finish();

        var dropped = new List<Narration>();
        // 5.5 s: "Too late" waited 4.5 s (dropped), "Just in time" 2.5 s.
        Assert.Equal("Just in time", q.Next(5.5, dropped)?.Text);
        Assert.Equal(["Too late"], dropped.Select(d => d.Text));
    }

    [Fact]
    public void Commentary_at_exactly_four_seconds_still_plays()
    {
        var q = new NarrationQueue();
        q.Enqueue(Line("Edge", 0));
        Assert.Equal("Edge", q.Next(NarrationQueue.StaleSeconds)?.Text);
    }

    [Fact]
    public void A_question_is_never_dropped_as_stale()
    {
        var q = new NarrationQueue();
        q.Enqueue(Question("Slow start", 0));
        Assert.Equal("Slow start", q.Next(30)?.Text);
    }

    [Fact]
    public void A_newer_question_replaces_one_still_waiting_and_cuts_off_one_playing()
    {
        var q = new NarrationQueue();
        q.Enqueue(Question("Round 3", 0));
        q.Next(0);
        Assert.True(q.Enqueue(Question("Round 4", 1)));
        q.Enqueue(Question("Round 5", 2));
        q.Finish();

        Assert.Equal("Round 5", q.Next(2)?.Text);
        q.Finish();
        Assert.Null(q.Next(2));
    }

    private static Narration Opener(string text, double at) =>
        new(text, "redline.announcer", NarrationPriority.Commentary, at, Opener: true);

    [Fact]
    public void A_question_waits_behind_a_playing_opener()
    {
        var q = new NarrationQueue();
        q.Enqueue(Opener("Qualifying! Three questions, fastest correct answers take pole.", 0));
        Assert.Equal("Qualifying! Three questions, fastest correct answers take pole.", q.Next(0)?.Text);

        // Lap 1's question arrives at once: it does not cut the opener off, and plays as soon as it ends.
        Assert.False(q.Enqueue(Question("Lap 1", 0)));
        Assert.Null(q.Next(1));
        q.Finish();
        Assert.Equal("Lap 1", q.Next(3.4)?.Text);
    }

    [Fact]
    public void A_question_goes_behind_a_waiting_opener_but_still_cuts_other_commentary()
    {
        var q = new NarrationQueue();
        q.Enqueue(Line("Still talking", 0));
        q.Next(0);
        q.Enqueue(Opener("Opener", 0));
        q.Enqueue(Line("Later", 0));

        Assert.True(q.Enqueue(Question("Lap 1", 1)));       // cuts the ordinary line playing...
        q.Finish();
        Assert.Equal("Opener", q.Next(1)?.Text);            // ...but the opener goes first,
        q.Finish();
        Assert.Equal("Lap 1", q.Next(4)?.Text);             // then the question, ahead of the other commentary.
        q.Finish();
        Assert.Equal("Later", q.Next(4)?.Text);
    }

    [Fact]
    public void Without_an_opener_a_question_still_preempts_as_before()
    {
        var q = new NarrationQueue();
        q.Enqueue(Line("Round 1. Geography, easy.", 0));
        q.Next(0);
        q.Enqueue(Line("Waiting", 0));
        Assert.True(q.Enqueue(Question("Q", 0.5)));
        Assert.Equal(["Q", "Waiting"], q.Pending.Select(p => p.Text));
    }

    [Fact]
    public void Clear_forgets_everything()
    {
        var q = new NarrationQueue();
        q.Enqueue(Line("a", 0));
        q.Next(0);
        q.Enqueue(Line("b", 0));

        q.Clear();

        Assert.Null(q.Current);
        Assert.Empty(q.Pending);
        Assert.Null(q.Next(0));
    }
}
