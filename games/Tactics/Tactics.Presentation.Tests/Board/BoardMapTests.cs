using Tactics.Logic;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.Tests.Board;

/// <summary><see cref="BoardMap"/>: a map file read the way <c>web/play/src/games/tactics/map.ts</c> reads it.</summary>
public sealed class BoardMapTests
{
    [Fact]
    public void Tiles_are_three_bytes_each_row_by_row()
    {
        byte[] tiles = [3, 1, 0, 0, 0, 17];   // 2x1: height 3 of material 1, then deep water
        var json = $$"""
            { "id": "t", "size": [2, 1], "materials": ["grass/lush", "water/clear"], "tiles": "{{Convert.ToBase64String(tiles)}}",
              "props": [ { "id": "crate", "at": [1, 0], "blocks": true, "cover": "half" } ], "zones": {} }
            """;

        var map = BoardMap.Parse(json);

        Assert.Equal((2, 1), (map.Width, map.Height));
        Assert.Equal(new TileInfo(3, 1, TileFlags.None), map[new Tile(0, 0)]);
        Assert.Equal(new TileInfo(0, 0, TileFlags.Water | TileFlags.Deep), map[new Tile(1, 0)]);
        Assert.Equal("crate", Assert.Single(map.Props).Id);
    }

    [Fact]
    public void The_real_river_crossing_has_its_river_ford_and_ridge()
    {
        var map = Pack.Map("river_crossing");

        Assert.Equal((14, 12), (map.Width, map.Height));
        Assert.Equal("water/clear", map.Materials[map[new Tile(6, 0)].Material]);
        Assert.True(map[new Tile(6, 0)].Flags.HasFlag(TileFlags.Deep));
        Assert.False(map[new Tile(6, 7)].Flags.HasFlag(TileFlags.Deep));   // the ford
        Assert.Equal(3, map[new Tile(10, 0)].Height);                      // the ridge
    }

    [Fact]
    public void A_broken_map_file_throws_instead_of_drawing_nonsense()
        => Assert.ThrowsAny<Exception>(() => BoardMap.Parse(
            """{ "id": "t", "size": [2, 1], "materials": ["a"], "tiles": "AAA=", "props": [], "zones": {} }"""));

    [Theory]
    [MemberData(nameof(MapIds))]
    public void Every_sample_map_loads(string id) => Assert.Equal((14, 12), (Pack.Map(id).Width, Pack.Map(id).Height));

    public static TheoryData<string> MapIds() => new(Pack.Maps);
}
