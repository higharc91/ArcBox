using System.Collections.Immutable;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.Tests.Board;

/// <summary>Where one ground's top runs out into its neighbour's: the world's alone, and only ever on the world's side of a stamp.</summary>
public sealed class GroundBlendTests
{
    private const int TileTexels = GroundBlend.Size / 4;

    private static readonly ImmutableSortedDictionary<string, ImmutableArray<Tile>> NoZones = ImmutableSortedDictionary<string, ImmutableArray<Tile>>.Empty;

    /// <summary>A map from one character a tile: its material's index, height 1.</summary>
    private static BattleMap Map(string id, string[] materials, ImmutableHashSet<Tile>? stamped, params string[] rows)
    {
        var bytes = new byte[rows.Length * rows[0].Length * 3];
        for (var y = 0; y < rows.Length; y++)
            for (var x = 0; x < rows[0].Length; x++)
            {
                var at = (y * rows[0].Length + x) * 3;
                (bytes[at], bytes[at + 1]) = ((byte)1, (byte)(rows[y][x] - '0'));
            }
        return BattleMap.FromBytes(id, rows[0].Length, rows.Length, bytes, [.. materials], [], NoZones, stamped);
    }

    private static GroundEdge[] Edges(BattleMap map, int x, int y) => [.. GroundBlend.Of(new BoardTerrain(map, null), new Tile(x, y))];

    private static (int Dx, int Dy) Way(GroundEdge edge) => GroundBlend.Ways[edge.Mask];

    private static double Cover(int mask, int fromU, int toU, int fromV, int toV)
    {
        var (bytes, count, set) = (GroundBlend.Mask(mask), 0, 0);
        for (var v = fromV; v < toV; v++)
            for (var u = fromU; u < toU; u++)
            {
                count++;
                set += bytes[v * GroundBlend.Size + u] > 0 ? 1 : 0;
            }
        return (double)set / count;
    }

    private static int MaskOf(int dx, int dy) => GroundBlend.Ways.IndexOf((dx, dy));

    [Fact]
    public void The_side_towards_the_neighbour_is_solid_so_no_seam_shows()
    {
        var mask = MaskOf(1, 0);
        foreach (var tile in (ReadOnlySpan<int>)[0, 1, 2, 3])
            Assert.Equal(1, Cover(mask, (tile + 1) * TileTexels - 2, (tile + 1) * TileTexels, 0, GroundBlend.Size));
    }

    [Fact]
    public void It_stops_before_the_far_side_so_no_cut_shows_there()
    {
        var mask = MaskOf(1, 0);
        foreach (var tile in (ReadOnlySpan<int>)[0, 1, 2, 3])
            Assert.Equal(0, Cover(mask, tile * TileTexels, tile * TileTexels + 2, 0, GroundBlend.Size));
    }

    [Fact]
    public void It_thins_as_it_goes()
    {
        var mask = MaskOf(0, 1);
        var bands = Enumerable.Range(0, 8).Select(band => Cover(mask, 0, GroundBlend.Size, TileTexels - (band + 1) * TileTexels / 8, TileTexels - band * TileTexels / 8)).ToArray();

        Assert.True(bands.Zip(bands.Skip(1)).All(b => b.Second <= b.First));
    }

    [Fact]
    public void A_corner_only_reaches_round_its_own_corner()
    {
        var mask = MaskOf(1, 1);

        Assert.Equal(1, Cover(mask, TileTexels - 2, TileTexels, TileTexels - 2, TileTexels));
        Assert.Equal(0, Cover(mask, 0, TileTexels / 4, 0, TileTexels));
        Assert.Equal(0, Cover(mask, 0, TileTexels, 0, TileTexels / 4));
    }

    [Fact]
    public void Every_mask_differs()
    {
        Assert.Equal(8, GroundBlend.Ways.Distinct().Count());
        Assert.Equal(16, Enumerable.Range(0, 16).Select(m => Convert.ToBase64String(GroundBlend.Mask(m))).Distinct().Count());
    }

    /// <summary>What it leaves of the dither must be the pack's upright checker on the screen, in whole art pixels, or it combs as a turned top does.</summary>
    [Fact]
    public void The_half_covered_band_is_an_upright_checker_of_art_pixels()
    {
        var bytes = GroundBlend.Mask(MaskOf(1, 0));
        var (checks, broken) = (0, 0);
        for (var v = 0; v < GroundBlend.Size; v++)
            for (var u = 0; u < GroundBlend.Size; u++)
            {
                if (!GroundBlend.HalfLevel(u, v, GroundBlend.Ways[MaskOf(1, 0)]))
                    continue;
                checks++;
                broken += bytes[v * GroundBlend.Size + u] > 0 == GroundBlend.Checker(u, v) ? 0 : 1;
            }
        Assert.True(checks > 500);
        Assert.Equal(0, broken);
    }

