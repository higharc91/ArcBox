namespace Tactics.Logic.Tests;

public sealed class FlankingCoverTests
{
    private const string Field = """
        0. 0. 0. 0. 0. 0. 0. 0.
        0. 0. 0. 0. 0. 0. 0. 0.
        0. 0. 0. 0. 0. 0. 0. 0.
        """;

    private static readonly AbilityId Attack = new("common.attack");

    private static UnitId U(string id) => new(id);

    private static Tile T(int x, int y) => new(x, y);

    /// <summary>A fighter who acts first (initiative 19): STR +3, +2 proficiency.</summary>
    private static readonly UnitSheet Fighter = TestSheets.Fighter with { InitiativeBonus = 10 };

    /// <summary>The same fighter with a longbow: DEX +1, +2 proficiency.</summary>
    private static readonly UnitSheet Archer = Fighter with { Weapon = TestConditions.Longbow };

    /// <summary>A goblin (AC 15) with 30 HP.</summary>
    private static readonly UnitSheet Goblin = TestSheets.Goblin with { MaxHp = 30 };

    /// <summary>kara attacks gob once, with an ally (the rogue) at <paramref name="ally"/> if given; returns the attack roll.</summary>
    private static RollResult Attacks(string map, UnitSheet attacker, Tile at, Tile gob, Tile? ally = null,
        Func<BattleState, BattleState>? change = null)
    {
        var battle = TestBattle.On(map).Party("kara", attacker, at).Enemy("gob", Goblin, gob).WithConditions([.. TestConditions.All]);
        if (ally is { } tile)
            battle.Party("ari", TestSheets.Rogue, tile);
        var (state, _) = battle.Start(seed: 11);
        Assert.Equal(U("kara"), state.Active);
        var (_, events) = battle.Apply(change?.Invoke(state) ?? state, new Use(U("kara"), Attack, new Target(Unit: U("gob"))));
        return TestConditions.FirstAttack(events);
    }

    [Fact]
    public void Ally_opposite_gives_melee_advantage()
    {
        var flanked = Attacks(Field, Fighter, T(2, 1), T(3, 1), ally: T(4, 1));
        Assert.Equal(2, flanked.D20s.Length);
        Assert.Equal(flanked.D20s.Max(), flanked.Kept);

        Assert.Single(Attacks(Field, Fighter, T(2, 1), T(3, 1), ally: T(3, 0)).D20s);     // beside the target, not opposite
        Assert.Single(Attacks(Field, Fighter, T(2, 1), T(3, 1)).D20s);                   // no ally
        Assert.Single(Attacks(Field, Archer, T(0, 1), T(3, 1), ally: T(4, 1)).D20s);      // ranged attacks don't flank
    }

    [Fact]
    public void Downed_ally_does_not_flank()
    {
        BattleState Downed(BattleState s) => s.Change("ari", a => a with { Life = LifeState.Downed, Hp = 0, Countdown = 3 });
        BattleState Stunned(BattleState s) => s.Give("ari", TestConditions.Lasting(TestConditions.Stunned));

        Assert.Single(Attacks(Field, Fighter, T(2, 1), T(3, 1), ally: T(4, 1), change: Downed).D20s);
        Assert.Single(Attacks(Field, Fighter, T(2, 1), T(3, 1), ally: T(4, 1), change: Stunned).D20s);   // incapacitated
    }

    [Fact]
    public void Half_cover_adds_2_ac_to_ranged_attack()
    {
        // A crate (half cover) stands between the archer and the goblin, right in front of the goblin.
        const string crateMap = """
            0. 0. 0. 0. 0. 0. 0. 0.
            0. 0. 0. 0. 0c 0. 0. 0.
            0. 0. 0. 0. 0. 0. 0. 0.
            """;

        Assert.Equal(15, Attacks(Field, Archer, T(0, 1), T(5, 1)).Vs);                   // in the open: AC 15
        var behindCrate = Attacks(crateMap, Archer, T(0, 1), T(5, 1));
        Assert.Equal(17, behindCrate.Vs);                                                // half cover: +2
        Assert.Equal(3, behindCrate.Modifier);                                           // the roll itself is unchanged
        Assert.Equal(17, Attacks(Field, Archer, T(0, 1), T(5, 1), ally: T(2, 1)).Vs);     // a unit in the way: half cover
        Assert.Equal(15, Attacks(crateMap, Fighter, T(5, 0), T(5, 1)).Vs);               // melee ignores cover

        // DEX saves get the cover bonus too: d20 + 2 DEX + 2 cover.
        var dodge = new AbilityDef(new AbilityId("test.dodge_this"), "Dodge this", 1, new CostDef(),
            new TargetDef(TargetKind.Enemy, Range: 8, Los: true, new AreaDef(AreaShape.Single)),
            [new SaveEffect(Stat.Dex, Dc: 10, OnFail: [], OnSuccess: [])]);
        var battle = TestBattle.On(crateMap).Party("kara", Fighter with { Abilities = [dodge.Id] }, T(0, 1))
            .Enemy("gob", Goblin, T(5, 1)).With(dodge);
        var (state, _) = battle.Start();
        var (_, events) = battle.Apply(state, new Use(U("kara"), dodge.Id, new Target(Unit: U("gob"))));
        Assert.Equal(4, Assert.Single(events.OfType<Rolled>()).Result.Modifier);
    }

    [Fact]
    public void High_ground_plus_2_hit()
    {
        const string steps = """
            0. 0. 0. 0. 0. 0.
            2. 0. 1. 0. 0. 0.
            0. 0. 0. 0. 0. 0.
            """;

        Assert.Equal(7, Attacks(steps, Fighter, T(0, 1), T(1, 1)).Modifier);            // 2 levels up: +3 STR +2 prof +2
        Assert.Equal(5, Attacks(steps, Fighter, T(2, 1), T(3, 1)).Modifier);            // 1 level up: no bonus
        Assert.Equal(5, Attacks(steps, Fighter, T(1, 0), T(1, 1)).Modifier);            // level: no bonus
        Assert.Equal(5, Attacks(steps, Archer, T(0, 1), T(4, 1)).Modifier);             // ranged: +1 DEX +2 prof +2
    }
}
