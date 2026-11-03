using System.Collections.Immutable;
using System.Numerics;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.Tests.Board;

public sealed class CreatureFacingTests
{
    private static UnitSprite Sprite(bool compass) => new("test.png", 100, 120, 50, 110, 30,
        new UnitAnim([0, 1, 2, 3], 5, true), new UnitAnim([17], 1, false), 0,
        ImmutableDictionary<string, UnitAnim>.Empty)
    {
        PixelScale = 0.5f, Columns = 8, RowsPerDirection = 3,
        Directions = compass ? ["south", "south-west", "west", "north-west", "north", "north-east", "east", "south-east"] : [],
    };

    [Theory]
    [InlineData(1, 1, 0)]
    [InlineData(0, 1, 1)]
    [InlineData(-1, 1, 2)]
    [InlineData(-1, 0, 3)]
    [InlineData(-1, -1, 4)]
    [InlineData(0, -1, 5)]
    [InlineData(1, -1, 6)]
    [InlineData(1, 0, 7)]
    [InlineData(0, 0, 0)]
    public void Every_map_direction_uses_its_own_authored_view_without_mirroring(float du, float dv, int row)
    {
        Assert.Equal((row, false), Sprite(true).Looking(du, dv));
    }

    [Theory]
    [InlineData(1, 0, 0, false)]
    [InlineData(0, 1, 0, true)]
    [InlineData(-1, 0, 1, false)]
    [InlineData(0, -1, 1, true)]
    public void Existing_two_row_sheets_keep_their_original_mirrored_facings(float du, float dv, int row, bool mirror)
        => Assert.Equal((row, mirror), Sprite(false).Looking(du, dv));

    [Fact]
    public void A_back_view_attack_addresses_the_same_facing_block_as_its_walk()
    {
        var s = Sprite(true);
        Assert.Equal(new ArtRect(400, 1560, 100, 120), s.Source(12, 4));
        Assert.Equal(new ArtRect(100, 1680, 100, 120), s.Source(17, 4));
    }

    [Fact]
    public void Overhead_marks_measure_visible_world_height_instead_of_authoring_pixels()
    {
        var s = Sprite(true);
        var mark = UnitPlan.Overhead(new Vector2(200, 300), s);
        Assert.Equal(200, mark.X);
        Assert.InRange(mark.Y, 250, 260); // head at 260; the mark is a small gap above, rather than forty pixels too high
    }
}
