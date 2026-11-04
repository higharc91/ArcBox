using System.Text.Json;

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
            if (!double.IsFinite(mob.Pace) || mob.Pace <= 0) Error(file, p + "/pace", "Pace must be positive seconds per step.");
            if (mob.Xp < 0 || mob.Gold < 0) Error(file, p, "Mob rewards cannot be negative.");
        }
        return def;
    });

    public void CheckMobs(IEnumerable<Loaded<RegionMob>> mobs, IEnumerable<Loaded<RegionDef>> regions, IReadOnlyDictionary<string, BattleMap> maps, IReadOnlyDictionary<string, EncounterDef> encounters)
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
                foreach (var tile in mob.Patch.IsDefault ? [] : mob.Patch)
                    if (!arena.InBounds(new Tile(tile.X - stamp.At.X, tile.Y - stamp.At.Y)))
                        Bad("patch", "Every patch tile must lie inside the arena stamp.");
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
                if (tile == region.Spawns.Start || tile == region.Spawns.Recovery || region.Beats.Any(b => b.At == tile))
                    Bad("patch", "Mob patches must avoid spawns, recovery and story beats.");
        }
    }
}
