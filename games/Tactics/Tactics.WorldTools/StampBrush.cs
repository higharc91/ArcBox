using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Tactics.Logic;

namespace Tactics.WorldTools;

/// <summary>One tile a stamp puts down.</summary>
public sealed record StampCell(Tile At, string Material, int Height, TileFlags Flags);

/// <summary>
/// What pressing Apply stamp does: the tiles to write, or the problems that stop it. <see cref="Pending"/> is every old area that may
/// still hold a stamp's tiles once it's done, since nothing records what was under them: they wait for the painter to repaint them.
/// </summary>
public sealed record StampStroke(string Map, Tile At, IReadOnlyList<StampCell> Cells, IReadOnlyList<RegionStamp> Pending, ImmutableArray<string> Problems)
{
    /// <summary>What to tell the painter after a stroke that went through.</summary>
    public string Report => Pending.Count switch
    {
        0 => $"Stamped '{Map}' at {At}. Undo takes it back.",
        1 => $"Stamped '{Map}' at {At}. 1 old area still holds a stamp: repaint it. Undo takes this one back.",
        var count => $"Stamped '{Map}' at {At}. {count} old areas still hold a stamp: repaint them. Undo takes this one back.",
    };
}

/// <summary>Writes a battle map onto a region's tiles. A pure sum, so the plugin's button and <see cref="RegionSource.Stamp"/> agree.</summary>
public static class StampBrush
{
    private const char Separator = '|';

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
    /// The stroke for a stamp at <paramref name="at"/>. <paramref name="applied"/> is where this stamp's tiles are now, if anywhere, and
    /// <paramref name="pending"/> the old areas it already left. An area the new tiles cover completely stops being pending, so moving
    /// a stamp back clears it, and one with a map <paramref name="maps"/> can't find stays. It's refused, with nothing changed, if the map
    /// doesn't fit in the region or a height would leave 0 to 9, since a layer can't hold it.
    /// </summary>
    public static StampStroke Apply(BattleMap map, Tile at, int heightOffset, RegionStamp? applied, IReadOnlyList<RegionStamp> pending,
        Func<string, BattleMap?> maps, int width, int height)
    {
        var problems = new List<string>();
        if (at.X < 0 || at.Y < 0 || at.X + map.Width > width || at.Y + map.Height > height)
            problems.Add($"The stamp '{map.Id}' at {at} is {map.Width}x{map.Height}, which doesn't fit in the {width}x{height} region. Move the marker.");

        var cells = Cells(map, at, heightOffset).ToList();
        if (cells.FirstOrDefault(c => c.Height is < 0 or > WorldRegion.MaxHeight) is { } high)
            problems.Add($"The stamp '{map.Id}' at height {heightOffset} would put a tile at {high.Height}, but heights go from 0 to {WorldRegion.MaxHeight}. Change HeightOffset.");
        if (problems.Count > 0)
            return new StampStroke(map.Id, at, [], pending, [.. problems]);

        var covered = cells.Select(c => c.At).ToHashSet();
        IEnumerable<RegionStamp> old = applied is null ? pending : [.. pending, applied];
        return new StampStroke(map.Id, at, cells, [.. old.Distinct().Where(area => maps(area.Map) is not { } oldMap || Left(area, oldMap, covered).Count > 0)], []);
    }

    /// <summary>Every tile the stamps cover, leaving out any whose map <paramref name="maps"/> can't find.</summary>
    public static HashSet<Tile> Covered(IEnumerable<RegionStamp> stamps, Func<string, BattleMap?> maps)
        => [.. stamps.SelectMany(s => maps(s.Map) is { } map ? Cells(map, s.At, s.Height).Select(c => c.At) : [])];

    /// <summary>The line about an old area, or null if <paramref name="covered"/> holds all of it. <paramref name="map"/> is null if it's gone.</summary>
    public static string? PendingWarning(RegionStamp area, BattleMap? map, IReadOnlySet<Tile> covered)
    {
        if (map is null)
            return $"The old stamp '{area.Map}' at {area.At} may still be in the world. Repaint it, then press Done repainting.";
        var left = Left(area, map, covered);
        return left.Count == 0
            ? null
            : $"{TileSpread.Say(left, "still holds", "still hold")} the old stamp '{area.Map}'. Repaint {TileSpread.It(left)}, then press Done repainting.";
    }

    /// <summary>A stamp as text, to keep on a marker.</summary>
    public static string Encode(RegionStamp stamp)
        => string.Join(Separator, stamp.Map, stamp.At.X.ToString(CultureInfo.InvariantCulture), stamp.At.Y.ToString(CultureInfo.InvariantCulture), stamp.Height.ToString(CultureInfo.InvariantCulture));

    /// <summary>The stamp <see cref="Encode"/> wrote, or null if the text isn't one.</summary>
    public static RegionStamp? Decode(string text)
    {
        static bool Number(string s, out int n) => int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out n);

        return text.Split(Separator) is [var map, var x, var y, var height] && Number(x, out var tileX) && Number(y, out var tileY) && Number(height, out var offset)
            ? new RegionStamp(map, new Tile(tileX, tileY), offset)
            : null;
    }

    private static List<Tile> Left(RegionStamp area, BattleMap map, IReadOnlySet<Tile> covered)
        => [.. Cells(map, area.At, area.Height).Select(c => c.At).Where(t => !covered.Contains(t))];
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
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException or InvalidOperationException or KeyNotFoundException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
