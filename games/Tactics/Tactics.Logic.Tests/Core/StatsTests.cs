namespace Tactics.Logic.Tests;

public sealed class StatsTests
{
    [Theory]
    [InlineData(8, -1)]
    [InlineData(9, -1)]
    [InlineData(10, 0)]
    [InlineData(11, 0)]
    [InlineData(15, 2)]
    [InlineData(1, -5)]
    [InlineData(20, 5)]
    public void Mod_floors_toward_negative_infinity(int score, int mod) => Assert.Equal(mod, Stats.Mod(score));

    [Theory]
    [InlineData(1, 2)]
    [InlineData(4, 2)]
    [InlineData(5, 3)]
    [InlineData(8, 3)]
    [InlineData(9, 4)]
    [InlineData(10, 4)]
    public void Proficiency_steps_at_five_and_nine(int level, int bonus) => Assert.Equal(bonus, Stats.Proficiency(level));
}
