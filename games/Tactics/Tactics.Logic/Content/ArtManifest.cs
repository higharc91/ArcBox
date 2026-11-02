using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tactics.Logic;

/// <summary>
/// Definition of a prop from <c>props/manifest.json</c>.
/// </summary>
public sealed record PropArtDef(string Id, ImmutableSortedSet<string> Variants, bool Blocks, Cover Cover, int Height);

/// <summary>
/// What walking needs of a building in <c>town/manifest.json</c>. A footprint of <see cref="Width"/> x <see cref="Depth"/> covers tiles
/// x .. x + width - 1 and y .. y + depth - 1 from its back corner, and <see cref="Blocks"/> says, by row and column, which of them stop the party.
/// </summary>
/// <param name="Id">The building's id.</param>
/// <param name="Variants">The variants the manifest lists.</param>
/// <param name="Width">Tiles along x.</param>
/// <param name="Depth">Tiles along y.</param>
/// <param name="Blocks">One row per y, one entry per x: true stops the party.</param>
/// <param name="Cover">How much it hides.</param>
/// <param name="Origin">The pixel of its back corner in its image.</param>
/// <param name="Doors">Each door's threshold in its image.</param>
public sealed record BuildingArtDef(string Id, ImmutableSortedSet<string> Variants, int Width, int Depth, ImmutableArray<ImmutableArray<bool>> Blocks, Cover Cover,
    (int X, int Y) Origin, ImmutableArray<(int X, int Y)> Doors)
{
    private const double HalfWidth = 32, HalfHeight = 16;   // a tile is a 64 x 32 diamond

    /// <summary>The tiles it covers, standing at <paramref name="at"/>.</summary>
    public IEnumerable<Tile> Footprint(Tile at)
    {
        for (var y = 0; y < Depth; y++)
            for (var x = 0; x < Width; x++)
                yield return new Tile(at.X + x, at.Y + y);
    }

    /// <summary>The tiles that stop the party, standing at <paramref name="at"/>.</summary>
    public IEnumerable<Tile> Blocked(Tile at) => Footprint(at).Where(t => Blocks[t.Y - at.Y][t.X - at.X]);

    /// <summary>
    /// The tile outside each door, standing at <paramref name="at"/>. A threshold is a pixel: taken back through the projection it lands on the footprint's floor, near
    /// the +x or +y face its door is in, which are the two faces the camera sees, and the tile outside is the one across the nearer of them.
    /// </summary>
    public IEnumerable<Tile> DoorSteps(Tile at)
    {
        foreach (var door in Doors)
        {
            var (across, down) = ((door.X - Origin.X) / HalfWidth, (door.Y - Origin.Y) / HalfHeight);
            var (u, v) = ((down + across) / 2, (down - across) / 2);
            yield return Width - u < Depth - v
                ? new Tile(at.X + Width, at.Y + Math.Clamp((int)Math.Floor(v), 0, Depth - 1))
                : new Tile(at.X + Math.Clamp((int)Math.Floor(u), 0, Width - 1), at.Y + Depth);
        }
    }
}

/// <summary>
/// Every key the art sets define: <c>"&lt;set&gt;.&lt;asset&gt;"</c> (<c>"signature.radiant_bolt"</c>,
/// <c>"characters.goblin_archer"</c>) and, for an asset with variants, <c>"&lt;set&gt;.&lt;asset&gt;.&lt;variant&gt;"</c>
/// (<c>"beasts.wolf.grey"</c>). Content's presentation keys and sprites must be among them.
/// </summary>
public sealed record ArtManifest(
    [property: JsonConverter(typeof(OrdinalJsonConverter))] ImmutableSortedSet<string> Keys,
    ImmutableDictionary<string, PropArtDef> Props,
    ImmutableDictionary<string, BuildingArtDef> Buildings)
{
    public ArtManifest(ImmutableSortedSet<string> keys) : this(keys, ImmutableDictionary<string, PropArtDef>.Empty, ImmutableDictionary<string, BuildingArtDef>.Empty) { }

    public ArtManifest(ImmutableSortedSet<string> keys, ImmutableDictionary<string, PropArtDef> props) : this(keys, props, ImmutableDictionary<string, BuildingArtDef>.Empty) { }

    /// <summary>
    /// No art at all, for a region with nothing in it that art defines. <see cref="WorldGround.Of"/> refuses a region's prop or building it has no definition for, so this can't
    /// quietly make one walkable.
    /// </summary>
    public static ArtManifest None { get; } = new([]);
}