    [Fact]
    public void A_masked_top_keeps_its_colours_and_loses_only_what_the_mask_leaves_out()
    {
        var top = new byte[GroundBlend.Size * GroundBlend.Size * 4];
        new Random(1).NextBytes(top);
        for (var i = 3; i < top.Length; i += 4)
            top[i] = 255;
        var mask = MaskOf(1, 0);

        var masked = GroundBlend.Masked(top, mask);

        for (var i = 0; i < GroundBlend.Size * GroundBlend.Size; i++)
            if (GroundBlend.Mask(mask)[i] > 0)
                Assert.True(top.AsSpan(i * 4, 4).SequenceEqual(masked.AsSpan(i * 4, 4)));
            else
                Assert.Equal(0, masked[i * 4 + 3]);
    }

    [Fact]
    public void A_high_resolution_top_uses_the_same_ground_edge_at_double_resolution()
    {
        const int size = GroundBlend.Size * 2;
        var top = new byte[size * size * 4];
        new Random(1).NextBytes(top);
        for (var i = 3; i < top.Length; i += 4)
            top[i] = 255;
        var way = MaskOf(1, 0);
        var mask = GroundBlend.Mask(way, size, 8);

        var masked = GroundBlend.Masked(top, way, 8);
        var hasSoftEdge = false;

        for (var y = 0; y < size; y += 2)
            for (var x = 0; x < size; x += 2)
            {
                var originalIndex = (y * size + x) * 4;
                var maskIndex = y * size + x;
                if (mask[maskIndex] is > 0 and < 255)
                {
                    hasSoftEdge = true;
                    Assert.Equal(mask[maskIndex], masked[originalIndex + 3]);
                }
                else if (mask[maskIndex] == 255)
                    Assert.True(top.AsSpan(originalIndex, 4).SequenceEqual(masked.AsSpan(originalIndex, 4)));
                else
                    Assert.Equal(0, masked[originalIndex + 3]);
            }
        Assert.True(hasSoftEdge);
    }

    [Fact]
    public void Ford_tops_fade_over_a_broad_soft_band()
    {
        const int size = GroundBlend.Size * 2;
        var mask = GroundBlend.Mask(MaskOf(1, 0), size, 8);
        var partial = Enumerable.Range(0, size).Count(x => mask[128 * size + x] is > 0 and < 255);

        Assert.InRange(partial, 300, 460);
    }

    [Fact]
    public void Opted_in_native_edges_use_whole_pixel_clusters_without_feathering_or_checker_dither()
    {
        var top = Enumerable.Repeat((byte)127, GroundBlend.Size * GroundBlend.Size * 4).ToArray();
        for (var i = 3; i < top.Length; i += 4) top[i] = 255;
        foreach (var way in Enumerable.Range(0, 16))
        {
            var result = GroundBlend.Masked(top, way, clustered: true);
            var alphas = Enumerable.Range(0, result.Length / 4).Select(i => result[i * 4 + 3]).ToHashSet();
            Assert.Equal(new byte[] { 0, 255 }, alphas.Order().ToArray());
            for (var y = 0; y < GroundBlend.Size; y += 2)
                for (var x = 0; x < GroundBlend.Size; x += 2)
                {
                    var i = (y * GroundBlend.Size + x) * 4;
                    Assert.Equal(result[i + 3], result[i + 7]);
                    Assert.Equal(result[i + 3], result[i + GroundBlend.Size * 4 + 3]);
                    Assert.Equal(127, result[i]);
                }
        }
        Assert.NotEqual(new ArtImage("ground.png", 1, 0, Blend: 0).Key,
            new ArtImage("ground.png", 1, 0, Blend: 0, ClusteredEdges: true).Key);
    }

    [Fact]
    public void Only_the_looser_ground_runs_into_the_other()
    {
        var map = Map("test", ["ash/grey", "dirt/dark"], null, "01");

        Assert.Equal(["ash/grey"], Edges(map, 1, 0).Select(e => e.Material));
        Assert.Empty(Edges(map, 0, 0));
    }

    [Fact]
    public void Two_grounds_of_a_family_still_blend_but_only_one_way()
    {
        var map = Map("test", ["grass/autumn", "grass/dry"], null, "01");

        Assert.Equal(1, Edges(map, 0, 0).Length + Edges(map, 1, 0).Length);
    }

    [Theory]
    [InlineData("cobble/warm")]
    [InlineData("flagstone/grey")]
    [InlineData("planks/oak")]
    [InlineData("water/clear")]
    public void What_was_laid_or_flows_neither_runs_nor_is_run_into(string laid)
    {
        var map = Map("test", ["ash/grey", laid], null, "01");

        Assert.Empty(Edges(map, 0, 0));
        Assert.Empty(Edges(map, 1, 0));
    }

    [Fact]
    public void It_doesnt_cross_a_step()
    {
        var bytes = new byte[] { 1, 0, 0, 2, 1, 0 };
        var map = BattleMap.FromBytes("test", 2, 1, bytes, ["ash/grey", "dirt/dark"], [], NoZones);

        Assert.Empty(Edges(map, 1, 0));
    }

