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
    public void Nobody_helps_an_ally_with_no_foe_in_reach()
    {
        // Two goblins cut off from the party by a wall (as goblins across the river were in play): nothing they do this
        // turn reaches a foe, and Help's advantage would go to waste, so neither spends its action on it.
        string Row() => string.Join(' ', Enumerable.Range(0, 12).Select(x => x == 6 ? "0#" : "0."));
        var battle = On(string.Join('\n', Enumerable.Repeat(Row(), 3)))
            .With(A("common.help")).WithConditions(C("helped"))
            .Enemy("gob1", First(Monster("goblin")), T(10, 1), new AiControlled("brute"))
            .Enemy("gob2", Monster("goblin"), T(10, 2), new AiControlled("brute"))
            .Party("kara", TestSheets.Fighter, T(1, 1));
        var (state, _) = battle.Start();

        var plan = Ai.Plan(state, U("gob1"), battle.Rules);

        Assert.True(UseOf(plan, "common.help") is null, plan.Explain);
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
    public void An_area_spell_keeps_off_a_Downed_hero_when_an_equal_aim_exists_unless_the_caster_is_cruel()
    {
        // #110: every blast that catches Aldo and Bree is worth the same; the ones centred at (4, 2) and (5, 2) also catch Cass,
        // who is Downed. Fair play keeps the fireball off her; a cruel caster aims at her too.
        var fireball = A("wizard.fireball");
        var mage = First(TestSheets.Cleric) with
        {
            Level = 5, CastingStat = "Int", Scores = TestSheets.Scores(8, 14, 12, 16, 10, 10), Resources = Ordinal.Map(("mana", 3)),
            Abilities = [fireball.Id], Move = 0,
        };
        Tile Aim(string profile)
        {
            var battle = On(string.Join('\n', Enumerable.Repeat(string.Join(' ', Enumerable.Repeat("0.", 12)), 5))).With(fireball)
                .Enemy("mage", mage, T(6, 4), new AiControlled(profile))
                .Party("aldo", TestSheets.Fighter, T(5, 2))
                .Party("bree", TestSheets.Fighter, T(6, 2))
                .Party("cass", TestSheets.Fighter, T(3, 2));
            var state = battle.Start().State.Change("cass", u => u with { Hp = 0, Life = LifeState.Downed, Countdown = 3 });
            var plan = Ai.Plan(state, U("mage"), battle.Rules);
            var aim = UseOf(plan, "wizard.fireball")?.Target.Tile;
            Assert.True(aim is { } a && Reach.Distance(a, T(5, 2)) <= 2 && Reach.Distance(a, T(6, 2)) <= 2, plan.Explain);
            ApplyAll(state, plan, battle.Rules);
            return aim!.Value;
        }

        Assert.True(Reach.Distance(Aim("caster"), T(3, 2)) > 2);
        Assert.True(Reach.Distance(Aim("boss_cruel"), T(3, 2)) <= 2);
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
    private static BattleState Hazard(BattleState state, string ability, params Tile[] tiles)
    {
        var zone = Assert.IsType<ZoneEffect>(A(ability).Effects.First(e => e is ZoneEffect));
        return state with
        {
            Zones = [new Zone(1, U("kara"), zone.Kind, [.. tiles], 10, false, zone.Difficult, zone.Passives)],
            NextSerial = 2,
        };
    }

    [Theory]
    [InlineData("wizard.wall_of_fire")]
    [InlineData("druid.spike_growth")]
    [InlineData("druid.moonbeam")]
    public void Avoids_hazardous_high_ground_when_a_near_equal_safe_shot_exists(string ability)
    {
        // The northern perch is closer. Its existing spell zone must outweigh that one-step benefit.
        var battle = On("""
            0. 0. 1. 0. 0. 0. 0.
            0. 0. 0. 0. 0. 0. 0.
            0. 0. 0. 1. 0. 0. 0.
            """).With(A(ability))
            .Enemy("archer", First(TestSheets.Fighter with { Weapon = TestConditions.Longbow }), T(1, 1), new AiControlled("archer"))
            .Party("kara", TestSheets.Cleric with { Move = 0 }, T(6, 1));
        var state = Hazard(battle.Start().State, ability, T(2, 0));
        var plan = Ai.Plan(state, U("archer"), battle.Rules);
        Assert.NotEqual(T(2, 0), EndTile(state, U("archer"), plan));
        Assert.NotNull(UseOf(plan, "common.attack"));
        ApplyAll(state, plan, battle.Rules);
    }

    [Theory]
    [InlineData("wizard.wall_of_fire")]
    [InlineData("druid.spike_growth")]
    [InlineData("druid.moonbeam")]
    public void Aggressive_unit_crosses_a_hazard_when_it_is_the_only_way_to_hit(string ability)
    {
        var battle = On("0. 0. 0. 0. 0.").With(A(ability))
            .Enemy("brute", First(TestSheets.Fighter with { MaxHp = 100, Move = 5 }), T(0, 0), new AiControlled("brute"))
            .Party("kara", TestSheets.Cleric with { MaxHp = 1, Move = 0 }, T(4, 0));
        var state = Hazard(battle.Start().State, ability, T(1, 0), T(2, 0));
        var plan = Ai.Plan(state, U("brute"), battle.Rules);
        Assert.True(UseOf(plan, "common.attack")?.Target.Unit == U("kara"), plan.Explain);
        Assert.Contains(plan.Commands.OfType<Move>().SelectMany(m => m.Path), t => t == T(1, 0));
        ApplyAll(state, plan, battle.Rules);
    }

    [Fact]
    public void Leaving_an_enter_only_zone_does_not_charge_for_the_tile_already_occupied()
    {
        var battle = On("0. 0. 0. 0. 0.").With(A("druid.spike_growth"))
            .Enemy("brute", First(TestSheets.Fighter), T(0, 0), new AiControlled("brute"))
            .Party("kara", TestSheets.Cleric with { Move = 0 }, T(3, 0));
        var plain = battle.Start().State;
        var state = Hazard(plain, "druid.spike_growth", T(0, 0));
        var expected = Ai.Plan(plain, U("brute"), battle.Rules);
        var actual = Ai.Plan(state, U("brute"), battle.Rules);
        Assert.Equal(JsonSerializer.Serialize(expected.Commands, ArcJson.Options), JsonSerializer.Serialize(actual.Commands, ArcJson.Options));
        Assert.Equal(expected.Score, actual.Score);
    }

    [Fact]
    public void A_safe_destination_does_not_hide_spike_damage_along_the_route()
    {
        var battle = On("""
            0. 0. 0. 1. 0. 0. 0.
            0. 0# 0# 0# 0# 0. 0.
            0. 0. 0. 0. 1. 0. 0.
            """).With(A("druid.spike_growth"))
            .Enemy("archer", First(TestSheets.Fighter with { Weapon = TestConditions.Longbow, Move = 6 }), T(0, 1), new AiControlled("archer"))
            .Party("kara", TestSheets.Cleric with { Move = 0 }, T(6, 1));
        var state = Hazard(battle.Start().State, "druid.spike_growth", T(2, 0));
        var plan = Ai.Plan(state, U("archer"), battle.Rules);
        Assert.DoesNotContain(plan.Commands.OfType<Move>().SelectMany(m => m.Path), t => t == T(2, 0));
        Assert.NotNull(UseOf(plan, "common.attack"));
        ApplyAll(state, plan, battle.Rules);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void End_turn_hazards_respect_immunity(bool immune, bool takesPerch)
    {
        var battle = On("""
            0. 0. 1. 0. 0. 0. 0.
            0. 0. 0. 0. 0. 0. 0.
            0. 0. 0. 1. 0. 0. 0.
            """).With(A("wizard.wall_of_fire"))
            .Enemy("archer", First(TestSheets.Fighter with { Weapon = TestConditions.Longbow,
                Immune = immune ? Ordinal.Set("fire") : Ordinal.Set() }), T(1, 1), new AiControlled("archer"))
            .Party("kara", TestSheets.Cleric with { Move = 0 }, T(6, 1));
        var state = Hazard(battle.Start().State, "wizard.wall_of_fire", T(2, 0));
        state = state with { Zones = [state.Zones[0] with { Passives = [.. state.Zones[0].Passives.Select(p => p with { Hook = HookKind.OnTurnEnd })] }] };
        var before = JsonSerializer.Serialize(state, ArcJson.Options);
        var plan = Ai.Plan(state, U("archer"), battle.Rules);
        Assert.Equal(takesPerch, EndTile(state, U("archer"), plan) == T(2, 0));
        Assert.Equal(before, JsonSerializer.Serialize(state, ArcJson.Options));
        Assert.Equal(plan.Explain, Ai.Plan(state, U("archer"), battle.Rules).Explain);
        ApplyAll(state, plan, battle.Rules);
    }

    [Fact]
    public void An_enter_passive_already_spent_this_turn_does_not_deter_a_move()
    {
        var battle = On("""
            0. 0. 1. 0. 0. 0. 0.
            0. 0. 0. 0. 0. 0. 0.
            0. 0. 0. 1. 0. 0. 0.
            """).With(A("druid.spike_growth"))
            .Enemy("archer", First(TestSheets.Fighter with { Weapon = TestConditions.Longbow }), T(1, 1), new AiControlled("archer"))
            .Party("kara", TestSheets.Cleric with { Move = 0 }, T(6, 1));
        var state = Hazard(battle.Start().State, "druid.spike_growth", T(2, 0));
        state = state with { Zones = [state.Zones[0] with { Passives = [.. state.Zones[0].Passives.Select(p => p with { OncePerTurn = true })] }] };
        state = state.Change("archer", u => u with { UsedThisTurn = Ordinal.Set("druid.spike_growth.step") });
        var plan = Ai.Plan(state, U("archer"), battle.Rules);
        Assert.Equal(T(2, 0), EndTile(state, U("archer"), plan));
        ApplyAll(state, plan, battle.Rules);
    }

    [Fact]
    public void Spike_growth_on_the_first_step_is_counted_even_when_the_destination_is_adjacent()
    {
        var battle = On("""
            0. 1. 0. 0. 0.
            0. 1. 0. 0. 0.
            """).With(A("druid.spike_growth"))
            .Enemy("archer", First(TestSheets.Fighter with { Weapon = TestConditions.Longbow }), T(0, 0), new AiControlled("archer"))
            .Party("kara", TestSheets.Cleric with { Move = 0 }, T(4, 0));
        var state = Hazard(battle.Start().State, "druid.spike_growth", T(1, 0));
        var plan = Ai.Plan(state, U("archer"), battle.Rules);
        Assert.NotEqual(T(1, 0), EndTile(state, U("archer"), plan));
        Assert.NotNull(UseOf(plan, "common.attack"));
        ApplyAll(state, plan, battle.Rules);
    }

    [Fact]
    public void Moonbeam_danger_uses_the_zone_casters_save_dc_and_exact_half_damage_rounding()
    {
        var battle = On("0. 0. 0. 0. 0. 0. 0.").With(A("druid.moonbeam"))
            .Enemy("archer", First(TestSheets.Fighter with { MaxHp = 100, Move = 0 }), T(0, 0), new AiControlled("archer"))
            .Party("kara", TestSheets.Cleric with { Move = 0 }, T(6, 0));
        var plain = battle.Start().State;
        var state = Hazard(plain, "druid.moonbeam", T(0, 0));
        var plainScore = Ai.Plan(plain, U("archer"), battle.Rules).Score;
        var hazardScore = Ai.Plan(state, U("archer"), battle.Rules).Score;
        // Caster WIS 16 gives DC 13; fighter CON save +4 fails 8/20. 2d10 averages 11, rounded halves average 5.25.
        var damage = .4 * 11 + .6 * 5.25;
        Assert.Equal(Base.AiProfiles["archer"].Caution * .5 * damage, plainScore - hazardScore, precision: 6);
    }

    [Theory]
    [InlineData(false, false, null, 2)]
    [InlineData(true, false, null, 1)]
    [InlineData(false, true, null, 1)]
    [InlineData(false, false, 1, 1)]
    public void Overlapping_zones_honor_shared_passive_limits(bool perTurn, bool perRound, int? uses, int expectedTicks)
    {
        var battle = On("0. 0. 0. 0. 0. 0. 0.").With(A("druid.moonbeam"))
            .Enemy("archer", First(TestSheets.Fighter with { MaxHp = 100, Move = 0 }), T(0, 0), new AiControlled("archer"))
            .Party("kara", TestSheets.Cleric with { Move = 0 }, T(6, 0));
        var plain = battle.Start().State;
        var state = Hazard(plain, "druid.moonbeam", T(0, 0));
        var passive = state.Zones[0].Passives.Single(p => p.Hook == HookKind.OnTurnStart)
            with { Hook = HookKind.OnTurnEnd, OncePerTurn = perTurn, OncePerRound = perRound, UsesPerBattle = uses };
        var zone = state.Zones[0] with { Passives = [passive] };
        state = state with { Zones = [zone, zone with { Serial = 2 }], NextSerial = 3 };
        var difference = Ai.Plan(plain, U("archer"), battle.Rules).Score - Ai.Plan(state, U("archer"), battle.Rules).Score;
        Assert.Equal(expectedTicks * Base.AiProfiles["archer"].Caution * .5 * 7.55, difference, precision: 6);
    }

    [Fact]
    public void Zone_hook_conditions_test_the_affected_unit_instead_of_the_caster()
    {
        var battle = On("0. 0. 0. 0. 0. 0. 0.").With(A("druid.moonbeam"))
            .Enemy("archer", First(TestSheets.Fighter with { Move = 0 }), T(0, 0), new AiControlled("archer"))
            .Party("kara", TestSheets.Goblin with { Move = 0 }, T(6, 0));
        var plain = battle.Start().State;
        var state = Hazard(plain, "druid.moonbeam", T(0, 0));
        state = state with { Zones = [state.Zones[0] with { Passives = [.. state.Zones[0].Passives.Select(p =>
            p with { When = new WhenDef(TargetTag: "goblinoid") })] }] };
        Assert.Equal(Ai.Plan(plain, U("archer"), battle.Rules).Score, Ai.Plan(state, U("archer"), battle.Rules).Score);
    }

}
