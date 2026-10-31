using System.Collections.Immutable;
using System.Text.Json;

namespace Tactics.Logic;

/// <summary>The region checks.</summary>
internal sealed partial class Validation
{
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
    /// on foot from the start, round the stamps' blocking props; no marker strands the party away from another; and a neighbour
    /// that's loaded has an edge back on the facing side with the negated offset. A stamp on a map that failed to load skips the map
    /// checks, as an encounter does.
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
        private readonly Terrain _ground = WorldGround.Of(region, maps).Terrain;

        /// <summary>A stamp that sits inside the region, on a known map.</summary>
        private sealed record Placed(int Index, RegionStamp Stamp, BattleMap Map)
        {
            public IEnumerable<(Tile World, Tile OnMap)> Tiles()
            {
                for (var y = 0; y < Map.Height; y++)
                    for (var x = 0; x < Map.Width; x++)
                        yield return (new Tile(Stamp.At.X + x, Stamp.At.Y + y), new Tile(x, y));
            }

            /// <summary>The world tiles of the map's <c>party_spawn</c> zone, in the map's order; none if it has no such zone.</summary>
            public ImmutableArray<Tile> PartySpawn()
                => Map.Zones.TryGetValue("party_spawn", out var zone)
                    ? [.. zone.Select(t => new Tile(Stamp.At.X + t.X, Stamp.At.Y + t.Y))]
                    : [];

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

