using System.Numerics;

namespace Redline.Presentation.Meshes;

/// <summary>One live oak: ground position, overall scale (crown radius in metres), yaw, and a 0..1 shade for colour variety.</summary>
public readonly record struct TreeInstance(Vector3 Position, float Scale, float Yaw, float Shade);

/// <summary>
/// Clusters of live oaks round the circuit, the way they stand in the Texas fields at COTA: most clusters within a few
/// hundred metres of the track (where the cameras see them), a few further out, none closer than
/// <c>minTrackDistance</c> to the centre line and none where <c>blocked</c> says a landmark stands. Deterministic per seed.
/// </summary>
public static class TreeScatter
{
    public static IReadOnlyList<TreeInstance> Place(
        IReadOnlyList<TrackSample> samples, TerrainSampler ground, int count, int seed = 99,
        double minTrackDistance = 48, Func<Vector2, bool>? blocked = null)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        var grid = new SampleGrid(samples);
        var rng = new Random(seed);
        var result = new List<TreeInstance>(count);
        for (var tries = 0; tries < count * 40 && result.Count < count; tries++)
        {
            // Cluster centre: out from a random point of the lap, biased towards the track.
            var q = samples[rng.Next(samples.Count)];
            var side = rng.Next(2) == 0 ? -1 : 1;
            var reach = 50 + 380 * Math.Pow(rng.NextDouble(), 2.2);
            var centre = q.Position + q.Left * (float)(side * reach)
                + q.Tangent * (float)((rng.NextDouble() - 0.5) * 60);

            var size = 3 + rng.Next(10);
            for (var j = 0; j < size && result.Count < count; j++)
            {
                var p = centre + new Vector2((float)((rng.NextDouble() - 0.5) * 46), (float)((rng.NextDouble() - 0.5) * 46));
                var scale = (float)(3.6 + rng.NextDouble() * 3.4);
                var yaw = (float)(rng.NextDouble() * Math.PI * 2);
                var shade = (float)rng.NextDouble();
                if (!ground.Contains(p) || blocked?.Invoke(p) == true)
                    continue;
                if (grid.Nearest(p.X, p.Y, minTrackDistance + scale).Index >= 0)
                    continue;
                result.Add(new TreeInstance(new Vector3(p.X, ground.Height(p.X, p.Y), p.Y), scale, yaw, shade));
            }
        }
        return result;
    }
}
