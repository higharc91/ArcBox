namespace Tactics.Logic.Tests;

public sealed class MobMotionTests
{
    private static HashSet<Tile> Patch() => [new(1, 1), new(1, 0), new(2, 1), new(1, 2), new(0, 1)];

    [Fact]
    public void Restarting_with_the_same_step_reproduces_the_walk_and_keeps_it_in_the_patch()
    {
        var patch = Patch();
        Tile Walk()
        {
            var at = new Tile(1, 1);
            for (var step = 0; step < 100; step++)
            {
                at = MobMotion.Step("ford", "wolves", 42, step, at, patch, _ => true, new(9, 9));
                Assert.Contains(at, patch);
            }
            return at;
        }
        Assert.Equal(Walk(), Walk());
    }

    [Fact]
    public void Pausing_freezes_the_mob_and_occupied_destinations_are_excluded()
    {
        var at = new Tile(1, 1);
        Assert.Equal(at, MobMotion.Step("ford", "wolves", 42, 1, at, Patch(), _ => false, at, paused: true));
        for (var step = 0; step < 100; step++)
        {
            var next = MobMotion.Step("ford", "wolves", 42, step, at, Patch(), t => t != new Tile(0, 1), new(1, 0), new(2, 1));
            Assert.DoesNotContain(next, new[] { new Tile(0, 1), new Tile(1, 0), new Tile(2, 1) });
        }
    }

    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(1, 2, true)]
    [InlineData(2, 1, true)]
    [InlineData(2, 2, false)]
    [InlineData(1, 3, false)]
    public void Contact_is_same_tile_or_cardinal_only(int x, int y, bool expected)
        => Assert.Equal(expected, MobMotion.Contact(new(1, 1), new(x, y)));
}
