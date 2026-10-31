using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tactics.Logic;
using Tactics.Logic.Tests;

namespace Tactics.WorldTools.Tests;

/// <summary>The draft, exported the way a painter's scene is and loaded as the game loads it, so these prove what the game will get.</summary>
public sealed class AshenFordDraftTests
{
    private const string Id = "ashen_ford", Map = "river_crossing";

    /// <summary>The beats that stand on the far bank, reached by the ford.</summary>
    private static readonly string[] FarBeats = ["ch1_old_maud", "ch1_river_crossing", "ch1_skarrow_song"];

    /// <summary>The beats at the Ford Bridge.</summary>
    private static readonly string[] FordBeats = ["ch1_the_ford_choice", "ch1_ford_assault", "ch1_river_crossing"];

    /// <summary>What the road is made of: the path, the town's cobbles and square, and the plank bridge.</summary>
    private static readonly string[] RoadMaterials = ["cobble/warm", "flagstone/warm", "path/packed", "planks/oak"];

    private static readonly Lazy<Proved> Draft = new(Prove);

    private sealed record Proved(RulesPack Pack, RegionSource Source, ExportResult Export, WorldRegion Region, DraftWalk Walk)
    {
        private readonly Lazy<HashSet<Tile>> _nearBank = new(() => Walk.From(Region.Def.Spawns.Start, ZoneOf(Pack.MapFor(Map)!, Region.Def.Stamps[0], "ford")));

        public RegionStamp Stamp => Region.Def.Stamps.Single();

        public BattleMap Crossing => Pack.MapFor(Map)!;

        public Tile Start => Region.Def.Spawns.Start;

        public Tile Recovery => Region.Def.Spawns.Recovery;

        public ImmutableArray<Tile> Zone(string zone) => ZoneOf(Crossing, Stamp, zone);

        public bool InStamp(Tile t)
            => t.X >= Stamp.At.X && t.Y >= Stamp.At.Y && t.X < Stamp.At.X + Crossing.Width && t.Y < Stamp.At.Y + Crossing.Height;

        public Tile Spot(string beat) => Region.Def.Beats.Single(b => b.Beat == beat).At;

        public string Material(Tile t) => Region.TileAt(t) is { } tile ? Region.Def.Materials[tile.Material] : "";

        public IEnumerable<Tile> Tiles
            => Enumerable.Range(0, Region.Def.Size[1]).SelectMany(y => Enumerable.Range(0, Region.Def.Size[0]).Select(x => new Tile(x, y)));

        /// <summary>The start's side of the river: everything reached with the ford's own tiles shut.</summary>
        public HashSet<Tile> NearBank => _nearBank.Value;
    }

    private static ImmutableArray<Tile> ZoneOf(BattleMap map, RegionStamp stamp, string zone)
        => [.. map.Zones[zone].Select(t => new Tile(t.X + stamp.At.X, t.Y + stamp.At.Y))];

    private static PaintedRegion Painted(RegionSource source) => new(source.Id, source.Name, source.Chapter, source.Width, source.Height)
    {
        Materials = source.Tiles.ToDictionary(t => t.Key, t => t.Value.Material),
        Heights = source.Tiles.ToDictionary(t => t.Key, t => t.Value.Height),
        Flags = source.Tiles.ToDictionary(t => t.Key, t => t.Value.Flags),
        Beats = [.. source.Beats],
        Stamps = [.. source.Stamps],
        Starts = [source.Start],
        Recoveries = [source.Recovery],
    };

    /// <summary>Exports the draft into a copy of the pack, lists it there, and loads the copy, which runs every check the game does.</summary>
    private static Proved Prove()
    {
        var pack = RulesPack.Load([ContentPaths.BaseRules]);
        var source = AshenFordDraft.Build(pack);
        var rules = Directory.CreateTempSubdirectory("tactics-draft-test-").FullName;
        try
        {
            foreach (var file in Directory.EnumerateFiles(ContentPaths.BaseRules, "*", SearchOption.AllDirectories))
            {
                var to = Path.Combine(rules, Path.GetRelativePath(ContentPaths.BaseRules, file));
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Copy(file, to);
            }

            var export = RegionExport.Export(Painted(source), rules, ContentPaths.ContentBase);
            Assert.True(export.Written is not null, string.Join("\n", export.Problems));
            var path = Path.Combine(rules, "manifest.json");
            var manifest = JsonNode.Parse(File.ReadAllText(path))!;
            manifest["files"]!.AsArray().Add($"world/{Id}.json");
            File.WriteAllText(path, manifest.ToJsonString());

            var region = RulesPack.Load([rules]).RegionFor(Id)!;
            return new Proved(pack, source, export, region, new DraftWalk(region, new Dictionary<string, BattleMap> { [Map] = pack.MapFor(Map)! }));
        }
        finally
        {
            Directory.Delete(rules, recursive: true);
        }
    }

