using System.Collections.Immutable;

namespace Tactics.Logic.Tests;

public sealed class AimPreviewTests
{
    [Theory]
    [InlineData(Edge.None)]
    [InlineData(Edge.Advantage)]
    [InlineData(Edge.Disadvantage)]
    public void Phone_hit_odds_match_the_engines_d20_distribution_and_do_not_roll(Edge edge)
    {
        var condition = new ConditionDef(new("aim_edge"), "Aim effect", new StaticMods(OwnAttacks: edge), [], []);
        var battle = TestBattle.On("0. 0. 0.\n0. 0. 0.")
            .WithConditions(condition)
            .Party("hero", TestSheets.Fighter with { InitiativeBonus = 30 }, new(0, 0))
            .Enemy("foe", TestSheets.Goblin, new(1, 0));
        var (state, _) = battle.Start();
        state = state.Give("hero", new ActiveCondition(condition.Id, null, null, false, 0, Stat.Con, false));
        var before = state;
        var target = Choices.For(state, new("hero"), battle.Rules).Abilities.Single(a => a.Ability.Value == "common.attack").Targets.Single();
        var preview = Assert.Single(target.Affected);
        var roll = Assert.IsType<AimRoll>(preview.Roll);
        Assert.Equal("hit", roll.Kind); Assert.Equal(edge, roll.Edge);
        Assert.Equal(Math.Round(Odds.Hit(roll.Modifier, roll.Vs, edge) * 100, 1), roll.Chance);
        Assert.True(preview.DamageMax > preview.DamageMin);
        if (edge != Edge.None) Assert.Contains("Aim effect", roll.Reason);
        Assert.Equal(before, state);
        // Independently resolve deterministic seeds: every recorded attack uses the advertised AC and modifier.
        var use = new Use(new("hero"), new("common.attack"), target.Target);
        var events = Battle.Apply(state, use, battle.Rules).Events.OfType<Rolled>().Where(r => r.Kind == RollKind.Attack).ToArray();
        Assert.NotEmpty(events);
        Assert.All(events, r => { Assert.Equal(roll.Modifier, r.Result.Modifier); Assert.Equal(roll.Vs, r.Result.Vs); });
    }
    [Theory]
    [InlineData(Edge.None)]
    [InlineData(Edge.Advantage)]
    [InlineData(Edge.Disadvantage)]
    public void Save_failure_chance_uses_the_targets_save_edge_and_actual_DC(Edge edge)
    {
        var condition = new ConditionDef(new("nimble"), "Nimble", new StaticMods(
            SaveEdges: ImmutableSortedDictionary<Stat, Edge>.Empty.Add(Stat.Dex, edge)), [], []);
        var spell = TestAbilities.GuidingBolt with { Id = new("test.save"), Cost = new CostDef(Action: 1),
            Effects = [new SaveEffect(Stat.Dex, 14, [new DamageEffect(TestAbilities.Flat("2d6"), "fire", [])], [], HalfOnSuccess: true)] };
        var battle = TestBattle.On("0. 0. 0.\n0. 0. 0.").With(spell).WithConditions(condition)
            .Party("hero", TestSheets.Fighter with { InitiativeBonus = 30, Abilities = [spell.Id] }, new(0, 0))
            .Enemy("foe", TestSheets.Goblin, new(1, 0));
        var (state, _) = battle.Start();
        state = state.Give("foe", new ActiveCondition(condition.Id, null, null, false, 0, Stat.Con, false));
        var choice = Choices.For(state, new("hero"), battle.Rules).Abilities.Single(a => a.Ability == spell.Id).Targets.Single();
        var preview = Assert.Single(choice.Affected); var roll = Assert.IsType<AimRoll>(preview.Roll);
        Assert.Equal("fail save", roll.Kind); Assert.Equal(edge, roll.Edge); Assert.Equal(14, roll.Vs);
        Assert.Equal(Math.Round(Odds.SaveFail(roll.Modifier, 14, edge) * 100, 1), roll.Chance);
        Assert.Equal(1, preview.DamageMin); Assert.Equal(12, preview.DamageMax);
        if (edge != Edge.None) Assert.Contains("Nimble", roll.Reason);
        var events = Battle.Apply(state, new Use(new("hero"), spell.Id, choice.Target), battle.Rules).Events.OfType<Rolled>();
        var save = Assert.Single(events, r => r.Kind == RollKind.Save);
        Assert.Equal(roll.Modifier, save.Result.Modifier); Assert.Equal(roll.Vs, save.Result.Vs);
    }

    [Fact]
    public void Splittable_multiattack_ranges_describe_the_opening_attack_instead_of_claiming_all_attacks_hit_one_target()
    {
        var battle = TestBattle.On("0. 0. 0.\n0. 0. 0.")
            .Party("hero", TestSheets.Fighter with { InitiativeBonus = 30, Attacks = 2 }, new(0, 0))
            .Enemy("foe", TestSheets.Goblin, new(1, 0));
        var (state, _) = battle.Start();
        UnitPreview Preview(BattleState at) => Choices.For(at, new("hero"), battle.Rules).Abilities
            .Single(a => a.Ability.Value == "common.attack").Targets.Single().Affected.Single();
        var two = Preview(state);
        var one = Preview(state.Change("hero", u => u with { Sheet = u.Sheet with { Attacks = 1 } }));
        Assert.Equal(one.DamageMin, two.DamageMin); Assert.Equal(one.DamageMax, two.DamageMax);
        Assert.True(two.Damage > one.Damage); // existing whole-use expectation remains available to callers
    }

}
