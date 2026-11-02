using Tactics.Presentation.World;

namespace Tactics.Presentation.Tests.World;

/// <summary><see cref="WorldReveal"/>: the region's title stays up until the world is there to see (interaction review 5b, item 14).</summary>
public sealed class WorldRevealTests
{
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void While_the_world_builds_the_title_is_whole_and_the_panels_wait(double lifted)
    {
        var reveal = WorldReveal.Of(open: false, lifted);

        Assert.Equal(1, reveal.Card);
        Assert.Equal(0, reveal.Panels);
        Assert.True(reveal.CardUp);
        Assert.False(reveal.Revealed);
    }

    [Fact]
    public void Open_but_not_yet_lifted_the_title_is_still_whole()
    {
        var reveal = WorldReveal.Of(open: true, 0);

        Assert.Equal(WorldReveal.Loading, reveal);
    }

    [Fact]
    public void As_the_fade_lifts_the_title_gives_way_and_then_the_panels_come_in()
    {
        var half = WorldReveal.Of(open: true, 0.5);
        var most = WorldReveal.Of(open: true, 0.75);

        Assert.Equal(0.5f, half.Card, 3);
        Assert.Equal(0, half.Panels);
        Assert.True(half.CardUp);
        Assert.Equal(0.25f, most.Card, 3);
        Assert.Equal(0.5f, most.Panels, 3);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.7)]
    public void Once_lifted_only_the_world_shows(double lifted)
    {
        var reveal = WorldReveal.Of(open: true, lifted);

        Assert.Equal(0, reveal.Card);
        Assert.Equal(1, reveal.Panels);
        Assert.False(reveal.CardUp);
        Assert.True(reveal.Revealed);
    }

    // Frames as the TV runs them: some building, then the fade lifting at a wandering frame rate, then a fight and back. The card never
    // comes down before the world opens, never goes up again while it's lifting, and the panels never show while half the card does.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Over_a_stream_of_frames_the_title_only_comes_down_with_the_world(int seed)
    {
        var random = new Random(seed);
        var (open, lifted, last) = (false, 0.0, WorldReveal.Loading);
        for (var frame = 0; frame < 2000; frame++)
        {
            var resumed = false;
            if (!open && random.NextDouble() < 0.02)
                open = true;
            else if (open && random.NextDouble() < 0.002)
                (lifted, resumed) = (0, true);      // back from a fight: the fade starts again
            else if (open)
                lifted = Math.Min(1, lifted + random.NextDouble() * 0.05);

            var reveal = WorldReveal.Of(open, lifted);
            Assert.InRange(reveal.Card, 0, 1);
            Assert.InRange(reveal.Panels, 0, 1);
            if (reveal.Panels > 0)
                Assert.True(reveal.Card < 0.5f, $"the panels came in over the card at frame {frame}");
            if (!open)
                Assert.Equal(WorldReveal.Loading, reveal);
            if (reveal.Revealed)
                Assert.Equal(1, lifted, 6);
            if (!resumed)
            {
                Assert.True(reveal.Card <= last.Card + 1e-6, $"the title came back up at frame {frame}");
                Assert.True(reveal.Panels >= last.Panels - 1e-6, $"the panels went away at frame {frame}");
            }
            last = reveal;
        }
    }

    [Fact]
    public void A_fade_that_isnt_a_number_keeps_the_title_up()
    {
        Assert.Equal(WorldReveal.Loading, WorldReveal.Of(open: true, double.NaN));
    }
}
