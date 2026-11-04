using System.Collections.Immutable;
using Tactics.Logic;

namespace Tactics.WorldTools;

/// <summary>What <see cref="RegionWriter.Write"/> made: a region with no problems, or null and everything that stopped it.</summary>
public sealed record WriteResult(RegionDef? Region, ImmutableArray<string> Problems);

/// <summary>Turns a <see cref="RegionSource"/> into a <see cref="RegionDef"/>.</summary>
public static class RegionWriter
{
    /// <summary>A tile's material is one byte.</summary>
    private const int MaxMaterials = byte.MaxValue + 1;

    private const string HeightFix = "Repaint the Height layer, or change the stamp's HeightOffset.";

    private static readonly TileFlags KnownFlags = Enum.GetValues<TileFlags>().Aggregate((all, flag) => all | flag);

    /// <summary>
    /// Writes the region, or says why it can't: a tile outside the region, a height outside 0 to 9, a material that isn't in
    /// <paramref name="knownMaterials"/>, undefined flag bits or too many materials. Each kind of problem, and each unknown material, is
    /// one line with its count and place. The same grid always gives the same region, however it was
    /// painted: chunks go by row and materials by ordinal name, so a small paint change gives a small diff. A chunk with nothing
    /// painted is left out, and an unpainted tile in a listed chunk is written <see cref="TileFlags.Impassable"/>, since a blank
    /// tile would be walkable ground. Whether the region's markers can be reached, and whether its stamps still match their maps,
    /// are the pack loader's checks.
    /// </summary>
    public static WriteResult Write(RegionSource source, IReadOnlySet<string> knownMaterials)
    {
        bool Inside(Tile t) => t.X >= 0 && t.Y >= 0 && t.X < source.Width && t.Y < source.Height;

        var tiles = source.Tiles
            .Where(t => Inside(t.Key))
            .OrderBy(t => t.Key.Y / WorldRegion.ChunkSize).ThenBy(t => t.Key.X / WorldRegion.ChunkSize)
            .ThenBy(t => t.Key.Y).ThenBy(t => t.Key.X)
            .ToList();

        var problems = new List<string>();
        var outside = source.Tiles.Keys.Where(t => !Inside(t)).ToList();
        if (outside.Count > 0)
            problems.Add($"{TileSpread.Say(outside, "is", "are")} outside the {source.Width}x{source.Height} region. Erase what's painted there, or change Size on the Region node.");

        var materials = tiles.Select(t => t.Value.Material).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select((name, index) => (name, index)).ToDictionary(m => m.name, m => m.index, StringComparer.Ordinal);
        foreach (var unknown in tiles.Where(t => !knownMaterials.Contains(t.Value.Material)).GroupBy(t => t.Value.Material, StringComparer.Ordinal))
        {
            var where = unknown.Select(t => t.Key).ToList();
            problems.Add($"{TileSpread.Say(where, "uses", "use")} the material '{unknown.Key}', which isn't in the palette or a stamped map. Repaint {TileSpread.It(where)} from the palette.");
        }

        var heights = tiles.Where(t => t.Value.Height is < 0 or > WorldRegion.MaxHeight).ToList();
        if (heights is [var one])
            problems.Add($"The tile {one.Key} has height {one.Value.Height}, but heights go from 0 to {WorldRegion.MaxHeight}. {HeightFix}");
        else if (heights.Count > 1)
            problems.Add($"{TileSpread.Say([.. heights.Select(t => t.Key)], "", "have")} heights outside 0 to {WorldRegion.MaxHeight}, the first at {heights.OrderBy(t => t.Key.Y).ThenBy(t => t.Key.X).First().Value.Height}. {HeightFix}");

        var flags = tiles.Where(t => (t.Value.Flags & ~KnownFlags) != 0).ToList();
        if (flags.Count > 0)
        {
            var bits = flags.Aggregate(TileFlags.None, (all, t) => all | (t.Value.Flags & ~KnownFlags));
            List<Tile> where = [.. flags.Select(t => t.Key)];
            problems.Add($"{TileSpread.Say(where, "has", "have")} undefined flag bits 0x{(int)bits:x2}. Repaint {TileSpread.It(where)} on the Flags layer.");
        }

        if (materials.Count > MaxMaterials)
            problems.Add($"The region uses {materials.Count} materials, but a tile can only index {MaxMaterials}. Repaint some tiles so it uses fewer.");
        if (problems.Count > 0)
            return new WriteResult(null, [.. problems]);

        var chunks = tiles.GroupBy(t => (X: t.Key.X / WorldRegion.ChunkSize, Y: t.Key.Y / WorldRegion.ChunkSize))
            .Select(chunk => new RegionChunk(chunk.Key.X, chunk.Key.Y, Convert.ToBase64String(Bytes(chunk, materials))));
        var props = source.Props.Count == 0
            ? default
            : source.Props
                .SelectMany(g => g.At.Select(t => (g.Id, g.Variant, Tile: t)))
                .GroupBy(p => (p.Id, p.Variant))
                .OrderBy(g => g.Key.Id, StringComparer.Ordinal)
                .ThenBy(g => g.Key.Variant, StringComparer.Ordinal)
                .Select(g => new RegionProps(g.Key.Id, g.Key.Variant, [.. g.Select(p => p.Tile).Distinct().Order()]))
                .ToImmutableArray();
        var buildings = source.Buildings.Count == 0
            ? default
            : source.Buildings.Distinct().OrderBy(b => b.At.Y).ThenBy(b => b.At.X).ThenBy(b => b.Id, StringComparer.Ordinal).ThenBy(b => b.Variant, StringComparer.Ordinal).ToImmutableArray();
        var people = source.People.Count == 0
            ? default
            : source.People.DistinctBy(PersonText.Key).OrderBy(p => p.At.Y).ThenBy(p => p.At.X).ThenBy(p => p.Preset, StringComparer.Ordinal).ThenBy(p => p.Face, StringComparer.Ordinal).ThenBy(p => p.Variant, StringComparer.Ordinal).ToImmutableArray();
        var def = new RegionDef(source.Id, source.Name, source.Chapter, [source.Width, source.Height],
            [.. materials.OrderBy(m => m.Value).Select(m => m.Key)], [.. chunks], [.. source.Stamps], [.. source.Beats],
            new RegionSpawns(source.Start, source.Recovery, source.NamedSpawns.Count == 0 ? null : source.NamedSpawns.ToImmutableSortedDictionary(StringComparer.Ordinal)), [], props, buildings, people);
        return new WriteResult(def, []);
    }

