using System.Text.Json;

namespace Tactics.Logic;

/// <summary>The region checks.</summary>
internal sealed partial class Validation
{
    /// <summary>The levels a step on foot can climb or drop: the rules' default Jump.</summary>
    private const int StepLevels = 2;

    /// <summary>
    /// A region (<c>world/&lt;id&gt;.json</c>): its chapter, its size, the shape of its chunks and its edges' sides, so
    /// <see cref="WorldRegion.From"/> can't fail on it. What it says about the rest of the pack is checked by
    /// <see cref="CheckRegions"/> once everything is loaded.
    /// </summary>
    public RegionDef? Region(JsonElement e, string file, string ptr)
        => Guarded(file, ptr, () =>
        {
            var def = Deserialize<RegionDef>(e, file, ptr);
            if (def is null)
                return null;
            if (def.Size is not [var w and > 0, var h and > 0])
            {
                Error(file, Pointer.Append(ptr, "size"), "The size must be [w, h], both at least 1.");
                return null;
            }

            var errors = Errors.Count;
            if (def.Chapter < 1)
                Error(file, Pointer.Append(ptr, "chapter"), $"The chapter must be at least 1, got {def.Chapter}.");

            var seen = new HashSet<(int, int)>();
            for (var i = 0; i < def.Chunks.Length; i++)
            {
                var chunk = def.Chunks[i];
                var at = Pointer.Append(Pointer.Append(ptr, "chunks"), i);
                if (chunk.X < 0 || chunk.Y < 0 || (long)chunk.X * WorldRegion.ChunkSize >= w || (long)chunk.Y * WorldRegion.ChunkSize >= h)
                {
                    Error(file, at, $"Chunk ({chunk.X}, {chunk.Y}) is outside the {w}x{h} region.");
                    continue;
                }
                if (!seen.Add((chunk.X, chunk.Y)))
                    Error(file, at, $"Chunk ({chunk.X}, {chunk.Y}) is listed twice.");
                WorldRegion.Decode(chunk, def.Materials.Length, out var problems);
                foreach (var problem in problems)
                    Error(file, Pointer.Append(at, "tiles"), problem);
            }

            for (var i = 0; i < def.Edges.Length; i++)
                if (Facing(def.Edges[i].Side) is null)
                    Error(file, Pointer.Append(Pointer.Append(Pointer.Append(ptr, "edges"), i), "side"),
                        $"The side '{def.Edges[i].Side}' must be north, east, south or west.");

            return Errors.Count == errors ? def : null;
        });

    /// <summary>
    /// Checks each region against the rest of the pack (spec §3.4): every stamp sits inside the region on a known map, tile
    /// for tile, with no map stamped twice and no two stamps overlapping; the spawns, beat spots and stamps can be reached
    /// on foot from the start; and a neighbour that's loaded has an edge back on the facing side. Edges' offsets aren't
    /// compared. A stamp on a map that failed to load skips the map checks, as an encounter does.
    /// </summary>
    public void CheckRegions(IEnumerable<Loaded<RegionDef>> regions, IReadOnlyDictionary<string, BattleMap> maps)
    {
        var loaded = regions.ToList();
        var byId = loaded.ToDictionary(r => r.Id, r => r.Def, StringComparer.Ordinal);
        foreach (var (def, _, _, file, ptr) in loaded)
        {
            try
            {
                new RegionCheck(this, WorldRegion.From(def), byId, maps, file, ptr).Run();
            }
            catch (Exception ex) when (NonFatal(ex))
            {
                Error(file, ptr, $"This couldn't be checked: {ex.Message}");
            }
        }
    }

    /// <summary>The side that faces <paramref name="side"/> across an edge, or null if it isn't a side.</summary>
    private static string? Facing(string side) => side switch
    {
        "north" => "south",
        "east" => "west",
        "south" => "north",
        "west" => "east",
        _ => null,
    };

    /// <summary>The checks of one region; see <see cref="CheckRegions"/>.</summary>
    private sealed class RegionCheck(Validation v, WorldRegion region, IReadOnlyDictionary<string, RegionDef> regions,
        IReadOnlyDictionary<string, BattleMap> maps, string file, string ptr)
    {
        private readonly RegionDef _def = region.Def;

        /// <summary>A stamp that sits inside the region, on a known map.</summary>
        private sealed record Placed(int Index, RegionStamp Stamp, BattleMap Map)
        {
            public IEnumerable<(Tile World, Tile OnMap)> Tiles()
            {
                for (var y = 0; y < Map.Height; y++)
                    for (var x = 0; x < Map.Width; x++)
                        yield return (new Tile(Stamp.At.X + x, Stamp.At.Y + y), new Tile(x, y));
            }

            public bool Overlaps(Placed other)
                => Stamp.At.X < other.Stamp.At.X + other.Map.Width && other.Stamp.At.X < Stamp.At.X + Map.Width
                   && Stamp.At.Y < other.Stamp.At.Y + other.Map.Height && other.Stamp.At.Y < Stamp.At.Y + Map.Height;
        }

        public void Run()
        {
            var placed = Stamps();
            Reachable(placed);
            Edges();
        }

        private string At(string list, int index) => Pointer.Append(Pointer.Append(ptr, list), index);

        private List<Placed> Stamps()
        {
            var placed = new List<Placed>();
            var mapsSeen = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < _def.Stamps.Length; i++)
            {
                var stamp = _def.Stamps[i];
                var at = At("stamps", i);
                if (!mapsSeen.Add(stamp.Map))
                    v.Error(file, Pointer.Append(at, "map"), $"The map '{stamp.Map}' is stamped twice, but an encounter on it must resolve to one place.");
                if (!maps.TryGetValue(stamp.Map, out var map))
                {
                    if (!v._broken.Contains((RefKind.Map, stamp.Map)))
                        v.Error(file, Pointer.Append(at, "map"), $"Unknown map '{stamp.Map}'.");
                    continue;
                }
                if (!Fits(stamp.At, map))
                {
                    v.Error(file, at, $"The stamp '{stamp.Map}' at {stamp.At} is {map.Width}x{map.Height}, which doesn't fit in the {_def.Size[0]}x{_def.Size[1]} region.");
                    continue;
                }

                var here = new Placed(i, stamp, map);
                foreach (var other in placed.Where(here.Overlaps))
                    v.Error(file, at, $"The stamp '{stamp.Map}' at {stamp.At} overlaps the stamp '{other.Stamp.Map}' at {other.Stamp.At}.");
                Match(here, at);
                placed.Add(here);
            }
            return placed;
        }

