using System.Collections.Immutable;
using System.Text.Json;
using ArcBox.Sdk;

namespace Tactics.Logic.Tests;

/// <summary>
/// The rogue's numbers after the rebalance for human play (#108): at most two attacks per Attack action at any level and in
/// any subclass, Sneak Attack within 1.5× the SRD's dice, and Steady Aim folded into Hide.
/// </summary>
public sealed class RogueContentTests
{
    private static RulesPack Rules => BasePackTests.Rules;

    private static UnitSheet Rogue(int level, string? subclass = null)
        => SheetBuilder.ForCharacter(SheetBuilder.DefaultCharacter("vex", "human", "rogue", subclass, level, Rules), Rules);

    /// <summary>The most Sneak Attack d6s at each level: 1.5× the SRD's, rounded down (2d6 at 1–2, 3d6 at 3–4, 5d6 at 5–8, 7d6 at 9–10).</summary>
    private static int SneakCap(int level) => level switch { <= 2 => 2, <= 4 => 3, <= 8 => 5, _ => 7 };

    public static TheoryData<int> Levels => [.. Enumerable.Range(1, 10)];

    [Theory]
    [MemberData(nameof(Levels))]
    public void The_rogue_makes_at_most_two_attacks_per_action(int level)
    {
        Assert.InRange(Rogue(level).Attacks, 1, 2);
        foreach (var subclass in Rules.Classes["rogue"].Subclasses.Where(_ => level >= 3))
            Assert.InRange(Rogue(level, subclass.Id).Attacks, 1, 2);
    }

    [Theory]
    [MemberData(nameof(Levels))]
    public void Sneak_attack_stays_within_one_and_a_half_times_the_srd(int level)
    {
        var sneak = Assert.IsType<DamageEffect>(Assert.Single(Rules.Passives["rogue.sneak_attack"].Effects));
        var dice = sneak.Dice!.At(level);

        Assert.Equal((6, 0), (dice.Sides, dice.Bonus));
        Assert.InRange(dice.Count, 1, SneakCap(level));
    }

    [Fact]
    public void Steady_aim_is_folded_into_hide()
    {
        Assert.DoesNotContain(new AbilityId("rogue.steady_aim"), Rules.Abilities.Keys);
        Assert.All(Enumerable.Range(1, 10), level => Assert.DoesNotContain(new AbilityId("rogue.steady_aim"), Rogue(level).Abilities));

        // Hide gives what Steady Aim gave (the next attack has advantage), but only out of plain view.
        var hide = Tooltips.For(Rules.Abilities[new AbilityId("rogue.cunning_hide")], 3, Rules);
        Assert.Contains("Only when you aren't in plain view", hide);
        Assert.Contains("Steady Aim", hide);
        Assert.True(Rules.Conditions[new ConditionId("hiding")].Static?.Invisible);
    }

    /// <summary>A level-1 rogue (with <paramref name="weapon"/>), acting first, a goblin with HP to spare and an ally.</summary>
    private static (BattleState State, RulesPack Rules) Fight(string weapon, Tile ally)
    {
        var map = AsciiMap.Parse("""
            0. 0. 0. 0. 0.
            0. 0. 0. 0. 0.
            0. 0. 0. 0. 0.
            """);
        var rules = RulesPack.InMemory("rogue", Rules.Abilities.Values, Rules.Passives.Values, Rules.Conditions.Values, [map],
            Rules.Injuries.Values);
        var rogue = Rogue(1) with { InitiativeBonus = 50, Weapon = Rules.Weapons[weapon] };
        var fighter = SheetBuilder.ForCharacter(SheetBuilder.DefaultCharacter("kara", "human", "fighter", null, 1, Rules), Rules);
        var setup = new BattleSetup(map,
        [
            new UnitPlacement(new UnitId("vex"), rogue, new PlayerControlled(new ProfileId("pr_vex")), new Tile(1, 1), []),
            new UnitPlacement(new UnitId("gob"), SheetBuilder.ForMonster("goblin", Rules) with { MaxHp = 300 }, new AiControlled("brute"),
                new Tile(2, 1), []),
            new UnitPlacement(new UnitId("kara"), fighter, new PlayerControlled(new ProfileId("pr_kara")), ally, []),
        ], ObjectivesDef.Default, [], Difficulty.Normal, 0, []);
        return (Battle.Start(setup, rules, seed: 9).State, rules);
    }