    /// <summary>
    /// A copy of <paramref name="source"/> that <see cref="Write"/> accepts, so the checks that need a built region still run when it
    /// has problems: never a region to keep. Tiles outside are dropped, undefined flag bits cleared, and a bad material or height
    /// replaced. On a stamped tile the replacement comes from <paramref name="stamped"/>, so a problem already reported doesn't come
    /// back as a stamp mismatch.
    /// </summary>
    public static RegionSource Repair(RegionSource source, IReadOnlySet<string> knownMaterials, IReadOnlyDictionary<Tile, TileInfoNamed> stamped)
    {
        var repaired = new RegionSource(source.Id, source.Name, source.Chapter, source.Width, source.Height) { Start = source.Start, Recovery = source.Recovery };
        var anyMaterial = knownMaterials.Order(StringComparer.Ordinal).First();
        foreach (var (tile, info) in source.Tiles.Where(t => t.Key.X >= 0 && t.Key.Y >= 0 && t.Key.X < source.Width && t.Key.Y < source.Height))
        {
            var expected = stamped.GetValueOrDefault(tile);
            var material = knownMaterials.Contains(info.Material) ? info.Material : expected?.Material ?? anyMaterial;
            var height = info.Height is >= 0 and <= WorldRegion.MaxHeight ? info.Height : Math.Clamp(expected?.Height ?? info.Height, 0, WorldRegion.MaxHeight);
            repaired.Set(tile, height, material, info.Flags & KnownFlags);
        }
        repaired.Beats.AddRange(source.Beats);
        repaired.Props.AddRange(source.Props);
        repaired.Buildings.AddRange(source.Buildings);
        repaired.People.AddRange(source.People);
        foreach (var (name, at) in source.NamedSpawns) repaired.NamedSpawns.Add(name, at);
        foreach (var stamp in source.Stamps)
            repaired.AddStamp(stamp);
        return repaired;
    }

    /// <summary>One chunk's bytes: every tile impassable, then the painted ones.</summary>
    private static byte[] Bytes(IEnumerable<KeyValuePair<Tile, TileInfoNamed>> tiles, Dictionary<string, int> materials)
    {
        var bytes = new byte[WorldRegion.ChunkBytes];
        for (var i = 2; i < bytes.Length; i += 3)
            bytes[i] = (byte)TileFlags.Impassable;
        foreach (var (tile, info) in tiles)
        {
            var at = (tile.Y % WorldRegion.ChunkSize * WorldRegion.ChunkSize + tile.X % WorldRegion.ChunkSize) * 3;
            (bytes[at], bytes[at + 1], bytes[at + 2]) = ((byte)info.Height, (byte)materials[info.Material], (byte)info.Flags);
        }
        return bytes;
    }
}
