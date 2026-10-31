using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>
/// The engine reached a state its rules forbid (for example hooks triggering each other deeper than
/// <see cref="Hooks.MaxDepth"/>, or two units on one tile). This is a bug in the engine or the content, never a player's
/// mistake; the adapter keeps the state from before the command (spec §8).
/// </summary>
public sealed class RulesInvariantException(string message) : Exception(message);

/// <summary>Checks that a state obeys the rules' invariants. Cheap enough to run after every command.</summary>
public static class Invariants
{
    /// <summary>
    /// Whether <see cref="Battle.Start"/> and <see cref="Battle.Apply"/> check every state they produce. On in Debug builds;
    /// tests turn it on in any build.
    /// </summary>
    public static bool Enabled { get; set; }
#if DEBUG
        = true;
#endif

    /// <summary>
    /// Throws <see cref="RulesInvariantException"/>, naming the first broken rule, unless:
    /// <list type="bullet">
    /// <item>every Up unit stands on a walkable tile on the map, or in deep water (where it was revived; it can swim out),
    /// and no two Up or Downed units share a tile (except a unit part-way through a walk, which may pass through an ally
    /// while a reaction is pending);</item>
    /// <item>every unit's HP is between 0 and its max, and nothing it has is negative (temporary HP, resources, per-battle
    /// uses, Move, extra actions, countdown);</item>
    /// <item><see cref="BattleState.Order"/> names only units in the battle's roster, each at most once, and holds every
    /// Up or Downed unit except those <see cref="BattleState.Joining"/> it next round, which are in the battle and not in
    /// it;</item>
    /// <item>while the battle goes on, <see cref="BattleState.TurnIndex"/> is a valid index into the order.</item>
    /// </list>
    /// </summary>
    public static void Check(BattleState s, RulesPack rules)
    {
        var map = rules.MapFor(s.MapId) ?? throw new RulesInvariantException($"The rules pack has no map '{s.MapId}'.");
        Check(s, rules, new Terrain(map, s.HeightDelta, [], s.Sunk));
    }

    /// <summary><see cref="Check(BattleState, RulesPack)"/> with the map as it is now already built.</summary>
    internal static void Check(BattleState s, RulesPack rules, Terrain terrain)
    {
        var walking = s.Queue.OfType<MoveStep>().Select(m => m.Unit).ToImmutableSortedSet();
        var holders = new Dictionary<Tile, UnitId>();   // lookups only
        foreach (var u in s.Units.Values)
        {
            CheckNumbers(u);
            if (!DownedRules.InBattle(u))
                continue;
            if (u.Life == LifeState.Up && !terrain.Walkable(u.Pos) && !InDeepWater(terrain, u.Pos))
                Fail($"{u.Id} stands on {u.Pos}, which isn't a walkable tile on the map.");
            if (holders.TryGetValue(u.Pos, out var other) && !walking.Contains(u.Id) && !walking.Contains(other))
                Fail($"{other} and {u.Id} share the tile {u.Pos}.");
            holders.TryAdd(u.Pos, u.Id);
        }

        var seen = new HashSet<UnitId>();   // lookups only
        foreach (var id in s.Order)
        {
            if (!s.Units.ContainsKey(id))
                Fail($"The turn order names {id}, which isn't in the battle.");
            if (!seen.Add(id))
                Fail($"{id} is in the turn order twice.");
        }
        foreach (var id in s.Joining)
        {
            if (!s.Units.TryGetValue(id, out var joining) || !DownedRules.InBattle(joining))
                Fail($"{id} is joining the turn order but isn't in the battle.");
            if (seen.Contains(id))
                Fail($"{id} is joining the turn order but already in it.");
        }
        foreach (var u in s.Units.Values)
            if (DownedRules.InBattle(u) && !seen.Contains(u.Id) && !s.Joining.Contains(u.Id))
                Fail($"{u.Id} is {u.Life} but missing from the turn order.");

        if (s.Outcome is null && (s.TurnIndex < 0 || s.TurnIndex >= s.Order.Length))
            Fail($"TurnIndex {s.TurnIndex} is outside the turn order of {s.Order.Length}.");
    }

    /// <summary>A deep tile that would be walkable but for its depth (not impassable, no blocking prop).</summary>
    private static bool InDeepWater(Terrain terrain, Tile tile)
        => terrain.Deep(tile) && (terrain.Map[tile].Flags & TileFlags.Impassable) == 0
           && terrain.PropAt(tile) is not { Blocks: true };

    private static void CheckNumbers(UnitState u)
    {
        if (u.Hp < 0 || u.Hp > u.Sheet.MaxHp)
            Fail($"{u.Id} has {u.Hp} HP, outside 0–{u.Sheet.MaxHp}.");
        foreach (var (name, value) in u.Resources)
            if (value < 0)
                Fail($"{u.Id} has {value} {name}.");
        foreach (var (name, value) in u.UsesLeft)
            if (value < 0)
                Fail($"{u.Id} has {value} uses of {name} left.");
        if (u.TempHp < 0 || u.Economy.MoveLeft < 0 || u.Economy.ExtraActions < 0 || u.Countdown < 0)
            Fail($"{u.Id} has a negative temporary HP, Move, extra actions or countdown.");
    }

    private static void Fail(string message) => throw new RulesInvariantException(message);
}
