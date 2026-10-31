using System.Collections.Immutable;
using System.Text.Json;
using ArcBox.Sdk;

namespace Tactics.Logic.Tests;

/// <summary>The utility AI (spec §5.2–5.4) on small ASCII scenarios and on the base pack's river crossing.</summary>
public sealed class PlannerTests
{
    private static RulesPack Base => BasePackTests.Rules;

    private static UnitId U(string id) => new(id);

    private static AbilityDef A(string id) => Base.Abilities[new AbilityId(id)];

    private static ConditionDef C(string id) => Base.Conditions[new ConditionId(id)];

    /// <summary>A builder on <paramref name="ascii"/> whose rules pack has the base pack's AI profiles.</summary>
    private static TestBattle On(string ascii) => TestBattle.On(ascii).WithAiProfiles(Base.AiProfiles.Values);

    /// <summary>A sheet that acts first.</summary>
    private static UnitSheet First(UnitSheet sheet) => sheet with { InitiativeBonus = 30 };

    private static UnitSheet Monster(string id) => SheetBuilder.ForMonster(id, Base);

    private static Tile T(int x, int y) => new(x, y);

    /// <summary>
    /// Applies every command of <paramref name="plan"/>, each of which must be accepted, answering any reaction offered to a
    /// player with "pass", until the plan ends or the battle does. Returns the state after the plan.
    /// </summary>
    private static BattleState ApplyAll(BattleState state, TurnPlan plan, RulesPack rules)
    {
        foreach (var command in plan.Commands)
        {
            if (state.Outcome is not null)
                break;
            var resolution = Battle.Apply(state, command, rules);
            Assert.True(resolution.Rejected is null, $"{command} was refused: {resolution.Rejected?.Reason}\n{plan.Explain}");
            state = resolution.State;
            while (state.Pending is ReactionOffer offer)
                state = Battle.Apply(state, new React(offer.Reactor, offer.Options[0]), rules).State;
        }
        return state;
    }

    private static Tile EndTile(BattleState state, UnitId unit, TurnPlan plan)
        => plan.Commands.OfType<Move>().LastOrDefault()?.Path[^1] ?? state.Units[unit].Pos;

    private static Use? UseOf(TurnPlan plan, string ability)
        => plan.Commands.OfType<Use>().FirstOrDefault(u => u.Ability.Value == ability);

    [Fact]
    public void Archer_takes_high_ground_and_cover()
    {
        // The ridge (height 2) at x = 2 gives +2 to hit; the crate at (3, 2) gives half cover to (2, 2) only.
        var battle = On("""
            0. 0. 0. 0. 0. 0. 0.
            0. 0. 2. 0. 0. 0. 0.
            0. 0. 2. 0c 0. 0. 0.
            0. 0. 2. 0. 0. 0. 0.
            0. 0. 0. 0. 0. 0. 0.
            """).With(A("goblin.nimble_escape")).WithConditions(C("disengaged"))
            .Enemy("archer", First(Monster("goblin_archer")), T(1, 2), new AiControlled("archer"))
            .Party("bow1", TestSheets.Fighter with { Weapon = TestConditions.Longbow }, T(6, 1))
            .Party("bow2", TestSheets.Fighter with { Weapon = TestConditions.Longbow }, T(6, 3));
        var (state, _) = battle.Start();

        var plan = Ai.Plan(state, U("archer"), battle.Rules);

        Assert.Equal(T(2, 2), EndTile(state, U("archer"), plan));
        Assert.NotNull(UseOf(plan, "common.attack"));
        Assert.True(plan.Commands.IndexOf(plan.Commands.OfType<Move>().Single()) < plan.Commands.IndexOf(UseOf(plan, "common.attack")!),
            plan.Explain);
        ApplyAll(state, plan, battle.Rules);
    }

    [Fact]
    public void Archer_shoots_at_its_weapons_full_range()
    {
        // The longbow reaches 8; common.attack's own range is 1. The archer can't move, so it must shoot from where it stands.
        var battle = On("""
            0. 0. 0. 0. 0. 0. 0. 0. 0. 0.
            """)
            .Enemy("archer", First(TestSheets.Fighter with { Weapon = TestConditions.Longbow, Move = 0 }), T(0, 0),
                new AiControlled("archer"))
            .Party("kara", TestSheets.Fighter, T(8, 0));
        var (state, _) = battle.Start();

        var plan = Ai.Plan(state, U("archer"), battle.Rules);

        Assert.True(UseOf(plan, "common.attack")?.Target.Unit == U("kara"), plan.Explain);
        ApplyAll(state, plan, battle.Rules);
    }