    [Fact]
    public void A_corner_goes_only_where_no_side_already_reaches_it()
    {
        var alone = Map("test", ["dirt/dark", "ash/grey"], null, "00", "01");
        Assert.Equal([(1, 1)], Edges(alone, 0, 0).Select(Way));

        var beside = Map("test", ["dirt/dark", "ash/grey"], null, "01", "01");
        Assert.Equal([(1, 0)], Edges(beside, 0, 0).Select(Way));
    }

    [Fact]
    public void A_road_keeps_a_tight_edge_so_a_narrow_track_stays_narrow()
    {
        var map = Map("test", ["path/packed", "dirt/dark"], null, "01");

        var edge = Assert.Single(Edges(map, 1, 0));
        Assert.Equal("path/packed", edge.Material);
        Assert.Equal(MaskOf(-1, 0) + GroundBlend.Ways.Length, edge.Mask);

        // The far side of the tile is left as it was, where a ground's blend reaches.
        Assert.Equal(0, Cover(edge.Mask, TileTexels * 3 / 4, TileTexels, 0, GroundBlend.Size));
        Assert.True(Cover(MaskOf(-1, 0), TileTexels * 3 / 4, TileTexels, 0, GroundBlend.Size) > 0);
    }

    [Fact]
    public void A_stamp_runs_out_into_the_world_whatever_it_is_and_the_world_never_runs_into_it()
    {
        var stamped = ImmutableHashSet.Create(new Tile(1, 0));
        var map = Map("test", ["ash/grey", "rock/grey"], stamped, "01");

        var edge = Assert.Single(Edges(map, 0, 0));
        Assert.Equal("rock/grey", edge.Material);
        Assert.True(edge.Stamped);
        Assert.Empty(Edges(map, 1, 0));
    }

    [Fact]
    public void A_battle_board_has_none()
    {
        var plan = BoardPlan.Build(new BoardTerrain(Pack.Map("river_crossing"), null), Pack.Art);

        Assert.DoesNotContain(plan.Items, i => i is Face { Kind: FaceKind.Blend });
    }

    [Fact]
    public void In_the_world_it_lies_on_the_tile_in_the_neighbours_own_art_and_colour()
    {
        var map = Map("ashen_ford", ["ash/grey", "dirt/dark"], null, "01");
        var plan = BoardPlan.BuildBlock(new BoardTerrain(map, null), Pack.Art, new TileRect(0, 0, 2, 1), grid: false, world: true);

        var tops = plan.Items.OfType<Face>().Where(f => f.Kind == FaceKind.Top).ToArray();
        var blend = Assert.Single(plan.Items.OfType<Face>(), f => f.Kind == FaceKind.Blend);

        Assert.Equal(new Tile(1, 0), blend.Tile);
        Assert.Equal(tops[1].Points, blend.Points);
        Assert.Equal(tops[1].Uvs, blend.Uvs);
        Assert.Equal("terrain/top/native_ash_ford_charred.png", blend.Image!.Path); // Ashen Ford's own ash
        Assert.Equal(MaskOf(-1, 0), blend.Image.Blend);
        Assert.Null(blend.Image.Glow);

        // The ash on the dirt's tile is the colour the ash next door is at that corner, so there's no step where they meet.
        Assert.Equal(tops[0].VertexTints[1], blend.VertexTints[0]);
        Assert.Equal(tops[0].VertexTints[2], blend.VertexTints[3]);
    }

    [Fact]
    public void It_sorts_over_its_tile_and_under_what_stands_on_it()
    {
        var map = Map("ashen_ford", ["ash/grey", "dirt/dark"], null, "01");
        var plan = BoardPlan.BuildBlock(new BoardTerrain(map, null), Pack.Art, new TileRect(1, 0, 1, 1), grid: false, world: true);
        var order = plan.Items.OfType<Face>().Select(f => f.Kind).ToArray();

        Assert.True(Array.IndexOf(order, FaceKind.Top) < Array.IndexOf(order, FaceKind.Blend));
    }

    [Fact]
    public void Where_light_doesnt_reach_a_blend_is_one_face_as_its_top_is()
    {
        var map = Map("ashen_ford", ["ash/grey", "dirt/dark"], null, "01");
        var plan = BoardPlan.BuildBlock(new BoardTerrain(map, null), Pack.Art, new TileRect(0, 0, 2, 1), grid: false, world: true);
        var lighting = BoardLighting.Of([plan], "ashen_ford", Pack.Art.Lighting);

        var lit = LitBoard.Build(plan, lighting).Items.OfType<LitFace>().ToArray();

        Assert.Equal(lit.Count(f => f.Face.Kind == FaceKind.Top && f.Art) / 2, lit.Count(f => f.Face.Kind == FaceKind.Blend));
    }

    [Fact]
    public void An_image_with_a_blend_is_its_own_image()
    {
        var art = new ArtImage("terrain/top/ash_grey.png", 1, 0);

        Assert.NotEqual(art.Key, (art with { Blend = 0 }).Key);
        Assert.NotEqual((art with { Blend = 1 }).Key, (art with { Blend = 0 }).Key);
    }
}
