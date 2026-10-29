namespace Redline.Presentation.Tests;

public class CircuitDataTests
{
    [Fact]
    public void Cota_loads_170_points_and_5512m_lap()
    {
        var c = CircuitData.Load("cota");

        Assert.Equal("Circuit of the Americas", c.Name);
        Assert.Equal(170, c.Points.Count);
        Assert.InRange(c.LapLength, 5507, 5517);
        Assert.True(c.Elevation.Count >= 2);
    }

    [Fact]
    public void Unknown_circuit_throws()
    {
        Assert.Throws<KeyNotFoundException>(() => CircuitData.Load("nowhere"));
    }
}
