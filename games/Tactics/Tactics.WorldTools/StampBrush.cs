using System.Collections.Immutable;
using System.Text.Json;
using Tactics.Logic;

namespace Tactics.WorldTools;

/// <summary>One tile a stamp puts down.</summary>
public sealed record StampCell(Tile At, string Material, int Height, TileFlags Flags);

/// <summary>
/// What pressing Apply stamp does: the tiles to write, or the problems that stop it. <see cref="LeftBehind"/> is what a move leaves:
/// the old area's tiles still hold the stamp, as nothing records what was under them, so the painter repaints them.
/// </summary>
public sealed record StampStroke(string Map, Tile At, IReadOnlyList<StampCell> Cells, IReadOnlyList<Tile> LeftBehind, ImmutableArray<string> Problems)
{
    /// <summary>What to tell the painter after a stroke that went through.</summary>
    public string Report => LeftBehind.Count == 0
        ? $"Stamped '{Map}' at {At}. Undo takes it back."
        : $"Stamped '{Map}' at {At}. {TileSpread.Say(LeftBehind, "still holds", "still hold")} the old stamp's tiles: repaint {TileSpread.It(LeftBehind)}. Undo takes it all back.";
}

/// <summary>Writes a battle map onto a region's tiles. A pure sum, so the plugin's button and <see cref="RegionSource.Stamp"/> agree.</summary>
public static class StampBrush
{
    /// <summary>Every tile of <paramref name="map"/> placed at <paramref name="at"/>, exactly as the map has it plus the height offset.</summary>
    public static IEnumerable<StampCell> Cells(BattleMap map, Tile at, int heightOffset)
    {
        for (var y = 0; y < map.Height; y++)
            for (var x = 0; x < map.Width; x++)
            {
                var tile = map[new Tile(x, y)];
                yield return new StampCell(new Tile(at.X + x, at.Y + y), map.Materials[tile.Material], tile.Height + heightOffset, tile.Flags);
            }
    }

    /// <summary>
    /// The stroke for a stamp at <paramref name="at"/>. <paramref name="appliedAt"/> is where the stamp's tiles are now, if anywhere.
    /// It's refused if the map doesn't fit in the region or a height would leave 0 to 9, since a layer can't hold it.
    /// </summary>
    public static StampStroke Apply(BattleMap map, Tile at, int heightOffset, Tile? appliedAt, int width, int height)
    {
        var problems = new List<string>();
        if (at.X < 0 || at.Y < 0 || at.X + map.Width > width || at.Y + map.Height > height)
            problems.Add($"The stamp '{map.Id}' at {at} is {map.Width}x{map.Height}, which doesn't fit in the {width}x{height} region. Move the marker.");

        var cells = Cells(map, at, heightOffset).ToList();
        if (cells.FirstOrDefault(c => c.Height is < 0 or > WorldRegion.MaxHeight) is { } high)
            problems.Add($"The stamp '{map.Id}' at height {heightOffset} would put a tile at {high.Height}, but heights go from 0 to {WorldRegion.MaxHeight}. Change HeightOffset.");
        if (problems.Count > 0)
            return new StampStroke(map.Id, at, [], [], [.. problems]);

        var covered = cells.Select(c => c.At).ToHashSet();
        IReadOnlyList<Tile> left = appliedAt is { } old && old != at
            ? [.. Cells(map, old, heightOffset).Select(c => c.At).Where(t => !covered.Contains(t))]
            : [];
        return new StampStroke(map.Id, at, cells, left, []);
    }
}

/// <summary>Reads battle maps straight from a pack's files, so a broken pack elsewhere doesn't stop the workspace reading one.</summary>
public static class StampMaps
{
    /// <summary>The map in <c>maps/&lt;id&gt;.json</c>, without its props or zones, or null if there's no such readable file.</summary>
    public static BattleMap? Read(string rulesDirectory, string id)
    {
        var path = Path.Combine(rulesDirectory, "maps", $"{id}.json");
        if (id.Length == 0 || id.IndexOfAny(['/', '\\']) >= 0 || !File.Exists(path))
            return null;
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllBytes(path));
            var root = json.RootElement;
            var size = root.GetProperty("size");
            ImmutableArray<string> materials = [.. root.GetProperty("materials").EnumerateArray().Select(m => m.GetString()!)];
            return BattleMap.FromBytes(id, size[0].GetInt32(), size[1].GetInt32(), Convert.FromBase64String(root.GetProperty("tiles").GetString()!),
                materials, [], ImmutableSortedDictionary<string, ImmutableArray<Tile>>.Empty);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException or InvalidOperationException or KeyNotFoundException or IOException)
        {
            return null;
        }
    }
}