    [Fact]
    public void Brute_finishes_low_hp_enemy()
    {
        // The fighter (AC 18) has 2 HP left; the rogue (AC 14) is easier to hit but can't be killed with one blow.
        var battle = On("""
            0. 0. 0. 0. 0.
            0. 0. 0. 0. 0.
            0. 0. 0. 0. 0.
            """)
            .Enemy("brute", First(TestSheets.Goblin), T(2, 1), new AiControlled("brute"))
            .Party("kara", TestSheets.Fighter, T(1, 1))
            .Party("vex", TestSheets.Rogue, T(3, 1));
        var state = battle.Start().State.Change("kara", u => u with { Hp = 2 });

        var plan = Ai.Plan(state, U("brute"), battle.Rules);

        Assert.Equal(U("kara"), UseOf(plan, "common.attack")?.Target.Unit);
        ApplyAll(state, plan, battle.Rules);
    }

    [Fact]
    public void Healer_heals_ally_below_half()
    {
        var cureWounds = A("cleric.cure_wounds");
        var battle = On("""
            0. 0. 0. 0. 0.
            0. 0. 0. 0. 0.
            0. 0. 0. 0. 0.
            """).With(cureWounds)
            .Enemy("priest", First(TestSheets.Cleric.Knowing(cureWounds)), T(2, 1), new AiControlled("healer"))
            .Enemy("gob", TestSheets.Goblin, T(2, 0))
            .Party("kara", TestSheets.Fighter, T(1, 1));
        var (start, _) = battle.Start();
        var wounded = start.Change("gob", u => u with { Hp = 3 });   // 3 of 7

        var plan = Ai.Plan(wounded, U("priest"), battle.Rules);

        Assert.Equal(U("gob"), UseOf(plan, "cleric.cure_wounds")?.Target.Unit);
        ApplyAll(wounded, plan, battle.Rules);

        // Unhurt, the goblin needs no healing.
        Assert.Null(UseOf(Ai.Plan(start, U("priest"), battle.Rules), "cleric.cure_wounds"));
    }

    [Fact]
    public void Avoids_ending_on_high_threat_tile_when_cautious()
    {
        // Three swordsmen can reach x = 4 next round and strike x ≥ 3; the archer can still shoot (8, 2) from (2, 2).
        var battle = On("""
            0. 0. 0. 0. 0. 0. 0. 0. 0. 0.
            0. 0. 0. 0. 0. 0. 0. 0. 0. 0.
            0. 0. 0. 0. 0. 0. 0. 0. 0. 0.
            0. 0. 0. 0. 0. 0. 0. 0. 0. 0.
            0. 0. 0. 0. 0. 0. 0. 0. 0. 0.
            """).With(A("goblin.nimble_escape")).WithConditions(C("disengaged"))
            .Enemy("archer", First(Monster("goblin_archer")), T(4, 2), new AiControlled("archer"))
            .Party("a", TestSheets.Fighter, T(8, 1))
            .Party("b", TestSheets.Fighter, T(8, 2))
            .Party("c", TestSheets.Fighter, T(8, 3));
        var (state, _) = battle.Start();
        var threat = ThreatMap.For(state, Side.Enemy, battle.Rules);

        var plan = Ai.Plan(state, U("archer"), battle.Rules);

        var end = EndTile(state, U("archer"), plan);
        Assert.True(threat.At(T(4, 2)) > 0);
        Assert.True(threat.At(end) == 0, $"Ended on {end} (threat {threat.At(end)}).\n{plan.Explain}");
        Assert.True(U("b") == UseOf(plan, "common.attack")?.Target.Unit, plan.Explain);
        ApplyAll(state, plan, battle.Rules);
    }

