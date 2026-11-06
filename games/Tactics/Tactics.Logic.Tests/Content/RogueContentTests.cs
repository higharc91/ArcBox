using System.Collections.Immutable;
using System.Text.Json;
using ArcBox.Sdk;

namespace Tactics.Logic.Tests;

/// <summary>
/// The rogue's numbers after the rebalance for human play (#108): at most two attacks per Attack action at any level and in
/// any subclass, its once-per-turn damage riders (Sneak Attack and any subclass passive like it) within the ruling's caps,
/// Vex marking only for itself, and Steady Aim folded into Hide.
/// </summary>
public sealed class RogueContentTests
{
    private static RulesPack Rules => BasePackTests.Rules;

    private static UnitSheet Rogue(int level, string? subclass = null)
        => SheetBuilder.ForCharacter(SheetBuilder.DefaultCharacter("vex", "human", "rogue", subclass, level, Rules), Rules);

    /// <summary>
    /// The ruling's cap on the d6s of a rogue's once-per-turn damage riders at each level: 2d6 at 1–2, 3d6 at 3–4, 5d6 at 5–8,
    /// 7d6 at 9–10 (ruling R29: Sneak Attack and any subclass passive like it, together).
    /// </summary>
    private static int RiderCap(int level) => level switch { <= 2 => 2, <= 4 => 3, <= 8 => 5, _ => 7 };

    /// <summary>The rogue with no subclass and, from level 3, with each of its subclasses.</summary>
    private static IEnumerable<(string? Subclass, UnitSheet Sheet)> Rogues(int level)
        => new string?[] { null }.Concat(Rules.Classes["rogue"].Subclasses.Where(_ => level >= 3).Select(s => s.Id))
            .Select(sub => (sub, Rogue(level, sub)));

    /// <summary>The damage effects a passive's hit runs: its own and those of its <c>if</c> branches.</summary>
    private static IEnumerable<DamageEffect> Damage(IEnumerable<EffectDef> effects)
        => effects.SelectMany(e => e switch
        {
            DamageEffect d => [d],
            IfEffect i => Damage(i.Then.IsDefault ? [] : i.Then).Concat(Damage(i.Else.IsDefault ? [] : i.Else)),
            _ => Enumerable.Empty<DamageEffect>(),
        });

    public static TheoryData<int> Levels => [.. Enumerable.Range(1, 10)];

    [Theory]
    [MemberData(nameof(Levels))]
    public void The_rogues_ranged_attack_budget_is_one_while_melee_remains_two(int level)
    {
        foreach (var subclass in new string?[] { null }.Concat(Rules.Classes["rogue"].Subclasses.Where(_ => level >= 3).Select(s => s.Id)))
        {
            var character = SheetBuilder.DefaultCharacter("vex", "human", "rogue", subclass, level, Rules);
            Assert.Equal(2, SheetBuilder.ForCharacter(character, Rules).Attacks);
            Assert.Equal(1, SheetBuilder.ForCharacter(character with { Weapon = "shortbow" }, Rules).Attacks);
        }
    }

    [Fact]
    public void Sneak_attack_damage_never_falls_on_level_up()
    {
        var dice = Assert.Single(Rules.PassiveFor("rogue.sneak_attack")!.Effects.OfType<DamageEffect>()).Dice!;
        foreach (var level in Enumerable.Range(2, 9))
            Assert.True(dice.At(level).Average >= dice.At(level - 1).Average);
    }

    [Theory]
    [InlineData("fighter")]
    [InlineData("ranger")]
    public void Classes_without_a_ranged_override_keep_their_existing_attack_schedule(string cls)
    {
        foreach (var level in Enumerable.Range(1, 10))
        {
            var character = SheetBuilder.DefaultCharacter("other", "human", cls, null, level, Rules);
            Assert.Equal(SheetBuilder.ForCharacter(character, Rules).Attacks,
                SheetBuilder.ForCharacter(character with { Weapon = "shortbow" }, Rules).Attacks);
        }
    }

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
    public void Once_per_turn_damage_riders_stay_within_the_rulings_caps(int level)
    {
        foreach (var (subclass, sheet) in Rogues(level))
        {
            // Every once-per-turn (or -round) damage a hit adds, summed as average damage against the cap's d6s.
            var riders = sheet.Passives.Select(Rules.PassiveFor).OfType<PassiveDef>()
                .Where(p => p.Hook == HookKind.OnAttackHit && (p.OncePerTurn || p.OncePerRound))
                .SelectMany(p => Damage(p.Effects).Select(d => (p.Id, Dice: d.Dice!.At(level), d.Bonus)))
                .ToArray();
            Assert.All(riders, r => Assert.True(r.Bonus.IsDefaultOrEmpty, $"{r.Id} adds a bonus the cap can't size."));
            var average = riders.Sum(r => r.Dice.Count * (r.Dice.Sides + 1) / 2.0 + r.Dice.Bonus);
            Assert.True(average <= RiderCap(level) * 3.5,
                $"Level {level} {subclass ?? "rogue"}: {string.Join(" + ", riders.Select(r => $"{r.Id} {r.Dice}"))} is over {RiderCap(level)}d6.");
        }
        Assert.Contains(Rules.PassiveFor("rogue.sneak_attack")!.Effects.OfType<DamageEffect>(), d => d.Dice!.At(level).Count >= 1);
    }

