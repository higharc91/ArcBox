namespace Tactics.Logic;

/// <summary>One deterministic roaming step. The caller owns the step counter and pauses it with the world.</summary>
public static class MobMotion
{
    /// <summary>Choose among staying still and legal cardinal neighbours, independently of the leader's direction.</summary>
    public static Tile Step(string region, string group, int epoch, long step, Tile at,
        IReadOnlySet<Tile> patch, Func<Tile, bool> walkable, Tile leader, Tile? entering = null, bool paused = false)
    {
        if (paused) return at;
        Tile[] candidates = [at, new(at.X, at.Y - 1), new(at.X + 1, at.Y), new(at.X, at.Y + 1), new(at.X - 1, at.Y)];
        var legal = candidates.Where(t => patch.Contains(t) && walkable(t) && t != leader && t != entering).ToArray();
        if (legal.Length == 0) return at;
        // Stable across processes: String.GetHashCode and Random's implementation are not part of the save contract.
        ulong hash = 14695981039346656037;
        void Mix(ulong value) { hash = unchecked((hash ^ value) * 1099511628211); }
        foreach (var ch in region) Mix(ch);
        Mix(0);
        foreach (var ch in group) Mix(ch);
        Mix(unchecked((ulong)epoch));
        Mix(unchecked((ulong)step));
        Mix(unchecked((ulong)at.X));
        Mix(unchecked((ulong)at.Y));
        return legal[(int)(hash % (ulong)legal.Length)];
    }

    /// <summary>Only the leader's tile is passed; followers cannot trigger an encounter.</summary>
    public static bool Contact(Tile leader, Tile mob)
        => Math.Abs((long)leader.X - mob.X) + Math.Abs((long)leader.Y - mob.Y) <= 1;
}
