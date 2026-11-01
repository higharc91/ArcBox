namespace Tactics.Logic.Tests;

public sealed class RollsTests
{
    private const string Field = """
        0. 0. 0.
        """;

    /// <summary>A writer whose RNG starts at <paramref name="seed"/>.</summary>
    private static BattleWriter Writer(ulong seed)
    {
        var battle = TestBattle.On(Field).Party("kara", TestSheets.Fighter, new Tile(0, 0));
        var (state, _) = battle.Start();
        return new BattleWriter(state with { Rng = Rng.Seeded(seed) }, battle.Rules);
    }

    [Fact]
    public void Advantage_keeps_higher()
    {
        Assert.Equal([13, 17], TestAbilities.Next(Rng.Seeded(6), 20, 20));     // seed 6 rolls 13 then 17

        var adv = Rolls.D20(Writer(6), modifier: 2, vs: 18, Edge.Advantage, critOn20: true, autoMissOn1: true);
        Assert.Equal([13, 17], adv.D20s);
        Assert.Equal(17, adv.Kept);
        Assert.Equal(2, adv.Modifier);
        Assert.Equal(19, adv.Total);
        Assert.Equal(18, adv.Vs);
        Assert.True(adv.Success);

        var dis = Rolls.D20(Writer(6), modifier: 2, vs: 18, Edge.Disadvantage, critOn20: true, autoMissOn1: true);
        Assert.Equal([13, 17], dis.D20s);
        Assert.Equal(13, dis.Kept);
        Assert.Equal(15, dis.Total);
        Assert.False(dis.Success);
    }

    [Fact]
    public void Adv_and_disadv_cancel()
    {
        Assert.Equal(Edge.None, Rolls.Combine(0, 0));
        Assert.Equal(Edge.Advantage, Rolls.Combine(2, 0));
        Assert.Equal(Edge.Disadvantage, Rolls.Combine(0, 3));
        Assert.Equal(Edge.None, Rolls.Combine(1, 1));
        Assert.Equal(Edge.None, Rolls.Combine(3, 1));

        var none = Rolls.D20(Writer(6), 0, 10, Rolls.Combine(1, 2), critOn20: true, autoMissOn1: true);
        Assert.Equal([13], none.D20s);
        Assert.Equal(13, none.Kept);
    }

    [Fact]
    public void Nat20_crits_and_doubles_dice_not_bonus()
    {
        var w = Writer(39);                                                         // seed 39 rolls a 20 first
        var attack = Rolls.D20(w, modifier: 0, vs: 30, Edge.None, critOn20: true, autoMissOn1: true);
        Assert.Equal(20, attack.Kept);
        Assert.True(attack.Crit);
        Assert.True(attack.Success);                                                // a natural 20 hits whatever the AC
        Assert.False(attack.Fumble);

        var dice = TestAbilities.Next(w.State.Rng, 6, 6, 6, 6);
        Assert.Equal(dice.Sum() + 3, Rolls.Damage(w, Dice.Parse("2d6+3"), crit: true));   // 4d6, +3 once

        var w2 = Writer(39);
        Rolls.D20(w2, 0, 30, Edge.None, critOn20: true, autoMissOn1: true);
        Assert.Equal(dice[0] + dice[1] + 3, Rolls.Damage(w2, Dice.Parse("2d6+3"), crit: false));

        var save = Rolls.D20(Writer(39), modifier: 0, vs: 30, Edge.None, critOn20: false, autoMissOn1: false);
        Assert.False(save.Crit);
        Assert.False(save.Success);                                                 // saves don't crit
    }

    [Fact]
    public void Nat1_misses_even_if_total_beats_AC()
    {
        var w = Writer(4);                                                          // seed 4 rolls a 1 first
        var attack = Rolls.D20(w, modifier: 30, vs: 10, Edge.None, critOn20: true, autoMissOn1: true);
        Assert.Equal(1, attack.Kept);
        Assert.Equal(31, attack.Total);
        Assert.False(attack.Success);
        Assert.True(attack.Fumble);
        Assert.False(attack.Crit);

        var save = Rolls.D20(Writer(4), modifier: 30, vs: 10, Edge.None, critOn20: false, autoMissOn1: false);
        Assert.True(save.Success);
        Assert.False(save.Fumble);
    }

    [Fact]
    public void Contest_tie_defender_wins()
    {
        // Seed 6: the attacker rolls 13, the defender 17. 13 + 4 = 17 + 0: a tie.
        var (attacker, defender) = Rolls.Contest(Writer(6), attackerModifier: 4, Edge.None, defenderModifier: 0, Edge.None);
        Assert.Equal(17, attacker.Total);
        Assert.Equal(17, defender.Total);
        Assert.False(attacker.Success);
        Assert.True(defender.Success);
        Assert.Equal(17, attacker.Vs);
        Assert.Equal(17, defender.Vs);

        var (winner, loser) = Rolls.Contest(Writer(6), attackerModifier: 5, Edge.None, defenderModifier: 0, Edge.None);
        Assert.True(winner.Success);
        Assert.False(loser.Success);
    }

    [Fact]
    public void Damage_never_negative()
    {
        var w = Writer(1);
        Assert.Equal(0, Rolls.Damage(w, Dice.Parse("1d4-10"), crit: false));
        Assert.Equal(5, Rolls.Damage(w, Dice.Parse("5"), crit: true));
    }
}
