using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json;
using Tactics.Logic;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.Tests.Board;

/// <summary><see cref="BuildingPlan"/> against the town manifest's notes and the art itself.</summary>
public sealed class BuildingPlanTests
{
    private static readonly Tile At = new(10, 10);

    public static TheoryData<string> Buildings => [.. Pack.Manifests.Buildings.Keys.Order(StringComparer.Ordinal)];

    private static string VariantOf(string id) => Pack.Manifests.Buildings[id].Variants[0];

    private static BattleMap Ground(string id, string? variant = null, int height = 0, int size = 24)
        => BattleMap.FromBytes("town", size, size, TilesAt(size, height), ["cobble/warm"], [], ImmutableSortedDictionary<string, ImmutableArray<Tile>>.Empty, null,
            [new BuildingPlacement(id, variant ?? VariantOf(id), At, height)]);

    private static byte[] TilesAt(int size, int height)
    {
        var bytes = new byte[size * size * 3];
        for (var i = 0; i < size * size; i++)
            bytes[i * 3] = (byte)height;
        return bytes;
    }

    private static ImmutableArray<BoardItem> Items(BattleMap map) => BoardPlan.BuildBlock(new BoardTerrain(map, null), Pack.Art, new TileRect(0, 0, map.Width, map.Height), grid: false).Items;

    private static Face[] Columns(string id, int height = 0) => [.. Items(Ground(id, height: height)).OfType<Face>().Where(f => f.Kind == FaceKind.Building)];

    private static JsonElement Manifest(string id) => Pack.Manifest("town").RootElement.GetProperty("buildings").GetProperty(id);

    private static Vector2 Point(JsonElement e) => new(e[0].GetSingle(), e[1].GetSingle());

    [Theory]
    [MemberData(nameof(Buildings))]
    public void Every_pixel_of_a_building_is_in_a_column_on_the_32_pixel_grid_from_its_origin(string id)
    {
        var art = Pack.Art.Building(id, VariantOf(id))!;
        var corner = BoardProjection.Corner(At.X, At.Y, 0);
        var frame = corner - art.Origin;
        var columns = Columns(id);
        var (rgba, width, height) = PngTexels.Read(Path.Combine(Pack.Root, art.Image.Path));

        foreach (var column in columns)
        {
            Assert.True(column.Points[1].X - column.Points[0].X <= BuildingPlan.ColumnWidth);
            Assert.Equal(0, (column.Points[0].X - corner.X) % BuildingPlan.ColumnWidth == 0 || column.Points[0].X == frame.X ? 0 : 1);
        }
        var covered = new bool[(int)art.Size.X, (int)art.Size.Y];
        foreach (var column in columns)
            for (var y = (int)(column.Points[0].Y - frame.Y); y < (int)(column.Points[2].Y - frame.Y); y++)
                for (var x = (int)(column.Points[0].X - frame.X); x < (int)(column.Points[1].X - frame.X); x++)
                    covered[x, y] = true;
        var frameWidth = (int)art.Size.X;
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                if (rgba[(y * width + x) * 4 + 3] > 0 && !covered[x % frameWidth, y])
                    Assert.Fail($"{id}: pixel ({x}, {y}) is in no column");
    }

    [Fact]
    public void A_column_draws_its_slice_of_the_image_at_one_texel_a_pixel()
    {
        var art = Pack.Art.Building("tavern", "red")!;
        var frame = BoardProjection.Corner(At.X, At.Y, 0) - art.Origin;

        foreach (var column in Columns("tavern"))
        {
            Assert.Contains("tavern_red.png", column.Image!.Path);
            Assert.True((column.Points[0] - frame - column.Uvs[0] * art.Size).Length() < 0.001f);
            Assert.True((column.Points[2] - frame - column.Uvs[2] * art.Size).Length() < 0.001f);
        }
    }

    [Theory]
    [InlineData(-3, 0, 1)]      // the left overhang counts as the left corner
    [InlineData(-2, 0, 1)]
    [InlineData(-1, 1, 1)]
    [InlineData(0, 2, 1)]
    [InlineData(1, 2, 1)]
    [InlineData(2, 2, 0)]
    [InlineData(3, 2, 0)]       // and the right as the right
    public void A_column_sorts_at_the_frontmost_tile_of_the_tavern_it_crosses(int column, int x, int y)
        => Assert.Equal((x, y), Pack.Art.Building("tavern", "red")!.FrontOf(column));