    [Fact]
    public void A_rogues_vex_leaves_a_paladins_vow_alone()
    {
        // #108, ruling R28: Vex is the rogue's own mark (vexed), so it doesn't take the place of a paladin's Vow of Enmity.
        var (state, rules) = Fight("rapier", new Tile(2, 0), Paladin(6, "paladin.avenger"));   // beside the goblin, not opposite
        state = Act(state, rules, new Use(new UnitId("kara"), new AbilityId("avenger.vow_of_enmity"), new Target(Unit: new UnitId("gob"))));
        state = Act(state, rules, new EndTurn(new UnitId("kara")));
        (state, _) = HitWith(state, rules, "vex");

        var gob = state.Units[new UnitId("gob")];
        Assert.Contains(gob.Conditions, c => c.Id.Value == "marked" && c.Source == new UnitId("kara"));
        Assert.Contains(gob.Conditions, c => c.Id.Value == "vexed" && c.Source == new UnitId("vex"));
        state = Act(Act(state, rules, new EndTurn(new UnitId("vex"))), rules, new EndTurn(new UnitId("gob")));
        var swing = Battle.Apply(state, new Use(new UnitId("kara"), RulesPack.CommonAttack.Id, new Target(Unit: new UnitId("gob"))), rules);
        Assert.Equal(2, swing.Events.OfType<Rolled>().First(r => r.Kind == RollKind.Attack).Result.D20s.Length);   // the vow still counts
    }

    [Fact]
    public void Passives_that_read_a_mark_do_not_fire_on_a_target_only_vexed()
    {
        // Vengeful Strikes and Ferocious Bond read "marked": the rogue's Vex isn't one.
        foreach (var (sheet, passive) in new[]
                 {
                     (Paladin(6, "paladin.avenger"), "avenger.vengeful_strikes"),
                     (SheetBuilder.ForCharacter(SheetBuilder.DefaultCharacter("kara", "human", "ranger", "ranger.beastmaster", 10, Rules), Rules),
                         "beastmaster.ferocious_bond"),
                 })
        {
            var (state, rules) = Fight("rapier", new Tile(2, 0), sheet with { InitiativeBonus = 40 });
            (state, _) = HitWith(state, rules, "vex");
            Assert.Contains(state.Units[new UnitId("gob")].Conditions, c => c.Id.Value == "vexed");
            while (state.Active != new UnitId("kara"))     // the beastmaster's hound may come first
                state = Act(state, rules, new EndTurn(state.Active!.Value));
            var (_, events) = HitWith(state, rules, "kara");
            Assert.DoesNotContain(new PassiveTriggered(new UnitId("kara"), passive), events);
        }
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

    private static UnitSheet Paladin(int level, string subclass)
        => SheetBuilder.ForCharacter(SheetBuilder.DefaultCharacter("kara", "human", "paladin", subclass, level, Rules), Rules)
            with { InitiativeBonus = 60 };

    private static BattleState Act(BattleState state, RulesPack rules, Command command)
    {
        var applied = Battle.Apply(state, command, rules);
        Assert.True(applied.Rejected is null, $"{command}: {applied.Rejected?.Reason}");
        return applied.State;
    }

    /// <summary><paramref name="unit"/>'s weapon attack on the goblin, from the first seed on which its first attack hits.</summary>
    private static (BattleState State, ImmutableArray<BattleEvent> Events) HitWith(BattleState state, RulesPack rules, string unit)
    {
        for (ulong seed = 1; seed < 200; seed++)
        {
            var used = Battle.Apply(state with { Rng = Rng.Seeded(seed) }, new Use(new UnitId(unit), RulesPack.CommonAttack.Id,
                new Target(Unit: new UnitId("gob"))), rules);
            if (used.Events.OfType<AttackResolved>().FirstOrDefault() is { Hit: true, Crit: false })
                return (used.State, used.Events);
        }
        throw new InvalidOperationException("No seed hits.");
    }

    /// <summary>A level-1 rogue (with <paramref name="weapon"/>), acting first, a goblin with HP to spare and an ally.</summary>
    private static (BattleState State, RulesPack Rules) Fight(string weapon, Tile ally, UnitSheet? allySheet = null)
    {
        var map = AsciiMap.Parse("""
            0. 0. 0. 0. 0.
            0. 0. 0. 0. 0.
            0. 0. 0. 0. 0.
            """);
        var rules = RulesPack.InMemory("rogue", Rules.Abilities.Values, Rules.Passives.Values, Rules.Conditions.Values, [map],
            Rules.Injuries.Values, monsters: Rules.Monsters.Keys.Select(m => (m, SheetBuilder.ForMonster(m, Rules))));   // a beastmaster's hound
        var rogue = Rogue(1) with { InitiativeBonus = 50, Weapon = Rules.Weapons[weapon] };
        var fighter = allySheet ?? SheetBuilder.ForCharacter(SheetBuilder.DefaultCharacter("kara", "human", "fighter", null, 1, Rules), Rules);
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
        Assert.Contains(state.Units[new UnitId("gob")].Conditions, c => c.Id.Value == "vexed" && c.Source == new UnitId("vex"));
        Assert.DoesNotContain(state.Units[new UnitId("gob")].Conditions, c => c.Id.Value == "marked");
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