    [Fact]
    public void Never_targets_downed_unless_cruel()
    {
        // The rogue lies Downed next to the goblin; the fighter is out of reach this turn.
        TestBattle Battle(string profile) => On("""
            0. 0. 0. 0. 0. 0. 0. 0. 0. 0. 0. 0.
            0. 0. 0. 0. 0. 0. 0. 0. 0. 0. 0. 0.
            0. 0. 0. 0. 0. 0. 0. 0. 0. 0. 0. 0.
            """)
            .Enemy("gob", First(TestSheets.Goblin), T(1, 1), new AiControlled(profile))
            .Party("vex", TestSheets.Rogue, T(2, 1))
            .Party("kara", TestSheets.Fighter, T(11, 1));

        foreach (var (profile, cruel) in new[] { ("brute", false), ("boss", false), ("boss_cruel", true) })
        {
            var battle = Battle(profile);
            var state = battle.Start().State.Change("vex", u => u with { Hp = 0, Life = LifeState.Downed, Countdown = 3 });

            var plan = Ai.Plan(state, U("gob"), battle.Rules);

            var attacksDowned = plan.Commands.OfType<Use>().Any(u => u.Target.Unit == U("vex"));
            Assert.True(attacksDowned == cruel, $"{profile}: {plan.Explain}");
            ApplyAll(state, plan, battle.Rules);
        }
    }

    [Fact]
    public void Bonus_action_never_strikes_a_player_the_main_action_may_have_downed()
    {
        // The priest's mace can down Kara (1 HP); its spiritual weapon (a bonus action) must then go to Vex (sturdy, so no
        // kill is on offer there), not to her: she may be Downed by then.
        var weapon = A("cleric.spiritual_weapon");
        var priest = First(TestSheets.Cleric.Knowing(weapon)) with { Resources = Ordinal.Map(("devotion", 2)) };
        TestBattle Battle(string profile) => On("""
            0. 0. 0. 0. 0. 0.
            0. 0. 0. 0. 0. 0.
            """).With(weapon)
            .Enemy("priest", priest, T(1, 0), new AiControlled(profile))
            .Party("kara", TestSheets.Fighter, T(0, 0))
            .Party("vex", TestSheets.Rogue with { MaxHp = 30 }, T(5, 1));

        var battle = Battle("brute");
        var state = battle.Start().State.Change("kara", u => u with { Hp = 1 });
        var plan = Ai.Plan(state, U("priest"), battle.Rules);

        Assert.True(U("kara") == UseOf(plan, "common.attack")?.Target.Unit, plan.Explain);
        Assert.True(U("vex") == UseOf(plan, "cleric.spiritual_weapon")?.Target.Unit, plan.Explain);
        ApplyAll(state, plan, battle.Rules);
    }

