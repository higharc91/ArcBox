using System.Text.Json;
using System.Collections.Immutable;

namespace Tactics.Logic;

internal sealed partial class Validation
{
    public MobsFile? MobsFile(JsonElement e, string file, string ptr) => Guarded(file, ptr, () =>
    {
        var def = Deserialize<MobsFile>(e, file, ptr);
        if (def is null) return null;
        if (string.IsNullOrWhiteSpace(def.Region)) Error(file, "/region", "A mob file needs its region.");
        if (def.Mobs.IsDefault) { Error(file, "/mobs", "A mob file needs a mobs array."); return null; }
        for (var i = 0; i < def.Mobs.Length; i++)
        {
            var mob = def.Mobs[i]; var p = $"/mobs/{i}";
            if (string.IsNullOrWhiteSpace(mob.Id)) Error(file, p + "/id", "A mob needs an id.");
            if (mob.Patch.IsDefaultOrEmpty || mob.Patch.Distinct().Count() != mob.Patch.Length) Error(file, p + "/patch", "A patch needs distinct tiles.");
            if (mob.Show.IsDefaultOrEmpty || mob.Show.Length > 3) Error(file, p + "/show", "Show one to three creature sprites.");
            else for (var j = 0; j < mob.Show.Length; j++) Ref(RefKind.Art, mob.Show[j], file, p + $"/show/{j}");
            if (!mob.Patch.IsDefault && !mob.Show.IsDefault && mob.Patch.Length < mob.Show.Length) Error(file, p + "/patch", "A patch needs room for every shown creature.");
            if (!double.IsFinite(mob.Pace) || mob.Pace <= 0) Error(file, p + "/pace", "Pace must be positive seconds per step.");
            if (mob.Xp < 0 || mob.Gold < 0) Error(file, p, "Mob rewards cannot be negative.");
        }
        return def;
    });

