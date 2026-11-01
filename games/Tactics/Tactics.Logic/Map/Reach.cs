namespace Tactics.Logic;

/// <summary>Melee reach, high ground and range-by-height rules. Integer geometry only.</summary>
public static class Reach
{
    /// <summary>
    /// True when <paramref name="attacker"/> can hit <paramref name="target"/> in melee: 4-adjacent (or exactly 2 tiles in a
    /// straight line with a reach weapon) and a height difference of at most 2 (3 with a reach weapon).
    /// </summary>
    public static bool Melee(Terrain t, Tile attacker, Tile target, bool reachWeapon)
    {
        var distance = Distance(attacker, target);
        var inLine = attacker.X == target.X || attacker.Y == target.Y;
        if (distance != 1 && !(reachWeapon && distance == 2 && inLine))
            return false;

        var difference = Math.Abs(t.HeightAt(attacker) - t.HeightAt(target));
        return difference <= (reachWeapon ? 3 : 2);
    }

    /// <summary>+2 to hit when the attacker stands at least 2 levels above the target, otherwise 0.</summary>
    public static int HighGroundBonus(Terrain t, Tile attacker, Tile target)
        => t.HeightAt(attacker) - t.HeightAt(target) >= 2 ? 2 : 0;

    /// <summary>Extra ranged reach in tiles: ⌊height advantage / 2⌋, never negative.</summary>
    public static int RangeBonus(Terrain t, Tile attacker, Tile target)
        => Math.Max(0, (t.HeightAt(attacker) - t.HeightAt(target)) / 2);

    /// <summary>Manhattan distance between two tiles.</summary>
    public static int Distance(Tile a, Tile b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

    /// <summary>The tile directly across <paramref name="target"/> from <paramref name="attacker"/>; null unless they are 4-adjacent.</summary>
    public static Tile? OppositeOf(Tile attacker, Tile target)
        => Distance(attacker, target) == 1
            ? new Tile(target.X + (target.X - attacker.X), target.Y + (target.Y - attacker.Y))
            : null;
}
