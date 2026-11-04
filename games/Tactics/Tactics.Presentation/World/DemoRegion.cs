using System.Collections.Immutable;
using System.Text.Json;
using System.Numerics;
using ArcBox.Sdk;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Beats;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.World;

/// <summary>
/// Dev-only ground for <c>--world-demo</c>: a region of the pack, read from its files, or <see cref="Hilly"/>, a stress case far busier than
/// any painted region. Nothing here is rules: the numbers are chosen to cost the TV something.
/// </summary>
public static class DemoRegion
{
    private const string PropsMap = "demo_props";
    private const string WalkerSprite = "characters.class_fighter";
    private const int WalkerHp = 34;

    // One prop to this many tiles, and of those, one in this many gives light. That's 960 lights on 160 x 120, as the lighting tests use.
    private const int TilesPerProp = 5;
    private const int PropsPerLight = 4;

    private static readonly ImmutableArray<string> Materials = ["grass/lush", "grass/dry", "dirt/brown", "rock/grey", "sand/desert", "water/clear"];
    private const byte GrassA = 0, GrassB = 1, Dirt = 2, Rock = 3, Sand = 4, Water = 5;

    private static readonly string[] Plain = ["tree", "pine", "bush", "rock_medium", "rock_large", "tall_grass", "stump", "boulder", "dead_tree", "crate"];
    private static readonly string[] Lit = ["torch", "campfire", "lamp_post", "lantern"];
    private static readonly string[] TallPlain = ["tree", "pine", "dead_tree"];
    private static readonly string[] TallLit = ["lamp_post", "lantern"];

    /// <summary>The region the TV opens for <paramref name="id"/>: the pack's, or the art check's courtyard (<see cref="ThingsDemo"/>), which no pack has.</summary>
    public static WorldGround Load(string packRoot, string id) => id == ThingsDemo.Region ? ThingsDemo.Ground() : FromPack(packRoot, id);

    /// <summary>The region <paramref name="id"/> as the base pack at <paramref name="packRoot"/> has it, with only its stamped maps read.</summary>
    public static WorldGround FromPack(string packRoot, string id)
    {
        var rules = Path.Combine(packRoot, "rules");
        var def = JsonSerializer.Deserialize<RegionDef>(File.ReadAllText(Path.Combine(rules, "world", $"{id}.json")), ArcJson.Options)
            ?? throw new FormatException($"Region {id} is empty.");
        var maps = def.Stamps.Select(s => s.Map).Distinct(StringComparer.Ordinal)
            .ToDictionary(m => m, m => BoardMap.Parse(File.ReadAllText(Path.Combine(rules, "maps", $"{m}.json"))), StringComparer.Ordinal);
        return WorldGround.Of(WorldRegion.From(def), maps, ArtManifests.Load(packRoot));
    }

    /// <summary>
    /// A flat lawn with one tavern on it, across the corner of four blocks, a crate before it and a lamp post behind it: the proof of what sorting and fading a building takes.
    /// </summary>
    public static WorldGround Tavern(string packRoot)
    {
        const int side = WorldRegion.ChunkSize;
        var def = new RegionDef("demo_tavern", "Demo tavern", 0, [side, side], ["grass/autumn"], [new RegionChunk(0, 0, Convert.ToBase64String(new byte[WorldRegion.ChunkBytes]))], [], [],
            new RegionSpawns(TavernWalk()[0], TavernWalk()[0]), [],
            [new RegionProps("crate", null, [new Tile(18, 15)]), new RegionProps("lamp_post", null, [new Tile(15, 12)])],
            [new RegionBuilding("tavern", "red", new Tile(14, 14))]);
        return WorldGround.Of(WorldRegion.From(def), new Dictionary<string, BattleMap>(), ArtManifests.Load(packRoot));
    }

    /// <summary>Twice round <see cref="Tavern"/>, one tile from its walls.</summary>
    public static ImmutableArray<Tile> TavernWalk()
    {
        var (left, top, right, bottom) = (13, 13, 17, 16);
        ImmutableArray<Tile> lap =
        [
            .. Enumerable.Range(left, right - left).Select(x => new Tile(x, top)),
            .. Enumerable.Range(top, bottom - top).Select(y => new Tile(right, y)),
            .. Enumerable.Range(0, right - left).Select(i => new Tile(right - i, bottom)),
            .. Enumerable.Range(0, bottom - top).Select(i => new Tile(left, bottom - i)),
        ];
        return [.. lap, .. lap];
    }

