using Tactics.Logic;
using Tactics.Presentation.Board;
using Tactics.Presentation.Tests.Board;
using Tactics.Presentation.World;

namespace Tactics.Presentation.Tests.World;

/// <summary><see cref="WorldSpots"/>: the places on a region that need looking at are found, and are what they say.</summary>
public sealed class WorldSpotsTests
{
    private static readonly Lazy<WorldGround> Ground = new(() => Pack.Ground("ashen_ford"));
    private static readonly Lazy<BoardTerrain> Terrain = new(() => new BoardTerrain(Ground.Value.Map, null));

    private static WorldSpot Spot(string name) => WorldSpots.Of(Terrain.Value, Ground.Value.Terrain).Single(s => s.Name == name);

    [Fact]
    public void Every_spot_is_ground_to_stand_on()
    {
        var spots = WorldSpots.Of(Terrain.Value, Ground.Value.Terrain);

        Assert.Equal(["behind-cliff", "behind-crate", "water-block-edge", "block-corner", "region-edge", "void-near", "void-far"], spots.Select(s => s.Name));
        Assert.All(spots, s => Assert.True(Ground.Value.Terrain.Walkable(s.Stand), s.Name));
    }

    [Fact]
    public void Behind_a_cliff_the_ground_in_front_rises_past_a_figures_feet()
    {
        var stand = Spot("behind-cliff").Stand;

        Assert.True(Terrain.Value.Top(new Tile(stand.X + 1, stand.Y + 1)) - Terrain.Value.Top(stand) >= 3);
    }

    [Fact]
    public void Behind_a_crate_is_the_tile_behind_a_stamped_one()
    {
        var stand = Spot("behind-crate").Stand;

        Assert.Contains(Ground.Value.Map.Props, p => p.Id == "crate" && p.At == new Tile(stand.X + 1, stand.Y + 1));
    }

    [Fact]
    public void Water_at_a_block_edge_has_water_on_both_sides_of_the_edge()
    {
        var look = Spot("water-block-edge").Look;

        Assert.True(look.X % WorldTuning.BlockSize == 0 || look.Y % WorldTuning.BlockSize == 0);
        Assert.True(Terrain.Value.Look(look).Water);
        var before = look.X % WorldTuning.BlockSize == 0 ? new Tile(look.X - 1, look.Y) : new Tile(look.X, look.Y - 1);
        Assert.True(Terrain.Value.Look(before).Water);
    }

    [Fact]
    public void A_block_corner_is_where_four_blocks_meet()
    {
        var look = Spot("block-corner").Look;

        Assert.Equal(0, look.X % WorldTuning.BlockSize);
        Assert.Equal(0, look.Y % WorldTuning.BlockSize);
        Assert.True(look.X > 0 && look.Y > 0);
    }

    [Fact]
    public void The_regions_edge_is_a_tile_on_the_front_of_the_map()
    {
        var look = Spot("region-edge").Look;

        Assert.True(look.X == Terrain.Value.Map.Width - 1 || look.Y == Terrain.Value.Map.Height - 1);
    }

    [Fact]
    public void A_void_edge_has_void_in_front_or_behind()
    {
        var near = Spot("void-near").Look;
        var far = Spot("void-far").Look;

        Assert.True(Terrain.Value.Look(new Tile(near.X + 1, near.Y)).Void || Terrain.Value.Look(new Tile(near.X, near.Y + 1)).Void);
        Assert.True(Terrain.Value.Look(new Tile(far.X - 1, far.Y)).Void || Terrain.Value.Look(new Tile(far.X, far.Y - 1)).Void);
    }
}
