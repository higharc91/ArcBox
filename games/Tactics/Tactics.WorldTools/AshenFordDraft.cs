using Tactics.Logic;

namespace Tactics.WorldTools;

/// <summary>
/// Claude's first pass of the Ashen Ford, for the user to paint over. It's laid out by hand, as lines and rectangles in tile
/// coordinates, and noise only roughens their edges, from a fixed seed, so the same pack always gives the same region.
/// <para>
/// West to east: Emberwick behind its wall on warm green ground; the scarp, whose north end the road climbs round before running down
/// through the autumn woods to the ford, with a side road south to the docks. The river cuts the region in two from edge to edge, and
/// River Crossing is stamped at its only crossing. Past it, the far bank is burned and the terraced hills climb away to the north-east.
/// The north-west and south-west corners are left as void, as the edge of the map.
/// </para>
/// <para>
/// Every step between two walkable tiles is 2 or less, so nothing is one way: <see cref="Land.EaseSteps"/> sees to it.
/// </para>
/// </summary>
public static class AshenFordDraft
{
    /// <summary>The region's columns.</summary>
    public const int Width = 160;

    /// <summary>The region's rows.</summary>
    public const int Height = 120;

    private const string Id = "ashen_ford", Crossing = "river_crossing";

    private const int Seed = 20261031;

    /// <summary>The height of the water, which is River Crossing's height 0, so the stamp's offset.</summary>
    private const int WaterLevel = 1;

    /// <summary>The height of Emberwick's ground and its fields.</summary>
    private const int TownLevel = 3;

    private const TileFlags DeepWater = TileFlags.Water | TileFlags.Deep, Shallows = TileFlags.Water | TileFlags.Difficult;

    private const string Autumn = "grass/autumn", Dry = "grass/dry", Green = "grass/green", Lush = "grass/lush";
    private const string Earth = "dirt/brown", DarkEarth = "dirt/dark", Track = "path/packed", Sand = "sand/desert";
    private const string Leaves = "forest_floor/leafy", Moss = "forest_floor/mossy", Needles = "forest_needles/pine";
    private const string Rock = "rock/grey", DarkRock = "rock/dark", Ash = "ash/grey", Basalt = "basalt/dark", Water = "water/clear";
    private const string Street = "cobble/warm", Wall = "cobble/grey", Square = "flagstone/warm", Quay = "flagstone/grey";
    private const string Deck = "planks/oak", Roof = "planks/dark";

    /// <summary>River Crossing's corner: its column 6 and 7 are the river, its rows 7 and 8 the ford.</summary>
    private static readonly Tile StampAt = new(100, 52);

    /// <summary>The river, north to south. It runs straight for a while either side of the stamp, whose channel it must meet.</summary>
    private static readonly Line River = new((117, -3), (113, 14), (105, 30), (106.5, 42), (106.5, 52), (106.5, 64), (106.5, 72), (111, 86), (108, 102), (112, 112), (113, 123));

    /// <summary>The scarp's crest, from the south edge north-east to its tip, which the road climbs round.</summary>
    private static readonly Line Scarp = new((57, 130), (56, 102), (60, 78), (58, 54), (62, 38), (68, 20));

    /// <summary>The spur that carries River Crossing's rock ridge up into the hills.</summary>
    private static readonly Line Spur = new((112, 50), (120, 42), (128, 34));

    /// <summary>The road: x, y and its height there. East gate to the stamp, the side road to the docks, then the stamp's exit to the region's east edge.</summary>
    private static readonly Road[] Roads =
    [
        new(Track, Earth, 1.5, 2.5, [(50, 62, 3), (52, 52, 3), (55, 38, 4), (60, 22, 5), (68, 12, 6), (78, 17, 5), (86, 34, 4), (92, 48, 3), (95, 57, 1.4), (99.5, 61.5, 1)]),
        new(Track, Earth, 1, 2, [(94, 54, 2), (91, 64, 2), (93, 76, 2), (96, 88, 2), (96, 94, 2)]),
        new(Track, DarkEarth, 1.5, 2.5, [(113.5, 57.5, 2), (122, 58, 2), (140, 62, 2), (160, 58, 2)]),
    ];

    /// <summary>The goat path from the far road up the terraces to the camp, and the track to Maud's.</summary>
    private static readonly Road[] Tracks =
    [
        new(Earth, DarkEarth, 1, 2, [(120, 57, 2), (124, 46, 3), (118, 38, 4.5), (124, 32, 5), (133, 33, 6), (136, 28, 7)]),
        new(DarkEarth, Ash, 0.5, 1.5, [(125, 58, 2), (130, 70, 2), (126, 82, 3), (131, 90, 4)]),
    ];