    /// <summary>
    /// A small town square at the ford's dusk, for judging the light against mockup 02 before the real Emberwick is built: cobbles, lamps, a well, and buildings round them. It takes
    /// the ford's id for its light. Nothing here is the region's.
    /// </summary>
    public static WorldGround Town(string packRoot)
    {
        const int side = WorldRegion.ChunkSize;
        ImmutableArray<string> materials = ["cobble/warm", "grass/autumn", "path/packed"];
        const byte cobble = 0, grass = 1, road = 2;
        var tiles = new byte[side * side * 3];
        for (var y = 0; y < side; y++)
            for (var x = 0; x < side; x++)
                (tiles[(y * side + x) * 3], tiles[(y * side + x) * 3 + 1]) = ((byte)1, x is >= 9 and <= 21 && y is >= 10 and <= 21 ? cobble : x is >= 14 and <= 16 && y > 21 ? road : grass);
        var start = new Tile(15, 19);
        var def = new RegionDef("ashen_ford", "Demo town", 0, [side, side], materials, [Chunk(tiles, side, side, 0, 0)], [], [], new RegionSpawns(start, start), [],
        [
            new RegionProps("lamp_post", "iron", [new Tile(10, 11), new Tile(20, 11), new Tile(10, 20), new Tile(20, 20), new Tile(13, 25), new Tile(17, 25)]),
            new RegionProps("well", null, [new Tile(15, 15)]),
            new RegionProps("crate", "oak", [new Tile(22, 14), new Tile(22, 15)]),
            new RegionProps("tree", "autumn", [new Tile(3, 12), new Tile(26, 18), new Tile(25, 24), new Tile(5, 24)]),
            new RegionProps("pine", "green", [new Tile(27, 9), new Tile(3, 18)]),
        ],
        [
            new RegionBuilding("tavern", "red", new Tile(9, 8)),
            new RegionBuilding("store", "blue", new Tile(13, 8)),
            new RegionBuilding("smithy", "oak", new Tile(16, 8)),
            new RegionBuilding("cottage", "shakes_oak", new Tile(19, 8)),
            new RegionBuilding("chapel", "warm", new Tile(6, 10)),
            new RegionBuilding("house", "green", new Tile(6, 14)),
            new RegionBuilding("cottage", "thatch_red", new Tile(6, 17)),
            new RegionBuilding("house", "blue", new Tile(23, 11)),
            new RegionBuilding("stall", "red", new Tile(12, 12)),
            new RegionBuilding("stall", "yellow", new Tile(13, 12)),
            new RegionBuilding("notice_board_u", "oak", new Tile(18, 12)),
            new RegionBuilding("clutter_barrels", "oak", new Tile(21, 18)),
            new RegionBuilding("wall_u", "warm", new Tile(10, 27)), new RegionBuilding("wall_u", "warm", new Tile(11, 27)), new RegionBuilding("wall_u", "warm", new Tile(12, 27)),
            new RegionBuilding("wall_tower", "warm", new Tile(13, 27)),
            new RegionBuilding("gate_u", "warm", new Tile(14, 27)),
            new RegionBuilding("wall_tower", "warm", new Tile(17, 27)),
            new RegionBuilding("wall_u", "warm", new Tile(18, 27)), new RegionBuilding("wall_u", "warm", new Tile(19, 27)), new RegionBuilding("wall_u", "warm", new Tile(20, 27)),
        ]);
        return WorldGround.Of(WorldRegion.From(def), new Dictionary<string, BattleMap>(), ArtManifests.Load(packRoot));
    }

    /// <summary>What a material looks like in the ford's light, for choosing them.</summary>
    private static readonly ImmutableArray<string> SwatchMaterials =
    [
        "ash/grey", "basalt/dark", "dirt/dark", "dirt/brown", "path/packed", "grass/dry", "grass/autumn", "grass/frost", "grass/green", "grass/lush",
        "forest_floor/leafy", "forest_floor/mossy", "forest_needles/pine", "mud/swamp", "rock/grey", "rock/dark", "cobble/warm", "cobble/grey", "flagstone/warm",
        "flagstone/grey", "flagstone/dark", "pavers/warm", "pavers/grey", "pavers/dark", "sand/desert", "planks/oak",
    ];