    [Fact]
    public void Marks_its_quarry_when_its_own_hits_then_deal_more()
    {
        // The mark does nothing to the goblin by itself: its worth is the 1d6 the archer's own passive adds to every hit on a
        // marked foe (Hunter's Mark). The archer shoots, then marks the goblin with its bonus action.
        var quarry = new ConditionDef(new ConditionId("quarry"), "Quarry", null, [], Ordinal.Set("magical"));
        var mark = new AbilityDef(new AbilityId("test.mark"), "Mark", Tier: 1,
            new CostDef(Bonus: 1, Resource: Ordinal.Map(("focus", 1)), Concentration: true),
            new TargetDef(TargetKind.Enemy, Range: 6, Los: true, new AreaDef(AreaShape.Single)),
            [new ApplyEffect(quarry.Id, Rounds: null)]);
        var bonus = new PassiveDef("test.mark_bonus", HookKind.OnAttackHit, new WhenDef(TargetHasCondition: quarry.Id),
            [new DamageEffect(TestAbilities.Flat("1d6"), "force", [])], null);
        var archer = First(TestSheets.Fighter with { Weapon = TestConditions.Longbow, Side = Side.Enemy, Resources = Ordinal.Map(("focus", 2)) })
            .Knowing(mark) with { Passives = [bonus.Id] };
        var battle = On("""
            0. 0. 0. 0. 0. 0.
            0. 0. 0. 0. 0. 0.
            """).With(mark).WithPassives(bonus).WithConditions(quarry)
            .Enemy("archer", archer, T(0, 0), new AiControlled("archer"))
            .Party("vex", TestSheets.Rogue with { MaxHp = 40 }, T(5, 1));
        var state = battle.Start().State;

        var plan = Ai.Plan(state, U("archer"), battle.Rules);

        Assert.True(U("vex") == UseOf(plan, "test.mark")?.Target.Unit, plan.Explain);
        var after = ApplyAll(state, plan, battle.Rules);
        Assert.Contains(after.Units[U("vex")].Conditions, c => c.Id == quarry.Id);

        // Once marked, marking it again is worth nothing.
        var marked = state.Give("vex", TestConditions.Lasting(quarry, U("archer")));
        Assert.Null(UseOf(Ai.Plan(marked, U("archer"), battle.Rules), "test.mark"));

        // A passive that re-applies the quarry condition on hit must not recurse forever when scoring QuarryDamage.
        var reapply = bonus with { Effects = [new DamageEffect(TestAbilities.Flat("1d6"), "force", []), new ApplyEffect(quarry.Id, Rounds: 2)] };
        var reapplyingBattle = On("0. 0. 0. 0. 0. 0.")
            .With(mark).WithPassives(reapply).WithConditions(quarry)
            .Enemy("archer", archer with { Passives = [reapply.Id] }, T(0, 0), new AiControlled("archer"))
            .Party("vex", TestSheets.Rogue with { MaxHp = 40 }, T(5, 0));
        var reapplyingPlan = Ai.Plan(reapplyingBattle.Start().State, U("archer"), reapplyingBattle.Rules);
        Assert.Equal(U("vex"), UseOf(reapplyingPlan, "test.mark")?.Target.Unit);
    }

    [Fact]
    public void Plan_scores_do_not_depend_on_the_order_tiles_are_scored_in()
    {
        // Mirror images, so the planner scores the tiles near the goblin first in one and last in the other. Bless (a burst
        // that includes its caster) on the goblin alone isn't worth its Devotion, but with the priest inside its area it is:
        // the same aim must be worth more from a tile inside the area than from one outside, whichever is scored first.
        var bless = A("cleric.bless");
        var priest = First(TestSheets.Cleric.Knowing(bless)) with { Move = 2, Resources = Ordinal.Map(("devotion", 10)) };
        TurnPlan PlanOn(bool mirrored)
        {
            Tile At(int x, int y) => mirrored ? T(14 - x, y) : T(x, y);
            var battle = On("""
                0. 0. 0. 0. 0. 0. 0. 0. 0. 0. 0. 0. 0. 0. 0.
                0. 0. 0. 0. 0. 0. 0. 0. 0. 0. 0. 0. 0. 0. 0.
                """).With(bless)
                .Enemy("priest", priest, At(3, 0), new AiControlled("healer"))
                .Enemy("gob", TestSheets.Goblin, At(0, 0))
                .Party("kara", TestSheets.Fighter, At(14, 1));
            return Ai.Plan(battle.Start().State, U("priest"), battle.Rules);
        }

        var (plain, mirror) = (PlanOn(mirrored: false), PlanOn(mirrored: true));

        Assert.True(Math.Abs(plain.Score - mirror.Score) < 1e-6, $"{plain.Explain}\n{mirror.Explain}");
    }