    /// <summary>The town's buildings: left, top, width, depth, and how high their walls stand.</summary>
    private static readonly (int X, int Y, int W, int D, int Walls)[] Buildings =
    [
        // The square: the chapel to the north, the inn to the south and the general store on the west side.
        (26, 45, 9, 8, 4), (24, 72, 10, 6, 3), (17, 66, 4, 8, 2),

        // North-west.
        (12, 47, 7, 4, 2), (12, 52, 4, 7, 2), (17, 53, 3, 7, 2),

        // South-west.
        (12, 65, 4, 5, 2), (12, 71, 7, 4, 2),

        // Behind the chapel and the inn.
        (24, 41, 5, 2, 1), (31, 41, 6, 2, 1), (36, 72, 2, 5, 1),

        // North-east, and the granary to the south-east.
        (41, 46, 6, 4, 2), (41, 51, 3, 7, 2), (45, 52, 3, 7, 2), (40, 65, 8, 11, 3),

        // Along the south lane.
        (13, 81, 5, 3, 2), (20, 81, 6, 3, 2), (28, 81, 4, 3, 2), (34, 81, 6, 3, 2), (42, 81, 5, 3, 2),

        // The docks' warehouses, the fishers' sheds and the Ash Market's stalls.
        (87, 90, 7, 4, 2), (89, 103, 6, 5, 2), (84, 95, 3, 3, 1), (90, 96, 3, 2, 1), (95, 104, 3, 2, 1),
    ];

    /// <summary>How many of <see cref="Buildings"/> are in the town, ahead of the docks'.</summary>
    private const int TownBuildings = 20;

    /// <summary>The fields round the town: left, top, width, depth, and whether their furrows run along x.</summary>
    private static readonly (int X, int Y, int W, int D, bool Across)[] Fields =
    [
        (34, 8, 12, 12, true), (34, 22, 12, 11, false), (34, 34, 8, 4, false),
        (34, 87, 10, 9, true), (34, 98, 14, 11, false), (45, 87, 5, 9, true),
    ];

    /// <summary>The camp's low hide tents: left, top, width, depth.</summary>
    private static readonly (int X, int Y, int W, int D)[] Tents = [(126, 19, 3, 3), (131, 17, 4, 3), (137, 20, 3, 4), (125, 25, 3, 3), (138, 26, 3, 2)];

    /// <summary>The party starts on the square, in front of the inn.</summary>
    private static readonly Tile StartSpawn = new(30, 65);

    /// <summary>A defeat wakes the party at the chapel door, on the square the road runs through: the Church of the Hearth takes in the wounded.</summary>
    private static readonly Tile RecoverySpawn = new(30, 55);

    /// <summary>
    /// The beats. The three at the ford stand clear of the stamp, as a step into it starts its fight. Force walks in from Emberwick, so
    /// <c>ch1_ford_assault</c> is on the near bank; parley fades the party to the hills first, so <c>ch1_river_crossing</c> is on the far one.
    /// </summary>
    private static readonly (string Beat, int X, int Y)[] Spots =
    [
        ("ch1_mayors_request", 34, 59),
        ("ch1_the_ford_choice", 94, 59),
        ("ch1_ford_assault", 96, 63),
        ("ch1_river_crossing", 117, 58),
        ("ch1_old_maud", 132, 93),
        ("ch1_granary", 38, 70),
        ("ch1_skarrow_song", 133, 23),
        ("ch1_the_ember", 99, 99),
    ];

    /// <summary>Builds the region. <paramref name="rules"/> must hold the River Crossing map.</summary>
    public static RegionSource Build(RulesPack rules)
    {
        var crossing = rules.MapFor(Crossing) ?? throw new ArgumentException($"The pack has no map '{Crossing}'.", nameof(rules));
        var land = new Land(new Random(Seed));
        land.Ground();
        land.RaiseScarp();
        land.RaiseHills();
        land.CutWater();
        land.PlantFields();
        land.BuildTown();
        land.BuildDocks();
        foreach (var road in Roads)
            land.Lay(road);
        land.PitchCamp();
        land.RaiseMauds();
        foreach (var track in Tracks)
            land.Lay(track);
        land.CloseEdges();
        land.Stamp(crossing);
        land.ClearSpecks();
        land.FillSealed(StartSpawn);
        land.EaseSteps();
        land.FaceTerraces();

        var source = new RegionSource(Id, "The Ashen Ford", 1, Width, Height) { Start = StartSpawn, Recovery = RecoverySpawn };
        land.CopyTo(source);
        source.Stamp(crossing, StampAt, WaterLevel);
        source.Beats.AddRange(Spots.Select(s => new BeatSpot(s.Beat, new Tile(s.X, s.Y))));
        return source;
    }