    public void CheckMobs(IEnumerable<Loaded<RegionMob>> mobs, IEnumerable<Loaded<RegionDef>> regions, IReadOnlyDictionary<string, BattleMap> maps, IReadOnlyDictionary<string, EncounterDef> encounters, IEnumerable<Loaded<RegionThing>> things)
    {
        var known = regions.ToDictionary(r => r.Id, r => r.Def, StringComparer.Ordinal);
        foreach (var loaded in mobs)
        {
            var mob = loaded.Def.Def;
            void Bad(string field, string message) => Error(loaded.File, loaded.Pointer + "/" + field, message);
            if (!known.TryGetValue(loaded.Def.Region, out var region)) { Bad("region", "Unknown mob region."); continue; }
            if (!encounters.TryGetValue(mob.Encounter, out var encounter)) Bad("encounter", "Unknown mob encounter.");
            else if (encounter.Map != mob.Arena) Bad("arena", "The mob encounter must fight on its arena map.");
            var stamp = region.Stamps.FirstOrDefault(s => s.Map == mob.Arena);
            if (stamp is null || !maps.TryGetValue(mob.Arena, out var arena)) Bad("arena", "A mob arena must name a stamp in its region.");
            else
            {
                if (arena.Zones.TryGetValue("party_spawn", out var spawn) && !spawn.IsDefaultOrEmpty)
                {
                    var terrain = new Terrain(arena, ImmutableSortedDictionary<Tile, int>.Empty, []);
                    var reached = WorldWalk.Reach(terrain, spawn[0], ClassDef.DefaultJump);
                    if (!reached.Any(t => t.X == 0 || t.Y == 0 || t.X == arena.Width - 1 || t.Y == arena.Height - 1))
                        Bad("arena", "A mob arena needs a walkable party spawn edge.");
                }
                foreach (var tile in mob.Patch.IsDefault ? [] : mob.Patch)
                {
                    var local = new Tile(tile.X - stamp.At.X, tile.Y - stamp.At.Y);
                    if (!arena.InBounds(local)) { Bad("patch", "Every patch tile must lie inside the arena stamp."); continue; }
                    if ((arena[local].Flags & (TileFlags.Impassable | TileFlags.Deep)) != 0 || arena.Props.Any(p => p.At == local && p.Blocks))
                        Bad("patch", "Mob patches must be walkable and clear of blocking props.");
                    if (arena.Materials[arena[local].Material].StartsWith("path/", StringComparison.Ordinal)
                        || arena.Materials[arena[local].Material].StartsWith("cobble/", StringComparison.Ordinal)
                        || arena.Materials[arena[local].Material].StartsWith("planks/", StringComparison.Ordinal)
                        || arena.Materials[arena[local].Material].StartsWith("flagstone/", StringComparison.Ordinal))
                        Bad("patch", "Mob patches must not touch roads.");
                    if ((!region.People.IsDefault && region.People.Any(p => p.At == tile || (!p.Walk.IsDefault && p.Walk.Contains(tile))))
                        || things.Any(t => t.Def.Region == loaded.Def.Region && t.Def.Def.At == tile))
                        Bad("patch", "Mob patches must avoid people and interactables.");
                }
            }
            if (!mob.Patch.IsDefaultOrEmpty)
            {
                var remaining = mob.Patch.ToHashSet();
                var queue = new Queue<Tile>(); queue.Enqueue(mob.Patch[0]); remaining.Remove(mob.Patch[0]);
                while (queue.TryDequeue(out var tile))
                    foreach (var next in new[] { new Tile(tile.X + 1, tile.Y), new Tile(tile.X - 1, tile.Y), new Tile(tile.X, tile.Y + 1), new Tile(tile.X, tile.Y - 1) })
                        if (remaining.Remove(next)) queue.Enqueue(next);
                if (remaining.Count != 0) Bad("patch", "A mob patch must be cardinally connected.");
            }
            foreach (var tile in mob.Patch.IsDefault ? [] : mob.Patch)
                if (tile == region.Spawns.Start || tile == region.Spawns.Recovery || (region.Spawns.Named?.Values.Contains(tile) ?? false) || region.Beats.Any(b => b.At == tile))
                    Bad("patch", "Mob patches must avoid spawns, recovery and story beats.");
        }
        if (Art is null || Errors.Count != 0) return;
        foreach (var group in mobs.GroupBy(m => m.Def.Region, StringComparer.Ordinal))
        {
            var region = known[group.Key];
            var ground = WorldGround.Of(WorldRegion.From(region), maps, Art);
            var danger = group.SelectMany(m => m.Def.Def.Patch).SelectMany(t => new[] { t, new Tile(t.X + 1, t.Y), new Tile(t.X - 1, t.Y), new Tile(t.X, t.Y + 1), new Tile(t.X, t.Y - 1) }).Where(ground.Map.InBounds).ToHashSet();
            var before = WorldWalk.Reach(ground.Terrain, region.Spawns.Start, ClassDef.DefaultJump);
            var obstructed = ground.Map.WithProps([.. ground.Map.Props, .. danger.Select(t => new PropPlacement("mob-danger", t, true, Cover.Total))]);
            var safe = WorldWalk.Reach(new Terrain(obstructed, ImmutableSortedDictionary<Tile, int>.Empty, []), region.Spawns.Start, ClassDef.DefaultJump);
            var cutOff = before.Where(t => !danger.Contains(t) && !safe.Contains(t)).Order().ToArray();
            if (cutOff.Length != 0)
            {
                var source = group.First();
                Error(source.File, source.Pointer + "/patch", $"Mob danger blocks the only route to {cutOff.Length} previously reachable tiles, including {cutOff[0]}.");
            }
            foreach (var target in things.Where(t => t.Def.Region == group.Key))
                if (before.Any(t => Interactables.InReach(ground.Terrain, t, target.Def.Def)) && !safe.Any(t => Interactables.InReach(ground.Terrain, t, target.Def.Def)))
                    Error(target.File, target.Pointer + "/at", "Mob danger blocks access to this interactable.");
        }
    }
}
