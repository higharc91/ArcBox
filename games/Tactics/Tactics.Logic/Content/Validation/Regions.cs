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
            if (w > WorldRegion.MaxSide || h > WorldRegion.MaxSide)
            {
                Error(file, Pointer.Append(ptr, "size"), $"The size {w}x{h} is over the limit of {WorldRegion.MaxSide} tiles a side.");
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

            if (!def.Props.IsDefault)
            {
                for (var i = 0; i < def.Props.Length; i++)
                {
                    var group = def.Props[i];
                    var groupPtr = Pointer.Append(Pointer.Append(ptr, "props"), i);
                    if (art is not null)
                    {
                        if (!art.Props.TryGetValue(group.Id, out var propDef))
                            Error(file, Pointer.Append(groupPtr, "id"), $"Unknown prop '{group.Id}'.");
                        else if (group.Variant is not null && !propDef.Variants.Contains(group.Variant))
                            Error(file, Pointer.Append(groupPtr, "variant"), $"Unknown variant '{group.Variant}' for prop '{group.Id}'.");
                    }
                    for (var j = 0; j < group.At.Length; j++)
                    {
                        var tile = group.At[j];
                        var tilePtr = Pointer.Append(Pointer.Append(groupPtr, "at"), j);
                        if (tile.X < 0 || tile.Y < 0 || tile.X >= w || tile.Y >= h)
                            Error(file, tilePtr, $"Prop '{group.Id}' at {tile} is outside the {w}x{h} region.");
                        else if (!seen.Contains((tile.X / WorldRegion.ChunkSize, tile.Y / WorldRegion.ChunkSize)))
                            Error(file, tilePtr, $"Prop '{group.Id}' at {tile} is in a void chunk.");
                    }
                }
            }

            if (!def.Buildings.IsDefault)
            {
                for (var i = 0; i < def.Buildings.Length; i++)
                {
                    var building = def.Buildings[i];
                    var at = Pointer.Append(Pointer.Append(ptr, "buildings"), i);
                    BuildingArtDef? known = null;
                    if (art is not null)
                    {
                        if (!art.Buildings.TryGetValue(building.Id, out known))
                            Error(file, Pointer.Append(at, "id"), $"Unknown building '{building.Id}'.");
                        else if (!known.Variants.Contains(building.Variant))
                            Error(file, Pointer.Append(at, "variant"), $"Unknown variant '{building.Variant}' for building '{building.Id}'.");
                    }

                    var tiles = known?.Footprint(building.At).ToList() ?? [building.At];
                    if (tiles.Any(t => t.X < 0 || t.Y < 0 || t.X >= w || t.Y >= h))
                        Error(file, Pointer.Append(at, "at"), $"The building '{building.Id}' at {building.At} is outside the {w}x{h} region.");
                    else if (tiles.Any(t => !seen.Contains((t.X / WorldRegion.ChunkSize, t.Y / WorldRegion.ChunkSize))))
                        Error(file, Pointer.Append(at, "at"), $"The building '{building.Id}' at {building.At} is in a void chunk.");
                }
            }

            return Errors.Count == errors ? def : null;
        });

    /// <summary>
    /// Checks each region against the rest of the pack (spec §3.4): every stamp sits inside the region on a known map, tile
    /// for tile, with no map stamped twice and no two stamps overlapping; neither spawn is inside a stamp; the spawns, beat spots and stamps can be reached
    /// on foot from the start, round the stamps' blocking props; the recovery spawn can walk back to the start; and a neighbour
    /// that's loaded has an edge back on the facing side with the negated offset. A stamp on a map that failed to load skips the map
    /// checks, as an encounter does. Whether a one-way drop strands the party depends on the story's order, which is
    /// <c>StoryOrder</c>'s to check at export.
    /// </summary>
    public void CheckRegions(IEnumerable<Loaded<RegionDef>> regions, IReadOnlyDictionary<string, BattleMap> maps,
        IEnumerable<Loaded<RegionThing>>? things = null, IReadOnlySet<string>? injuries = null)
    {
        var loaded = regions.ToList();
        var byId = loaded.ToDictionary(r => r.Id, r => r.Def, StringComparer.Ordinal);
        var thingsOf = (things ?? []).ToLookup(t => t.Def.Region, StringComparer.Ordinal);
        foreach (var lost in thingsOf.Where(g => !byId.ContainsKey(g.Key)).SelectMany(g => g))
            Error(lost.File, Pointer.Append(lost.Pointer, "id"), $"The interactable '{lost.Id}' is in the region '{lost.Def.Region}', which isn't loaded.");
        foreach (var (def, _, _, file, ptr) in loaded)
        {
            try
            {
                var check = new RegionCheck(this, WorldRegion.From(def), byId, maps, file, ptr);
                var stamps = check.Run();
                if (thingsOf[def.Id].ToList() is [_, ..] mine)
                    new ThingCheck(this, WorldRegion.From(def), maps, stamps, mine, injuries ?? new HashSet<string>()).Run();
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
        /// <summary>The ground, or null for a pack loaded with no art and a region that needs some: what it blocks can't be said, so walking isn't judged.</summary>
        private readonly Terrain? _ground = v.Art is null && WorldGround.NeedsArt(region.Def) ? null : WorldGround.Of(region, maps, v.Art ?? ArtManifest.None).Terrain;

        /// <summary>A stamp that sits inside the region, on a known map.</summary>
        private sealed record Placed(int Index, RegionStamp Stamp, BattleMap Map)
        {
            public BattleStage Stage => new(Map: Stamp.Map, Origin: Stamp.At, Height: Stamp.Height, Width: Map.Width, Depth: Map.Height);

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

        /// <summary>Runs the checks, and gives the world tiles each stamp holds, for <see cref="ThingCheck"/>.</summary>
        public ImmutableArray<BattleStage> Run()
        {
            var placed = Stamps();
            Props(placed);
            Buildings(placed);
            People(placed);
            Spawns(placed);
            Reachable(placed);
            Edges();
            return [.. placed.Select(p => p.Stage)];
        }

        private void Props(List<Placed> stamps)
        {
            if (_def.Props.IsDefault)
                return;
            for (var i = 0; i < _def.Props.Length; i++)
            {
                var group = _def.Props[i];
                var groupPtr = Pointer.Append(Pointer.Append(ptr, "props"), i);
                for (var j = 0; j < group.At.Length; j++)
                {
                    var tile = group.At[j];
                    foreach (var stamp in stamps.Where(s => s.Stage.Holds(tile)))
                    {
                        var at = Pointer.Append(Pointer.Append(groupPtr, "at"), j);
                        v.Error(file, at, $"Prop '{group.Id}' at {tile} is on the stamp '{stamp.Stamp.Map}'.");
                    }
                }
            }
        }

        /// <summary>A footprint can't be on a stamp, whose tiles must match its map, or on another footprint. <see cref="Region"/> has seen that each one fits.</summary>
        private void Buildings(List<Placed> stamps)
        {
            var taken = new Dictionary<Tile, RegionBuilding>();
            for (var i = 0; i < region.Buildings.Length; i++)
            {
                var building = region.Buildings[i];
                var at = Pointer.Append(At("buildings", i), "at");
                var tiles = v.Art?.Buildings.GetValueOrDefault(building.Id)?.Footprint(building.At).ToList() ?? [building.At];
                foreach (var stamp in stamps.Where(s => tiles.Any(s.Stage.Holds)))
                    v.Error(file, at, $"The building '{building.Id}' at {building.At} is on the stamp '{stamp.Stamp.Map}'.");
                foreach (var other in tiles.Select(taken.GetValueOrDefault).OfType<RegionBuilding>().Distinct())
                    v.Error(file, at, $"The building '{building.Id}' at {building.At} overlaps the building '{other.Id}' at {other.At}.");
                foreach (var tile in tiles)
                    taken.TryAdd(tile, building);
            }
        }

        /// <summary>
        /// Everyone a region shows has to be drawable and has to stand where someone could: on the region, off the stamps, where a fight would run through them, and not on what
        /// blocks the party. A walk has to be one a person could walk: back to where it began, a step at a time. With no art, what's drawable can't be said, and with no ground, nor can what blocks.
        /// </summary>
        private void People(List<Placed> stamps)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < region.People.Length; i++)
            {
                var person = region.People[i];
                var at = At("people", i);
                if (person.Id is { } id && (string.IsNullOrWhiteSpace(id) || !ids.Add(id)))
                    v.Error(file, Pointer.Append(at, "id"), $"The person id '{id}' must be non-empty and unique in the region.");
                if (v.Art is { } art && !art.Keys.Contains($"characters.{person.Preset}"))
                    v.Error(file, Pointer.Append(at, "preset"), $"Unknown character preset '{person.Preset}'.");
                if (!RegionPerson.Faces.Contains(person.Face))
                    v.Error(file, Pointer.Append(at, "face"), $"The {person.Preset} at {person.At} faces '{person.Face}', which is none of {string.Join(", ", RegionPerson.Faces)}.");
                if (!Standing(person, person.At, stamps, Pointer.Append(at, "at")))
                    continue;

                var walk = person.Walk.IsDefault ? [] : person.Walk;
                if (walk.Length > RegionPerson.MostSteps)
                    v.Error(file, Pointer.Append(at, "walk"), $"The {person.Preset} at {person.At} walks {walk.Length} tiles, and a walk is at most {RegionPerson.MostSteps}.");
                var from = person.At;
                for (var j = 0; j < walk.Length && Standing(person, walk[j], stamps, Pointer.Append(Pointer.Append(at, "walk"), j)); j++)
                {
                    Stepped(person, from, walk[j], Pointer.Append(Pointer.Append(at, "walk"), j));
                    from = walk[j];
                }
                if (walk.Length > 0 && from == walk[^1])
                    Stepped(person, from, person.At, Pointer.Append(at, "walk"));
            }
        }

        private bool Standing(RegionPerson person, Tile tile, List<Placed> stamps, string at)
        {
            if (region.TileAt(tile) is null)
            {
                v.Error(file, at, $"The {person.Preset} at {tile} is off the region.");
                return false;
            }
            foreach (var stamp in stamps.Where(s => s.Stage.Holds(tile)))
            {
                v.Error(file, at, $"The {person.Preset} at {tile} is on the stamp '{stamp.Stamp.Map}'.");
                return false;
            }
            if (_ground is not null && !_ground.Walkable(tile))
            {
                v.Error(file, at, $"The {person.Preset} at {tile} stands on a tile that blocks.");
                return false;
            }
            return true;
        }

        private void Stepped(RegionPerson person, Tile from, Tile to, string at)
        {
            if (Math.Abs(from.X - to.X) + Math.Abs(from.Y - to.Y) != 1)
                v.Error(file, at, $"The {person.Preset}'s walk goes from {from} to {to}, which isn't a step.");
            else if (Math.Abs(region.TileAt(from)!.Height - region.TileAt(to)!.Height) > ClassDef.DefaultJump)
                v.Error(file, at, $"The {person.Preset}'s walk goes from {from} to {to}, which is too steep to walk.");
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

        /// <summary>
        /// Reports a spawn inside a stamp (overworld 5c): a new campaign would begin on a battlefield, and a beaten party would wake on
        /// the one it just lost.
        /// </summary>
        private void Spawns(List<Placed> stamps)
        {
            var spawns = Pointer.Append(ptr, "spawns");
            foreach (var (name, at, reason) in new[]
            {
                ("start", _def.Spawns.Start, "a new campaign would begin on its battlefield"),
                ("recovery", _def.Spawns.Recovery, "a beaten party would wake on its battlefield"),
            })
                foreach (var stamp in stamps.Where(s => s.Stage.Holds(at)))
                    v.Error(file, Pointer.Append(spawns, name), $"The {name} spawn at {at} is inside the stamp '{stamp.Stamp.Map}', so {reason}.");
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
            if (_ground is null)
                return;
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

            // A beaten party wakes here at any point in the story. The order of everything else is StoryOrder's to judge: the loader can't read lore.
            if (WorldWalk.Trapped(_ground, start, ClassDef.DefaultJump).Contains(_def.Spawns.Recovery))
                v.Error(file, Pointer.Append(spawns, "recovery"), $"The recovery spawn at {_def.Spawns.Recovery} can't walk back to the start.");
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