    [Fact]
    public void Fireball_aim_avoids_allies()
    {
        var fireball = A("wizard.fireball");
        var mage = First(TestSheets.Cleric) with
        {
            Level = 5, CastingStat = "Int", Scores = TestSheets.Scores(8, 14, 12, 16, 10, 10), Resources = Ordinal.Map(("mana", 3)),
            Abilities = [fireball.Id],
        };
        var battle = On("""
            0. 0. 0. 0. 0. 0. 0. 0. 0. 0.
            0. 0. 0. 0. 0. 0. 0. 0. 0. 0.
            0. 0. 0. 0. 0. 0. 0. 0. 0. 0.
            0. 0. 0. 0. 0. 0. 0. 0. 0. 0.
            0. 0. 0. 0. 0. 0. 0. 0. 0. 0.
            0. 0. 0. 0. 0. 0. 0. 0. 0. 0.
            0. 0. 0. 0. 0. 0. 0. 0. 0. 0.
            0. 0. 0. 0. 0. 0. 0. 0. 0. 0.
            """).With(fireball)
            .Enemy("mage", mage, T(6, 7), new AiControlled("caster"))
            .Enemy("gob", TestSheets.Goblin, T(4, 2))
            .Party("a", TestSheets.Fighter, T(5, 2))
            .Party("b", TestSheets.Fighter, T(6, 2))
            .Party("c", TestSheets.Fighter, T(7, 2));
        var (state, _) = battle.Start();

        var plan = Ai.Plan(state, U("mage"), battle.Rules);

        var cast = UseOf(plan, "wizard.fireball");
        Assert.NotNull(cast);
        var aim = Assert.NotNull(cast.Target.Tile);
        Assert.True(Reach.Distance(aim, T(4, 2)) > 2, $"The blast at {aim} catches the goblin.\n{plan.Explain}");
        Assert.All(new[] { T(5, 2), T(6, 2), T(7, 2) }, t => Assert.True(Reach.Distance(aim, t) <= 2, $"{t} missed by {aim}"));
        ApplyAll(state, plan, battle.Rules);
    }

    [Fact]
    public void Coward_flees_below_30_percent()
    {
        TestBattle Battle() => On("""
            0. 0. 0. 0. 0. 0. 0. 0. 0. 0.
            0. 0. 0. 0. 0. 0. 0. 0. 0. 0.
            0. 0. 0. 0. 0. 0. 0. 0. 0. 0.
            """).With(A("goblin.nimble_escape")).WithConditions(C("disengaged"))
            .Enemy("kob", First(TestSheets.Goblin.Knowing(A("goblin.nimble_escape"))), T(3, 1), new AiControlled("coward"))
            .Party("kara", TestSheets.Fighter, T(2, 1));

        // At 2 of 7 HP (under 30%) it slips away without attacking.
        var battle = Battle();
        var hurt = battle.Start().State.Change("kob", u => u with { Hp = 2 });
        var plan = Ai.Plan(hurt, U("kob"), battle.Rules);

        Assert.Null(UseOf(plan, "common.attack"));
        Assert.True(Reach.Distance(EndTile(hurt, U("kob"), plan), T(2, 1)) >= 4, plan.Explain);
        var after = ApplyAll(hurt, plan, battle.Rules);
        Assert.Equal(2, after.Units[U("kob")].Hp);   // it disengaged first, so no opportunity attack

        // Unhurt, it fights.
        var fresh = Battle();
        var (state, _) = fresh.Start();
        Assert.Equal(U("kara"), UseOf(Ai.Plan(state, U("kob"), fresh.Rules), "common.attack")?.Target.Unit);
    }

    [Fact]
    public void Difficulty_scales_focus_fire_caution_and_hoard()
    {
        var brute = AiProfile.From(Base.AiProfiles["brute"]);
        var story = brute.ForDifficulty(Difficulty.Story);
        var hard = brute.ForDifficulty(Difficulty.Hard);

        Assert.Equal(brute, brute.ForDifficulty(Difficulty.Normal));
        Assert.Equal((brute.FocusFire * 0.5, brute.Caution * 0.5, brute.Hoard * 1.5), (story.FocusFire, story.Caution, story.Hoard));
        Assert.Equal((brute.FocusFire * 1.5, brute.Caution * 1.25, brute.Hoard * 0.75), (hard.FocusFire, hard.Caution, hard.Hoard));
        Assert.Equal(brute with { FocusFire = 0, Caution = 0, Hoard = 0 }, hard with { FocusFire = 0, Caution = 0, Hoard = 0 });

        // A unit plays its controller's profile, scaled by the battle's difficulty; an unknown id plays the default weights.
        var state = On("0. 0.").Enemy("gob", TestSheets.Goblin, T(0, 0), new AiControlled("brute")).Party("kara", TestSheets.Fighter, T(1, 0))
            .Start().State with { Difficulty = Difficulty.Hard };
        Assert.Equal(hard, AiProfile.For(state, U("gob"), Base));
        Assert.Equal(AiProfile.From(Base.AiProfiles["coward"]).ForDifficulty(Difficulty.Hard), AiProfile.For(state, U("gob"), Base, "coward"));
        Assert.Equal((AiProfile.Default with { Id = "nobody" }).ForDifficulty(Difficulty.Hard), AiProfile.For(state, U("gob"), Base, "nobody"));
    }