    [Fact]
    public void Columns_sort_where_they_stand_as_props_do_so_a_unit_on_a_tile_in_front_goes_over()
    {
        var byColumn = Columns("tavern").OrderBy(c => c.Points[0].X).ToArray();
        var art = Pack.Art.Building("tavern", "red")!;
        var origin = BoardProjection.Corner(At.X, At.Y, 0).X;

        foreach (var face in byColumn)
        {
            var (x, y) = art.FrontOf((int)MathF.Floor((face.Points[0].X - origin) / BuildingPlan.ColumnWidth));
            Assert.Equal(new Tile(At.X + x, At.Y + y), face.Tile);
            Assert.Equal(BoardOrder.Key(face.Tile, BoardLayer.Prop), face.Order);
        }
    }

    [Theory]
    [InlineData("gate_u", 11, 10)]
    [InlineData("gate_v", 10, 11)]
    public void A_gates_arch_sorts_after_whoever_walks_under_it(string gate, int x, int y)
    {
        var under = BoardOrder.Key(new Tile(x, y), BoardLayer.Unit);

        var overhead = Columns(gate).Where(c => c.Tile == new Tile(x, y)).ToArray();

        Assert.NotEmpty(overhead);
        Assert.All(overhead, c => Assert.True(c.Order > under));
    }

    [Theory]
    [InlineData("bridge_u")]
    [InlineData("steps_v")]
    public void What_is_walked_on_sorts_under_whoever_walks_on_it(string id)
        => Assert.All(Columns(id), c => Assert.True(c.Order < BoardOrder.Key(c.Tile, BoardLayer.Unit)));

    [Fact]
    public void A_buildings_pieces_keep_to_their_tiles_block_so_blocks_add_up_to_the_whole()
    {
        var map = Ground("tavern");
        var terrain = new BoardTerrain(map, null);
        var whole = Items(map).Where(i => i is Face { Kind: FaceKind.Building or FaceKind.Shadow } or Sprite).Select(i => (i.Order, i.Tile, BoardPlan.Bounds(i))).ToHashSet();

        var parts = new[] { new TileRect(0, 0, 12, 24), new TileRect(12, 0, 12, 24) }
            .SelectMany(r => BoardPlan.BuildBlock(terrain, Pack.Art, r, grid: false).Items)
            .Where(i => i is Face { Kind: FaceKind.Building or FaceKind.Shadow } or Sprite).Select(i => (i.Order, i.Tile, BoardPlan.Bounds(i))).ToArray();

        Assert.NotEmpty(whole);
        Assert.Equal(whole.Count, parts.Length);
        Assert.Subset(whole, parts.ToHashSet());
    }

    [Theory]
    [MemberData(nameof(Buildings))]
    public void A_buildings_shadow_lies_on_the_ground_outside_its_footprint(string id)
    {
        var def = Pack.Manifests.Buildings[id];
        var footprint = def.Footprint(At).ToHashSet();

        var shadows = Items(Ground(id)).OfType<Face>().Where(f => f.Kind == FaceKind.Shadow).ToArray();

        Assert.Equal(Manifest(id).GetProperty("variants").EnumerateObject().First().Value.TryGetProperty("shadow", out _), shadows.Length > 0);
        Assert.All(shadows, s =>
        {
            Assert.DoesNotContain(s.Tile, footprint);
            Assert.Equal(BoardOrder.Key(s.Tile, BoardLayer.Ground), s.Order);
            Assert.True(s.Order < BoardOrder.Key(s.Tile, BoardLayer.Unit));
            Assert.False(s.Lit);
        });
    }

    [Fact]
    public void A_shadow_samples_its_image_where_the_ground_is_under_it_and_goes_down_with_the_ground()
    {
        var art = Pack.Art.Building("tavern", "red")!;
        var frame = BoardProjection.Corner(At.X, At.Y, 2) - art.Origin;
        var bytes = TilesAt(24, 2);
        foreach (var x in Enumerable.Range(0, 24))
            bytes[(16 * 24 + x) * 3] = 3;          // the row y = 16 stands a step up
        var map = BattleMap.FromBytes("town", 24, 24, bytes, ["cobble/warm"], [], ImmutableSortedDictionary<string, ImmutableArray<Tile>>.Empty, null, [new BuildingPlacement("tavern", "red", At, 2)]);

        foreach (var shadow in Items(map).OfType<Face>().Where(f => f.Kind == FaceKind.Shadow))
        {
            var (x, y) = (shadow.Tile.X, shadow.Tile.Y);
            Assert.Equal(BoardProjection.Corner(x, y, y == 16 ? 3 : 2), shadow.Points[0]);
            Assert.Equal(BoardProjection.Corner(x, y, 2) - frame, shadow.Uvs[0] * art.Size);
        }
    }

    [Fact]
    public void An_animated_store_loops_at_its_manifests_fps_and_a_still_one_doesnt()
    {
        var store = Manifest("store");

        var image = Assert.Single(Columns("store").Select(c => c.Image!).Distinct());

        Assert.Equal((store.GetProperty("frames").GetInt32(), store.GetProperty("fps").GetDouble()), (image.Frames, image.Fps));
        Assert.Equal(1, Columns("tavern")[0].Image!.Frames);
    }

