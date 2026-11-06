using ArcBox.Sdk;

namespace Tactics.Logic.Tests;

/// <summary>#107: the AI spreads a multi-attack's attacks once it expects the first foe to drop, and counts a once-only rider once.</summary>
public sealed class MultiAttackAiTests
{
    private static RulesPack Base => BasePackTests.Rules;

    private static UnitId U(string id) => new(id);

    private static Tile T(int x, int y) => new(x, y);

    private const string Field = """
        0. 0. 0. 0. 0.
        0. 0. 0. 0. 0.
        0. 0. 0. 0. 0.
        """;

    /// <summary>An AI fighter with three attacks who acts first and stays where she is.</summary>
    private static readonly UnitSheet Kara = TestSheets.Fighter with { Attacks = 3, InitiativeBonus = 40, Move = 0 };

    private static UnitSheet Gob(int hp) => TestSheets.Goblin with { MaxHp = hp, Ac = 12, Move = 0 };

    private static (TestBattle Battle, BattleState State) Fight(int weakHp)
    {
        var battle = TestBattle.On(Field).WithAiProfiles(Base.AiProfiles.Values)
            .Party("kara", Kara, T(2, 1), new AiControlled("brute"))
            .Enemy("a", Gob(weakHp), T(3, 1)).Enemy("b", Gob(40), T(1, 1));
        return (battle, battle.Start(seed: 2).State);
    }

    private static Use Attack(TurnPlan plan)
        => Assert.Single(plan.Commands.OfType<Use>(), u => u.Ability == RulesPack.CommonAttack.Id);

    [Fact]
    public void After_a_predicted_kill_the_AI_sends_the_rest_of_its_attacks_to_another_foe()
    {
        var (battle, state) = Fight(weakHp: 2);
        var plan = Ai.Plan(state, U("kara"), battle.Rules);

        var attack = Attack(plan);
        Assert.Equal(U("a"), attack.Target.Unit);
        Assert.Equal(3, attack.Targets.Length);
        Assert.Equal(U("a"), attack.Targets[0]);
        Assert.Equal(U("b"), attack.Targets[^1]);
        Assert.Contains("common.attack on a, ", plan.Explain);

        // The engine takes the plan as it is.
        var (_, events) = battle.Apply(state, attack);
        Assert.Contains(events, e => e is AttackResolved { Target.Value: "b" });
    }

    [Fact]
    public void With_no_kill_in_sight_the_AI_keeps_every_attack_on_one_foe()
    {
        var (battle, state) = Fight(weakHp: 40);
        Assert.True(Attack(Ai.Plan(state, U("kara"), battle.Rules)).Targets.IsDefaultOrEmpty);
    }

    /// <summary>A weapon hit that also deals a flat 10 radiant: the rider whose worth is easy to check.</summary>
    private static AbilityDef Rider(bool once) => new(new AbilityId("test.rider"), "Rider", 0,
        new CostDef(Action: 1, Resource: Ordinal.Map(("second_wind", 1))),
        new TargetDef(TargetKind.Enemy, 1, false, new AreaDef(AreaShape.Single)),
        [new AttackEffect("weapon", null, [new DamageEffect(null, null, [], Weapon: true), new DamageEffect(TestAbilities.Flat("10"), "radiant", [])], [], [])],
        IsWeaponAttack: true, RiderOnce: once);

    private static AbilityChoice Choice(bool once, UnitSheet? sheet = null)
    {
        var ability = Rider(once);
        var battle = TestBattle.On(Field)
            .Party("kara", (sheet ?? Kara) with { Abilities = [ability.Id] }, T(2, 1))
            .Enemy("a", TestSheets.Goblin with { MaxHp = 400, Ac = 0 }, T(3, 1))
            .With(ability);
        var state = battle.Start(seed: 2).State;
        return Choices.For(state, U("kara"), battle.Rules).Abilities.Single(a => a.Ability == ability.Id);
    }

    [Fact]
    public void A_rider_once_is_counted_once_with_the_chance_that_any_attack_hits()
    {
        // +5 against AC 0: 95% to hit, 5% to crit. Each attack's longsword is 0.9 × 7.5 + 0.05 × 12 = 7.35; the rider lands
        // when any of the three hits: (1 − 0.05³) × 10. Every hit carrying it would be 3 × (7.35 + 9.5).
        var once = Choice(once: true);
        Assert.InRange(Assert.Single(once.Targets[0].Affected).Damage, 31.95, 32.15);
        Assert.InRange(Assert.Single(Choice(once: false).Targets[0].Affected).Damage, 50.4, 50.7);
        Assert.Equal(3, once.Attacks);
        Assert.Null(Choice(once: true, Kara with { Attacks = 1 }).Attacks);
    }
}
