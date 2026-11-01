using System.Collections.Immutable;
using System.Text.Json;
using ArcBox.Sdk;
using Tactics.Logic;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.World;

/// <summary>
/// Dev-only ground for <c>--world-demo</c>: a region of the pack, read from its files, or <see cref="Hilly"/>, a stress case far busier than
/// any painted region. Nothing here is rules: the numbers are chosen to cost the TV something.
/// </summary>
public static class DemoRegion
{
    private const string PropsMap = "demo_props";

    // One prop to this many tiles, and of those, one in this many gives light. That's 960 lights on 160 x 120, as the lighting tests use.
    private const int TilesPerProp = 5;
    private const int PropsPerLight = 4;

    private static readonly ImmutableArray<string> Materials = ["grass/lush", "grass/dry", "dirt/brown", "rock/grey", "sand/desert", "water/clear"];
    private const byte GrassA = 0, GrassB = 1, Dirt = 2, Rock = 3, Sand = 4, Water = 5;

    private static readonly string[] Plain = ["tree", "pine", "bush", "rock_medium", "rock_large", "tall_grass", "stump", "boulder", "dead_tree", "crate"];
    private static readonly string[] Lit = ["torch", "campfire", "lamp_post", "lantern"];

    /// <summary>The region <paramref name="id"/> as the base pack at <paramref name="packRoot"/> has it, with only its stamped maps read.</summary>
    public static WorldGround FromPack(string packRoot, string id)
    {
        var rules = Path.Combine(packRoot, "rules");
        var def = JsonSerializer.Deserialize<RegionDef>(File.ReadAllText(Path.Combine(rules, "world", $"{id}.json")), ArcJson.Options)
            ?? throw new FormatException($"Region {id} is empty.");
        var maps = def.Stamps.Select(s => s.Map).Distinct(StringComparer.Ordinal)
            .ToDictionary(m => m, m => BoardMap.Parse(File.ReadAllText(Path.Combine(rules, "maps", $"{m}.json"))), StringComparer.Ordinal);
        return WorldGround.Of(WorldRegion.From(def), maps);
    }

    /// <summary>A generated region with a river through rolling hills, thick with props and lights. The same every time.</summary>
    public static WorldGround Hilly(int width, int height)
    {
        var tiles = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                Lay(tiles.AsSpan((y * width + x) * 3, 3), x, y, height);

        var (columns, rows) = ((width + WorldRegion.ChunkSize - 1) / WorldRegion.ChunkSize, (height + WorldRegion.ChunkSize - 1) / WorldRegion.ChunkSize);
        ImmutableArray<RegionChunk> chunks = [.. Enumerable.Range(0, rows).SelectMany(r => Enumerable.Range(0, columns).Select(c => Chunk(tiles, width, height, c, r)))];
        var def = new RegionDef("demo_hills", "Demo hills", 0, [width, height], Materials, chunks, [new RegionStamp(PropsMap, new Tile(0, 0), 0)], [],
            new RegionSpawns(new Tile(width / 2, height / 2), new Tile(width / 2, height / 2)), []);
        var props = BattleMap.FromBytes(PropsMap, 1, 1, new byte[3], ["void/deep"], Scatter(tiles, width, height),
            ImmutableSortedDictionary<string, ImmutableArray<Tile>>.Empty);
        return WorldGround.Of(WorldRegion.From(def), new Dictionary<string, BattleMap> { [PropsMap] = props });
    }

    private static double RiverAt(int x, int height) => height * 0.5 + height * 0.18 * Math.Sin(x * 0.07) + height * 0.06 * Math.Sin(x * 0.19 + 1);

    private static void Lay(Span<byte> tile, int x, int y, int height)
    {
        var fromRiver = Math.Abs(y - RiverAt(x, height));
        if (fromRiver < 3)
        {
            (tile[0], tile[1], tile[2]) = (0, Water, (byte)(TileFlags.Water | TileFlags.Deep));
            return;
        }
        var rolling = 2.6 * Math.Sin(x * 0.21 + 0.5) * Math.Cos(y * 0.17) + 1.7 * Math.Sin(x * 0.53 + y * 0.31) + 1.1 * Math.Sin(x * 0.11 - y * 0.47);
        var banks = Math.Min(1, (fromRiver - 3) / 9);
        var level = (int)Math.Round((4.5 + rolling) * banks);
        var rise = Math.Clamp(level, 1, WorldRegion.MaxHeight);
        var patch = Math.Sin(x * 0.3) + Math.Sin(y * 0.37);
        var material = fromRiver < 5 ? Sand : rise >= 7 ? Rock : rise >= 5 ? Dirt : patch > 0 ? GrassA : GrassB;
        (tile[0], tile[1], tile[2]) = ((byte)rise, material, 0);
    }

    private static RegionChunk Chunk(byte[] tiles, int width, int height, int column, int row)
    {
        var bytes = new byte[WorldRegion.ChunkBytes];
        for (var y = 0; y < WorldRegion.ChunkSize; y++)
            for (var x = 0; x < WorldRegion.ChunkSize; x++)
            {
                var (worldX, worldY) = (column * WorldRegion.ChunkSize + x, row * WorldRegion.ChunkSize + y);
                if (worldX < width && worldY < height)
                    tiles.AsSpan((worldY * width + worldX) * 3, 3).CopyTo(bytes.AsSpan((y * WorldRegion.ChunkSize + x) * 3, 3));
            }
        return new RegionChunk(column, row, Convert.ToBase64String(bytes));
    }

    private static ImmutableArray<PropPlacement> Scatter(byte[] tiles, int width, int height)
    {
        var random = new Random(5);
        var props = ImmutableArray.CreateBuilder<PropPlacement>();
        var taken = new HashSet<Tile>();
        var count = width * height / TilesPerProp;
        while (props.Count < count)
        {
            var at = new Tile(random.Next(width), random.Next(height));
            if (tiles[(at.Y * width + at.X) * 3 + 1] == Water || !taken.Add(at))
                continue;
            var kinds = props.Count % PropsPerLight == 0 ? Lit : Plain;
            props.Add(new PropPlacement(kinds[random.Next(kinds.Length)], at, true, Cover.None));
        }
        return props.ToImmutable();
    }
}