        private bool Fits(Tile at, BattleMap map)
            => at.X >= 0 && at.Y >= 0 && (long)at.X + map.Width <= _def.Size[0] && (long)at.Y + map.Height <= _def.Size[1];

        /// <summary>Compares a stamp with its map, tile by tile. As with a chunk, the first bad tile is enough to find the problem.</summary>
        private void Match(Placed stamp, string at)
        {
            foreach (var (world, onMap) in stamp.Tiles())
                if (Difference(region.TileAt(world), stamp.Map[onMap], stamp) is { } difference)
                {
                    v.Error(file, at, $"The stamp '{stamp.Stamp.Map}' doesn't match its map at {world}: the region has {difference}.");
                    return;
                }
        }

        /// <summary>How the tiles differ, or null if they don't. Materials go by name, since the two lists differ.</summary>
        private string? Difference(TileInfo? tile, TileInfo expected, Placed stamp)
        {
            if (tile is null)
                return "void there";

            var height = (long)expected.Height + stamp.Stamp.Height;
            var material = stamp.Map.Materials[expected.Material];
            var differences = new List<string>();
            if (tile.Height != height)
                differences.Add($"height {tile.Height}, the map has {height}");
            if (_def.Materials[tile.Material] != material)
                differences.Add($"material '{_def.Materials[tile.Material]}', the map has '{material}'");
            if (tile.Flags != expected.Flags)
                differences.Add($"flags {tile.Flags}, the map has {expected.Flags}");
            return differences.Count > 0 ? string.Join("; ", differences) : null;
        }

        /// <summary>
        /// Reports each marker the party can't walk to. A stamp is reached when any of its tiles is. With nowhere to start from
        /// nothing else can be said, so a bad start is reported alone.
        /// </summary>
        private void Reachable(List<Placed> stamps)
        {
            var spawns = Pointer.Append(ptr, "spawns");
            var start = _def.Spawns.Start;
            if (!region.Walkable(start))
            {
                v.Error(file, Pointer.Append(spawns, "start"), $"The start spawn at {start} can't be stood on.");
                return;
            }

            var reached = Flood(start);
            void Unreached(string marker, Tile at, string pointer)
                => v.Error(file, pointer, $"{marker} at {at} can't be reached from the start.");

            if (!reached.Contains(_def.Spawns.Recovery))
                Unreached("The recovery spawn", _def.Spawns.Recovery, Pointer.Append(spawns, "recovery"));
            for (var i = 0; i < _def.Beats.Length; i++)
                if (!reached.Contains(_def.Beats[i].At))
                    Unreached($"The beat '{_def.Beats[i].Beat}'", _def.Beats[i].At, Pointer.Append(At("beats", i), "at"));
            foreach (var stamp in stamps)
                if (!stamp.Tiles().Any(t => reached.Contains(t.World)))
                    Unreached($"The stamp '{stamp.Stamp.Map}'", stamp.Stamp.At, At("stamps", stamp.Index));
        }

        /// <summary>Every tile that can be walked to from <paramref name="start"/>, stepping north, east, south or west.</summary>
        private HashSet<Tile> Flood(Tile start)
        {
            var reached = new HashSet<Tile> { start };
            var open = new Stack<Tile>([start]);
            while (open.TryPop(out var tile))
            {
                var height = region.TileAt(tile)!.Height;
                foreach (var next in (ReadOnlySpan<Tile>)[new(tile.X, tile.Y - 1), new(tile.X + 1, tile.Y), new(tile.X, tile.Y + 1), new(tile.X - 1, tile.Y)])
                    if (region.Walkable(next) && Math.Abs(region.TileAt(next)!.Height - height) <= StepLevels && reached.Add(next))
                        open.Push(next);
            }
            return reached;
        }

        private void Edges()
        {
            for (var i = 0; i < _def.Edges.Length; i++)
            {
                var edge = _def.Edges[i];
                if (!regions.TryGetValue(edge.Neighbour, out var neighbour))
                    continue;
                var facing = Facing(edge.Side);
                if (!neighbour.Edges.Any(e => e.Neighbour == _def.Id && e.Side == facing))
                    v.Error(file, At("edges", i), $"This region meets '{edge.Neighbour}' on its {edge.Side} side, but '{edge.Neighbour}' has no {facing} edge back to it.");
            }
        }
    }
}
