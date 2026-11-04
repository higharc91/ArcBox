using System.Collections.Immutable;
using ArcBox.Sdk;

namespace Tactics.Logic.Tests;

public sealed class HollowEncounterTests
{
    private static RulesPack Rules => BasePackTests.Rules;

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void Every_chamber_starts_with_the_actual_party_without_overlapping_units(int size)
    {
        foreach (var name in new[] { "throat", "diggings", "nest", "lair" })
        {
            var setup = Encounters.Setup("hollow_" + name, BasePackTests.Party(size, Rules), Difficulty.Normal, Rules);
            Assert.Equal(size, setup.Units.Count(u => u.Sheet.Side == Side.Party));
            Assert.Equal(size + setup.Units.Count(u => u.Sheet.Side == Side.Enemy), setup.Units.Select(u => u.At).Distinct().Count());
            var state = Battle.Start(setup, Rules, 74).State;
            Assert.Null(state.Outcome);
            Assert.Equal(name is "nest" or "lair", state.Environment);
            if (name == "lair")
            {
                var boss = Assert.Single(setup.Units, u => u.Sheet.Side == Side.Enemy);
                Assert.Contains("fire", boss.Sheet.Resist);
                Assert.Equal(size >= 4, boss.Sheet.Abilities.Contains(new AbilityId("ashmaw.party_maul")));
                Assert.Equal(1, boss.Sheet.Resources["roar"]);
            }
        }
    }