    /// <summary>The first seed's battle in which the rogue's first attack hits, with what it emitted.</summary>
    private static (BattleState State, ImmutableArray<BattleEvent> Events, RulesPack Rules) FirstHit(string weapon, Tile ally)
    {
        var (state, rules) = Fight(weapon, ally);
        for (ulong seed = 1; seed < 200; seed++)
        {
            var used = Battle.Apply(state with { Rng = Rng.Seeded(seed) }, new Use(new UnitId("vex"), RulesPack.CommonAttack.Id,
                new Target(Unit: new UnitId("gob"))), rules);
            if (used.Events.OfType<AttackResolved>().FirstOrDefault() is { Hit: true, Crit: false })
                return (used.State, used.Events, rules);
        }
        throw new InvalidOperationException("No seed hits.");
    }

    [Fact]
    public void Vex_gives_the_rogue_advantage_on_its_next_attack_against_what_it_hit()
    {
        // SRD 5.2's Weapon Mastery: a rapier's Vex. The first hit marks the goblin, so the second attack rolls two d20s.
        var (state, events, _) = FirstHit("rapier", new Tile(4, 2));

        Assert.Contains(new PassiveTriggered(new UnitId("vex"), "rogue.vex"), events);
        Assert.Contains(state.Units[new UnitId("gob")].Conditions, c => c.Id.Value == "marked" && c.Source == new UnitId("vex"));
        Assert.Equal([1, 2], events.OfType<Rolled>().Where(r => r.Kind == RollKind.Attack).Select(r => r.Result.D20s.Length));

        // A scimitar has another mastery (Nick), so no Vex.
        Assert.DoesNotContain(new PassiveTriggered(new UnitId("vex"), "rogue.vex"), FirstHit("scimitar", new Tile(4, 2)).Events);
    }

    [Fact]
    public void Sneak_attack_needs_advantage_or_an_ally_next_to_the_target()
    {
        // SRD 5.2: an ally that can act within 5 feet of the target is enough, opposite or not; no Vex with a scimitar.
        Assert.Contains(new PassiveTriggered(new UnitId("vex"), "rogue.sneak_attack"), FirstHit("scimitar", new Tile(2, 0)).Events);
        Assert.DoesNotContain(new PassiveTriggered(new UnitId("vex"), "rogue.sneak_attack"), FirstHit("scimitar", new Tile(4, 0)).Events);
        Assert.Contains("an ally is next to the target and you don't have disadvantage",
            Tooltips.For(Rules.Passives["rogue.sneak_attack"], 1, Rules));
    }

    [Fact]
    public void A_battle_saved_with_steady_aim_on_a_rogue_still_plays()
    {
        // A sheet from before #108 lists rogue.steady_aim, which the pack no longer has. The battle reads back, the phone and
        // the AI leave the ability out, and using it is refused rather than breaking anything.
        var steadyAim = new AbilityId("rogue.steady_aim");
        var map = AsciiMap.Parse("""
            0. 0. 0. 0. 0. 0.
            0. 0. 0. 0. 0. 0.
            """);
        var rules = RulesPack.InMemory("old-save", Rules.Abilities.Values, Rules.Passives.Values, Rules.Conditions.Values, [map],
            Rules.Injuries.Values);
        var old = Rogue(3) with { InitiativeBonus = 50, Abilities = Rogue(3).Abilities.Add(steadyAim) };
        var setup = new BattleSetup(map,
        [
            new UnitPlacement(new UnitId("vex"), old, new PlayerControlled(new ProfileId("pr_vex")), new Tile(0, 0), []),
            new UnitPlacement(new UnitId("gob"), SheetBuilder.ForMonster("goblin", Rules), new AiControlled("brute"), new Tile(4, 1), []),
        ], ObjectivesDef.Default, [], Difficulty.Normal, 0, []);
        var saved = JsonSerializer.Serialize(Battle.Start(setup, rules, seed: 2).State, ArcJson.Options);
        var state = JsonSerializer.Deserialize<BattleState>(saved, ArcJson.Options)!;

        Assert.Contains(steadyAim, state.Units[new UnitId("vex")].Sheet.Abilities);
        Assert.DoesNotContain(Choices.For(state, new UnitId("vex"), rules).Abilities, a => a.Ability == steadyAim);
        Assert.DoesNotContain(Ai.Plan(state, new UnitId("vex"), rules).Commands, c => c is Use { Ability: var a } && a == steadyAim);
        Assert.Equal(RejectCode.UnknownAbility,
            Battle.Apply(state, new Use(new UnitId("vex"), steadyAim, new Target()), rules).Rejected?.Code);
    }
}
