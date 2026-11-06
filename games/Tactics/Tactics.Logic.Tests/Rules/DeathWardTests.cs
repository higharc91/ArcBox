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
