using System.Numerics;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.World;
using static Tactics.Presentation.Tests.World.WalkFeed;

namespace Tactics.Presentation.Tests.World;

/// <summary><see cref="WalkBump"/>: a press into a wall is seen on the TV, once a press, and never for an old count (interaction review 5b, item 2).</summary>
public sealed class WalkBumpTests
{
    private static readonly Tile At = new(20, 20);

    private static ExploreView Wall(int bumps, int epoch = 1, int dx = 0, int dy = -1, int seq = 4)
        => View(At, new Tile(At.X + dx, At.Y + dy), false, [], seq, epoch) with { Blocked = true, Bumps = bumps };

    [Fact]
    public void A_first_view_doesnt_bump_whatever_its_count()
    {
        var bump = new WalkBump();

        Assert.False(bump.Receive(Wall(7)));
        Assert.Equal(Vector2.Zero, bump.Offset);
    }

    [Fact]
    public void Each_wall_refused_is_one_bump()
    {
        var bump = new WalkBump();
        bump.Receive(Wall(3));

        Assert.True(bump.Receive(Wall(4)));
        Assert.False(bump.Receive(Wall(4)));                                   // the same view again, as the host view's other changes send it
        Assert.False(bump.Receive(Wall(4) with { Blocked = false }));          // the thumb lifting isn't a bump
        Assert.True(bump.Receive(Wall(6)));                                    // two refused between views is still one nudge
    }

    [Fact]
    public void A_new_epoch_or_a_count_that_starts_again_doesnt_bump()
    {
        var bump = new WalkBump();
        bump.Receive(Wall(5));

        Assert.False(bump.Receive(Wall(9, epoch: 2)));         // a teleport or a new session: a count from somewhere else
        Assert.False(bump.Receive(Wall(0, epoch: 2)));         // a restarted server counting from 0 again
        Assert.True(bump.Receive(Wall(1, epoch: 2)));          // and from there it bumps as ever
    }

    [Fact]
    public void A_bump_goes_toward_the_wall_and_back()
    {
        var bump = new WalkBump();
        bump.Receive(Wall(0));
        bump.Receive(Wall(1, dx: 1, dy: 0));

        Assert.Equal(Vector2.Zero, bump.Offset);
        bump.Advance(WorldTuning.BumpSeconds / 2);
        Assert.Equal(WorldTuning.BumpTiles, bump.Offset.Length(), 3);
        Assert.True(bump.Offset.X > 0 && MathF.Abs(bump.Offset.Y) < 1e-6f, $"{bump.Offset}");
        bump.Advance(WorldTuning.BumpSeconds / 2 + 0.001);
        Assert.Equal(Vector2.Zero, bump.Offset);
        Assert.False(bump.Active);
    }

    [Fact]
    public void A_diagonal_wall_is_bumped_as_far_as_a_straight_one()
    {
        var bump = new WalkBump();
        bump.Receive(Wall(0));
        bump.Receive(Wall(1, dx: -1, dy: -1));
        bump.Advance(WorldTuning.BumpSeconds / 2);

        Assert.Equal(WorldTuning.BumpTiles, bump.Offset.Length(), 3);
        Assert.True(bump.Offset.X < 0 && bump.Offset.Y < 0);
    }

    [Fact]
    public void Another_press_in_the_middle_starts_it_again()
    {
        var bump = new WalkBump();
        bump.Receive(Wall(0));
        bump.Receive(Wall(1));
        bump.Advance(WorldTuning.BumpSeconds * 0.9);
        bump.Receive(Wall(2));

        Assert.True(bump.Active);
        Assert.Equal(Vector2.Zero, bump.Offset);
        bump.Advance(WorldTuning.BumpSeconds / 2);
        Assert.Equal(WorldTuning.BumpTiles, bump.Offset.Length(), 3);
    }

    // A stream of views as a sofa makes them: walking, presses into walls, resends, restarts and teleports. The bumps are exactly the counts
    // that went up within an epoch, after the first view, and the leader is never pushed further than the bump's reach.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Over_a_stream_the_bumps_are_the_counts_that_went_up(int seed)
    {
        var random = new Random(seed);
        var bump = new WalkBump();
        var (epoch, count, expected, seen, first) = (1, random.Next(20), 0, 0, true);
        for (var i = 0; i < 3000; i++)
        {
            var roll = random.NextDouble();
            var bumped = false;
            if (roll < 0.01)
                (epoch, count) = (epoch + 1, random.Next(3));
            else if (roll < 0.02)
                count = 0;          // a server restart, same epoch
            else if (roll < 0.2)
            {
                count += 1 + random.Next(2);
                bumped = !first;
            }
            if (bump.Receive(Wall(count, epoch, seq: i)))
                seen++;
            expected += bumped ? 1 : 0;
            first = false;
            bump.Advance(random.NextDouble() * 0.05);
            Assert.InRange(bump.Offset.Length(), 0, WorldTuning.BumpTiles + 1e-4f);
        }
        Assert.Equal(expected, seen);
    }
}
