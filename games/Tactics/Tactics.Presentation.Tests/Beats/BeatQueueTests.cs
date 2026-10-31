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
    public void A_view_with_a_gap_is_late_and_only_its_newest_beat_is_kept()
    {
        var queue = new BeatQueue();
        queue.Offer([B(1)]);
        queue.Next();

        queue.Offer([B(30), B(31)]);

        Assert.True(queue.Late(0));
        queue.KeepNewest();
        Assert.False(queue.Late(0));
        Assert.Equal(31, queue.Next()?.Seq);
        Assert.Null(queue.Next());
    }

    [Fact]
    public void Beats_following_on_from_the_last_are_not_late()
    {
        var queue = new BeatQueue();
        queue.Offer([B(1)]);
        queue.Next();

        queue.Offer([B(1), B(2), B(3)]);

        Assert.False(queue.Late(0));
    }

    [Fact]
    public void A_first_view_holding_old_beats_is_a_join_even_from_the_first_of_them()
    {
        var queue = new BeatQueue();

        queue.Offer([B(1), B(2), B(3), B(4), B(5)]);

        Assert.True(queue.Late(0));
    }

    [Fact]
    public void A_first_view_from_the_battles_own_start_is_not_a_join()
    {
        var queue = new BeatQueue();

        queue.Offer([B(1), B(2)]);

        Assert.False(queue.Late(0));
    }

    [Fact]
    public void A_queue_that_saw_the_battle_begin_can_take_a_burst()
    {
        var queue = new BeatQueue();
        queue.Offer([]);

        queue.Offer([B(1), B(2), B(3), B(4)]);

        Assert.False(queue.Late(0));
    }

    [Fact]
    public void A_new_battle_is_a_first_view_again()
    {
        var queue = new BeatQueue();
        queue.Offer([B(5), B(6)]);

        queue.Offer([B(1), B(2)]);

        Assert.False(queue.Late(0));
    }

    [Fact]
    public void Beats_waiting_longer_than_the_lag_allowed_are_late()
    {
        var queue = new BeatQueue();
        queue.Offer([]);
        queue.Offer([B(1), B(2)], 10);

        Assert.False(queue.Late(10 + BeatQueue.MostLag));
        Assert.True(queue.Late(10.01 + BeatQueue.MostLag));
    }

    [Fact]
    public void One_old_beat_alone_is_not_late_as_it_is_next_anyway()
    {
        var queue = new BeatQueue();
        queue.Offer([]);
        queue.Offer([B(1)]);

        Assert.False(queue.Late(1000));
    }

    [Fact]
    public void The_lag_is_that_of_the_oldest_beat_still_waiting()
    {
        var queue = new BeatQueue();
        queue.Offer([]);
        queue.Offer([B(1)]);
        queue.Offer([B(2), B(3)], 100);
        queue.Next();

        Assert.False(queue.Late(100 + BeatQueue.MostLag));
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