        /// <summary>
        /// Compares a stamp with its map, tile by tile: one error, with the first bad tile in full and how many more there are and
        /// where, so a painted-over patch is found in one read rather than a tile at a time.
        /// </summary>
        private void Match(Placed stamp, string at)
        {
            var bad = stamp.Tiles()
                .Select(t => (t.World, Difference: Difference(region.TileAt(t.World), stamp.Map[t.OnMap], stamp)))
                .Where(t => t.Difference is not null)
                .ToList();
            if (bad.Count == 0)
                return;

            var message = $"The stamp '{stamp.Stamp.Map}' doesn't match its map at {bad[0].World}: the region has {bad[0].Difference}.";
            if (bad.Count > 1)
                message += $" {bad.Count - 1} more {(bad.Count == 2 ? "tile differs" : "tiles differ")}, x {bad.Min(t => t.World.X)} to {bad.Max(t => t.World.X)}, "
                    + $"y {bad.Min(t => t.World.Y)} to {bad.Max(t => t.World.Y)}.";
            v.Error(file, at, message);
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
        /// Reports each marker the party can't walk to. A stamp must have every tile of its map's <c>party_spawn</c> zone reached,
        /// and reports the first that isn't, so a battle can't begin with the party cut off; a map with no such zone needs any
        /// one of its tiles reached. With nowhere to start from nothing else can be said, so a bad start is reported alone.
        /// </summary>
        private void Reachable(List<Placed> stamps)
        {
            var spawns = Pointer.Append(ptr, "spawns");
            var start = _def.Spawns.Start;
            if (!_ground.Walkable(start))
            {
                v.Error(file, Pointer.Append(spawns, "start"), $"The start spawn at {start} can't be stood on.");
                return;
            }

            var reached = WorldWalk.Reach(_ground, start, ClassDef.DefaultJump);
            void Unreached(string marker, Tile at, string pointer)
                => v.Error(file, pointer, $"{marker} at {at} can't be reached from the start.");

            if (!reached.Contains(_def.Spawns.Recovery))
                Unreached("The recovery spawn", _def.Spawns.Recovery, Pointer.Append(spawns, "recovery"));
            for (var i = 0; i < _def.Beats.Length; i++)
                if (!reached.Contains(_def.Beats[i].At))
                    Unreached($"The beat '{_def.Beats[i].Beat}'", _def.Beats[i].At, Pointer.Append(At("beats", i), "at"));
            foreach (var stamp in stamps)
            {
                var name = stamp.Stamp.Map;
                if (stamp.PartySpawn() is [_, ..] spawn)
                {
                    if (spawn.Where(t => !reached.Contains(t)).Cast<Tile?>().FirstOrDefault() is { } cutOff)
                        Unreached($"The party spawn of the stamp '{name}'", cutOff, At("stamps", stamp.Index));
                }
                else if (!stamp.Tiles().Any(t => reached.Contains(t.World)))
                    Unreached($"The stamp '{name}'", stamp.Stamp.At, At("stamps", stamp.Index));
            }
            Stranded(start, reached, stamps);
        }

        /// <summary>
        /// Reports each place the story puts the party that it can't walk on from: the recovery spawn, a beat's spot or a stamp's party
        /// spawn, on the far side of a one-way drop from a beat or stamp still to go to. Those can't be avoided, so they stop the pack. Going
        /// down a drop for good isn't wrong in itself: a pocket that holds the rest of the story is fine, and a pit with no marker in it is
        /// <c>StoryOrder</c>'s warning at export, as nothing sends the party in. A stamp with no party spawn has no one place to stop at.
        /// </summary>
        private void Stranded(Tile start, HashSet<Tile> reached, List<Placed> stamps)
        {
            var trapped = WorldWalk.Trapped(_ground, start, ClassDef.DefaultJump);
            if (trapped.Count == 0)
                return;

            List<(string Name, ImmutableArray<Tile> Tiles)> goals =
            [
                .. _def.Beats.Select(b => ($"beat '{b.Beat}'", ImmutableArray.Create(b.At))),
                .. stamps.Select(s => ($"stamp '{s.Stamp.Map}'", s.Tiles().Select(t => t.World).ToImmutableArray())),
            ];
            List<(string Name, string Pointer, ImmutableArray<Tile> Tiles)> stops =
            [
                ("recovery spawn", Pointer.Append(Pointer.Append(ptr, "spawns"), "recovery"), [_def.Spawns.Recovery]),
                .. _def.Beats.Select((b, i) => ($"beat '{b.Beat}'", Pointer.Append(At("beats", i), "at"), ImmutableArray.Create(b.At))),
                .. stamps.Select(s => ($"stamp '{s.Stamp.Map}'", At("stamps", s.Index), s.PartySpawn())),
            ];

            foreach (var (name, pointer, tiles) in stops)
            {
                if (tiles.Where(trapped.Contains).Cast<Tile?>().FirstOrDefault() is not { } stop)
                    continue;
                var onward = WorldWalk.Reach(_ground, stop, ClassDef.DefaultJump);
                if (goals.FirstOrDefault(g => g.Tiles.Any(reached.Contains) && !g.Tiles.Any(onward.Contains)) is { Name: not null } missed)
                    v.Error(file, pointer, $"The {name} at {stop} can be walked to, but the party can't get from there to the {missed.Name}.");
            }
        }

        private void Edges()
        {
            for (var i = 0; i < _def.Edges.Length; i++)
            {
                var edge = _def.Edges[i];
                if (!regions.TryGetValue(edge.Neighbour, out var neighbour))
                    continue;
                var facing = Facing(edge.Side);
                var expected = -(long)edge.Offset;
                var back = neighbour.Edges.Where(e => e.Neighbour == _def.Id && e.Side == facing).ToList();
                if (back.Count == 0)
                    v.Error(file, At("edges", i), $"This region meets '{edge.Neighbour}' on its {edge.Side} side, but '{edge.Neighbour}' has no {facing} edge back to it.");
                else if (!back.Any(e => e.Offset == expected))
                    v.Error(file, At("edges", i), $"This region meets '{edge.Neighbour}' on its {edge.Side} side at offset {edge.Offset}, "
                        + $"so '{edge.Neighbour}' needs {(facing == "east" ? "an" : "a")} {facing} edge at offset {expected}, not {back[0].Offset}.");
            }
        }
    }
}
