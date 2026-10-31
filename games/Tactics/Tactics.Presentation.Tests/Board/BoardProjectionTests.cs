using System.Numerics;
using Tactics.Logic;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.Tests.Board;

/// <summary>
/// <see cref="BoardProjection"/>: the art's own projection (ui/manifest.json: a 32 px tile, 2:1, x = U - V, y = (U + V) / 2;
/// terrain/manifest.json: one height step is 16 px).
/// </summary>
public sealed class BoardProjectionTests
{
    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(1, 0, 32, 16)]
    [InlineData(0, 1, -32, 16)]
    [InlineData(1, 1, 0, 32)]
    [InlineData(14, 12, 64, 416)]
    public void A_tile_corner_lands_where_the_art_says(int x, int y, float screenX, float screenY)
        => Assert.Equal(new Vector2(screenX, screenY), BoardProjection.Corner(x, y, 0));

    [Fact]
    public void Height_lifts_straight_up_by_its_steps()
    {
        Assert.Equal(new Vector2(32, 16 - 48), BoardProjection.Corner(1, 0, 3));
        Assert.Equal(new Vector2(32, 16 + 8), BoardProjection.Corner(1, 0, -0.5f));
    }

    [Fact]
    public void A_tiles_centre_is_the_middle_of_its_diamond()
    {
        Assert.Equal(new Vector2(0, 16), BoardProjection.Centre(new Tile(0, 0), 0));
        Assert.Equal(new Vector2(32, 64 - 32), BoardProjection.Centre(new Tile(2, 1), 2));
    }
}
