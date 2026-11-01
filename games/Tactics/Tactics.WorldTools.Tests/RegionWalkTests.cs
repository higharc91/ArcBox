using System.Collections.Immutable;
using Tactics.Logic;
using Tactics.Logic.Tests;

namespace Tactics.WorldTools.Tests;

/// <summary>What <see cref="RegionWalk"/> adds to <see cref="WorldWalk"/>: how long a walk takes.</summary>
public sealed class RegionWalkTests
{
    private const int TileBytes = 3;

    private static readonly Tile Corner = new(0, 0);
    private static readonly Tile Across = new(3, 3);

    /// <summary>One flat chunk, with <paramref name="walls"/> impassable.</summary>
    private static RegionWalk Walk(params Tile[] walls)
    {
        var bytes = new byte[WorldRegion.ChunkBytes];
        foreach (var wall in walls)
            bytes[(wall.Y * WorldRegion.ChunkSize + wall.X) * TileBytes + 2] = (byte)TileFlags.Impassable;
        var def = new RegionDef("r", "R", 1, [WorldRegion.ChunkSize, WorldRegion.ChunkSize], ["grass/lush"], [new RegionChunk(0, 0, Convert.ToBase64String(bytes))], [], [],
            new RegionSpawns(Corner, Corner), []);
        return new RegionWalk(WorldRegion.From(def), new Dictionary<string, BattleMap>());
    }

    [Fact]
    public void A_wall_of_buildings_cuts_a_walk_as_walls_do()
    {
        var bytes = new byte[WorldRegion.ChunkBytes];
        ImmutableArray<RegionBuilding> wall = [.. Enumerable.Range(0, WorldRegion.ChunkSize).Select(y => new RegionBuilding("wall_v", "grey", new Tile(2, y)))];
        var def = new RegionDef("r", "R", 1, [WorldRegion.ChunkSize, WorldRegion.ChunkSize], ["grass/lush"], [new RegionChunk(0, 0, Convert.ToBase64String(bytes))], [], [],
            new RegionSpawns(Corner, Corner), [], Buildings: wall);
        var art = ArtManifests.Load(ContentPaths.ContentBase);

        var walk = new RegionWalk(WorldRegion.From(def), new Dictionary<string, BattleMap>(), art.Props, art.Buildings);

        Assert.Null(walk.Seconds(Corner, t => t == Across));
    }

    [Fact]
    public void A_walk_across_the_axes_goes_diagonally()
        => Assert.Equal(3 * Math.Sqrt(2) * RegionWalk.StepSeconds, Walk().Seconds(Corner, t => t == Across)!.Value, precision: 9);

    [Fact]
    public void Times_count_diagonals_too()
        => Assert.Equal(3 * Math.Sqrt(2) * RegionWalk.StepSeconds, Walk().Times(Corner)[Across], precision: 9);

    [Fact]
    public void A_diagonal_cant_cut_a_corner_of_a_timed_walk()
    {
        // The first diagonal is shut by a wall on one corner, so the walk starts with a straight step.
        var walled = Walk(new Tile(1, 0)).Seconds(Corner, t => t == new Tile(1, 1));

        Assert.Equal(2 * RegionWalk.StepSeconds, walled!.Value, precision: 9);
    }

    [Fact]
    public void A_walk_with_a_wall_in_the_way_has_no_time()
        => Assert.Null(Walk(new Tile(1, 0), new Tile(0, 1)).Seconds(Corner, t => t == Across));
}
