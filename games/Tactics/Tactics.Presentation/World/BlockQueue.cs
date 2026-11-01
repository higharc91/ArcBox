using System.Numerics;

namespace Tactics.Presentation.World;

/// <summary>The blocks not yet recorded, handed out one at a time, the nearest to where the party is heading first.</summary>
public sealed class BlockQueue
{
    private readonly List<BlockPlan> _waiting;

    /// <summary>A queue of <paramref name="blocks"/>.</summary>
    public BlockQueue(IEnumerable<BlockPlan> blocks) => _waiting = [.. blocks];

    /// <summary>How many are waiting.</summary>
    public int Count => _waiting.Count;

    /// <summary>Where to queue around, in tiles: where the party will be in <see cref="WorldTuning.QueueAhead"/> seconds.</summary>
    public static Vector2 Aim(Vector2 leader, Vector2 velocity) => leader + velocity * WorldTuning.QueueAhead;

    /// <summary>The block <see cref="Next"/> would take, left in the queue, so a frame can see whether it fits.</summary>
    public BlockPlan? Peek(Vector2 aim)
    {
        var nearest = Nearest(aim);
        return nearest < 0 ? null : _waiting[nearest];
    }

    /// <summary>Takes the block whose middle is nearest <paramref name="aim"/>, in tiles, or null if none is left.</summary>
    public BlockPlan? Next(Vector2 aim)
    {
        var nearest = Nearest(aim);
        if (nearest < 0)
            return null;
        var block = _waiting[nearest];
        _waiting.RemoveAt(nearest);
        return block;
    }

    private int Nearest(Vector2 aim)
    {
        var nearest = _waiting.Count - 1;
        for (var i = nearest - 1; i >= 0; i--)
            if (Vector2.DistanceSquared(_waiting[i].Middle, aim) <= Vector2.DistanceSquared(_waiting[nearest].Middle, aim))
                nearest = i;
        return nearest;
    }
}
