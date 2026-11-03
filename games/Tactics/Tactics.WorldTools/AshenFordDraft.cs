using System.Collections.Immutable;
using Tactics.Logic;

namespace Tactics.WorldTools;

/// <summary>
/// Claude's draft of the Ashen Ford, for the user to paint over. It's laid out by hand, place by place, as rectangles, lines and named lists in tile coordinates. Noise only makes
/// the slow edge of a ring wander and the banks wind, from a fixed seed, so the same pack always gives the same region. Nothing that stands anywhere comes from a scatter, but for a
/// wood's trees, jittered inside a drawn outline.
/// <para>
/// West to east: walled Emberwick and its tired fields; the scarp, whose north end the road climbs round before running down through the woods to the ford, with a side road south
/// to the docks. The river cuts the region in two from edge to edge, and River Crossing is stamped at its only crossing. The ground burned in rings round the ford: ash, then
/// scorched earth, then thin grass, with the ash running on downstream along the far bank to Maud's. North-east of the ford the frozen hills climb to the camp. The north-west and
/// south-west corners are left as void, as the edge of the map.
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

    // The ground, from the ford out. A place is one or two of these.
    private const string Ash = "ash/grey", Scorch = "dirt/dark", Thin = "grass/dry", Stubble = "grass/autumn", Ploughed = "dirt/brown";
    private const string Track = "path/packed", Mud = "dirt/dark", Frost = "grass/frost", Water = "water/clear", Needles = "forest_needles/pine";

    /// <summary>The hills' and the scarp's bare stone is the pack's rough cave floor, as the top meant for rock is laid in slabs the size of a house. The stamp's own rock stays as its map has it.</summary>
    private const string Rock = "cave_floor/cool", DarkRock = "cave_floor/cool", StampRock = "rock/grey", Scree = "mine_gravel/brown";
    private const string Street = "cobble/warm", Plaza = "cobble/grey", Quay = "flagstone/grey", Deck = "planks/oak";

    /// <summary>What the road is made of: the packed path and the town's cobbled streets. Things stand on a square, but not on a road.</summary>
    public static readonly ImmutableHashSet<string> RoadMaterials = ["cobble/warm", "path/packed"];

    /// <summary>River Crossing's corner: its column 6 and 7 are the river, its rows 7 and 8 the ford.</summary>
    private static readonly Tile StampAt = new(100, 52);

    /// <summary>The heart of the old fire: the ford.</summary>
    private static readonly (double X, double Y) Ford = (107, 59);

    /// <summary>The ash doesn't stop in a ring downstream of the bridge: it runs on down the far bank to Maud's ("grey and bare for a mile").</summary>
    private static readonly Line BurnedBank = new((107.0, 59.0), (122.0, 76.0), (133.0, 96.0));

    /// <summary>How far from the fire's heart, in tiles, the ash stops, the scorched earth stops and the grass comes back thick enough to farm.</summary>
    private const double AshEnds = 24, ScorchEnds = 37, ThinEnds = 52;

    /// <summary>The river, north to south. It runs straight for a while either side of the stamp, whose channel it must meet.</summary>
    private static readonly Line River = new((117, -3), (113, 14), (105, 30), (106.5, 42), (106.5, 52), (106.5, 64), (106.5, 72), (111, 86), (108, 102), (112, 112), (113, 123));

    /// <summary>The scarp's crest, from the south edge north-east to its tip, which the road climbs round.</summary>
    private static readonly Line Scarp = new((57, 130), (56, 102), (60, 78), (58, 54), (62, 38), (68, 20));

    /// <summary>The spur that carries River Crossing's rock ridge up into the hills.</summary>
    private static readonly Line Spur = new((112, 50), (120, 42), (128, 34));

    /// <summary>How far a road runs each side of its line: a track a cart wide, the least that still joins along a diagonal, and its verge of dark earth beyond it.</summary>
    private const double Narrow = 0.72, Verge = 1.2;

    /// <summary>The road: x, y and its height there. East gate to the stamp, the side road to the docks, then the stamp's exit to the region's east edge.</summary>
    private static readonly Road[] Roads =
    [
        new(Track, Scorch, Narrow, Verge, [(48, 62, 3), (52, 56, 3), (55, 38, 4), (60, 22, 5), (68, 12, 6), (78, 17, 5), (86, 34, 4), (92, 48, 3), (95, 57, 1.4), (99.5, 61.5, 1)]),
        new(Track, Scorch, Narrow, Verge, [(94, 54, 2), (91, 64, 2), (93, 76, 2), (96, 88, 2), (96, 94, 2)]),
        new(Track, Scorch, Narrow, Verge, [(113.5, 57.5, 2), (122, 58, 2), (140, 62, 2), (160, 58, 2)]),
        new(Track, Thin, 0.5, 0, [(31, 80, 3), (31, 86, 3), (31, 92, 3)]),
    ];

    /// <summary>The goat path from the far road up the terraces to the camp, and the track to Maud's.</summary>
    private static readonly Road[] Tracks =
    [
        new(Mud, Mud, 1, 1, [(120, 57, 2), (124, 46, 3), (118, 38, 4.5), (124, 32, 5), (133, 33, 6), (135, 29, 7), (134, 26, 7)]),
        new(Scorch, Ash, 0.5, 0.5, [(125, 58, 2), (130, 70, 2), (126, 82, 3), (131, 90, 4)]),
    ];

    /// <summary>The town's walls, one tile thick: left, top, right and bottom.</summary>
    private const int TownLeft = 14, TownTop = 44, TownRight = 47, TownBottom = 79;

    /// <summary>Emberwick's square, and its streets, which all run into it or into each other: left, top, width and depth.</summary>
    private static readonly (string Name, int X, int Y, int W, int D)[] Streets =
    [
        ("High Street, to the east gate", 39, 61, 9, 3),
        ("Chapel Row, west", 15, 55, 9, 2),
        ("Chapel Row, east", 39, 55, 8, 2),
        ("Mill Lane", 15, 61, 9, 2),
        ("Tanners' Row", 15, 67, 9, 2),
        ("Granary Row", 39, 67, 8, 2),
        ("South Gate Road", 30, 68, 3, 12),
        ("South Lane", 15, 74, 32, 2),
        ("Back Lane", 15, 48, 32, 2),
        ("Church Walk", 27, 50, 2, 5),
        ("Smiths' Cut", 38, 50, 1, 5),
    ];

    /// <summary>The square, which is cobbled in grey, so it reads as a place to stand and not a road.</summary>
    private static readonly (int X, int Y, int W, int D) Square = (24, 55, 15, 12);

    /// <summary>The fields round the town: left, top, width, depth and what's on them. Straight edges, as a field has.</summary>
    private static readonly (string Name, int X, int Y, int W, int D, string Crop)[] Fields =
    [
        ("Mayor's Forty", 34, 8, 13, 13, Stubble),
        ("Long Acre", 34, 23, 13, 10, Ploughed),
        ("Hearthfield", 18, 33, 14, 9, Stubble),
        ("Gate Close", 34, 34, 13, 8, Ploughed),
        ("Southfield", 16, 82, 13, 8, Stubble),
        ("Wardens' Strip", 33, 82, 8, 12, Ploughed),
        ("Low Field", 42, 83, 9, 12, Stubble),
        ("Ten Rods", 34, 97, 14, 12, Ploughed),
    ];

    /// <summary>A building of the town kit, with its footprint's back corner at (<paramref name="X"/>, <paramref name="Y"/>). Every door in the kit faces +y, so a row stands on the north side of its street.</summary>
    private sealed record Building(string Id, string Variant, int X, int Y);

    /// <summary>Emberwick, by what it's for. Warm stone walls and towers come after, from <see cref="TownTowers"/> and <see cref="TownGates"/>.</summary>
    private static readonly Building[] Emberwick =
    [
        // The square's north side, every door on it.
        new("tavern", "red", 24, 53),               // The Last Coal
        new("chapel", "warm", 29, 52),              // the Church of the Hearth
        new("store", "green", 33, 53),              // the general store
        new("smithy", "oak", 36, 53),               // Tobin's

        // The market, across the south of the square, with the road to the south gate running through it.
        new("stall", "red", 25, 65), new("stall", "yellow", 26, 65), new("stall", "blue", 28, 65),
        new("stall", "green", 34, 65), new("stall", "purple", 35, 65),
        new("clutter_produce", "mixed", 27, 65), new("clutter_produce", "mixed", 36, 65),
        new("notice_board_u", "oak", 37, 57),

        // Clutter by the doors.
        new("clutter_barrels", "oak", 23, 54), new("clutter_crates", "oak", 32, 54), new("clutter_barrels", "oak", 35, 54),

        // Chapel Row, west and east of the square.
        new("house", "green", 15, 53), new("cottage", "thatch_green", 18, 53), new("house", "red", 21, 53),
        new("cottage", "slate_blue", 40, 53), new("house", "blue", 43, 53),

        // Mill Lane, and the High Street to the east gate.
        new("cottage", "shakes_oak", 15, 59), new("house", "blue", 18, 59), new("cottage", "thatch_red", 21, 59),
        new("house", "green", 40, 59), new("cottage", "shakes_oak", 43, 59),

        // Tanners' Row and Granary Row.
        new("cottage", "slate_blue", 15, 65), new("cottage", "thatch_green", 18, 65), new("house", "red", 21, 65),
        new("house", "blue", 40, 65), new("cottage", "thatch_red", 43, 65),

        // Back Lane, under the north wall.
        new("cottage", "thatch_red", 18, 46), new("house", "green", 24, 46), new("cottage", "shakes_oak", 35, 46), new("cottage", "slate_blue", 41, 46),

        // South Lane, low, so they don't hide the square.
        new("cottage", "thatch_green", 16, 72), new("cottage", "shakes_oak", 20, 72), new("cottage", "slate_blue", 25, 72), new("cottage", "thatch_red", 34, 72),

        // The granaries. The kit has no barn, so they're two big plank-roofed sheds in a fenced yard.
        new("cottage", "shakes_oak", 40, 72), new("cottage", "shakes_oak", 43, 72),
        new("clutter_crates", "oak", 42, 73), new("clutter_barrels", "oak", 45, 73),

        // Clutter down the lanes.
        new("clutter_barrels", "oak", 17, 60), new("clutter_crates", "oak", 42, 60), new("clutter_produce", "mixed", 17, 66),
        new("clutter_crates", "oak", 45, 54), new("clutter_barrels", "oak", 23, 66),
    ];

    /// <summary>The wall's towers, at its corners, in the middle of its runs and either side of each gate.</summary>
    private static readonly (int X, int Y)[] TownTowers =
    [
        (TownLeft, TownTop), (30, TownTop), (TownRight, TownTop),
        (TownLeft, 61), (TownRight, 60), (TownRight, 64),
        (TownLeft, TownBottom), (29, TownBottom), (33, TownBottom), (TownRight, TownBottom),
    ];

    /// <summary>The gates: the road east, and the lane to the south fields.</summary>
    private static readonly Building[] TownGates = [new("gate_v", "warm", TownRight, 61), new("gate_u", "warm", 30, TownBottom)];

    /// <summary>The wall, in warm stone, round the town from tower to tower, leaving out the gates.</summary>
    private static IEnumerable<Building> TownWall()
    {
        var taken = TownTowers.ToHashSet();
        var gates = TownGates.SelectMany(g => g.Id == "gate_v" ? Enumerable.Range(0, 3).Select(i => (g.X, g.Y + i)) : Enumerable.Range(0, 3).Select(i => (g.X + i, g.Y))).ToHashSet();
        foreach (var (x, y) in TownTowers)
            yield return new Building("wall_tower", "warm", x, y);
        foreach (var gate in TownGates)
            yield return gate;
        for (var x = TownLeft + 1; x < TownRight; x++)
            foreach (var y in new[] { TownTop, TownBottom })
                if (!taken.Contains((x, y)) && !gates.Contains((x, y)))
                    yield return new Building("wall_u", "warm", x, y);
        for (var y = TownTop + 1; y < TownBottom; y++)
            foreach (var x in new[] { TownLeft, TownRight })
                if (!taken.Contains((x, y)) && !gates.Contains((x, y)))
                    yield return new Building("wall_v", "warm", x, y);
    }

    /// <summary>What stands in Emberwick that isn't a building.</summary>
    private static readonly Stand[] InEmberwick =
    [
        // The square's well at (31, 60) and the signpost at the east fork (51, 63) are things to use (interactables/ashen_ford.json), not props.
        new("lamp_post", "iron", (25, 57), (37, 59), (25, 63), (37, 63), (28, 61), (34, 61)),       // the square
        new("lamp_post", "iron", (17, 54), (20, 54), (39, 54), (42, 54)),                           // Chapel Row
        new("lamp_post", "iron", (20, 60), (45, 60), (39, 60), (20, 66), (45, 66), (39, 66)),       // Mill Lane, the High Street, the rows
        new("lamp_post", "iron", (22, 47), (33, 47), (40, 47)),                                     // Back Lane
        new("lamp_post", "iron", (23, 73), (29, 73), (33, 73), (39, 73)),                           // South Lane
        new("torch", "fire", (46, 60), (46, 64), (29, 78), (33, 78)),                               // inside the gates
        new("sack", "burlap", (39, 72), (42, 71), (43, 71), (44, 70), (39, 70)),                    // the granary yard
        new("crate", "oak", (41, 71)),
        new("cart", "hay", (45, 70)),
        new("tree", "autumn", (46, 57), (16, 57), (19, 70), (36, 51), (20, 51), (16, 50)),
    ];

    /// <summary>What stands, of one kind, on these tiles.</summary>
    private sealed record Stand(string Id, string? Variant, params (int X, int Y)[] At);

    /// <summary>The farms outside the walls: one mill, one steading, fences along the field edges with a gap to walk through, and hay on the stubble.</summary>
    private static readonly Building[] OnTheFarms =
    [
        new("windmill", "stone", 49, 35),
        new("cottage", "thatch_red", 29, 84),       // the reeve's
    ];

    /// <summary>The ground a building out on the land stands on, made flat: left, top, width, depth and height.</summary>
    private static readonly (int X, int Y, int W, int D, int Height)[] Levelled = [(48, 34, 5, 6, TownLevel), (28, 83, 4, 5, TownLevel)];

    /// <summary>A straight run of something, from one end to the other, every <paramref name="Step"/> tiles.</summary>
    private static (int X, int Y)[] Run((int X, int Y) from, (int X, int Y) to, int step = 1)
    {
        var count = Math.Max(Math.Abs(to.X - from.X), Math.Abs(to.Y - from.Y)) / step;
        return [.. Enumerable.Range(0, count + 1).Select(i => (from.X + Math.Sign(to.X - from.X) * i * step, from.Y + Math.Sign(to.Y - from.Y) * i * step))];
    }

    private static readonly Stand[] InTheFields =
    [
        .. FarmFenceStands(),
        new("haystack", "hay", (37, 11), (41, 14), (44, 10), (38, 17), (21, 36), (26, 38), (29, 35), (19, 85), (24, 86), (20, 87), (26, 84), (45, 87), (48, 91)),
        new("training_dummy", "straw", (40, 28), (37, 87), (36, 17)),                               // scarecrows, set among the stubble fields
        new("crop_wheat_stubble", "gold",
            (35, 11), (45, 18), (36, 26), (44, 30), (18, 39), (24, 34), (31, 38), (36, 36), (44, 40),
            (17, 87), (28, 86), (34, 88), (39, 90), (43, 90), (49, 84),
            (36, 100), (40, 100), (44, 100), (36, 105), (40, 105), (44, 105)),                         // grain left standing in short, open rows
        new("farm_goat", "rust", (22, 83), (45, 89)),                                                   // two quiet goats in separate enclosed fields
        new("farm_dog", "brown", (52, 26)),                                                            // the reeve's dog, by the steading
        new("farm_sheep", "cream", (24, 89), (27, 90)),                                                // a few sheep on what the goats left
        new("pack_mule", "grey", (50, 23)),                                                            // grain panniers beside the cart at Long Acre's gate
        new("cart", "hay", (48, 23)),
        new("well", "stone", (30, 89)),
        new("torch", "fire", (28, 87)),
        new("tree", "autumn", (47, 7), (47, 21), (16, 43), (15, 81), (41, 96)),
    ];

    /// <summary>Ordinary wild neighbours of the farms: rooks at the scarecrows, mallards in the river shallows, and a fox at the wood's edge.</summary>
    private static readonly Stand[] Wildlife =
    [
        new("field_crow", "charcoal", (42, 28), (39, 87)),
        new("river_duck", "mallard", (116, 89), (115, 100)),
        new("red_fox", "russet", (82, 38)),
    ];

    /// <summary>
    /// A fence round each field, standing on the field's own level, with gaps for gates. Neighbouring fields share one rail (a tee where three
    /// arms meet), a rail stops against rock rather than on its lip, and the sunken lanes between the southern fields are left open as tracks.
    /// </summary>
    private static Stand[] FarmFenceStands()
    {
        var fence = new Dictionary<(int X, int Y), string>();
        void Across(int y, int left, int right, params int[] gates)
        {
            for (var x = left; x <= right; x++)
                if (!gates.Contains(x))
                    fence[(x, y)] = "oak";
        }
        void Along(int x, int top, int bottom, params int[] gates)
        {
            for (var y = top; y <= bottom; y++)
                if (!gates.Contains(y))
                    fence[(x, y)] = "oak_reversed";
        }
        void Join(int x, int y, string variant) => fence[(x, y)] = "join_" + variant;

        // Mayor's Forty, Long Acre and Gate Close, one above the other, share their rails; Hearthfield shares Gate Close's west one.
        Across(8, 34, 45); Join(46, 8, "ne");
        Along(46, 9, 20); Join(46, 21, "tee_sw");
        Across(21, 35, 45, 39, 40);                         // Mayor's Forty opens into Long Acre
        Along(46, 22, 32); Join(46, 33, "tee_sw");
        Join(33, 33, "nw"); Across(33, 34, 45, 39, 40);     // Long Acre into Gate Close
        Along(46, 34, 41); Join(46, 42, "se");
        Along(33, 34, 41); Join(33, 42, "tee_sw");
        Across(42, 34, 45, 39, 40);                         // Gate Close to the lane under the town wall
        Across(42, 18, 32, 23, 24);                         // Hearthfield's gate
        Along(17, 37, 41); Join(17, 42, "sw");

        // Southfield, the reeve's: its cottage at (29, 84) stands in the east rail, and its door opens at (29, 86).
        Join(16, 82, "nw"); Across(82, 17, 28, 21, 22); Join(29, 82, "ne");
        Along(16, 83, 87); Join(16, 88, "sw");
        Across(88, 17, 28); Join(29, 88, "se");
        Along(29, 83, 87, 84, 85, 86);

        // Wardens' Strip and Low Field, with the sunken track at x 41 between them.
        Join(32, 82, "nw"); Across(82, 33, 39, 36, 37); Join(40, 82, "ne");
        Along(32, 83, 89);
        Along(40, 83, 91, 87, 88); Join(40, 92, "se");
        Across(92, 33, 39);
        Join(42, 82, "nw"); Across(82, 43, 49, 45, 46); Join(50, 82, "ne");
        Along(42, 83, 91, 87, 88); Join(42, 92, "sw");
        Along(50, 83, 90);                                  // it stops where the bank drops to the stream
        Across(92, 43, 49);

        // Ten Rods, above the scarp, its gate on the lane at y 96.
        Across(97, 40, 46, 42, 43); Join(47, 97, "ne");
        Along(47, 98, 105); Join(47, 106, "se");
        Across(106, 34, 46);

        Stand Of(string id, string variant, string key) => new(id, variant, [.. fence.Where(f => f.Value == key).Select(f => f.Key).Order()]);
        return
        [
            Of("fence", "oak", "oak"),
            Of("fence", "oak_reversed", "oak_reversed"),
            .. new[] { "nw", "ne", "sw", "se", "tee_sw" }.Select(v => Of("fence_join", v, "join_" + v)),
        ];
    }

    /// <summary>The road east: a signpost at each fork, a cart or two left on the verge, a wall of the scarp's crags to its west.</summary>
    private static readonly Stand[] OnTheRoad =
    [
        new("signpost", "oak", (66, 9), (88, 49)),
        new("cart", "oak", (53, 30), (92, 41)),
        new("crag", "grey", (56, 62), (58, 50), (60, 40), (63, 32), (58, 74), (57, 88), (56, 100), (60, 46), (57, 56), (60, 68), (56, 80), (59, 94), (55, 108), (58, 114), (65, 28)),
        new("rock_cluster", "grey", (59, 56), (57, 66), (61, 36), (58, 82), (57, 94), (66, 24), (62, 44), (60, 62), (55, 72), (60, 86), (58, 104), (56, 118), (68, 22)),
        new("boulder", "grey", (55, 58), (63, 50), (54, 76), (63, 72), (62, 90), (53, 96), (61, 110)),
        new("rock_large", "grey", (53, 72), (52, 84)),
    ];

    /// <summary>The burned ground: a farm the fire took, stones split by it, and nothing that grows.</summary>
    private static readonly Stand[] OnTheBurnedGround =
    [
        new("ruin_wall", "dark", (84, 44), (85, 44), (84, 46), (87, 47)),               // what's left of a farmhouse by the road
        new("rubble", "dark", (85, 45), (86, 46), (83, 45), (86, 44), (88, 45), (84, 47)),
        new("rubble", "ashen", (72, 58), (74, 73), (88, 68), (82, 84), (125, 66), (136, 74), (148, 70), (121, 84)),
        new("boulder", "dark", (86, 72), (78, 56), (127, 69), (140, 78)),
        new("gravestone", "ashen", (74, 76), (75, 76), (77, 77)),                       // whoever the fire caught
    ];

    /// <summary>
    /// Round the Ford Bridge. The goblins' fires burn on the far bank and their banners stand back from the stamp, so a tall thing never hides a fight. The old Warden stones
    /// stand at the town's end of the bridge. What's nearest the road is low.
    /// </summary>
    private static readonly Stand[] AtTheFord =
    [
        new("pillar_broken", "grey", (97, 53), (98, 67), (94, 66), (92, 55)),           // the Warden stones
        new("pillar_broken", "grey", (91, 59), (95, 59), (93, 64), (97, 64)),             // down each side of their causeway
        new("pillar_broken", "grey", (119, 56), (123, 56), (118, 61), (122, 61)),       // and on the far bank
        new("ruin_arch", "grey", (96, 68), (94, 58)),
        new("ruin_wall", "grey", (100, 49), (101, 50)),                                 // the bridge's west abutment
        new("rubble", "ashen", (95, 67), (97, 66), (89, 53), (96, 51), (102, 50), (101, 46), (100, 47), (91, 60), (98, 64)),
        new("campfire", "fire", (119, 53), (123, 55), (116, 66)),            // goblin fires on the far bank
        new("banner", "goblin", (118, 50), (121, 64), (124, 52), (120, 68)),
        new("banner", "goblin", (103, 47), (99, 59), (98, 64), (118, 55), (118, 62)),   // and their claim on the old bridge, and on both ends of the crossing
        new("skull_pike", "bone", (118, 56), (118, 60), (116, 50), (99, 58), (99, 65), (103, 49)),
        new("torch", "fire", (96, 60), (98, 63)),                                       // their pickets on the near bank
        new("barricade", "oak", (114, 53), (114, 62), (96, 59), (96, 64)),
        new("barricade", "oak", (116, 55), (116, 61)),
        new("crate", "oak", (122, 53), (120, 60)),
        new("signpost", "oak", (127, 55)),
        new("tall_grass", "dry", (98, 52), (98, 55), (99, 67), (114, 66), (115, 68), (112, 66), (101, 65)),
    ];

    /// <summary>
    /// The old Warden bridge, upstream of the ford and behind it, so it never hides a fight. River Crossing's map is a ford, its tiles can't change, and the crossing has to stay
    /// the only one, so what's left of the bridge is a stump from each bank with a span gone, on piers of its own stone. It can't be walked: its tiles are still the river.
    /// </summary>
    private static readonly Building[] OldBridge = [new("bridge_u", "grey", 105, 48), new("bridge_u", "grey", 106, 48), new("bridge_u", "grey", 108, 48)];

    /// <summary>The bridge's deck stands this high, level with the east bank and clear of the water.</summary>
    private const int BridgeDeck = 3;

    /// <summary>The Wardens' paved road, what's left of it, either side of the crossing: x, y for each end.</summary>
    private static readonly (double X, double Y)[][] Causeways = [[(88, 61), (99.5, 61.5)], [(113.5, 58), (124, 58)]];

    /// <summary>The docks' sheds on their yard, each door on the lane behind the quay. The market's stalls stand on the quay itself.</summary>
    private static readonly Building[] AtTheDocks =
    [
        new("cottage", "shakes_oak", 88, 90), new("house", "blue", 91, 90),         // the fishers'
        new("cottage", "thatch_green", 88, 98), new("store", "blue", 91, 98),       // the chandler's
        new("clutter_crates", "oak", 90, 91), new("clutter_barrels", "oak", 90, 99),
        new("stall", "purple", 96, 92), new("stall", "red", 96, 95),                // the Ash Market
        new("stall", "blue", 96, 103), new("stall", "purple", 96, 106),
        new("clutter_crates", "oak", 97, 101), new("clutter_barrels", "oak", 95, 108),
    ];

    /// <summary>The River Docks. By day it's fish, and by night the Ash Market lights its lanterns: they're all that lights the place.</summary>
    private static readonly Stand[] OnTheDocks =
    [
        new("fishing_skiff", "oak", (105, 95), (105, 101), (105, 107)),
        new("lantern_iron", "iron", (98, 91), (98, 96), (98, 102), (98, 107), (107, 93), (107, 99), (107, 105)),
        new("barrel", "oak", (100, 93), (101, 93), (100, 105)),
        new("crate", "oak", (102, 99), (103, 99), (101, 105)),
        new("sack", "burlap", (104, 93), (105, 105), (94, 91), (95, 100)),
        new("cart", "oak", (92, 87)),
    ];

    /// <summary>
    /// The Skarrow camp is four hide tents pulled in round one fire, a hide stretched on its rack, a barricade across the way in and a low palisade behind, on ground trodden to frozen
    /// mud that stops where the camp stops. There's no food to be seen, and it's dark past the fire.
    /// </summary>
    private static readonly Stand[] AtTheCamp =
    [
        new("tent", "hide", (130, 22), (132, 20), (135, 20), (136, 23)),
        new("campfire", "fire", (133, 22)),
        new("mantlet", "hide", (130, 24)),                                                  // the drying rack, with nothing much on it
        new("log", "oak", (131, 24)),
        new("stump", "oak", (135, 22)),
        new("ribcage", "bleached", (129, 20)),
        new("cage", "rust", (137, 20)),
        new("sack", "burlap", (134, 20)),
        new("banner", "goblin", (131, 19), (136, 19)),
        new("skull_pike", "bone", (129, 26), (137, 26)),
        new("barricade", "oak", (131, 26), (132, 27), (136, 27)),
        new("palisade", "log", (128, 21), (128, 22), (128, 23), (129, 18), (130, 18), (132, 17), (133, 17), (134, 17), (136, 18), (137, 19), (138, 21), (138, 22)),
    ];

    /// <summary>The Skarrow Hills: bare terraces of frost and rock, a few snowbound pines, and the crags that close them.</summary>
    private static readonly Stand[] InTheHills =
    [
        new("pine_snowy", "snow", (122, 26), (119, 30), (141, 36), (146, 38), (126, 38), (150, 42)),
        new("crag", "dark", (116, 22), (130, 40), (143, 28), (152, 34), (122, 30), (127, 16), (136, 12), (146, 18), (150, 24), (156, 30), (146, 34), (138, 38), (124, 12), (114, 16)),
        new("rock_cluster", "dark", (122, 44), (136, 43), (147, 44), (118, 32), (128, 34), (141, 22), (119, 18), (132, 14), (148, 30), (154, 42), (140, 14)),
        new("boulder", "dark", (127, 42), (140, 41), (121, 36), (125, 22), (134, 36), (144, 37), (150, 38), (116, 28), (142, 24)),
        new("dead_tree", "dark", (116, 44), (132, 46), (144, 48)),
    ];

    /// <summary>Old Maud's hut, alone on the burned bank.</summary>
    private static readonly Building[] AtMauds = [new("cottage", "thatch_green", 130, 88)];

    /// <summary>The charms she's hung about it, the dead trees that stand round it, and nothing else for a long way.</summary>
    private static readonly Stand[] RoundMauds =
    [
        new("dead_tree", "dark", (125, 85), (127, 92), (136, 86), (138, 93), (129, 97), (134, 98)),
        new("skull_pike", "bone", (128, 90), (133, 88), (134, 91), (129, 87)),
        new("shrine", "stone", (127, 88)),
        new("grave_cross", "ashen", (136, 90), (137, 89)),
        new("torch", "fire", (132, 90)),
    ];

    /// <summary>The reeds, in beds along both banks: from a row to a row, north to south, clear of the stamp. A bed fills every other bank tile that touches water.</summary>
    private static readonly (int From, int To)[] Reedbeds = [(26, 50), (66, 86)];

    /// <summary>A wood: its trees stand about every <paramref name="Every"/> tiles inside <paramref name="Outline"/>, jittered, clear of the roads, and of <see cref="WoodPaths"/>.</summary>
    private sealed record Wood(string Name, int Every, (string Id, string? Variant)[] Trees, (double X, double Y)[] Outline, bool Floor = false);

    /// <summary>Living woods far from the fire, thinning to dead standing timber by the ford.</summary>
    private static readonly Wood[] Woods =
    [
        new("Scarpfoot Wood", 2, [("pine", "green"), ("tree", "autumn"), ("pine", "green")], [(65, 22), (73, 20), (79, 28), (80, 38), (74, 46), (67, 46), (64, 34)], Floor: true),
        new("Bridgewood", 2, [("tree", "autumn"), ("pine", "green"), ("tree", "autumn")], [(82, 4), (100, 5), (98, 18), (91, 24), (86, 18)], Floor: true),
        new("Cinder Wood", 2, [("dead_tree", "grey"), ("dead_tree", "dark")], [(66, 52), (82, 50), (86, 60), (84, 80), (72, 96), (64, 88)]),
        new("Black Stand", 3, [("dead_tree", "dark"), ("dead_tree", "grey")], [(120, 72), (146, 66), (152, 86), (146, 106), (124, 110), (118, 96)]),
    ];

    /// <summary>The share of a wood's spots with no tree, so it's never a wall.</summary>
    private const double WoodGaps = 0.06;

    /// <summary>What grows under a living wood's trees, on some of the tiles they leave: all of it low, and most of it to walk through.</summary>
    private static readonly (string Id, string? Variant)[] Undergrowth =
    [
        ("bush", "autumn"), ("fern", "autumn"), ("tall_grass", "dry"), ("fern", "autumn"), ("bush", "green"), ("mushroom_cluster", "brown"), ("stump", "oak"), ("log", "oak"),
    ];

    /// <summary>How far apart undergrowth stands, and the share of its spots left bare.</summary>
    private const int UndergrowthEvery = 2;

    private const double UndergrowthGaps = 0.4;

    /// <summary>How far anything in a wood stands from a road or a way through it.</summary>
    private const double WoodClear = 1.9;

    /// <summary>Ways through the woods, a tile or so each side, so a party can walk them: left along the wood's length, not across it.</summary>
    private static readonly Line[] WoodPaths =
    [
        new((80.0, 26.0), (72.0, 34.0), (70.0, 46.0)),
        new((90.0, 12.0), (92.0, 24.0)),
        new((74.0, 50.0), (78.0, 66.0), (72.0, 88.0)),
        new((130.0, 72.0), (140.0, 90.0), (128.0, 106.0)),
    ];

    /// <summary>All of it, place by place.</summary>
    private static readonly Stand[] Everything = [.. InEmberwick, .. InTheFields, .. Wildlife, .. OnTheRoad, .. OnTheBurnedGround, .. AtTheFord, .. OnTheDocks, .. AtTheCamp, .. InTheHills, .. RoundMauds];

    /// <summary>Someone standing on a tile, looking one way, and walking a loop if they've somewhere to pace.</summary>
    private static RegionPerson Stood(string preset, int x, int y, string face, params (int X, int Y)[] walk)
        => new(preset, new Tile(x, y), face, walk.Length == 0 ? default : [.. walk.Select(t => new Tile(t.X, t.Y))]);

    /// <summary>
    /// Who lives where the lore says. Pip stands on the mayor's beat, as he opens it, and a beat's figure stands in for its own person (<c>WorldPeople</c>), so nobody shows twice. Lark
    /// and Corvin have no scene in this chapter, so they're where a singer and a scholar would be: on the square by the well, and reading the notice board.
    /// </summary>
    private static readonly RegionPerson[] Named =
    [
        Stood("mayor", 29, 62, "se"),                   // Halden Varrow, on the square
        Stood("innkeeper", 25, 55, "sw"),               // Bree Tallow, at The Last Coal's door
        Stood("priest", 30, 55, "sw"),                  // Mother Aubrine, at the chapel
        Stood("blacksmith", 36, 55, "se"),              // Tobin Fell, at the smithy
        Stood("guard_captain", 45, 64, "ne"),           // Dagna Holt, at the east gate
        Stood("hermit", 130, 90, "sw"),                 // Old Maud, at her hut
        Stood("goblin_shaman", 134, 24, "nw"),          // Mother Skarrow, by the fire
        Stood("child", 33, 59, "sw"),                   // Pip
        Stood("minstrel", 29, 58, "sw"),                // Lark, playing to the square
        Stood("sage", 36, 58, "ne"),                    // Corvin Ashby, at the notice board
        Stood("noble", 41, 68, "se"),                   // Celandine Marrowby, watching her granary
    ];

    /// <summary>Everyone else, by place: no one on a road's line or a door, so the party walks past them and not through them.</summary>
    private static readonly RegionPerson[] Townsfolk =
    [
        // The market: a stallholder behind each stall, and a customer or two.
        Stood("merchant", 26, 64, "sw"), Stood("merchant", 28, 64, "sw"), Stood("merchant", 35, 64, "sw"),
        Stood("farmer", 26, 66, "ne"), Stood("barmaid", 35, 66, "ne"),
        Stood("child", 30, 59, "se", (30, 60), (30, 61), (31, 61), (32, 61), (32, 60), (32, 59), (31, 59)),     // running round the well
        Stood("farmhand", 37, 60, "sw", (37, 61), (36, 61), (36, 60)),

        // The gates.
        Stood("guard", 46, 61, "sw"), Stood("guard", 46, 63, "ne"), Stood("guard", 30, 78, "se"), Stood("guard", 32, 78, "sw"), Stood("guard", 48, 60, "sw"),

        // The streets.
        Stood("farmer", 41, 61, "se", (42, 61), (43, 61), (43, 62), (42, 62), (41, 62)),
        Stood("barmaid", 20, 62, "se", (21, 62), (22, 62), (22, 61), (21, 61), (20, 61)),
        Stood("child", 18, 56, "nw"), Stood("guard", 26, 49, "se", (27, 49), (28, 49), (29, 49), (29, 48), (28, 48), (27, 48), (26, 48)),

        // The fields.
        Stood("farmer", 38, 13, "sw"), Stood("farmhand", 22, 37, "se"), Stood("farmer", 25, 87, "ne"), Stood("farmhand", 37, 85, "sw"), Stood("farmer", 30, 86, "se"),
        Stood("merchant", 52, 31, "se"),                // and his cart, on the verge

        // The docks: fishers on the jetties, and the Ash Market's stallholders.
        Stood("fisher", 102, 93, "se"), Stood("fisher", 105, 92, "sw"), Stood("fisher", 104, 99, "se"), Stood("fisher", 103, 104, "se"), Stood("merchant", 97, 93, "sw"), Stood("merchant", 97, 104, "sw"),

        // The camp, thin and cold, and the goblins who hold the ford's approach.
        Stood("goblin_spear", 132, 25, "se"), Stood("goblin_archer", 135, 25, "sw"), Stood("goblin_cutthroat", 132, 23, "se"), Stood("goblin_spear", 134, 27, "sw", (134, 28), (133, 28), (133, 27)),
        Stood("goblin_spear", 99, 63, "nw"), Stood("goblin_spear", 99, 64, "se"), Stood("goblin_archer", 95, 58, "se"),
        Stood("goblin_spear", 117, 55, "sw"), Stood("goblin_spear", 117, 61, "ne"), Stood("goblin_cutthroat", 120, 54, "se"), Stood("goblin_archer", 120, 63, "sw"),
    ];

    /// <summary>The party starts on the square, in front of the inn.</summary>
    private static readonly Tile StartSpawn = new(26, 58);

    /// <summary>A defeat wakes the party at the chapel door: the Church of the Hearth takes in the wounded.</summary>
    private static readonly Tile RecoverySpawn = new(31, 57);

    /// <summary>
    /// The beats. The three at the ford stand clear of the stamp, as a step into it starts its fight. Force walks in from Emberwick, so
    /// <c>ch1_ford_assault</c> is on the near bank; parley fades the party to the hills first, so <c>ch1_river_crossing</c> is on the far one.
    /// </summary>
    private static readonly (string Beat, int X, int Y)[] Spots =
    [
        ("ch1_mayors_request", 33, 59),
        ("ch1_the_ford_choice", 94, 59),
        ("ch1_ford_assault", 96, 63),
        ("ch1_river_crossing", 117, 58),
        ("ch1_old_maud", 132, 93),
        ("ch1_granary", 42, 70),
        ("ch1_skarrow_song", 133, 24),
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
        land.Level();
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
        land.PaveCauseways();
        land.RaiseOldBridge();

        var source = new RegionSource(Id, "The Ashen Ford", 1, Width, Height) { Start = StartSpawn, Recovery = RecoverySpawn };
        land.CopyTo(source);
        source.Stamp(crossing, StampAt, WaterLevel);
        source.Beats.AddRange(Spots.Select(s => new BeatSpot(s.Beat, new Tile(s.X, s.Y))));
        source.Buildings.AddRange(Emberwick.Concat(TownWall()).Concat(OnTheFarms).Concat(AtTheDocks).Concat(AtMauds).Concat(OldBridge).Select(b => new RegionBuilding(b.Id, b.Variant, new Tile(b.X, b.Y))));
        source.People.AddRange(Named.Concat(Townsfolk));
        return source;
    }

    /// <summary>Dresses the region with what stands in each place: the lists above, by name.</summary>
    public static void Dress(RegionSource source, RulesPack? pack = null)
    {
        if (source.Props.Count > 0)
            throw new InvalidOperationException("The scene already has props.");

        var placed = new Dictionary<Tile, (string Id, string? Variant)>();
        foreach (var stand in Everything)
            foreach (var (x, y) in stand.At)
            {
                var tile = new Tile(x, y);
                // Farm boundaries follow the land; never stand a fence on an impassable cliff face or a drop's lip.
                if ((stand.Id is "fence" or "fence_join") && FenceOnCliff(source, tile))
                    continue;
                placed.TryAdd(tile, (stand.Id, stand.Variant));
            }
        PlantWoods(source, placed);
        GrowReeds(source, placed);
        foreach (var group in placed.GroupBy(p => p.Value))
            source.Props.Add(new RegionProps(group.Key.Id, group.Key.Variant, [.. group.Select(p => p.Key).Order()]));
    }

    private static bool FenceOnCliff(RegionSource source, Tile tile)
    {
        if (source.Get(tile) is not { } ground || ground.Flags.HasFlag(TileFlags.Impassable) || ground.Material.StartsWith("cave_floor/", StringComparison.Ordinal))
            return true;

        // A fence may end against rock that rises beside it, but never stands on the lip of a drop.
        foreach (var (x, y) in new[] { (tile.X + 1, tile.Y), (tile.X - 1, tile.Y), (tile.X, tile.Y + 1), (tile.X, tile.Y - 1) })
            if (source.Get(new Tile(x, y)) is { } neighbour && ground.Height - neighbour.Height > 1)
                return true;

        return false;
    }

    /// <summary>Fills <see cref="Reedbeds"/>: a reed on every other bank tile with water beside it.</summary>
    private static void GrowReeds(RegionSource source, Dictionary<Tile, (string Id, string? Variant)> placed)
    {
        (int X, int Y)[] beside = [(1, 0), (-1, 0), (0, 1), (0, -1)];
        foreach (var (from, to) in Reedbeds)
            for (var y = from; y <= to; y++)
                for (var x = 90; x < 125; x++)
                {
                    var tile = new Tile(x, y);
                    if ((x + y) % 2 == 0 && source.Get(tile) is { Flags: TileFlags.None } ground && ground.Material is not (Deck or Quay) && !RoadMaterials.Contains(ground.Material)
                        && beside.Any(b => source.Get(new Tile(x + b.X, y + b.Y)) is { } next && next.Flags.HasFlag(TileFlags.Water)))
                        placed.TryAdd(tile, ("reeds", "swamp"));
                }
    }

    /// <summary>Stands each wood's trees in its outline, each on its own spot of a grid, moved a tile or so. Only this is seeded.</summary>
    private static void PlantWoods(RegionSource source, Dictionary<Tile, (string Id, string? Variant)> placed)
    {
        var random = new Random(Seed);
        var roads = Roads.Concat(Tracks).Select(r => r.Line).Concat(WoodPaths).ToArray();
        foreach (var wood in Woods)
        {
            var (left, right) = ((int)wood.Outline.Min(p => p.X), (int)wood.Outline.Max(p => p.X));
            var (top, bottom) = ((int)wood.Outline.Min(p => p.Y), (int)wood.Outline.Max(p => p.Y));
            // A living wood has a second stand of trees in the gaps of the first, thinner, so it isn't a grid.
            foreach (var (offset, gaps) in wood.Floor ? [(0, WoodGaps), (1, 0.5)] : new[] { (0, WoodGaps) })
                for (var y = top + offset; y <= bottom; y += wood.Every)
                    for (var x = left + offset; x <= right; x += wood.Every)
                {
                    var tile = new Tile(x + random.Next(wood.Every / 2 + 1), y + random.Next(wood.Every / 2 + 1));
                    var tree = wood.Trees[random.Next(wood.Trees.Length)];
                    if (random.NextDouble() < gaps)
                        continue;
                    if (Inside(wood.Outline, tile.X, tile.Y) && source.Get(tile) is { Flags: TileFlags.None } ground && !RoadMaterials.Contains(ground.Material)
                        && ground.Material != Quay && roads.All(r => r.Nearest(tile.X, tile.Y).Distance > WoodClear))
                        placed.TryAdd(tile, tree);
                }

            if (wood.Floor)
                Plant(source, placed, wood, roads, random);
        }
    }

    /// <summary>Stands <see cref="Undergrowth"/> through a wood, on what the trees left, jittered as they are.</summary>
    private static void Plant(RegionSource source, Dictionary<Tile, (string Id, string? Variant)> placed, Wood wood, Line[] roads, Random random)
    {
        var (left, right) = ((int)wood.Outline.Min(p => p.X), (int)wood.Outline.Max(p => p.X));
        var (top, bottom) = ((int)wood.Outline.Min(p => p.Y), (int)wood.Outline.Max(p => p.Y));
        for (var y = top + 1; y <= bottom; y += UndergrowthEvery)
            for (var x = left + 1; x <= right; x += UndergrowthEvery)
            {
                var tile = new Tile(x + random.Next(2), y + random.Next(2));
                var plant = Undergrowth[random.Next(Undergrowth.Length)];
                if (random.NextDouble() >= UndergrowthGaps && Inside(wood.Outline, tile.X, tile.Y) && source.Get(tile) is { Flags: TileFlags.None } ground && !RoadMaterials.Contains(ground.Material)
                    && roads.All(r => r.Nearest(tile.X, tile.Y).Distance > WoodClear))
                    placed.TryAdd(tile, plant);
            }
    }

    /// <summary>Whether a point's inside an outline.</summary>
    private static bool Inside((double X, double Y)[] outline, double x, double y)
    {
        var inside = false;
        for (var (i, j) = (0, outline.Length - 1); i < outline.Length; j = i++)
            if ((outline[i].Y > y) != (outline[j].Y > y) && x < (outline[j].X - outline[i].X) * (y - outline[i].Y) / (outline[j].Y - outline[i].Y) + outline[i].X)
                inside = !inside;
        return inside;
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

        /// <summary>The stamp's size, once it's down.</summary>
        private (int W, int H) _stamp;

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

        /// <summary>How far a tile is from the fire: from the ford, or from the bank it burned down. A slow wobble keeps the rings from being circles.</summary>
        private double FromFire(int x, int y)
            => Math.Min(Distance(x, y, Ford.X, Ford.Y), BurnedBank.Nearest(x, y).Distance + 10) + (Noise(x, y, 26, 4) - 0.5) * 16;

        /// <summary>What the ground is at a tile, by how far it is from the fire.</summary>
        private string Ring(int x, int y) => FromFire(x, y) switch
        {
            < AshEnds => Ash,
            < ScorchEnds => Scorch,
            _ => Thin,
        };

        /// <summary>The rolling land under everything, one level from top to bottom, and each tile's ring.</summary>
        public void Ground()
        {
            foreach (var (x, y) in Everywhere())
            {
                var height = FarBank(x, y) || x > 64 ? 2 : 2 + (Rough(x, y, 40, 6) > 0.5 ? 1 : 0);
                Paint(x, y, height, Ring(x, y));
            }
        }

        /// <summary>The scarp: a wall of rock with a stepped shoulder, and a rise past its tip for the road to climb.</summary>
        public void RaiseScarp()
        {
            foreach (var (x, y) in Everywhere())
            {
                var saddle = 1 - Distance(x, y, 70, 12) / 20;
                if (saddle > 0)
                    _heights[x, y] += (int)Math.Round(saddle * 3);

                var distance = Scarp.Nearest(x, y).Distance + (Rough(x, y, 14, 12) - 0.5) * 4;
                if (distance < 3)
                    Paint(x, y, 6 + (int)(Rough(x, y, 12, 14) * 4), Rock, TileFlags.Impassable);
                else if (distance < 7)
                {
                    var shoulder = (7 - distance) / 4;
                    _heights[x, y] += (int)Math.Round(shoulder * 2) * 2 - 1;
                    if (shoulder > 0.5)
                        _materials[x, y] = Rock;
                }
            }
        }

        /// <summary>The far bank: terraces two levels apart climbing to the north-east, frozen mud and then cold rock, and the burned ground at their foot.</summary>
        public void RaiseHills()
        {
            foreach (var (x, y) in Everywhere())
            {
                if (!FarBank(x, y))
                    continue;

                var hill = Hill(x, y);
                if (hill <= 0.05)
                    continue;

                var level = hill switch { > 0.76 => 9, > 0.5 => 7, > 0.27 => 5, _ => 3 };
                var loose = Rough(x, y, 10, 20) > (level >= 7 ? 0.48 : 0.55);
                Paint(x, y, level, loose ? Scree : level >= 7 ? DarkRock : Frost, level == 9 ? TileFlags.Impassable : TileFlags.None);
                _terraced[x, y] = true;
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
                var river = River.Nearest(x, y).Distance + (Rough(x, y, 12, 28) - 0.5) * 3 * away;
                if (river < bank)
                    Wet(x, y, river, deep, shallow, bank, _terraced[x, y] ? Frost : Ring(x, y) == Thin ? Scorch : Ring(x, y));
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

        /// <summary>The fields, each one crop from edge to edge, flat, at the town's level.</summary>
        public void PlantFields()
        {
            foreach (var (_, left, top, width, depth, crop) in Fields)
                foreach (var (x, y) in Rectangle(left, top, width, depth))
                {
                    if (Walkable(x, y) && !IsWater(x, y))
                        Paint(x, y, TownLevel, crop);
                    _fixed[x, y] = true;
                }
        }

        /// <summary>The flats for what stands out on the land, and a wood's floor where it has one: drifts of needles over dark earth, so the art's green patches don't repeat across it.</summary>
        public void Level()
        {
            foreach (var (left, top, width, depth, height) in Levelled)
                foreach (var (x, y) in Rectangle(left, top, width, depth))
                {
                    _heights[x, y] = height;
                    _fixed[x, y] = true;
                }

            foreach (var wood in Woods.Where(w => w.Floor))
                foreach (var (x, y) in Everywhere().Where(t => Inside(wood.Outline, t.X, t.Y) && Walkable(t.X, t.Y)))
                    _materials[x, y] = Rough(x, y, 8, 60) > 0.5 ? Needles : Mud;
        }

        /// <summary>Emberwick's ground, flat so that a building stands square on it: grass in the yards, its streets and its square.</summary>
        public void BuildTown()
        {
            foreach (var (x, y) in Rectangle(TownLeft, TownTop, TownRight - TownLeft + 1, TownBottom - TownTop + 1))
            {
                Paint(x, y, TownLevel, Thin);
                _fixed[x, y] = true;
            }

            foreach (var (_, left, top, width, depth) in Streets)
                foreach (var (x, y) in Rectangle(left, top, width, depth))
                    Paint(x, y, TownLevel, Street);
            foreach (var (x, y) in Rectangle(Square.X, Square.Y, Square.W, Square.D))
                Paint(x, y, TownLevel, Plaza);
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

                // A road ends at the town's wall: its streets go on from there.
                if (InTown(x, y))
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
                        Paint(x, y, level, road.Verge);
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

        private static bool InTown(int x, int y) => x >= TownLeft && x <= TownRight && y >= TownTop && y <= TownBottom;

        /// <summary>The River Docks: a stone quay, plank piers out over the shallows, and a hull moored at each.</summary>
        public void BuildDocks()
        {
            foreach (var (x, y) in Rectangle(86, 88, 15, 22))
            {
                if (_flags[x, y].HasFlag(TileFlags.Deep) || (x < 95 && IsWater(x, y)))
                    continue;
                Paint(x, y, WaterLevel + 1, x >= 94 ? Quay : Scorch);
                _fixed[x, y] = true;
            }

            foreach (var pier in (ReadOnlySpan<int>)[92, 98, 104])
            {
                // The quay ends at x=100; start the timber on the first water tile so it
                // meets the stone edge cleanly instead of laying two tiles over the plaza.
                foreach (var (x, y) in Rectangle(101, pier, 7, 2))
                {
                    Paint(x, y, WaterLevel + 1, Deck);
                    _fixed[x, y] = true;
                }
                foreach (var (x, y) in Rectangle(103, pier + 2, 4, 2))
                {
                    Paint(x, y, WaterLevel, Water, DeepWater);
                    _fixed[x, y] = true;
                }
            }
        }

        /// <summary>The Skarrow camp: a clearing of trodden frozen mud on the third terrace, no bigger than what stands on it.</summary>
        public void PitchCamp()
        {
            foreach (var (x, y) in Everywhere())
            {
                var distance = Distance(x, y, 133, 22.5);
                if (distance < 5.4 + (Rough(x, y, 7, 38) - 0.5) * 1.6)
                {
                    Paint(x, y, 7, Mud);
                    _fixed[x, y] = true;
                    _terraced[x, y] = false;
                }
            }
        }

        /// <summary>Old Maud's knoll, of bare ash, flat on top for the one hut.</summary>
        public void RaiseMauds()
        {
            foreach (var (x, y) in Everywhere())
            {
                var distance = Distance(x, y, 132, 91);
                if (distance < 11)
                {
                    Paint(x, y, distance < 7 ? 4 : distance < 9 ? 3 : 2, Ash);
                    _fixed[x, y] = distance < 7;
                }
            }
        }

        /// <summary>Rock round the whole region, which leaves only the deep water, the roads and the void corners' edges.</summary>
        public void CloseEdges()
        {
            foreach (var (x, y) in Everywhere())
            {
                var edge = EdgeDistance(x, y);
                var thick = 1.5 + (Rough(x, y, 16, 44) - 0.2) * 12;
                if (edge >= thick || _fixed[x, y] || _flags[x, y].HasFlag(TileFlags.Deep))
                    continue;
                var height = 4 + (int)((thick - edge) * 0.6) + (int)(Rough(x, y, 9, 46) * 3.4);
                Paint(x, y, Math.Min(height, WorldRegion.MaxHeight), DarkRock, TileFlags.Impassable);
                _fixed[x, y] = true;
            }
        }

        /// <summary>River Crossing's tiles, and the land round them drawn to their heights so the stamp doesn't sit like a tile on a table. Its ridge's rock runs on a little past it.</summary>
        public void Stamp(BattleMap map)
        {
            _stamp = (map.Width, map.Height);
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
                if (cell.Material == StampRock && distance + Rough(x, y, 8, 48) * 6 < 6)
                    _materials[x, y] = Rock;
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

        /// <summary>The Wardens' stone road either side of the crossing, grey and broken at the edges, over what's there and at its height.</summary>
        public void PaveCauseways()
        {
            foreach (var causeway in Causeways)
            {
                var line = new Line(causeway);
                foreach (var (x, y) in Everywhere())
                    if (line.Nearest(x, y).Distance + (Rough(x, y, 5, 52) - 0.5) * 2.4 < 1.3 && Walkable(x, y) && !IsWater(x, y) && !InStamp(x, y))
                        _materials[x, y] = Plaza;
            }
        }

        /// <summary>The old bridge's river tiles, raised under its deck, which keep them what they are, and a ramp of its stone at the west end.</summary>
        public void RaiseOldBridge()
        {
            foreach (var span in OldBridge)
            {
                _heights[span.X, span.Y] = BridgeDeck;
                _flags[span.X, span.Y] = DeepWater;
                _materials[span.X, span.Y] = Water;
            }
            Paint(104, 48, BridgeDeck, Plaza);
            Paint(103, 48, BridgeDeck - 1, Plaza);
        }

        private bool InStamp(int x, int y) => x >= StampAt.X && y >= StampAt.Y && x < StampAt.X + _stamp.W && y < StampAt.Y + _stamp.H;

        /// <summary>Faces each terrace's edge with scree, so the steps read from a distance and no terrace ends in a straight line of one stuff.</summary>
        public void FaceTerraces()
        {
            var faces = Everywhere().Where(t => _terraced[t.X, t.Y] && Walkable(t.X, t.Y) && !_fixed[t.X, t.Y]
                && ((t.X + 1 < Width && _heights[t.X + 1, t.Y] < _heights[t.X, t.Y] - 1) || (t.Y + 1 < Height && _heights[t.X, t.Y + 1] < _heights[t.X, t.Y] - 1)
                    || (t.X > 0 && _heights[t.X - 1, t.Y] < _heights[t.X, t.Y] - 1) || (t.Y > 0 && _heights[t.X, t.Y - 1] < _heights[t.X, t.Y] - 1))).ToList();
            foreach (var (x, y) in faces)
                _materials[x, y] = DarkRock;
        }

        public void CopyTo(RegionSource source)
        {
            foreach (var (x, y) in Everywhere().Where(t => !Void(t.X, t.Y)))
                source.Set(new Tile(x, y), _heights[x, y], _materials[x, y], _flags[x, y]);
        }
    }
}
