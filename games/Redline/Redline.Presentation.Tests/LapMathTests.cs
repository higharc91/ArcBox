namespace Redline.Presentation.Tests;

public class LapMathTests
{
    private const double L = 5512;

    [Fact]
    public void Distance_0_and_the_finish_line_are_the_same_point()
    {
        var f = new Redline.Logic.RedlineRules().FinishLine;
        Assert.Equal(0, LapMath.MetersAlongLap(f, f, L));
        Assert.Equal(0, LapMath.MetersAlongLap(0, f, L));
        Assert.Equal(L / 2, LapMath.MetersAlongLap(f / 2.0, f, L));
    }

    [Theory]
    [InlineData(1, 0, 0.0, 0.0)]
    [InlineData(2, 0, -3.4, 0.0)]
    [InlineData(2, 1, 3.4, 0.0)]
    [InlineData(3, 0, -4.4, 0.0)]
    [InlineData(3, 1, 0.0, 0.0)]
    [InlineData(3, 2, 4.4, 0.0)]
    [InlineData(6, 3, -4.4, 9.0)]
    [InlineData(6, 4, 0.0, 9.0)]
    [InlineData(6, 5, 4.4, 9.0)]
    public void Pack_slots(int groupSize, int index, double lateral, double back)
    {
        var slot = LapMath.Pack(index, groupSize);

        Assert.Equal(lateral, slot.Lateral, 9);
        Assert.Equal(back, slot.Back, 9);
    }

    [Fact]
    public void Grid_two_columns_8m_apart()
    {
        Assert.Equal(new PackSlot(-3.6, 0), LapMath.Grid(0));
        Assert.Equal(new PackSlot(3.6, 8), LapMath.Grid(1));
        Assert.Equal(new PackSlot(3.6, 168), LapMath.Grid(21));
    }

    [Fact]
    public void Elevation_turn1_apex_is_41m()
    {
        var c = CircuitData.Load("cota");

        Assert.Equal(41, LapMath.Elevation(c, 327), 9);
        Assert.Equal(0, LapMath.Elevation(c, 3459), 9);
    }

    [Fact]
    public void Elevation_wraps_around_the_lap()
    {
        var c = CircuitData.Load("cota");

        Assert.Equal(LapMath.Elevation(c, 327), LapMath.Elevation(c, 327 + c.LapLength), 6);
        Assert.Equal(LapMath.Elevation(c, 327), LapMath.Elevation(c, 327 - c.LapLength), 6);
    }
}