    [Fact]
    public void Plan_is_deterministic()
    {
        var rules = Base;
        var state = Battle.Start(Encounters.Setup("river_crossing", AiParty(4, 3, rules), Difficulty.Normal, rules), rules, seed: 11).State;

        var plans = Enumerable.Range(0, 3).Select(_ => Ai.Plan(state, state.Active!.Value, rules)).ToArray();

        var json = plans.Select(p => JsonSerializer.Serialize(p.Commands, ArcJson.Options) + p.Score.ToString("R") + p.Explain).ToArray();
        Assert.Equal(json[0], json[1]);
        Assert.Equal(json[0], json[2]);
        Assert.NotEmpty(plans[0].Commands);
        Assert.IsType<EndTurn>(plans[0].Commands[^1]);
    }

    [Fact]
    public void Every_plan_command_is_accepted_by_Apply()
    {
        var rules = Base;
        int[] levels = [1, 3, 5, 7, 10];
        Difficulty[] difficulties = [Difficulty.Story, Difficulty.Normal, Difficulty.Hard];
        var used = new SortedSet<string>(StringComparer.Ordinal);
        for (ulong seed = 1; seed <= 50; seed++)
        {
            var level = levels[seed % (ulong)levels.Length];
            var size = 4 + (int)(seed % 3);
            var setup = Encounters.Setup("river_crossing", AiParty(size, level, rules), difficulties[seed % 3], rules);
            var state = Battle.Start(setup, rules, seed).State;
            var options = new AiOptions(AllowItems: seed % 5 == 0);   // the party's starting kits carry potions

            for (var turn = 0; turn < 40 && state.Outcome is null; turn++)
            {
                var unit = state.Active!.Value;
                var plan = Ai.Plan(state, unit, rules, options);
                Assert.IsType<EndTurn>(plan.Commands[^1]);
                Assert.True(options.AllowItems || !plan.Commands.OfType<Use>().Any(u => u.Item is not null || u.Ability == Battle.UseItem),
                    plan.Explain);
                foreach (var command in plan.Commands)
                {
                    var resolution = Battle.Apply(state, command, rules);
                    Assert.True(resolution.Rejected is null,
                        $"Seed {seed}, level {level}, turn {turn}: {unit} {JsonSerializer.Serialize(command, ArcJson.Options)} was refused "
                        + $"({resolution.Rejected?.Reason}).\n{plan.Explain}");
                    state = resolution.State;
                    if (command is Use use)
                        used.Add(use.Ability.Value);
                    // A move cut short (an opportunity attack downed the mover) or a unit that can no longer act leaves the rest
                    // of the plan stale: plan again from here.
                    if (state.Outcome is not null || state.Active != unit
                        || command is Move move && state.Units[unit].Pos != move.Path[^1]
                        || !Modifiers.CanAct(state, unit, rules) && command is not EndTurn)
                        break;
                }
            }
        }
        Assert.Contains("common.attack", used);
        Assert.Contains(used, a => a.StartsWith("wizard.", StringComparison.Ordinal));
        Assert.Contains(used, a => a.StartsWith("cleric.", StringComparison.Ordinal));
    }

    /// <summary>A party of default characters (fighter, cleric, rogue, wizard, …) at <paramref name="level"/>, all run by the AI.</summary>
    private static ImmutableArray<PartyMember> AiParty(int size, int level, RulesPack rules)
    {
        string[] classes = ["fighter", "cleric", "rogue", "wizard"];
        var profiles = new Dictionary<string, string>
        {
            ["fighter"] = "brute", ["cleric"] = "healer", ["rogue"] = "skirmisher", ["wizard"] = "caster",
        };
        return [.. Enumerable.Range(0, size).Select(i =>
        {
            var cls = classes[i % classes.Length];
            var id = i < classes.Length ? cls : $"{cls}{i / classes.Length + 1}";
            return new PartyMember(U(id), SheetBuilder.DefaultCharacter(id, "human", cls, null, level, rules), new AiControlled(profiles[cls]));
        })];
    }
}
