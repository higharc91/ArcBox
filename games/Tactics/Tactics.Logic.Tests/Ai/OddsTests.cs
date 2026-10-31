namespace Tactics.Logic.Tests;

/// <summary>The AI's exact d20 maths (spec §5.2): hit, crit and save chances, and expected damage.</summary>
public sealed class OddsTests
{
    private const double Tolerance = 1e-12;

    [Fact]
    public void Hit_plus5_vs_ac15_is_55_percent()
        => Assert.Equal(0.55, Odds.Hit(5, 15, Edge.None), Tolerance);   // 10..20 hit: 11 of 20

    [Fact]
    public void Advantage_raises_to_79_75_percent()
        => Assert.Equal(0.7975, Odds.Hit(5, 15, Edge.Advantage), Tolerance);   // 1 − 0.45²

    [Fact]
    public void Disadvantage_lowers_to_30_25_percent()
        => Assert.Equal(0.3025, Odds.Hit(5, 15, Edge.Disadvantage), Tolerance);   // 0.55²

    [Fact]
    public void Nat20_always_hits_even_vs_ac30()
        => Assert.Equal(0.05, Odds.Hit(5, 30, Edge.None), Tolerance);

    [Fact]
    public void Nat1_always_misses_even_vs_ac1()
        => Assert.Equal(0.95, Odds.Hit(10, 1, Edge.None), Tolerance);

    [Fact]
    public void Crit_with_advantage_is_9_75_percent()
    {
        Assert.Equal(0.0975, Odds.Crit(Edge.Advantage), Tolerance);
        Assert.Equal(0.05, Odds.Crit(Edge.None), Tolerance);
        Assert.Equal(0.0025, Odds.Crit(Edge.Disadvantage), Tolerance);
    }

    [Fact]
    public void Save_fail_matches_formula()
    {
        // A save succeeds on d20 + bonus ≥ DC, with no natural 1 or 20 rule: P(success) = clamp((21 − DC + bonus) / 20, 0, 1).
        for (var bonus = -2; bonus <= 8; bonus++)
        for (var dc = 5; dc <= 25; dc++)
        {
            var success = Math.Clamp((21 - dc + bonus) / 20.0, 0, 1);
            Assert.Equal(1 - success, Odds.SaveFail(bonus, dc, Edge.None), Tolerance);
            Assert.Equal((1 - success) * (1 - success), Odds.SaveFail(bonus, dc, Edge.Advantage), Tolerance);
            Assert.Equal(1 - success * success, Odds.SaveFail(bonus, dc, Edge.Disadvantage), Tolerance);
        }
        Assert.Equal(0.5, Odds.SaveFail(3, 14, Edge.None), Tolerance);   // 11..20 saves
    }

    [Fact]
    public void Expected_damage_adds_the_crit_dice_once_more()
    {
        // 1d8+3 hits for 7.5 on average; a crit adds another 4.5 of dice.
        Assert.Equal(0.55 * 7.5 + 0.05 * 4.5, Odds.ExpectedDamage(Dice.Parse("1d8+3"), 0.55, 0.05), Tolerance);
        Assert.Equal(0, Odds.ExpectedDamage(Dice.Parse("1d4-5"), 1, 0), Tolerance);   // damage is never negative
        Assert.Equal(0.6 * 0.75, Odds.ExpectedDamage(Dice.Parse("1d4-2"), 0.6, 0), Tolerance);   // 0, 0, 1, 2: rolls below 0 count as 0
        Assert.Equal(0.5 * 0.75 + 0.1 * 3, Odds.ExpectedDamage(Dice.Parse("1d4-2"), 0.6, 0.1), Tolerance);   // a crit rolls 2d4-2
    }
}
