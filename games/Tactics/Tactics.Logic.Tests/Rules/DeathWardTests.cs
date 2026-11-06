namespace Tactics.Logic.Tests;

/// <summary>
/// Death Ward (#110, ruling R21): the ward stands its holder back up at 1 HP when it is Downed, and the spell ends there (SRD:
/// "the spell ends"), so a fresh casting wards the holder again.
/// </summary>
public sealed class DeathWardTests
{
    private static RulesPack Base => BasePackTests.Rules;

    private static UnitId U(string id) => new(id);

    private static Tile T(int x, int y) => new(x, y);

    private static readonly ConditionId Ward = new("death_ward");

    [Fact]
    public void Being_downed_cancels_remaining_strikes_even_when_death_ward_revives_the_attacker()
    {
        var strike = new AbilityDef(new("test.multi"), "Multi", 0, new CostDef(Action: 1),
            new TargetDef(TargetKind.Enemy, 1, false, new AreaDef(AreaShape.Single)),
            [new AttackEffect("weapon", null, [new DamageEffect(TestAbilities.Flat("1"), "fire", [])], [], [], Bonus: 100)],
            IsWeaponAttack: true);
        var retaliation = new PassiveDef("test.retaliation", HookKind.OnDamaged, null,
            [new DamageEffect(TestAbilities.Flat("100"), "fire", [])], null, Target: PassiveTarget.Other);
        var battle = TestBattle.On("0. 0. 0.")
            .Party("aldo", TestSheets.Fighter with { InitiativeBonus = 100, Attacks = 2, Abilities = [strike.Id] }, T(0, 0))
            .Party("ally", TestSheets.Cleric, T(2, 0))
            .Enemy("gob", TestSheets.Goblin with { MaxHp = 100, Passives = [retaliation.Id] }, T(1, 0))
            .With(strike).WithPassives(retaliation).WithConditions([.. Base.Conditions.Values]);
        var (state, _) = battle.Start(7);
        state = state.Give("aldo", TestConditions.Lasting(Base.Conditions[Ward], U("aldo")));
        var (after, events) = battle.Apply(state, new Use(U("aldo"), strike.Id, new Target(Unit: U("gob"))));
        Assert.True(events.Any(e => e is Revived { Unit: var unit } && unit == U("aldo")), string.Join("\n", events));
        Assert.Single(events.OfType<AttackResolved>(), e => e.Source == U("aldo"));
        Assert.Equal(99, after.Units[U("gob")].Hp);
    }

    [Fact]
    public void A_fired_ward_ends_and_a_fresh_casting_saves_the_holder_again()
    {
        var ward = Base.Abilities[new AbilityId("cleric.death_ward")];
        var priest = TestSheets.Cleric.Knowing(ward) with { InitiativeBonus = 30, Resources = Ordinal.Map(("devotion", 8)) };
        var battle = TestBattle.On("0. 0. 0. 0. 0.").With(ward).WithConditions([.. Base.Conditions.Values])
            .Party("priest", priest, T(0, 0))
            .Party("aldo", TestSheets.Fighter, T(1, 0))
            .Enemy("gob", TestSheets.Goblin, T(4, 0));
        var state = battle.Start().State;
        var blow = new DamageEffect(TestAbilities.Flat("100"), "slashing", []);

        for (var casting = 1; casting <= 2; casting++)
        {
            state = state.Change("priest", u => u with { Economy = u.Economy with { Action = true } });
            (state, _) = battle.Apply(state, new Use(U("priest"), ward.Id, new Target(Unit: U("aldo"))));
            Assert.Contains(state.Units[U("aldo")].Conditions, c => c.Id == Ward);

            (state, _) = battle.Run(state, "gob", "aldo", null, blow);

            var aldo = state.Units[U("aldo")];
            Assert.True(aldo is { Life: LifeState.Up, Hp: 1 }, $"casting {casting}: {aldo.Life} at {aldo.Hp} HP");
            Assert.DoesNotContain(aldo.Conditions, c => c.Id == Ward);
        }
    }
}
