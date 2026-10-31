using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Beats;

namespace Tactics.Presentation.Tests.Beats;

/// <summary><see cref="BeatQueue"/>: which beats of a view are new.</summary>
public sealed class BeatQueueTests
{
    private static Beat B(int seq) => new(seq, null, [new TurnEnded(new UnitId("a"))]);

    [Fact]
    public void Beats_come_out_oldest_first_and_each_only_once()
    {
        var queue = new BeatQueue();

        queue.Offer([B(1), B(2)]);
        queue.Offer([B(1), B(2), B(3)]);

        Assert.Equal(1, queue.Next()?.Seq);
        Assert.Equal(2, queue.Next()?.Seq);
        Assert.Equal(3, queue.Next()?.Seq);
        Assert.Null(queue.Next());
    }

    [Fact]
    public void A_beat_already_played_is_not_queued_again()
    {
        var queue = new BeatQueue();
        queue.Offer([B(1)]);
        queue.Next();

        queue.Offer([B(1)]);

        Assert.Null(queue.Next());
    }

    [Fact]
    public void A_view_with_a_gap_just_plays_what_is_there()
    {
        var queue = new BeatQueue();
        queue.Offer([B(1)]);
        queue.Next();

        queue.Offer([B(30), B(31)]);

        Assert.Equal(30, queue.Next()?.Seq);
        Assert.Equal(31, queue.Next()?.Seq);
    }

    [Fact]
    public void Numbers_that_start_again_are_a_new_battle()
    {
        var queue = new BeatQueue();
        queue.Offer([B(5), B(6)]);
        queue.Next();

        Assert.True(queue.Offer([B(1)]));

        Assert.Equal(1, queue.Next()?.Seq);
        Assert.Null(queue.Next());
    }

    [Fact]
    public void A_view_with_no_beats_changes_nothing()
    {
        var queue = new BeatQueue();
        queue.Offer([B(5)]);

        Assert.False(queue.Offer([]));

        Assert.Equal(5, queue.Next()?.Seq);
    }
}
