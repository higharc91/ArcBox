namespace Tactics.Logic.Tests;

public sealed class TacticalSpellChoiceTests
{
    private static RulesPack Base => BasePackTests.Rules;
    private static UnitId U(string id) => new(id);
    private const string Field = "0. 0. 0. 0. 0. 0.\n0. 0. 0. 0. 0. 0.\n0. 0. 0. 0. 0. 0.";

    [Fact]
    public void A_guardian_commands_a_lethal_foe_when_smite_cannot_finish_it()
    {
        var command = Base.Abilities[new("paladin.command")];
        var smite = Base.Abilities[new("paladin.divine_smite")];
        var paladin = SheetBuilder.ForCharacter(SheetBuilder.DefaultCharacter("guard", "human", "paladin", null, 3, Base), Base)
            with { InitiativeBonus = 100, Abilities = [command.Id, smite.Id], Resources = Ordinal.Map(("oath", 1)) };
        var brute = TestSheets.Fighter with { MaxHp = 200, Ac = 10, Move = 0, Attacks = 3,
            Scores = TestSheets.Scores(20, 10, 14, 8, 2, 8), Weapon = TestSheets.Fighter.Weapon with { Damage = Dice.Parse("50") } };
        var battle = TestBattle.On(Field).With(command, smite).WithAiProfiles(Base.AiProfiles.Values)
            .WithConditions([.. Base.Conditions.Values])
            .Party("guard", paladin, new(2, 1), new AiControlled("guardian"))
            .Party("ally", TestSheets.Fighter, new(1, 1))
            .Enemy("brute", brute, new(3, 1));
        var state = battle.Start().State;
        var plan = Ai.Plan(state, U("guard"), battle.Rules);
        Assert.True(plan.Commands.OfType<Use>().Any(u => u.Ability == command.Id && u.Target.Unit == U("brute")), plan.Explain);
    }

    [Fact]
    public void A_guardian_can_command_from_range_after_spending_its_move()
    {
        var command = Base.Abilities[new("paladin.command")];
        var smite = Base.Abilities[new("paladin.divine_smite")];
        var paladin = SheetBuilder.ForCharacter(SheetBuilder.DefaultCharacter("guard", "human", "paladin", null, 3, Base), Base)
            with { InitiativeBonus = 100, Abilities = [command.Id, smite.Id] };
        var battle = TestBattle.On(Field).With(command, smite).WithAiProfiles(Base.AiProfiles.Values)
            .WithConditions([.. Base.Conditions.Values])
            .Party("guard", paladin, new(0, 1), new AiControlled("guardian"))
            .Party("ally", TestSheets.Fighter, new(3, 1))
            .Enemy("foe", TestSheets.Fighter with { MaxHp = 40 }, new(4, 1));
        var state = battle.Start().State.Change("guard", u => u with { Economy = u.Economy with { MoveLeft = 0 } });
        var plan = Ai.Plan(state, U("guard"), battle.Rules);
        Assert.True(plan.Commands.OfType<Use>().Any(u => u.Ability == command.Id && u.Target.Unit == U("foe")), plan.Explain);
        foreach (var action in plan.Commands)
        {
            var result = Battle.Apply(state, action, battle.Rules);
            Assert.Null(result.Rejected);
            state = result.State;
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_ranger_walks_to_heal_an_ally_facing_downing_but_spares_focus_when_the_ally_is_healthy(bool urgent)
    {
        var cure = Base.Abilities[new("ranger.cure_wounds")];
        var ranger = SheetBuilder.ForCharacter(SheetBuilder.DefaultCharacter("ranger", "human", "ranger", null, 3, Base), Base)
            with { InitiativeBonus = 100, Abilities = [cure.Id], Resources = Ordinal.Map(("focus", 2)) };
        var ally = TestSheets.Fighter with { Level = 5, MaxHp = 40, Attacks = 2, Move = 0 };
        var foe = TestSheets.Fighter with { MaxHp = 200, Ac = 10, Move = 0,
            Weapon = TestSheets.Fighter.Weapon with { Damage = Dice.Parse("4") } };
        var battle = TestBattle.On(Field).With(cure).WithAiProfiles(Base.AiProfiles.Values)
            .WithPassives([.. Base.Passives.Values]).WithConditions([.. Base.Conditions.Values])
            .Party("ranger", ranger, new(0, 0), new AiControlled("archer"))
            .Party("ally", ally, new(2, 1))
            .Enemy("foe", foe, new(3, 1));
        var state = battle.Start().State;
        if (urgent) state = state.Change("ally", u => u with { Hp = 1 });
        var plan = Ai.Plan(state, U("ranger"), battle.Rules);
        var healsAlly = plan.Commands.OfType<Use>().Any(u => u.Ability == cure.Id && u.Target.Unit == U("ally"));
        Assert.True(healsAlly == urgent, plan.Explain);
        if (urgent) Assert.Contains(plan.Commands, c => c is Move);
        foreach (var action in plan.Commands)
        {
            var result = Battle.Apply(state, action, battle.Rules);
            Assert.Null(result.Rejected);
            state = result.State;
        }
        if (urgent) Assert.True(state.Units[U("ally")].Hp > 1);
    }
}