    /// <summary>A band of each of <see cref="SwatchMaterials"/>, six tiles deep, in the ford's light, with four bands to a look.</summary>
    public static WorldGround Swatches(string packRoot)
    {
        const int side = WorldRegion.ChunkSize, band = 6;
        var (width, depth) = (side, side * ((SwatchMaterials.Length * band + side - 1) / side));
        var tiles = new byte[width * depth * 3];
        for (var y = 0; y < depth; y++)
            for (var x = 0; x < width; x++)
                (tiles[(y * width + x) * 3], tiles[(y * width + x) * 3 + 1]) = ((byte)1, (byte)Math.Min(y / band, SwatchMaterials.Length - 1));
        var start = SwatchLooks()[0].Stand;
        ImmutableArray<RegionChunk> chunks = [.. Enumerable.Range(0, depth / side).Select(r => Chunk(tiles, width, depth, 0, r))];
        var def = new RegionDef("ashen_ford", "Swatches", 0, [width, depth], SwatchMaterials, chunks, [], [], new RegionSpawns(start, start), []);
        return WorldGround.Of(WorldRegion.From(def), new Dictionary<string, BattleMap>(), ArtManifests.Load(packRoot));
    }

    /// <summary>Where <c>--world-demo=swatches</c> looks.</summary>
    public static ImmutableArray<WorldSpot> SwatchLooks()
        => [.. Enumerable.Range(0, (SwatchMaterials.Length + 3) / 4).Select(i => new WorldSpot($"swatch-{i}", new Tile(16, i * 24 + 12), new Tile(16, i * 24 + 12)))];

    /// <summary>Round the square, behind the tavern and out through the gate of <see cref="Town"/>.</summary>
    public static ImmutableArray<Tile> TownWalk()
    {
        static IEnumerable<Tile> Line(Tile from, Tile to)
        {
            var (dx, dy) = (Math.Sign(to.X - from.X), Math.Sign(to.Y - from.Y));
            for (var at = from; at != to; at = new Tile(at.X + dx, at.Y + dy))
                yield return at;
        }
        Tile[] corners = [new(15, 19), new(19, 19), new(19, 13), new(21, 13), new(21, 7), new(8, 7), new(8, 9), new(11, 9), new(11, 19), new(15, 19), new(15, 30)];
        return [.. corners.Zip(corners.Skip(1)).SelectMany(c => Line(c.First, c.Second)), corners[^1]];
    }

    /// <summary>Where <c>--world-demo=town</c> looks: the square as the mockup has it, a walker behind a house, and through the gate.</summary>
    public static ImmutableArray<WorldSpot> TownSpots()
    {
        var square = new Tile(15, 15);
        return
        [
            new WorldSpot("town-square", square, new Tile(15, 18)), new WorldSpot("town-door", new Tile(11, 11), new Tile(11, 10)),
            new WorldSpot("town-behind", square, new Tile(14, 7)), new WorldSpot("town-gate", new Tile(15, 26), new Tile(15, 27)),
        ];
    }

    /// <summary>Where <c>--world-demo=tavern</c> looks: a walker on each side of it.</summary>
    public static ImmutableArray<WorldSpot> TavernSpots()
    {
        var look = new Tile(15, 15);
        return
        [
            new WorldSpot("tavern-behind", look, new Tile(15, 13)), new WorldSpot("tavern-behind-right", look, new Tile(17, 13)),
            new WorldSpot("tavern-door", look, new Tile(15, 16)), new WorldSpot("tavern-left", look, new Tile(13, 15)), new WorldSpot("tavern-right", look, new Tile(17, 15)),
        ];
    }

    /// <summary>One unit walking <paramref name="heading"/> at <paramref name="place"/>, <paramref name="seconds"/> into its stride: the party, until there is one.</summary>
    public static ImmutableArray<BoardUnit> StandIn(BoardTerrain terrain, UnitArt art, UnitPlace place, Vector2 heading, double seconds,
        string sprite = WalkerSprite, string identity = "walker")
    {
        var id = new UnitId(identity);
        var at = new Tile(Math.Clamp((int)place.Map.X, 0, terrain.Map.Width - 1), Math.Clamp((int)place.Map.Y, 0, terrain.Map.Height - 1));
        var unit = new UnitView(id, id.Value, "Party", sprite, at, WalkerHp, WalkerHp, 0, "Up", 0, [], null);
        var battle = new BattleView(terrain.Map.Id, terrain.Map.Id, 1, [id], null, [unit], [], null, BoardChanges.None, ObjectivesDef.Default, null);
        var noStrip = new BoardFit(new Vector2(0, float.MaxValue / 4), 1);     // the world has no top strip for its marks to clear
        return UnitPlan.Build(battle, terrain, art, noStrip,
            new Dictionary<UnitId, UnitPlace> { [id] = place },
            new Dictionary<UnitId, UnitPose> { [id] = new UnitPose(BeatScript.WalkAnim, seconds, heading) });
    }