    private static List<(string Id, string Place)> LoreBeats()
    {
        using var lore = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(ContentPaths.ContentBase, "lore", "arcs.json")));
        return
        [
            .. lore.RootElement.GetProperty("arcs").EnumerateArray().Where(arc => arc.GetProperty("chapter").GetInt32() == 1)
                .SelectMany(arc => arc.GetProperty("beats").EnumerateArray())
                .Select(beat => (beat.GetProperty("id").GetString()!, beat.GetProperty("place").GetString()!)),
        ];
    }

    private static int Distance(Tile a, Tile b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private static double Near(Proved draft, Tile at, int radius, Func<string, bool> material)
    {
        var around = draft.Tiles.Where(t => Distance(t, at) <= radius).ToList();
        return around.Count(t => material(draft.Material(t))) / (double)around.Count;
    }

    [Fact]
    public void The_draft_is_160_by_120()
    {
        var region = Draft.Value.Region.Def;
        Assert.Equal((Id, "The Ashen Ford", 1), (region.Id, region.Name, region.Chapter));
        Assert.Equal([160, 120], region.Size);
    }

    [Fact]
    public void The_same_draft_comes_out_every_time()
    {
        var (one, two) = (AshenFordDraft.Build(Draft.Value.Pack), AshenFordDraft.Build(Draft.Value.Pack));
        Assert.Equal(one.Tiles.Count, two.Tiles.Count);
        Assert.All(one.Tiles, t => Assert.Equal(t.Value, two.Get(t.Key)));
        Assert.Equal(one.Beats, two.Beats);
    }

    [Fact]
    public void River_crossing_is_stamped_exactly()
    {
        var draft = Draft.Value;
        var stamp = Assert.Single(draft.Region.Def.Stamps);
        Assert.Equal(Map, stamp.Map);
        foreach (var cell in StampBrush.Cells(draft.Crossing, stamp.At, stamp.Height))
        {
            var tile = draft.Region.TileAt(cell.At)!;
            Assert.Equal((cell.Height, cell.Material, cell.Flags), (tile.Height, draft.Material(cell.At), tile.Flags));
        }
    }

    [Fact]
    public void Every_beat_in_chapter_1_of_the_lore_has_a_spot()
    {
        var lore = LoreBeats();
        Assert.Equal(8, lore.Count);
        Assert.Equal(5, lore.Select(b => b.Place).Distinct().Count());
        Assert.Equal(lore.Select(b => b.Id).Order(StringComparer.Ordinal), Draft.Value.Region.Def.Beats.Select(b => b.Beat).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_draft_writes_with_no_problems()
    {
        var export = Draft.Value.Export;
        Assert.Empty(export.Problems);
        Assert.Empty(export.Warnings);
    }

    [Fact]
    public void Void_is_only_the_two_western_corners()
    {
        var chunks = Draft.Value.Region.Def.Chunks.Select(c => (c.X, c.Y)).ToHashSet();
        var missing = Enumerable.Range(0, 4).SelectMany(y => Enumerable.Range(0, 5).Select(x => (x, y))).Where(c => !chunks.Contains(c));
        Assert.Equal([(0, 0), (0, 3)], missing.Order());
    }

    [Fact]
    public void Every_marker_is_reachable_from_the_start()
    {
        var draft = Draft.Value;
        var reached = draft.Walk.From(draft.Start);
        Assert.Contains(draft.Recovery, reached);
        Assert.All(draft.Region.Def.Beats, b => Assert.True(reached.Contains(b.At), b.Beat));
    }

    [Fact]
    public void Every_zone_of_the_stamp_is_reachable_past_its_props()
    {
        var draft = Draft.Value;
        var reached = draft.Walk.From(draft.Start);
        foreach (var (zone, _) in draft.Crossing.Zones)
            Assert.All(draft.Zone(zone), t => Assert.True(reached.Contains(t), $"{zone} at {t}"));
    }

    [Fact]
    public void Every_tile_reached_from_the_start_can_walk_back()
    {
        var draft = Draft.Value;
        var pockets = draft.Walk.From(draft.Start).Except(draft.Walk.To(draft.Start)).ToList();
        Assert.True(pockets.Count == 0, $"{pockets.Count} tiles can't walk back, the first at {pockets.FirstOrDefault()}.");
    }

    [Fact]
    public void No_step_in_the_draft_is_one_way()
    {
        var draft = Draft.Value;
        foreach (var tile in draft.Walk.From(draft.Start))
            foreach (var next in draft.Walk.Steps(tile))
                Assert.True(draft.Walk.Legal(next, tile), $"{tile} drops to {next} with no way back up.");
    }

    [Fact]
    public void Neither_spawn_is_in_the_stamp()
    {
        var draft = Draft.Value;
        Assert.False(draft.InStamp(draft.Start));
        Assert.False(draft.InStamp(draft.Recovery));
    }

    [Fact]
    public void A_defeat_wakes_the_party_on_emberwicks_side_by_the_road()
    {
        var draft = Draft.Value;
        Assert.Contains(draft.Recovery, draft.NearBank);
        Assert.Contains(draft.Tiles, t => Distance(t, draft.Recovery) <= 4 && RoadMaterials.Contains(draft.Material(t)));

        // Not next to the stamp, or walking out of bed starts the fight again.
        Assert.True(draft.Walk.Seconds(draft.Recovery, draft.InStamp) >= 10);
    }

    [Fact]
    public void The_river_can_only_be_crossed_at_the_ford()
    {
        var draft = Draft.Value;
        var near = draft.NearBank;
        Assert.DoesNotContain(draft.Zone("exit")[0], near);
        Assert.All(FarBeats, beat => Assert.DoesNotContain(draft.Spot(beat), near));

        // The river reaches both edges of the region, so there's no way round.
        Assert.Contains(draft.Tiles, t => t.Y == 0 && (draft.Region.TileAt(t)?.Flags & TileFlags.Deep) == TileFlags.Deep);
        Assert.Contains(draft.Tiles, t => t.Y == 119 && (draft.Region.TileAt(t)?.Flags & TileFlags.Deep) == TileFlags.Deep);
    }

    [Fact]
    public void A_party_that_crossed_can_come_back()
    {
        var draft = Draft.Value;
        var exit = draft.Zone("exit")[0];
        Assert.NotNull(draft.Walk.Seconds(draft.Start, t => t == exit));
        Assert.NotNull(draft.Walk.Seconds(exit, t => t == draft.Start));
        Assert.All(FarBeats, beat => Assert.NotNull(draft.Walk.Seconds(draft.Spot(beat), t => t == draft.Spot("ch1_the_ember"))));
    }

    [Fact]
    public void No_beat_is_inside_the_stamp_or_a_step_from_it()
    {
        var draft = Draft.Value;

        // A step into an armed stamp starts its fight, so a spot's marker must stand clear of it.
        var stamp = draft.Tiles.Where(draft.InStamp).ToList();
        Assert.All(draft.Region.Def.Beats, b => Assert.False(stamp.Any(t => Distance(t, b.At) < 3), b.Beat));
    }

    [Fact]
    public void Every_beat_stands_on_open_ground()
    {
        var draft = Draft.Value;
        Assert.Equal(draft.Region.Def.Beats.Length, draft.Region.Def.Beats.Select(b => b.At).Distinct().Count());
        var homeward = draft.Walk.To(draft.Start);
        foreach (var beat in draft.Region.Def.Beats)
        {
            Assert.True(draft.Walk.Standable(beat.At), beat.Beat);
            var around = draft.Tiles.Count(t => Distance(t, beat.At) == 1 && draft.Walk.Standable(t) && homeward.Contains(t));
            Assert.True(around >= 4, $"{beat.Beat} has only {around} tiles around it.");
        }
    }

    [Fact]
    public void Emberwick_to_the_ford_is_a_minute_or_so_on_foot()
    {
        var draft = Draft.Value;
        var ford = draft.Zone("ford").ToHashSet();
        Assert.InRange(draft.Walk.Seconds(draft.Start, ford.Contains)!.Value, 60, 75);
    }

    [Fact]
    public void The_road_walks_from_the_square_to_the_ford_and_on_east()
    {
        var draft = Draft.Value;
        bool Road(Tile t) => RoadMaterials.Contains(draft.Material(t));

        HashSet<Tile> Along(Tile from)
        {
            var reached = new HashSet<Tile> { from };
            var open = new Stack<Tile>([from]);
            while (open.TryPop(out var tile))
                foreach (var next in draft.Walk.Steps(tile).Where(Road))
                    if (Math.Abs(draft.Region.TileAt(next)!.Height - draft.Region.TileAt(tile)!.Height) <= 1 && reached.Add(next))
                        open.Push(next);
            return reached;
        }

        Assert.Contains(Along(draft.Start), t => t.X == draft.Stamp.At.X - 1);
        var east = Along(draft.Tiles.First(t => t.X == draft.Stamp.At.X + draft.Crossing.Width && Road(t)));
        Assert.Contains(east, t => t.X == 159);
    }

    [Fact]
    public void Emberwick_is_a_village_of_streets_and_buildings()
    {
        var draft = Draft.Value;
        Assert.Equal("flagstone/warm", draft.Material(draft.Start));
        var town = draft.Tiles.Where(t => Distance(t, draft.Start) <= 24).ToList();
        var roofs = town.Where(t => draft.Region.TileAt(t)?.Flags.HasFlag(TileFlags.Impassable) == true && draft.Material(t).StartsWith("planks", StringComparison.Ordinal)).ToHashSet();

        var buildings = 0;
        while (roofs.Count > 0)
        {
            buildings++;
            var open = new Stack<Tile>([roofs.First()]);
            while (open.TryPop(out var tile))
                if (roofs.Remove(tile))
                    foreach (var (x, y) in new[] { (0, 1), (1, 0), (0, -1), (-1, 0) })
                        open.Push(new Tile(tile.X + x, tile.Y + y));
        }
        Assert.InRange(buildings, 16, 40);
        Assert.True(town.Count(t => draft.Material(t) == "cobble/warm") > 150);
    }

    [Fact]
    public void The_far_bank_looks_burned_and_the_near_bank_doesnt()
    {
        var draft = Draft.Value;
        static bool Burned(string material) => material is "ash/grey" or "basalt/dark";
        Assert.DoesNotContain(draft.NearBank, t => Burned(draft.Material(t)));
        Assert.True(Near(draft, draft.Spot("ch1_old_maud"), 8, Burned) > 0.6);
    }

    [Fact]
    public void The_hills_climb_in_terraces_to_the_camp()
    {
        var draft = Draft.Value;
        var near = draft.NearBank;
        var camp = draft.Spot("ch1_skarrow_song");
        Assert.True(draft.Region.TileAt(camp)!.Height >= 7, $"The camp is at height {draft.Region.TileAt(camp)!.Height}.");
        var hill = draft.Walk.From(draft.Start).Where(t => !near.Contains(t) && t.Y < draft.Stamp.At.Y).ToList();
        var levels = hill.GroupBy(t => draft.Region.TileAt(t)!.Height).Where(level => level.Count() >= 40).Select(level => level.Key).ToList();
        Assert.True(levels.Count >= 4, $"Only heights {string.Join(", ", levels.Order())} have any breadth.");
    }

    [Fact]
    public void The_docks_are_on_the_river_on_emberwicks_side()
    {
        var draft = Draft.Value;
        var docks = draft.Spot("ch1_the_ember");
        Assert.Contains(docks, draft.NearBank);
        Assert.True(docks.Y > draft.Stamp.At.Y + draft.Crossing.Height);
        Assert.True(Near(draft, docks, 6, m => m.StartsWith("water", StringComparison.Ordinal)) > 0.1);
        Assert.True(Near(draft, docks, 6, m => m.StartsWith("planks", StringComparison.Ordinal)) > 0.05);
    }

    [Fact]
    public void The_ford_beats_stand_where_a_party_arrives()
    {
        var draft = Draft.Value;

        // Parley fades the party to the hills, so its fight is met coming back from the far bank; force walks in from Emberwick.
        Assert.Contains(draft.Spot("ch1_the_ford_choice"), draft.NearBank);
        Assert.Contains(draft.Spot("ch1_ford_assault"), draft.NearBank);
        Assert.DoesNotContain(draft.Spot("ch1_river_crossing"), draft.NearBank);
        Assert.All(FordBeats, beat => Assert.True(draft.Walk.Seconds(draft.Spot(beat), draft.InStamp) <= 3, beat));
    }
}
