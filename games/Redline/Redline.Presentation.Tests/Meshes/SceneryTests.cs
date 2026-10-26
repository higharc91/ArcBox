using System.Numerics;
using Redline.Logic;
using Redline.Presentation.Meshes;

namespace Redline.Presentation.Tests.Meshes;

public class SceneryTests
{
    private static readonly CircuitData Cota = CircuitData.Load("cota");
    private static readonly IReadOnlyList<TrackSample> Samples = TrackGeometry.Resample(Cota);
    private static readonly Lazy<MeshData> Terrain = new(() => TerrainBuilder.Build(Cota, Samples, extent: 300, cell: 16));
    private static readonly Lazy<TerrainSampler> Ground = new(() => TerrainSampler.FromGrid(Terrain.Value));

    [Fact]
    public void Terrain_sampler_matches_the_grid_vertices_and_triangles()
    {
        var m = Terrain.Value;
        var g = Ground.Value;
        foreach (var i in new[] { 0, 17, 5000, m.Positions.Count - 1 })
            Assert.Equal(m.Positions[i].Y, g.Height(m.Positions[i].X, m.Positions[i].Z), 3);

        // The centroid of every 97th triangle lies on that triangle's plane.
        for (var t = 0; t < m.Indices.Count; t += 3 * 97)
        {
            var c = (m.Positions[m.Indices[t]] + m.Positions[m.Indices[t + 1]] + m.Positions[m.Indices[t + 2]]) / 3;
            Assert.Equal(c.Y, g.Height(c.X, c.Z), 2);
        }
    }

    [Fact]
    public void Terrain_sampler_rejects_a_non_grid()
    {
        var m = new MeshData();
        m.Positions.AddRange([Vector3.Zero, Vector3.UnitX, Vector3.UnitZ]);
        Assert.Throws<ArgumentException>(() => TerrainSampler.FromGrid(m));
    }

    [Fact]
    public void Trees_stand_on_the_ground_clear_of_the_track_and_landmarks()
    {
        var blockedCentre = Samples[0].Position;
        var trees = TreeScatter.Place(Samples, Ground.Value, 600, seed: 7,
            minTrackDistance: 48, blocked: p => Vector2.Distance(p, blockedCentre) < 300);

        Assert.InRange(trees.Count, 550, 600);
        foreach (var t in trees)
        {
            var flat = new Vector2(t.Position.X, t.Position.Z);
            Assert.Equal(Ground.Value.Height(flat.X, flat.Y), t.Position.Y, 3);
            Assert.True(Vector2.Distance(flat, blockedCentre) >= 300);
            var nearest = Samples.Min(s => Vector2.Distance(s.Position, flat));
            Assert.True(nearest >= 48, $"tree {nearest:0.0} m from the centre line");
        }
    }

    [Fact]
    public void Trees_are_deterministic_per_seed()
    {
        Assert.Equal(TreeScatter.Place(Samples, Ground.Value, 50, seed: 3), TreeScatter.Place(Samples, Ground.Value, 50, seed: 3));
        Assert.NotEqual(TreeScatter.Place(Samples, Ground.Value, 50, seed: 3), TreeScatter.Place(Samples, Ground.Value, 50, seed: 4));
    }

    [Fact]
    public void Wheels_split_into_four_meshes_around_their_axles()
    {
        var car = CarMeshBuilder.Build(Grid2026.Load().ByCode("NOR"));

        var (tyres, tyreRest) = WheelSplit.Split(car.Tyres, car.WheelCentres);
        var (rims, rimRest) = WheelSplit.Split(car.Rims, car.WheelCentres);

        Assert.Equal(4, tyres.Count);
        Assert.Empty(tyreRest.Indices);
        Assert.Equal(car.Tyres.Indices.Count, tyres.Sum(w => w.Indices.Count));
        Assert.NotEmpty(rimRest.Indices);   // mirror glass and exhaust stay on the body
        Assert.Equal(car.Rims.Indices.Count, rims.Sum(w => w.Indices.Count) + rimRest.Indices.Count);
        for (var w = 0; w < 4; w++)
        {
            // Relative to the wheel centre: the tread reaches the tyre radius all round the axle.
            var maxRadius = tyres[w].Positions.Max(p => MathF.Sqrt(p.Y * p.Y + p.Z * p.Z));
            Assert.InRange(maxRadius, CarMeshBuilder.FrontTyreRadius - 0.02f, CarMeshBuilder.RearTyreRadius + 0.02f);
            Assert.All(tyres[w].Indices, i => Assert.InRange(i, 0, tyres[w].Positions.Count - 1));
            Assert.Equal(tyres[w].Positions.Count, tyres[w].Normals.Count);
            Assert.Equal(tyres[w].Positions.Count, tyres[w].Colors.Count);
        }
    }
}
