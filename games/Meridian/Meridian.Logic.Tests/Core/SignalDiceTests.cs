namespace Meridian.Logic.Tests.Core;

public sealed class SignalDiceTests
{
    private static readonly MeridianRules Rules = new();

    [Fact]
    public void Faces_come_only_from_the_rules()
    {
        var (roll, _) = SignalDice.Roll(Rng.Seeded(1), 500, Rules);
        Assert.Equal(500, roll.Faces.Count);
        Assert.All(roll.Faces, f => Assert.Contains(f, Rules.DieFaces));
        Assert.Contains(0, roll.Faces);
        Assert.Contains(2, roll.Faces);
    }

    [Fact]
    public void Zero_dice_total_zero()
    {
        var rng = Rng.Seeded(1);
        var (roll, next) = SignalDice.Roll(rng, 0, Rules);
        Assert.Empty(roll.Faces);
        Assert.Equal(0, roll.Total);
        Assert.Equal(rng, next);
    }

    [Fact]
    public void Mean_of_100000_dice_is_near_five_sixths()
    {
        var (roll, _) = SignalDice.Roll(Rng.Seeded(5), 100_000, Rules);
        Assert.InRange(roll.Total / 100_000.0, 5 / 6.0 - 0.01, 5 / 6.0 + 0.01);
    }
}