    [Fact]
    public void Smoke_rises_from_each_chimney_and_loops_at_its_manifests_fps()
    {
        var tavern = Manifest("tavern");
        var smoke = Pack.Manifest("town").RootElement.GetProperty("overlays").GetProperty("smoke");
        var frame = BoardProjection.Corner(At.X, At.Y, 0) - Point(tavern.GetProperty("origin"));

        var sprites = Items(Ground("tavern", "red")).OfType<Sprite>().Where(s => s.Image.Path.EndsWith("smoke.png", StringComparison.Ordinal)).ToArray();

        Assert.Equal(tavern.GetProperty("attach").GetProperty("smoke").EnumerateArray().Select(p => frame + Point(p) - Point(smoke.GetProperty("anchor"))).ToHashSet(), sprites.Select(s => s.TopLeft).ToHashSet());
        Assert.All(sprites, s =>
        {
            Assert.Equal((smoke.GetProperty("frames").GetInt32(), smoke.GetProperty("fps").GetDouble()), (s.Image.Frames, s.Image.Fps));
            Assert.Equal(Point(smoke.GetProperty("size")), s.Size);
        });
    }

    [Fact]
    public void Smoke_goes_after_every_column_of_its_building()
    {
        var columns = Columns("tavern");

        var smoke = Items(Ground("tavern", "red")).OfType<Sprite>().Where(s => s.Image.Path.EndsWith("smoke.png", StringComparison.Ordinal));

        Assert.All(smoke, s => Assert.True(s.Order > columns.Max(c => c.Order)));
    }

    [Theory]
    [InlineData("tavern", "sign_mug")]
    [InlineData("smithy", "sign_anvil")]
    public void A_sign_hangs_from_its_root_after_the_columns_behind_it(string id, string overlay)
    {
        var building = Manifest(id);
        var sign = Pack.Manifest("town").RootElement.GetProperty("overlays").GetProperty(overlay);
        var frame = BoardProjection.Corner(At.X, At.Y, 0) - Point(building.GetProperty("origin"));

        var sprite = Assert.Single(Items(Ground(id)).OfType<Sprite>(), s => s.Image.Path.EndsWith($"{overlay}.png", StringComparison.Ordinal));

        Assert.Equal(frame + Point(building.GetProperty("attach").GetProperty("sign")[0]) - Point(sign.GetProperty("anchor")), sprite.TopLeft);
        Assert.All(Columns(id).Where(c => c.Points[0].X < sprite.TopLeft.X + sprite.Size.X && c.Points[1].X > sprite.TopLeft.X), c => Assert.True(sprite.Order > c.Order));
    }

    [Fact]
    public void A_sign_doesnt_hang_over_someone_in_front_of_the_wall_under_it()
    {
        var sprite = Assert.Single(Items(Ground("smithy")).OfType<Sprite>(), s => s.Image.Path.EndsWith("sign_anvil.png", StringComparison.Ordinal));

        Assert.True(sprite.Order < BoardOrder.Key(new Tile(At.X, At.Y + 2), BoardLayer.Unit));
    }

    [Fact]
    public void Every_piece_of_a_building_says_which_it_is_so_it_can_fade_as_one()
    {
        var map = BattleMap.FromBytes("town", 24, 24, TilesAt(24, 0), ["cobble/warm"], [], ImmutableSortedDictionary<string, ImmutableArray<Tile>>.Empty, null,
            [new BuildingPlacement("tavern", "red", new Tile(2, 2), 0), new BuildingPlacement("smithy", "oak", At, 0)]);

        var items = Items(map);

        Assert.Equal([0, 1], items.OfType<Face>().Where(f => f.Kind == FaceKind.Building).Select(f => f.Part).Distinct().Order());
        Assert.All(items.OfType<Sprite>(), s => Assert.True(s.Part >= 0));
        Assert.All(items.OfType<Face>().Where(f => f.Kind is FaceKind.Top or FaceKind.Shadow), f => Assert.Equal(-1, f.Part));
    }

    [Fact]
    public void A_building_stands_on_the_ground_at_its_back_corner()
    {
        var low = Columns("cottage")[0].Points[0];
        var high = Columns("cottage", height: 3)[0].Points[0];

        Assert.Equal(low - new Vector2(0, 3 * BoardProjection.HeightStep), high);
    }

    [Fact]
    public void A_map_with_no_buildings_draws_as_it_did()
    {
        var map = Pack.Map("river_crossing");

        Assert.DoesNotContain(BoardPlan.Build(new BoardTerrain(map, null), Pack.Art).Items, i => i is Face { Kind: FaceKind.Building or FaceKind.Shadow });
    }
}