/// <summary>Reads the art sets' manifests.</summary>
public static class ArtManifests
{
    /// <summary>
    /// Reads <c>&lt;set&gt;/manifest.json</c> for every set folder in <paramref name="contentBaseDir"/> (e.g.
    /// <c>content/tactics/base</c>; the <c>rules</c> folder is a rules pack, not an art set). The manifests differ in shape,
    /// so every entry of every top-level object is an asset (with its <c>"variants"</c>, when it has an object of them),
    /// and every name in a top-level list of names is one too (the characters set lists its presets that way). A manifest
    /// that can't be read is a <see cref="ContentError"/> for <c>"&lt;set&gt;/manifest.json"</c>; every one found is thrown
    /// together as a <see cref="ContentException"/>.
    /// </summary>
    public static ArtManifest Load(string contentBaseDir)
    {
        var keys = new List<string>();
        var errors = new List<ContentError>();
        var props = new Dictionary<string, PropArtDef>(StringComparer.Ordinal);
        var buildings = new Dictionary<string, BuildingArtDef>(StringComparer.Ordinal);
        foreach (var dir in Directory.GetDirectories(contentBaseDir).Order(StringComparer.Ordinal))
        {
            var set = Path.GetFileName(dir);
            var path = Path.Combine(dir, "manifest.json");
            if (set == "rules" || !File.Exists(path))
                continue;
            try
            {
                var bytes = File.ReadAllBytes(path);
                keys.AddRange(KeysOf(set, bytes));
                if (set == "town")
                {
                    using var doc = JsonDocument.Parse(bytes);
                    foreach (var building in doc.RootElement.GetProperty("buildings").EnumerateObject())
                        buildings[building.Name] = ParseBuilding(building.Name, building.Value);
                }
                if (set == "props")
                {
                    using var doc = JsonDocument.Parse(bytes);
                    if (doc.RootElement.TryGetProperty("props", out var propsObj) && propsObj.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in propsObj.EnumerateObject())
                        {
                            var variants = prop.Value.TryGetProperty("variants", out var vars) && vars.ValueKind == JsonValueKind.Object
                                ? vars.EnumerateObject().Select(v => v.Name)
                                : [];
                            var blocks = prop.Value.TryGetProperty("blocks", out var b) && b.GetBoolean();
                            var cover = prop.Value.TryGetProperty("cover", out var c) ? ParseCover(c.GetString()) : Cover.None;
                            var height = prop.Value.TryGetProperty("height", out var h) ? h.GetInt32() : 1;
                            props[prop.Name] = new PropArtDef(prop.Name, Ordinal.Set(variants), blocks, cover, height);
                        }
                    }
                }
            }
            catch (Exception ex) when (Validation.NonFatal(ex))
            {
                errors.Add(new ContentError($"{set}/manifest.json", "", Pointer.CleanMessage(ex.Message)));
            }
        }
        if (errors.Count > 0)
            throw new ContentException([.. errors]);
        return new ArtManifest(Ordinal.Set(keys), props.ToImmutableDictionary(StringComparer.Ordinal), buildings.ToImmutableDictionary(StringComparer.Ordinal));
    }

    /// <summary>A manifest writes <c>blocks</c> as rows for a footprint more than a tile deep, and as one row for a footprint one tile deep.</summary>
    private static BuildingArtDef ParseBuilding(string id, JsonElement building)
    {
        var footprint = building.GetProperty("footprint");
        var (width, depth) = (footprint[0].GetInt32(), footprint[1].GetInt32());
        var blocks = building.GetProperty("blocks");
        var rows = blocks[0].ValueKind == JsonValueKind.Array ? blocks.EnumerateArray().ToArray() : [blocks];
        if (rows.Length != depth || rows.Any(r => r.GetArrayLength() != width))
            throw new FormatException($"Building '{id}' has a footprint of {width}x{depth}, which its blocks don't match.");
        var origin = building.GetProperty("origin");
        var doors = building.TryGetProperty("attach", out var attach) && attach.TryGetProperty("door", out var door)
            ? door.EnumerateArray().Select(d => (d[0].GetInt32(), d[1].GetInt32())).ToImmutableArray()
            : [];
        return new BuildingArtDef(id, Ordinal.Set(building.GetProperty("variants").EnumerateObject().Select(v => v.Name)), width, depth,
            [.. rows.Select(r => r.EnumerateArray().Select(b => b.GetInt32() != 0).ToImmutableArray())],
            building.TryGetProperty("cover", out var cover) ? ParseCover(cover.GetString()) : Cover.None, (origin[0].GetInt32(), origin[1].GetInt32()), doors);
    }

    private static Cover ParseCover(string? cover) => cover switch
    {
        "full" or "total" => Cover.Total,
        "half" => Cover.Half,
        "three_quarters" => Cover.ThreeQuarters,
        _ => Cover.None,
    };

    /// <summary>The keys one set's manifest defines. Throws on JSON that isn't an object.</summary>
    private static List<string> KeysOf(string set, byte[] manifest)
    {
        var keys = new List<string>();
        using var doc = JsonDocument.Parse(manifest);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            throw new FormatException("An art manifest must be a JSON object.");
        foreach (var group in doc.RootElement.EnumerateObject())
        {
            if (group.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var asset in group.Value.EnumerateObject())
                {
                    keys.Add($"{set}.{asset.Name}");
                    if (asset.Value.ValueKind == JsonValueKind.Object && asset.Value.TryGetProperty("variants", out var variants)
                        && variants.ValueKind == JsonValueKind.Object)
                        keys.AddRange(variants.EnumerateObject().Select(v => $"{set}.{asset.Name}.{v.Name}"));
                }
            }
            else if (group.Value.ValueKind == JsonValueKind.Array)
            {
                keys.AddRange(group.Value.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String && IsName(item.GetString()!))
                    .Select(item => $"{set}.{item.GetString()}"));
            }
        }
        return keys;
    }

    private static bool IsName(string text) => text.Length > 0 && text.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
}