    /// <summary>Four distinct heroes for the memory gate, spread across the road at a shared ground height.</summary>
    public static ImmutableArray<BoardUnit> PerfParty(BoardTerrain terrain, UnitArt art, UnitPlace place, Vector2 heading, double seconds,
        ImmutableArray<string> roster = default)
        => [.. (roster.IsDefault ? WorldTextureBudget.Party : roster).SelectMany((sprite, i) => StandIn(terrain, art,
            new UnitPlace(place.Map + new Vector2(-i * .6f, i * .6f), place.Height), heading, seconds, sprite, $"walker-{i}"))];

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
        return WorldGround.Of(WorldRegion.From(def), new Dictionary<string, BattleMap> { [PropsMap] = props }, ArtManifest.None);
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

    /// <summary>A dense wood with a trail winding through it, containing over 1,500 tall props in one view's reach.</summary>
    public static WorldGround Wood(int width = 160, int height = 120)
    {
        var tiles = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                LayWood(tiles.AsSpan((y * width + x) * 3, 3), x, y, height);

        var (columns, rows) = ((width + WorldRegion.ChunkSize - 1) / WorldRegion.ChunkSize, (height + WorldRegion.ChunkSize - 1) / WorldRegion.ChunkSize);
        ImmutableArray<RegionChunk> chunks = [.. Enumerable.Range(0, rows).SelectMany(r => Enumerable.Range(0, columns).Select(c => Chunk(tiles, width, height, c, r)))];
        var start = WoodStart(width, height);
        var def = new RegionDef("demo_wood", "Demo wood", 0, [width, height], Materials, chunks, [new RegionStamp(PropsMap, new Tile(0, 0), 0)], [],
            new RegionSpawns(start, start), []);
        var props = BattleMap.FromBytes(PropsMap, 1, 1, new byte[3], ["void/deep"], ScatterWood(tiles, width, height),
            ImmutableSortedDictionary<string, ImmutableArray<Tile>>.Empty);
        return WorldGround.Of(WorldRegion.From(def), new Dictionary<string, BattleMap> { [PropsMap] = props }, ArtManifest.None);
    }

    /// <summary>Starting tile on the trail for <see cref="Wood"/>.</summary>
    public static Tile WoodStart(int width, int height) => new(4, (int)Math.Round(WoodTrailAt(4, height)));

    private static double WoodTrailAt(int x, int height) => height * 0.5 + height * 0.2 * Math.Sin(x * 0.045);

    private static void LayWood(Span<byte> tile, int x, int y, int height)
    {
        var fromTrail = Math.Abs(y - WoodTrailAt(x, height));
        if (fromTrail < 2.0)
        {
            tile[0] = 2; // rise
            tile[1] = Dirt;
            tile[2] = 0;
            return;
        }
        var rolling = Math.Sin(x * 0.15) * Math.Cos(y * 0.12) + Math.Sin(x * 0.3 + y * 0.2);
        var level = Math.Clamp((int)Math.Round(2.5 + rolling), 1, WorldRegion.MaxHeight);
        var material = fromTrail < 3.5 ? GrassB : GrassA;
        tile[0] = (byte)level;
        tile[1] = material;
        tile[2] = 0;
    }

    private static ImmutableArray<PropPlacement> ScatterWood(byte[] tiles, int width, int height)
    {
        var random = new Random(42);
        var props = ImmutableArray.CreateBuilder<PropPlacement>();
        var taken = new HashSet<Tile>();
        var targetCount = 7200;
        var attempts = 0;
        while (props.Count < targetCount && attempts++ < 50000)
        {
            var at = new Tile(random.Next(width), random.Next(height));
            var fromTrail = Math.Abs(at.Y - WoodTrailAt(at.X, height));
            if (fromTrail < 1.8 || !taken.Add(at))
                continue;
            var isLit = props.Count % 12 == 0;
            var kind = isLit ? TallLit[random.Next(TallLit.Length)] : TallPlain[random.Next(TallPlain.Length)];
            props.Add(new PropPlacement(kind, at, true, Cover.None));
        }
        return props.ToImmutable();
    }
}
