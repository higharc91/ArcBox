using System.Numerics;
using Redline.Presentation.Meshes;

namespace Redline.Presentation.Tests;

public class TrackPoseTests
{
    private static readonly CircuitData Cota = CircuitData.Load("cota");
    private static readonly IReadOnlyList<TrackSample> Samples = TrackGeometry.Resample(Cota);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-170, 3.6)]
    [InlineData(330, -4.4)]
    [InlineData(2600, 4.4)]
    [InlineData(5511, 0)]
    public void Frame_is_orthonormal_and_on_the_asphalt(double s, double lateral)
    {
        var pose = TrackPose.At(Samples, s, lateral);

        Assert.Equal(1, pose.Forward.Length(), 4);
        Assert.Equal(1, pose.Left.Length(), 4);
        Assert.Equal(1, pose.Up.Length(), 4);
        Assert.Equal(0, Vector3.Dot(pose.Forward, pose.Left), 4);
        Assert.Equal(0, Vector3.Dot(pose.Up, pose.Left), 4);
        Assert.True(pose.Up.Y > 0.95f);
        Assert.Equal(0, pose.Left.Y, 5);
        Assert.Equal(TrackMeshBuilder.TrackPoint(Samples, s, lateral), pose.Position);
        // (Left, Up, Forward) is right-handed like the car mesh's (+X, +Y, +Z).
        Assert.Equal(1, Vector3.Dot(Vector3.Cross(pose.Left, pose.Up), pose.Forward), 4);
    }

    [Fact]
    public void Forward_follows_travel_and_pitches_up_the_hill_to_turn_one()
    {
        var pose = TrackPose.At(Samples, 250, 0);
        var ahead = TrackMeshBuilder.TrackPoint(Samples, 252, 0);

        Assert.True(Vector3.Dot(Vector3.Normalize(ahead - pose.Position), pose.Forward) > 0.99f);
        Assert.True(pose.Forward.Y > 0.03f, $"climbing, got {pose.Forward.Y}");
    }

    [Fact]
    public void Yaw_turns_forward_towards_left()
    {
        var pose = TrackPose.At(Samples, 1000, 0);

        var quarter = pose.Yawed(Math.PI / 2);
        var round = pose.Yawed(2 * Math.PI);

        Assert.True(Vector3.Dot(quarter.Forward, pose.Left) > 0.999f);
        Assert.True(Vector3.Dot(round.Forward, pose.Forward) > 0.999f);
        Assert.Equal(pose.Up, quarter.Up);
    }
}
