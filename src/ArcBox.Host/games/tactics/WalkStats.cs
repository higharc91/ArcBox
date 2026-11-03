using System.Globalization;
using System.Numerics;
using Godot;
using Vector2 = System.Numerics.Vector2;

namespace ArcBox.Host.Games.Tactics;

/// <summary>
/// Dev only: <c>--walk-log</c>'s <c>WALKSTATS</c> lines. They say whether a walk is smooth, so that it doesn't have to be judged from screenshots: how long the
/// frames were, and how the leader moved on the screen frame by frame, as drawn (<c>drawn</c>) and as it would have been with its feet left unsnapped (<c>raw</c>).
/// A <c>flip</c> is a step the opposite way to the one before: that's the shake.
/// </summary>
internal sealed class WalkStats
{
    private const int FramesALine = 288;     // two seconds at 144

    private readonly List<(double Ms, Vector2 Raw, Vector2 Drawn, Vector2 Origin)> _frames = [];

    /// <summary>Notes a frame of walking, and prints a line every so often.</summary>
    public void Add(double seconds, Vector2 raw, Vector2 drawn, Vector2 origin)
    {
        _frames.Add((seconds * 1000, raw, drawn, origin));
        if (_frames.Count >= FramesALine)
        {
            Print();
        }
    }

    /// <summary>Prints what's noted so far: the party has stopped.</summary>
    public void Print()
    {
        if (_frames.Count > 1)
        {
            var ms = _frames.Select(f => f.Ms).Order().ToArray();
            var raw = _frames.Select(f => f.Raw).ToArray();
            var drawn = _frames.Select(f => f.Drawn).ToArray();
            var origin = _frames.Select(f => f.Origin).ToArray();
            GD.Print(FormattableString.Invariant($"WALKSTATS frames {ms.Length} ms median {At(ms, 0.5):0.00} p99 {At(ms, 0.99):0.00} worst {ms[^1]:0.00} over16 {ms.Count(m => m > 16)}; screen px a frame: raw {Steps(raw)}; drawn {Steps(drawn)}; ground {Steps(origin)}"));
        }
        _frames.Clear();
    }

    private static double At(double[] sorted, double share) => sorted[(int)Math.Min(sorted.Length - 1, sorted.Length * share)];

    private static string Steps(Vector2[] points)
    {
        var steps = points.Zip(points.Skip(1), (a, b) => (double)Vector2.Distance(a, b)).Order().ToArray();
        return string.Create(CultureInfo.InvariantCulture, $"median {At(steps, 0.5):0.0} worst {steps[^1]:0.0} flips {Flips(points.Select(p => p.X))}+{Flips(points.Select(p => p.Y))}");
    }

    private static int Flips(IEnumerable<float> values)
    {
        var (flips, last) = (0, 0);
        float? before = null;
        foreach (var value in values)
        {
            if (before is { } was && value != was)
            {
                var way = Math.Sign(value - was);
                flips += last != 0 && way != last ? 1 : 0;
                last = way;
            }
            before = value;
        }
        return flips;
    }
}