    [Fact]
    public void Ashmaw_uses_its_attacks_and_keeps_them_when_the_ash_shake_phase_starts()
    {
        var setup = Encounters.Setup("hollow_lair", BasePackTests.Party(4, Rules), Difficulty.Normal, Rules);
        var positions = new[] { new Tile(8, 6), new Tile(8, 7), new Tile(9, 6), new Tile(9, 7) };
        var hero = 0;
        setup = setup with { Units = [.. setup.Units.Select(u => u.Sheet.Side == Side.Enemy
            ? u with { Sheet = u.Sheet with { InitiativeBonus = 100 }, At = new(7, 6) }
            : u with { At = positions[hero++] })] };
        var state = Battle.Start(setup, Rules, 74).State;
        Assert.Equal(new UnitId("ashmaw"), state.Active);
        var plan = Ai.Plan(state, new("ashmaw"), Rules);
        Assert.Contains(plan.Commands.OfType<Use>(), u => u.Ability.Value.StartsWith("ashmaw.", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Commands.OfType<Use>(), u => u.Ability.Value == "common.dodge");
        foreach (var command in plan.Commands)
        {
            var result = Battle.Apply(state, command, Rules);
            Assert.Null(result.Rejected);
            state = result.State;
            while (state.Pending is ReactionOffer offer)
                state = Battle.Apply(state, new React(offer.Reactor, offer.Options.Single(o => o.Id == Reactions.Pass)), Rules).State;
        }
        var ashmaw = state.Units[new("ashmaw")];
        state = state with { Units = state.Units.SetItem(ashmaw.Id, ashmaw with { Hp = ashmaw.Sheet.MaxHp / 2 - 1 }) };
        var phase = Battle.Apply(state, new EndTurn(state.Active!.Value), Rules).State.Units[ashmaw.Id];
        Assert.Contains(new AbilityId("ashmaw.ash_shake"), phase.Sheet.Abilities);
        Assert.Contains(new AbilityId("ashmaw.party_maul"), phase.Sheet.Abilities);
    }

    [Theory]
    [InlineData(1, 32)] [InlineData(2, 60)] [InlineData(3, 90)]
    public void Smaller_parties_keep_the_original_bear_fight(int size, int hp)
    {
        var setup = Encounters.Setup("hollow_lair", BasePackTests.Party(size, Rules), Difficulty.Normal, Rules);
        var boss = Assert.Single(setup.Units, u => u.Sheet.Side == Side.Enemy);
        Assert.Equal(new UnitId("ashmaw"), boss.Id);
        Assert.Equal(hp, boss.Sheet.MaxHp);
        Assert.Equal(12, boss.Sheet.Ac);
        Assert.Equal(17, boss.Sheet.Score(Stat.Str));
        Assert.Contains(new AbilityId("ashmaw.maul"), boss.Sheet.Abilities);
        Assert.DoesNotContain(new AbilityId("ashmaw.party_maul"), boss.Sheet.Abilities);
        foreach (var attack in Rules.Abilities[new("ashmaw.maul")].Effects.OfType<AttackEffect>())
            Assert.Equal(Dice.Parse("1d6"), Assert.Single(attack.OnHit.OfType<DamageEffect>()).Dice!.At(2));
    }

    [Fact]
    public void Gaining_levels_strengthens_the_party_without_auto_levelling_Ashmaw()
    {
        BattleSetup AtLevel(int level) => Encounters.Setup("hollow_lair",
            [.. BasePackTests.Party(4, Rules).Select(m => m with
            { Character = SheetBuilder.DefaultCharacter(m.Character.Name, "human", m.Character.Class, null, level, Rules) })],
            Difficulty.Normal, Rules);
        var low = AtLevel(2);
        var high = AtLevel(4);
        var before = Assert.Single(low.Units, u => u.Sheet.Side == Side.Enemy);
        var after = Assert.Single(high.Units, u => u.Sheet.Side == Side.Enemy);
        Assert.Equal(2, before.Sheet.Level);
        Assert.Equal((before.Sheet.Level, before.Sheet.MaxHp, before.Sheet.Ac, before.Sheet.Score(Stat.Str)),
            (after.Sheet.Level, after.Sheet.MaxHp, after.Sheet.Ac, after.Sheet.Score(Stat.Str)));
        Assert.Equal(before.Sheet.Abilities.ToArray(), after.Sheet.Abilities.ToArray());
        Assert.True(high.Units.Where(u => u.Sheet.Side == Side.Party).Sum(u => u.Sheet.MaxHp)
            > low.Units.Where(u => u.Sheet.Side == Side.Party).Sum(u => u.Sheet.MaxHp));
    }

    [Fact]
    public void Grisk_buffs_only_adjacent_kobold_allies_and_the_bonus_expires()
    {
        var map = Rules.MapFor("hollow_diggings")!;
        var grisk = SheetBuilder.ForMonster("kobold_foreman", Rules) with { InitiativeBonus = 100 };
        var kobold = SheetBuilder.ForMonster("kobold", Rules);
        var spider = SheetBuilder.ForMonster("giant_spider", Rules);
        var hero = SheetBuilder.ForCharacter(SheetBuilder.DefaultCharacter("Hero", "human", "fighter", null, 2, Rules), Rules);
        UnitPlacement Place(string id, UnitSheet sheet, Tile at) => new(new UnitId(id), sheet, new AiControlled("brute"), at, []);
        var setup = new BattleSetup(map,
            [Place("grisk", grisk, new(5, 5)), Place("near", kobold, new(5, 6)), Place("far", kobold, new(8, 5)),
                Place("spider", spider, new(6, 5)), Place("hero", hero, new(6, 6))], ObjectivesDef.Default, [], Difficulty.Normal, 0, []);
        var state = Battle.Start(setup, Rules, 74).State;
        Assert.Equal(new UnitId("grisk"), state.Active);
        var result = Battle.Apply(state, new Use(new("grisk"), new("grisk.dig_faster"), new Target(Unit: new UnitId("grisk"))), Rules);
        Assert.Null(result.Rejected);
        Assert.Equal(1, Modifiers.For(result.State, new("near"), Rules).Attack);
        foreach (var id in new[] { "far", "spider", "hero", "grisk" })
            Assert.Equal(0, Modifiers.For(result.State, new(id), Rules).Attack);
        state = Battle.Apply(result.State, new EndTurn(new("grisk")), Rules).State;
        for (var i = 0; i < 20 && state.Active != new UnitId("grisk"); i++)
            state = Battle.Apply(state, new EndTurn(state.Active!.Value), Rules).State;
        Assert.Equal(new UnitId("grisk"), state.Active);
        state = Battle.Apply(state, new EndTurn(new("grisk")), Rules).State;
        Assert.Equal(0, Modifiers.For(state, new("near"), Rules).Attack);
    }
}
