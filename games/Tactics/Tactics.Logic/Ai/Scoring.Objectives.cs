using System.Collections.Immutable;

namespace Tactics.Logic;

internal sealed partial class Scorer
{
    private ImmutableArray<string>? _winLeaves;
    private ImmutableArray<string>? _defeatTags;
    private ImmutableArray<string>? _protectTags;
    private ImmutableArray<Tile>? _reachTiles;

    /// <summary>The leaves of the win expression in force (spec §4.8), in order.</summary>
    public ImmutableArray<string> WinLeaves => _winLeaves ??= LeavesOf(State.Objectives.Current.Win).ToImmutableArray();

    /// <summary>The tags the win names in <c>defeat:&lt;tag&gt;</c>.</summary>
    private ImmutableArray<string> DefeatTags => _defeatTags ??= TagsOf(WinLeaves, "defeat");

    /// <summary>The tags either expression in force names in <c>protect:&lt;tag&gt;</c>.</summary>
    private ImmutableArray<string> ProtectTags
        => _protectTags ??= TagsOf(LeavesOf(State.Objectives.Current.Win).Concat(LeavesOf(State.Objectives.Current.Lose)), "protect");

    /// <summary>
    /// Whether <paramref name="target"/> is what the objectives are about for <paramref name="user"/>, so its worth
    /// counts the objective weight on top (<see cref="Worth"/>):
    /// <list type="bullet">
    /// <item>a protected unit, either side's;</item>
    /// <item>for the party's side, a hostile with a tag the win names in <c>defeat:&lt;tag&gt;</c> (the warlord);</item>
    /// <item>for the other sides, a party-side unit with a tag the objectives name in <c>protect:&lt;tag&gt;</c>.</item>
    /// </list>
    /// </summary>
    public bool IsObjective(UnitState user, UnitState target)
    {
        if (target.Protected)
            return true;
        if (user.Sheet.Side == Side.Party)
            return Hostile(user, target) && HasAny(target, DefeatTags);
        return target.Sheet.Side == Side.Party && HasAny(target, ProtectTags);
    }

    /// <summary>
    /// For a party member, the tiles of the zones the win asks the party to reach (<c>reach:&lt;zone&gt;:&lt;k&gt;</c>); none
    /// for anyone else, or when the win has no such leaf.
    /// </summary>
    public ImmutableArray<Tile> ReachTiles(UnitState user)
    {
        if (!Objectives.HasTag(user, Objectives.PartyTag))
            return [];
        if (_reachTiles is { } known)
            return known;
        var tiles = ImmutableArray.CreateBuilder<Tile>();
        foreach (var leaf in WinLeaves)
            if (leaf.Split(':') is ["reach", var zone, _] && Terrain.Map.Zones.TryGetValue(zone, out var zoneTiles))
                tiles.AddRange(zoneTiles);
        return (_reachTiles = tiles.ToImmutable()).Value;
    }

    private static bool HasAny(UnitState unit, ImmutableArray<string> tags)
    {
        foreach (var tag in tags)
            if (Objectives.HasTag(unit, tag))
                return true;
        return false;
    }

    private static ImmutableArray<string> TagsOf(IEnumerable<string> leaves, string kind)
        => [.. leaves.Select(leaf => leaf.Split(':')).Where(parts => parts.Length == 2 && parts[0] == kind).Select(parts => parts[1])];

    private static IEnumerable<string> LeavesOf(ObjectiveExpr expr)
    {
        if (expr.Leaf is { } leaf)
            yield return leaf;
        foreach (var e in expr.Any.IsDefault ? [] : expr.Any)
            foreach (var inner in LeavesOf(e))
                yield return inner;
        foreach (var e in expr.All.IsDefault ? [] : expr.All)
            foreach (var inner in LeavesOf(e))
                yield return inner;
    }
}