    private static double Smooth(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static double Distance(double x, double y, double toX, double toY) => Math.Sqrt((x - toX) * (x - toX) + (y - toY) * (y - toY));

    /// <summary>A region tile is void in the corners left off the map: one chunk at the north-west, one at the south-west.</summary>
    private static bool Void(int x, int y) => x < WorldRegion.ChunkSize && (y < WorldRegion.ChunkSize || y >= 3 * WorldRegion.ChunkSize);

    /// <summary>How far a tile is from the region's edge, the void corners counting as edge.</summary>
    private static double EdgeDistance(int x, int y)
    {
        var edge = (double)Math.Min(Math.Min(x, Width - 1 - x), Math.Min(y, Height - 1 - y));
        var corner = WorldRegion.ChunkSize;
        var north = Distance(Math.Max(0, x - corner + 1), Math.Max(0, y - corner + 1), 0, 0);
        var south = Distance(Math.Max(0, x - corner + 1), Math.Max(0, 3 * corner - y), 0, 0);
        return Math.Min(edge, Math.Min(north, south));
    }

    /// <summary>A way laid over the land, and its verge. Each point of its line has a height, so it rises and falls smoothly.</summary>
    private sealed record Road(string Surface, string Verge, double Half, double Shoulder, (double X, double Y, double Height)[] Points)
    {
        public Line Line { get; } = new(Points);
    }

    /// <summary>A smoothed line in tile coordinates, with a value carried along it. It keeps its end points.</summary>
    private sealed class Line
    {
        private readonly (double X, double Y, double Value)[] _points;

        public Line(params (double X, double Y)[] points) : this([.. points.Select(p => (p.X, p.Y, 0.0))])
        {
        }

        public Line((double X, double Y, double Value)[] points)
        {
            var line = points;
            for (var pass = 0; pass < 3; pass++)
            {
                var next = new List<(double, double, double)> { line[0] };
                for (var i = 0; i + 1 < line.Length; i++)
                {
                    var (a, b) = (line[i], line[i + 1]);
                    next.Add((a.X * 0.75 + b.X * 0.25, a.Y * 0.75 + b.Y * 0.25, a.Value * 0.75 + b.Value * 0.25));
                    next.Add((a.X * 0.25 + b.X * 0.75, a.Y * 0.25 + b.Y * 0.75, a.Value * 0.25 + b.Value * 0.75));
                }
                next.Add(line[^1]);
                line = [.. next];
            }
            _points = line;
        }

        /// <summary>How far (<paramref name="x"/>, <paramref name="y"/>) is from the line, and the value at the nearest point.</summary>
        public (double Distance, double Value) Nearest(double x, double y)
        {
            var best = (Distance: double.MaxValue, Value: 0.0);
            for (var i = 0; i + 1 < _points.Length; i++)
            {
                var (a, b) = (_points[i], _points[i + 1]);
                var (dx, dy) = (b.X - a.X, b.Y - a.Y);
                var along = dx == 0 && dy == 0 ? 0 : Math.Clamp(((x - a.X) * dx + (y - a.Y) * dy) / (dx * dx + dy * dy), 0, 1);
                var distance = AshenFordDraft.Distance(x, y, a.X + dx * along, a.Y + dy * along);
                if (distance < best.Distance)
                    best = (distance, a.Value + (b.Value - a.Value) * along);
            }
            return best;
        }

        /// <summary>The line's column at a row, for a line that only runs south.</summary>
        public double ColumnAt(double y)
        {
            for (var i = 0; i + 1 < _points.Length; i++)
                if (y <= _points[i + 1].Y)
                    return _points[i].X + (_points[i + 1].X - _points[i].X) * Math.Clamp((y - _points[i].Y) / (_points[i + 1].Y - _points[i].Y), 0, 1);
            return _points[^1].X;
        }
    }

    /// <summary>The three layers while they're worked on, and each step that paints them.</summary>
    private sealed class Land
    {
        private const int NoiseSize = 256;

        private readonly double[] _noise = new double[NoiseSize * NoiseSize];
        private readonly int[,] _heights = new int[Width, Height];
        private readonly string[,] _materials = new string[Width, Height];
        private readonly TileFlags[,] _flags = new TileFlags[Width, Height];

        /// <summary>Tiles that are settled: <see cref="EaseSteps"/> raises what's round them, never them.</summary>
        private readonly bool[,] _fixed = new bool[Width, Height];

        /// <summary>The terraced tiles, which <see cref="FaceTerraces"/> finishes.</summary>
        private readonly bool[,] _terraced = new bool[Width, Height];

        public Land(Random random)
        {
            for (var i = 0; i < _noise.Length; i++)
                _noise[i] = random.NextDouble();
        }

        /// <summary>Every tile of the region, void corners and all.</summary>
        private static IEnumerable<(int X, int Y)> Everywhere()
        {
            for (var y = 0; y < Height; y++)
                for (var x = 0; x < Width; x++)
                    yield return (x, y);
        }

        private static IEnumerable<(int X, int Y)> Rectangle(int left, int top, int width, int depth)
        {
            for (var y = Math.Max(0, top); y < Math.Min(Height, top + depth); y++)
                for (var x = Math.Max(0, left); x < Math.Min(Width, left + width); x++)
                    yield return (x, y);
        }

        /// <summary>Smooth value noise from 0 to 1, with features about <paramref name="scale"/> tiles across. Each layer's offset gives it its own field.</summary>
        private double Noise(double x, double y, double scale, int layer)
        {
            (x, y) = (x / scale + layer * 37.1, y / scale + layer * 11.7);
            var (cellX, cellY) = ((int)Math.Floor(x), (int)Math.Floor(y));
            var (u, v) = (Smooth(x - cellX), Smooth(y - cellY));
            double At(int a, int b) => _noise[(b & (NoiseSize - 1)) * NoiseSize + (a & (NoiseSize - 1))];
            var top = At(cellX, cellY) * (1 - u) + At(cellX + 1, cellY) * u;
            var bottom = At(cellX, cellY + 1) * (1 - u) + At(cellX + 1, cellY + 1) * u;
            return top * (1 - v) + bottom * v;
        }

        /// <summary>Noise over two scales, which stays away from flat blobs, from about 0.2 to 0.8.</summary>
        private double Rough(double x, double y, double scale, int layer)
            => Noise(x, y, scale, layer) * 0.65 + Noise(x, y, scale / 2.7, layer + 1) * 0.35;

        private void Paint(int x, int y, int height, string material, TileFlags flags = TileFlags.None)
            => (_heights[x, y], _materials[x, y], _flags[x, y]) = (height, material, flags);

        private bool Walkable(int x, int y) => (_flags[x, y] & (TileFlags.Impassable | TileFlags.Deep)) == 0;

        private bool IsWater(int x, int y) => (_flags[x, y] & TileFlags.Water) != 0;

        private static bool FarBank(double x, double y) => x > River.ColumnAt(y);

        /// <summary>The hills' shape, 0 at their foot to 1 at the top. They rise north-east of the ford, with a spur down to the stamp's ridge.</summary>
        private double Hill(int x, int y)
        {
            if (!FarBank(x, y))
                return 0;
            var dome = 1 - Distance((x - 146) / 50.0, (y - 10) / 46.0, 0, 0);
            var spur = 0.34 - Spur.Nearest(x, y).Distance / 22;
            return Math.Max(dome, spur) + (Rough(x, y, 15, 2) - 0.5) * 0.28;
        }

        /// <summary>How burned the far bank is at a tile, from 0, rising to 1 near the ford and fading into the hills and against the stamp's green.</summary>
        private double Burn(int x, int y)
        {
            var clearOfStamp = Smooth((Math.Max(Math.Abs(x - (StampAt.X + 7.0)) - 7, Math.Abs(y - (StampAt.Y + 6.0)) - 6)) / 12);
            var hillside = 1 - Smooth(Hill(x, y) / 0.22);
            return (1.45 - Distance(x, y, 107, 60) / 105 + (Rough(x, y, 11, 4) - 0.5) * 0.8) * clearOfStamp * hillside;
        }

        /// <summary>The rolling land under everything: warm green round the town, autumn beyond, the woods past the scarp and the far bank's ash.</summary>
        public void Ground()
        {
            foreach (var (x, y) in Everywhere())
            {
                var (swell, patch, fine) = (Rough(x, y, 26, 6), Rough(x, y, 13, 8), Rough(x, y, 5, 10));
                var height = 2 + (swell > 0.5 ? 1 : 0) + (swell > 0.62 ? 1 : 0);
                if (FarBank(x, y))
                {
                    Paint(x, y, 2 + (swell > 0.55 ? 1 : 0), Dry);
                    continue;
                }

                var warmth = 1 - Distance(x, y, 30, 62) / 44 + (patch - 0.5) * 0.5;
                var inWoods = x is > 66 and < 96;
                var material = warmth switch
                {
                    > 0.42 => fine > 0.52 ? Lush : Green,
                    > 0.3 => patch > 0.5 ? Green : Autumn,
                    _ when inWoods && patch > 0.5 => fine > 0.5 ? Leaves : Moss,
                    _ when patch > 0.6 => Dry,
                    _ when fine > 0.66 => Earth,
                    _ => Autumn,
                };
                Paint(x, y, height, material);
            }
        }

        /// <summary>The scarp: a wall of rock with stepped shoulders, and a rise past its tip for the road to climb.</summary>
        public void RaiseScarp()
        {
            foreach (var (x, y) in Everywhere())
            {
                var saddle = 1 - Distance(x, y, 70, 12) / 20;
                if (saddle > 0)
                    _heights[x, y] += (int)Math.Round(saddle * 3);

                var distance = Scarp.Nearest(x, y).Distance + (Rough(x, y, 9, 12) - 0.5) * 5;
                if (distance < 3)
                    Paint(x, y, 5 + (int)(Rough(x, y, 6, 14) * 5), Rough(x, y, 5, 15) > 0.5 ? Rock : DarkRock, TileFlags.Impassable);
                else if (distance < 8)
                {
                    var shoulder = (8 - distance) / 5;
                    _heights[x, y] = Math.Max(_heights[x, y], _heights[x, y] + (int)Math.Round(shoulder * 2) * 2 - 1);
                    if (shoulder > 0.5)
                        _materials[x, y] = Rough(x, y, 4, 16) > 0.45 ? Rock : DarkEarth;
                    else if (shoulder > 0.2)
                        _materials[x, y] = Dry;
                }
            }
        }

        /// <summary>The far bank: terraces two levels apart climbing to the north-east, and the burned ground, with its streaks running out from the ford.</summary>
        public void RaiseHills()
        {
            foreach (var (x, y) in Everywhere())
            {
                if (!FarBank(x, y))
                    continue;

                var hill = Hill(x, y);
                if (hill > 0.05)
                {
                    var level = hill switch { > 0.76 => 9, > 0.5 => 7, > 0.27 => 5, _ => 3 };
                    var patch = Rough(x, y, 8, 18);
                    var material = level switch
                    {
                        9 => patch > 0.5 ? DarkRock : Rock,
                        7 => patch > 0.55 ? Dry : Needles,
                        5 => patch > 0.52 ? Autumn : Dry,
                        _ => patch > 0.6 ? Autumn : Dry,
                    };
                    Paint(x, y, level, material, level == 9 ? TileFlags.Impassable : TileFlags.None);
                    _terraced[x, y] = true;
                }

                var burn = Burn(x, y);
                if (burn < 0.15)
                    continue;

                var angle = Math.Atan2(y - 60, x - 107);
                var streak = Noise(angle * 16, Distance(x, y, 107, 60) / 9, 1, 20);
                var outcrop = Rough(x, y, 7, 22);
                var material2 = burn switch
                {
                    < 0.28 => DarkEarth,
                    _ when streak > 0.68 || outcrop > 0.64 => Basalt,
                    _ => Ash,
                };
                _materials[x, y] = material2;
                if (burn > 0.4 && outcrop > 0.66)
                {
                    _heights[x, y] += outcrop > 0.72 ? 3 : 1;
                    if (outcrop > 0.72)
                        (_materials[x, y], _flags[x, y]) = (Basalt, TileFlags.Impassable);
                }
            }
        }

        /// <summary>The river: deep water, shallows and banks that wander.</summary>
        public void CutWater()
        {
            foreach (var (x, y) in Everywhere())
            {
                // Nothing wanders by the stamp, whose channel is two columns wide and hard against its banks.
                var away = Math.Clamp((Math.Max(StampAt.Y - y, y - StampAt.Y - 11) - 1) / 12.0, 0, 1);
                var harbour = Smooth((y - 90) / 8.0) * Smooth((116 - y) / 8.0);
                var deep = 1 + away * (3.2 + harbour * 1.6);
                var (shallow, bank) = (deep + away * 2.6, deep + 1 + away * 3.4);
                var river = River.Nearest(x, y).Distance + (Rough(x, y, 9, 28) - 0.5) * 3 * away;
                if (river < bank)
                    Wet(x, y, river, deep, shallow, bank, (FarBank(x, y) ? Rough(x, y, 6, 30) > 0.5 ? DarkEarth : Ash : Rough(x, y, 6, 30) > 0.68 ? Sand : Earth));
            }
        }

        private void Wet(int x, int y, double distance, double deep, double shallow, double bank, string shore)
        {
            if (distance < deep)
                Paint(x, y, WaterLevel, Water, DeepWater);
            else if (distance < shallow)
                Paint(x, y, WaterLevel, Water, Shallows);
            else if (distance < bank && !_terraced[x, y])
                Paint(x, y, WaterLevel + 1, shore);
            else if (distance < bank)
                _heights[x, y] = Math.Min(_heights[x, y], WaterLevel + 2);
            _terraced[x, y] = false;
        }

        /// <summary>The warm fields: strips of turned earth and green, hedged in mossy ground.</summary>
        public void PlantFields()
        {
            foreach (var (left, top, width, depth, across) in Fields)
                foreach (var (x, y) in Rectangle(left, top, width, depth))
                {
                    var (along, row) = across ? (x - left, y - top) : (y - top, x - left);
                    var edge = along == 0 || row == 0 || along == (across ? width : depth) - 1 || row == (across ? depth : width) - 1;
                    var crop = row / 2 % 3 == 2 ? Green : row / 2 % 3 == 1 ? DarkEarth : Earth;
                    if (Walkable(x, y) && !IsWater(x, y))
                        Paint(x, y, TownLevel, edge ? Moss : crop);
                    _fixed[x, y] = true;
                }
        }

        /// <summary>Emberwick: a low stone wall with two gates, four streets, the square and its well, and the buildings' footprints.</summary>
        public void BuildTown()
        {
            const int left = 10, top = 39, right = 49, bottom = 85;
            foreach (var (x, y) in Rectangle(left, top, right - left + 1, bottom - top + 1))
            {
                var patch = Rough(x, y, 5, 32);
                Paint(x, y, TownLevel, patch > 0.54 ? Earth : patch > 0.42 ? Lush : Green);
                _fixed[x, y] = true;
            }

            foreach (var (x, y) in Rectangle(left, top, right - left + 1, bottom - top + 1))
                if (x is left or right || y is top or bottom)
                    Paint(x, y, TownLevel + 1, Wall, TileFlags.Impassable);

            void Street(int x, int y, int width, int depth, string material = AshenFordDraft.Street)
            {
                foreach (var (sx, sy) in Rectangle(x, y, width, depth))
                    Paint(sx, sy, TownLevel, material);
            }

            Street(left + 1, 61, right - left - 1, 3);     // the High Street, through to the east gate
            Street(right, 61, 1, 3);
            Street(left, 61, 1, 3);
            Street(21, 44, 2, 40);                         // the two cross streets
            Street(38, 44, 2, 40);
            Street(11, 79, 37, 2, Track);                  // the lanes behind the buildings
            Street(11, 44, 13, 2, Track);
            Street(37, 44, 12, 2, Track);
            Street(23, 54, 15, 16, Square);

            // The well.
            foreach (var (x, y) in Rectangle(28, 56, 3, 3))
                Paint(x, y, TownLevel + 1, Wall, TileFlags.Impassable);
            Paint(29, 57, TownLevel, Water, TileFlags.Water | TileFlags.Impassable);

            for (var i = 0; i < TownBuildings; i++)
                Raise(i);
        }

        /// <summary>A building's footprint, its walls, and a roof stepping up to a ridge. Every other roof is the other way about, so a street doesn't read as one long shed.</summary>
        private void Raise(int index)
        {
            var (left, top, width, depth, walls) = Buildings[index];
            var (eaves, ridge) = index == 0 ? (Quay, Roof) : index % 2 == 0 ? (Deck, Roof) : (Roof, Deck);
            Raise(left, top, width, depth, walls, eaves, ridge);
        }

        private void Raise(int left, int top, int width, int depth, int walls, string eaves, string ridge)
        {
            var ground = _heights[left, top];
            foreach (var (x, y) in Rectangle(left, top, width, depth))
            {
                var inset = Math.Min(Math.Min(x - left, left + width - 1 - x), Math.Min(y - top, top + depth - 1 - y));
                Paint(x, y, Math.Min(WorldRegion.MaxHeight, ground + walls + Math.Min(inset, 2)), inset == 0 ? eaves : ridge, TileFlags.Impassable);
                _fixed[x, y] = true;
            }
        }

        /// <summary>Lays a road or track. It settles its own tiles to its heights and draws the land each side towards them.</summary>
        public void Lay(Road road)
        {
            foreach (var (x, y) in Everywhere())
            {
                var (distance, height) = road.Line.Nearest(x, y);
                if (distance > road.Half + road.Shoulder + 5 || (_fixed[x, y] && _flags[x, y].HasFlag(TileFlags.Impassable)))
                    continue;

                // A track joins the road and stops there.
                if (_fixed[x, y] && _materials[x, y] == Track && road.Surface != Track)
                    continue;

                var level = (int)Math.Round(height);
                if (distance <= road.Half)
                {
                    Paint(x, y, level, road.Surface);
                    _fixed[x, y] = true;
                }
                else if (distance <= road.Half + road.Shoulder)
                {
                    if (!IsWater(x, y) && !_fixed[x, y])
                    {
                        var verge = distance <= road.Half + road.Shoulder * (Rough(x, y, 3, 34) - 0.1);
                        Paint(x, y, level, verge ? road.Verge : _materials[x, y]);
                        _fixed[x, y] = true;
                    }
                }
                else if (!IsWater(x, y) && !_fixed[x, y])
                {
                    var blend = Smooth((distance - road.Half - road.Shoulder) / 5);
                    _heights[x, y] = (int)Math.Round(level + (_heights[x, y] - level) * blend);
                }
            }
        }

        /// <summary>The River Docks: a stone quay, plank piers out over the shallows with boats moored at them, and the warehouses behind.</summary>
        public void BuildDocks()
        {
            foreach (var (x, y) in Rectangle(86, 88, 15, 22))
            {
                if (_flags[x, y].HasFlag(TileFlags.Deep) || (x < 95 && IsWater(x, y)))
                    continue;
                Paint(x, y, WaterLevel + 1, x > 95 || Rough(x, y, 3, 36) > 0.5 ? Quay : Earth);
                _fixed[x, y] = true;
            }

            foreach (var pier in (ReadOnlySpan<int>)[92, 98, 104])
            {
                foreach (var (x, y) in Rectangle(99, pier, 9, 2))
                {
                    Paint(x, y, WaterLevel + 1, Deck);
                    _fixed[x, y] = true;
                }
                foreach (var (x, y) in Rectangle(103, pier + 2, 4, 2))
                {
                    Paint(x, y, WaterLevel + 1, Roof, TileFlags.Impassable);
                    _fixed[x, y] = true;
                }
            }

            for (var i = TownBuildings; i < Buildings.Length; i++)
                Raise(i);
        }

        /// <summary>The Skarrow camp: a clearing on the third terrace, with low tents round a ring of stones.</summary>
        public void PitchCamp()
        {
            foreach (var (x, y) in Everywhere())
            {
                var distance = Distance(x, y, 133, 23);
                if (distance < 10.5 + (Rough(x, y, 5, 38) - 0.5) * 4)
                {
                    Paint(x, y, 7, distance < 6 || Rough(x, y, 4, 40) > 0.5 ? DarkEarth : Needles);
                    _fixed[x, y] = true;
                    _terraced[x, y] = false;
                }
            }

            foreach (var (left, top, width, depth) in Tents)
                foreach (var (x, y) in Rectangle(left, top, width, depth))
                {
                    var inset = Math.Min(Math.Min(x - left, left + width - 1 - x), Math.Min(y - top, top + depth - 1 - y));
                    Paint(x, y, 7 + 1 + Math.Min(inset, 1), inset == 0 ? Earth : DarkEarth, TileFlags.Impassable);
                }

            foreach (var (x, y) in Rectangle(131, 25, 3, 3))
                Paint(x, y, 7, x == 132 && y == 26 ? Ash : Basalt, TileFlags.Impassable);
        }

        /// <summary>Old Maud's hut: a small roofed footprint on a knoll of burned ground, with a yard of basalt in front.</summary>
        public void RaiseMauds()
        {
            foreach (var (x, y) in Everywhere())
            {
                var distance = Distance(x, y, 132, 91);
                if (distance < 11)
                {
                    var height = 4 - (int)(distance / 3.6);
                    Paint(x, y, Math.Max(height, 2), distance < 6 ? Ash : Rough(x, y, 4, 42) > 0.5 ? Ash : Basalt);
                    _fixed[x, y] = distance < 6;
                }
            }
            Raise(129, 85, 5, 4, 2, Deck, Roof);
        }

        /// <summary>Rock round the whole region, which leaves only the deep water, the roads and the void corners' edges.</summary>
        public void CloseEdges()
        {
            foreach (var (x, y) in Everywhere())
            {
                var edge = EdgeDistance(x, y);
                var thick = 1.5 + (Rough(x, y, 11, 44) - 0.2) * 12;
                if (edge >= thick || _fixed[x, y] || _flags[x, y].HasFlag(TileFlags.Deep))
                    continue;
                var dark = Rough(x, y, 5, 47) > 0.5;
                var rock = FarBank(x, y) && y > 50 ? dark ? Basalt : DarkRock : dark ? DarkRock : Rock;
                var height = 4 + (int)((thick - edge) * 0.6) + (int)(Rough(x, y, 5, 46) * 3.4);
                Paint(x, y, Math.Min(height, WorldRegion.MaxHeight), rock, TileFlags.Impassable);
                _fixed[x, y] = true;
            }
        }

        /// <summary>River Crossing's tiles, and the land round them drawn to their heights and their grass so the stamp doesn't sit like a tile on a table.</summary>
        public void Stamp(BattleMap map)
        {
            var stamped = StampBrush.Cells(map, StampAt, WaterLevel).ToDictionary(c => (c.At.X, c.At.Y));
            foreach (var cell in stamped.Values)
            {
                Paint(cell.At.X, cell.At.Y, cell.Height, cell.Material, cell.Flags);
                _fixed[cell.At.X, cell.At.Y] = true;
            }

            foreach (var (x, y) in Rectangle(StampAt.X - 12, StampAt.Y - 12, map.Width + 24, map.Height + 24))
            {
                if (_fixed[x, y] || IsWater(x, y) || _flags[x, y].HasFlag(TileFlags.Impassable))
                    continue;
                var edgeX = Math.Clamp(x, StampAt.X, StampAt.X + map.Width - 1);
                var edgeY = Math.Clamp(y, StampAt.Y, StampAt.Y + map.Height - 1);
                var cell = stamped[(edgeX, edgeY)];
                var distance = Math.Max(Math.Abs(x - edgeX), Math.Abs(y - edgeY));
                _heights[x, y] = (int)Math.Round(cell.Height + (_heights[x, y] - cell.Height) * Smooth(distance / 6.0));
                var fade = distance + Rough(x, y, 5, 48) * 8;
                if (cell.Material == Rock && fade < 7)
                    _materials[x, y] = Rock;
                else if (cell.Material != Rock && fade < 7)
                    _materials[x, y] = Lush;
                else if (cell.Material != Rock && fade < 10)
                    _materials[x, y] = Green;
                else if (fade < 13 && _materials[x, y] is not (Earth or Rock))
                    _materials[x, y] = Dry;
            }
        }

        /// <summary>
        /// Opens any impassable patch of four tiles or fewer, which would be a stray post with no reason to be there: nothing in the draft
        /// that's meant to stop the party is so small.
        /// </summary>
        public void ClearSpecks()
        {
            var seen = new bool[Width, Height];
            foreach (var (x, y) in Everywhere())
            {
                if (seen[x, y] || !_flags[x, y].HasFlag(TileFlags.Impassable))
                    continue;
                var patch = new List<(int X, int Y)>();
                var open = new Stack<(int X, int Y)>([(x, y)]);
                seen[x, y] = true;
                while (open.TryPop(out var tile))
                {
                    patch.Add(tile);
                    foreach (var (nextX, nextY) in (ReadOnlySpan<(int, int)>)[(tile.X + 1, tile.Y), (tile.X, tile.Y + 1), (tile.X - 1, tile.Y), (tile.X, tile.Y - 1)])
                        if (nextX is >= 0 and < Width && nextY is >= 0 and < Height && !seen[nextX, nextY] && _flags[nextX, nextY].HasFlag(TileFlags.Impassable))
                        {
                            seen[nextX, nextY] = true;
                            open.Push((nextX, nextY));
                        }
                }

                if (patch.Count > 4)
                    continue;
                foreach (var (patchX, patchY) in patch)
                {
                    _flags[patchX, patchY] = TileFlags.None;
                    _fixed[patchX, patchY] = false;
                    _heights[patchX, patchY] = Math.Min(_heights[patchX, patchY], 3);
                }
            }
        }

        /// <summary>Turns any ground the party can't get to into rock, which is all the rim's noise leaves: a pocket behind it would only be a hole in the wall.</summary>
        public void FillSealed(Tile start)
        {
            var reached = new bool[Width, Height];
            reached[start.X, start.Y] = true;
            var open = new Stack<(int X, int Y)>([(start.X, start.Y)]);
            while (open.TryPop(out var tile))
                foreach (var (x, y) in (ReadOnlySpan<(int, int)>)[(tile.X + 1, tile.Y), (tile.X, tile.Y + 1), (tile.X - 1, tile.Y), (tile.X, tile.Y - 1)])
                    if (x is >= 0 and < Width && y is >= 0 and < Height && !reached[x, y] && Walkable(x, y) && !Void(x, y))
                    {
                        reached[x, y] = true;
                        open.Push((x, y));
                    }

            foreach (var (x, y) in Everywhere())
                if (Walkable(x, y) && !reached[x, y])
                {
                    Paint(x, y, 5, Rock, TileFlags.Impassable);
                    _fixed[x, y] = true;
                }
        }

        /// <summary>
        /// Takes every step between walkable tiles down to the default Jump or less, so there's no drop that can't be climbed back. Settled
        /// tiles stay put, and what's beside them moves.
        /// </summary>
        public void EaseSteps()
        {
            for (var changed = true; changed;)
            {
                changed = false;
                foreach (var (x, y) in Everywhere())
                {
                    if (!Walkable(x, y))
                        continue;
                    foreach (var (nextX, nextY) in (ReadOnlySpan<(int, int)>)[(x + 1, y), (x, y + 1), (x - 1, y), (x, y - 1)])
                    {
                        if (nextX is < 0 or >= Width || nextY is < 0 or >= Height || !Walkable(nextX, nextY))
                            continue;
                        var step = _heights[nextX, nextY] - _heights[x, y];
                        if (step <= ClassDef.DefaultJump)
                            continue;
                        if (!_fixed[nextX, nextY])
                            _heights[nextX, nextY] = _heights[x, y] + ClassDef.DefaultJump;
                        else if (!_fixed[x, y])
                            _heights[x, y] = _heights[nextX, nextY] - ClassDef.DefaultJump;
                        else
                            throw new InvalidOperationException($"The settled tiles ({x}, {y}) and ({nextX}, {nextY}) are {step} levels apart.");
                        changed = true;
                    }
                }
            }
        }

        /// <summary>Faces each terrace's edge with bare rock, so the steps read from a distance.</summary>
        public void FaceTerraces()
        {
            var faces = Everywhere().Where(t => _terraced[t.X, t.Y] && Walkable(t.X, t.Y) && !_fixed[t.X, t.Y]
                && ((t.X + 1 < Width && _heights[t.X + 1, t.Y] < _heights[t.X, t.Y] - 1) || (t.Y + 1 < Height && _heights[t.X, t.Y + 1] < _heights[t.X, t.Y] - 1)
                    || (t.X > 0 && _heights[t.X - 1, t.Y] < _heights[t.X, t.Y] - 1) || (t.Y > 0 && _heights[t.X, t.Y - 1] < _heights[t.X, t.Y] - 1))).ToList();
            foreach (var (x, y) in faces)
                _materials[x, y] = Rough(x, y, 4, 50) > 0.5 ? Rock : DarkRock;
        }

        public void CopyTo(RegionSource source)
        {
            foreach (var (x, y) in Everywhere().Where(t => !Void(t.X, t.Y)))
                source.Set(new Tile(x, y), _heights[x, y], _materials[x, y], _flags[x, y]);
        }
    }
}
