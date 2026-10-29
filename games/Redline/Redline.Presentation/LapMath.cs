namespace Redline.Presentation;

/// <summary>A car's offset from its group anchor, in metres. +Lateral is left of travel; Back is metres behind.</summary>
public readonly record struct PackSlot(double Lateral, double Back);

/// <summary>Pure lap and formation maths for the TV scene.</summary>
public static class LapMath
{
    private const double PairLateral = 3.4;
    private static readonly double[] PackLaterals = [-4.4, 0, 4.4];
    private const double PackRowBack = 9;
    private const double GridLateral = 3.6;
    private const double GridRowBack = 8;

    /// <summary>Race distance as metres along the lap: distance / finishLine × lapLength, wrapped to [0, lapLength).</summary>
    public static double MetersAlongLap(double distance, int finishLine, double lapLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(finishLine, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lapLength, 0);
        return Wrap(distance / finishLine * lapLength, lapLength);
    }

    /// <summary>Slot of car <paramref name="indexInGroup"/> in a group of cars sharing the same distance.</summary>
    public static PackSlot Pack(int indexInGroup, int groupSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(groupSize, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(indexInGroup);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(indexInGroup, groupSize);
        return groupSize switch
        {
            1 => new PackSlot(0, 0),
            2 => new PackSlot(indexInGroup == 0 ? -PairLateral : PairLateral, 0),
            _ => new PackSlot(PackLaterals[indexInGroup % 3], PackRowBack * (indexInGroup / 3)),
        };
    }

    /// <summary>Starting-grid slot (0-based position): two columns, one row every 8 m.</summary>
    public static PackSlot Grid(int gridPosition)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(gridPosition);
        return new PackSlot(gridPosition % 2 == 0 ? -GridLateral : GridLateral, GridRowBack * gridPosition);
    }

    /// <summary>Track height at <paramref name="s"/> metres along the lap, cosine-interpolated between keyframes.</summary>
    public static double Elevation(CircuitData c, double s)
    {
        ArgumentNullException.ThrowIfNull(c);
        var keys = c.Elevation;
        s = Wrap(s, c.LapLength);

        var i = 0;
        while (i < keys.Count - 2 && keys[i + 1].S < s)
            i++;

        var (s0, h0) = keys[i];
        var (s1, h1) = keys[i + 1];
        var t = Math.Clamp((s - s0) / (s1 - s0), 0, 1);
        return h0 + (h1 - h0) * (0.5 - 0.5 * Math.Cos(Math.PI * t));
    }

    private static double Wrap(double value, double length)
    {
        var r = value % length;
        if (r < 0) r += length;
        // r + length can round to exactly length for tiny negative values.
        return r >= length ? 0 : r;
    }
}
