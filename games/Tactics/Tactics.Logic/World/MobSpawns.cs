namespace Tactics.Logic;

/// <summary>Authored dry entry strips, selected by distance to the party's world contact translated into arena tiles.</summary>
public static class MobSpawns
{
    public const string Prefix = "party_spawn_";
    public static string For(BattleMap map, Tile contact)
        => map.Zones.Where(z => z.Key.StartsWith(Prefix, StringComparison.Ordinal) && !z.Value.IsDefaultOrEmpty)
            .OrderBy(z => z.Value.Min(t => Math.Abs(t.X - contact.X) + Math.Abs(t.Y - contact.Y)))
            .ThenBy(z => z.Key, StringComparer.Ordinal).Select(z => z.Key).FirstOrDefault() ?? "party_spawn";
}
