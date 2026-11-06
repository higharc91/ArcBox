namespace Tactics.Logic.Tests;

/// <summary>
/// #110: the abilities the planner never used to plan. Teleports and leaps (empty-tile aims scored by where they land), a
/// summon placed by the foes, Action Surge (worth the best second action), a bonus-action Dash, and move–act–move for the
/// ranged and skirmisher profiles.
/// </summary>
public sealed class AbilityPlanningTests
{
    private static RulesPack Base => BasePackTests.Rules;

    private static UnitId U(string id) => new(id);

    private static Tile T(int x, int y) => new(x, y);

    private static AbilityDef A(string id) => Base.Abilities[new AbilityId(id)];

    private static TestBattle On(string ascii) => TestBattle.On(ascii).WithAiProfiles(Base.AiProfiles.Values);

    /// <summary>A sheet that acts first.</summary>
    private static UnitSheet First(UnitSheet sheet) => sheet with { InitiativeBonus = 30 };

    /// <summary>A level-5 INT caster (AC 12, 20 HP, Move 6) who knows <paramref name="abilities"/>.</summary>
    private static UnitSheet Mage(params AbilityDef[] abilities) => First(TestSheets.Cleric) with
    {
        Level = 5, CastingStat = "Int", Scores = TestSheets.Scores(8, 14, 12, 16, 10, 10), Ac = 12, MaxHp = 20, Move = 6,
        Resources = Ordinal.Map(("mana", 6)), Abilities = [.. abilities.Select(a => a.Id)],
    };

    private static string Open(int width, int height)
        => string.Join('\n', Enumerable.Repeat(string.Join(' ', Enumerable.Repeat("0.", width)), height));

    /// <summary>Applies every command of <paramref name="plan"/>, which must all be accepted; reactions are passed.</summary>
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
                state = Battle.Apply(state, new React(offer.Reactor, offer.Options.First(o => o.Id == Reactions.Pass)), rules).State;
        }
        return state;
    }

    private static Use? UseOf(TurnPlan plan, string ability)
        => plan.Commands.OfType<Use>().FirstOrDefault(u => u.Ability.Value == ability);

    [Fact]
    public void An_archer_steps_out_shoots_and_steps_back()
    {
        // The swordsmen reach x ≤ 5 next round (Move 6, then a step); the shortbow reaches x = 6 from x = 12. The archer
        // walks out to shoot and spends the rest of its Move getting back out of their reach.
        var shortbow = TestConditions.Longbow with { Id = "shortbow", Range = 6 };
        var battle = On(Open(14, 2))
            .Enemy("archer", First(TestSheets.Fighter with { Weapon = shortbow, Ac = 12, Move = 6 }), T(2, 0), new AiControlled("archer"))
            .Party("a", TestSheets.Fighter with { Move = 6 }, T(12, 0))
            .Party("b", TestSheets.Fighter with { Move = 6 }, T(12, 1));
        var state = battle.Start().State;
        var threat = ThreatMap.For(state, Side.Enemy, battle.Rules);

        var plan = Ai.Plan(state, U("archer"), battle.Rules);

        Assert.True(plan.Commands is [Move, Use { Ability.Value: "common.attack" }, Move, EndTurn], plan.Explain);
        var (stepOut, stepBack) = ((Move)plan.Commands[0], (Move)plan.Commands[2]);
        Assert.True(threat.At(stepOut.Path[^1]) > 0, plan.Explain);
        Assert.True(threat.At(stepBack.Path[^1]) == 0, plan.Explain);
        ApplyAll(state, plan, battle.Rules);
    }

    [Fact]
    public void An_archer_does_not_plan_to_walk_on_from_a_web()
    {
        // As above (with Move enough to step back past the web's difficult ground), but the tiles it shoots from are webbed: a
        // web it walks into may hold it (Restrained), so it plans no second walk after the shot.
        var shortbow = TestConditions.Longbow with { Id = "shortbow", Range = 6 };
        var battle = On(Open(14, 2)).With(A("wizard.web")).WithConditions(Base.Conditions[new ConditionId("restrained")])
            .Enemy("archer", First(TestSheets.Fighter with { Weapon = shortbow, Ac = 12, Move = 8 }), T(2, 0), new AiControlled("archer"))
            .Party("a", TestSheets.Fighter with { Move = 6 }, T(12, 0))
            .Party("b", TestSheets.Fighter with { Move = 6 }, T(12, 1));
        var web = A("wizard.web").Effects.OfType<ZoneEffect>().Single();
        var state = battle.Start().State with
        {
            Zones = [new Zone(1, null, web.Kind, [T(6, 0), T(6, 1)], 10, false, web.Difficult, web.Passives)],
            NextSerial = 2,
        };

        var plan = Ai.Plan(state, U("archer"), battle.Rules);

        var shot = plan.Commands.IndexOf(plan.Commands.OfType<Use>().FirstOrDefault()!);
        Assert.True(shot < 0 || !plan.Commands.Skip(shot).Any(c => c is Move), plan.Explain);
    }

    [Fact]
    public void A_mage_caught_in_melee_misty_steps_clear_and_still_casts()
    {
        // Walking away draws three opportunity attacks. Misty Step (4 tiles) lands out of the swordsmen's reach (Move 2, then a
        // step) without one, and Fire Bolt (range 6) still reaches them from there.
        var (bolt, step) = (A("wizard.fire_bolt"), A("wizard.misty_step"));
        var swordsman = TestSheets.Fighter with { Move = 2 };
        var battle = On(Open(11, 9)).With(bolt, step)
            .Enemy("mage", Mage(bolt, step), T(5, 2), new AiControlled("caster"))
            .Party("a", swordsman, T(4, 2))
            .Party("b", swordsman, T(6, 2))
            .Party("c", swordsman, T(5, 1));
        var state = battle.Start().State;
        var threat = ThreatMap.For(state, Side.Enemy, battle.Rules);

        var plan = Ai.Plan(state, U("mage"), battle.Rules);

        var hop = UseOf(plan, "wizard.misty_step");
        Assert.True(hop?.Target.Tile is { } landing && threat.At(landing) == 0, plan.Explain);
        Assert.NotNull(UseOf(plan, "wizard.fire_bolt"));
        Assert.DoesNotContain(plan.Commands, c => c is Move);
        var after = ApplyAll(state, plan, battle.Rules);
        Assert.Equal(hop!.Target.Tile, after.Units[U("mage")].Pos);
        Assert.Equal(20, after.Units[U("mage")].Hp);
    }

    [Fact]
    public void A_shadow_rogue_steps_through_the_shadows_to_reach_a_foe_it_cannot_walk_to()
    {
        // The foe is 9 tiles away and the rogue walks 4: it walks part of the way and Shadowsteps (6 tiles) beside it.
        var shadowstep = A("shadow.shadowstep");
        var rogue = First(TestSheets.Rogue.Knowing(shadowstep)) with { Resources = Ordinal.Map(("edge", 1)) };
        var battle = On(Open(12, 1)).With(shadowstep)
            .Enemy("vex", rogue, T(0, 0), new AiControlled("skirmisher"))
            .Party("kara", TestSheets.Fighter with { Move = 0 }, T(9, 0));
        var state = battle.Start().State;

        var plan = Ai.Plan(state, U("vex"), battle.Rules);

        var hop = UseOf(plan, "shadow.shadowstep");
        Assert.True(hop?.Target.Tile == T(8, 0), plan.Explain);
        Assert.True(plan.Commands.IndexOf(hop!) < plan.Commands.IndexOf(UseOf(plan, "common.attack")!), plan.Explain);
        Assert.Equal(U("kara"), UseOf(plan, "common.attack")?.Target.Unit);
        ApplyAll(state, plan, battle.Rules);
    }

    [Fact]
    public void A_druid_conjures_animals_beside_its_foes()
    {
        var conjure = A("druid.conjure_animals");
        var druid = First(TestSheets.Cleric.Knowing(conjure)) with { Level = 5, Resources = Ordinal.Map(("mana", 6)) };
        var battle = On(Open(10, 3)).With(conjure).WithMonsters(("wolf", SheetBuilder.ForMonster("wolf", Base)))
            .Enemy("druid", druid, T(1, 1), new AiControlled("caster"))
            .Party("kara", TestSheets.Fighter with { Move = 0 }, T(8, 1));
        var state = battle.Start().State;

        var plan = Ai.Plan(state, U("druid"), battle.Rules);

        var cast = UseOf(plan, "druid.conjure_animals");
        Assert.True(cast?.Target.Tile is { } aim && Reach.Distance(aim, T(8, 1)) == 1, plan.Explain);
        var after = ApplyAll(state, plan, battle.Rules);
        Assert.Equal(2, after.Units.Values.Count(u => u.SummonedBy == U("druid")));
    }

    [Fact]
    public void Action_surge_buys_the_best_second_action_when_one_is_worth_it()
    {
        var surge = A("fighter.action_surge");
        var fighter = First(TestSheets.Fighter.Knowing(surge)) with { Move = 0 };
        TestBattle Battle(Tile foe) => On(Open(8, 1)).With(surge)
            .Enemy("kara", fighter, T(0, 0), new AiControlled("brute"))
            .Party("ogre", TestSheets.Fighter with { MaxHp = 60, Move = 0 }, foe);

        var near = Battle(T(1, 0));
        var state = near.Start().State;
        var plan = Ai.Plan(state, U("kara"), near.Rules);

        Assert.True(plan.Commands is [Use { Ability.Value: "common.attack" }, Use { Ability.Value: "fighter.action_surge" },
            Use { Ability.Value: "common.attack" }, EndTurn], plan.Explain);
        var after = ApplyAll(state, plan, near.Rules);
        Assert.Equal(0, after.Units[U("kara")].UsesLeft["fighter.action_surge"]);

        // With nothing in reach, a second action is worth nothing: the surge is kept.
        var far = Battle(T(7, 0));
        Assert.Null(UseOf(Ai.Plan(far.Start().State, U("kara"), far.Rules), "fighter.action_surge"));
    }

    [Fact]
    public void After_a_shove_the_second_action_does_not_count_on_where_the_foe_stood()
    {
        // The shove sends Kara into the lava (row 0). She isn't where the plan found her any more, so the surged second
        // action can't be aimed at her (nor at anyone: only something on the fighter itself would still hold).
        var (surge, shove) = (A("fighter.action_surge"), A("common.shove"));
        var battle = On("""
            0x 0x 0x 0x 0x
            0. 0. 0. 0. 0.
            0. 0. 0. 0. 0.
            """).With(surge, shove)
            .Enemy("brute", First(TestSheets.Fighter.Knowing(surge)) with { Move = 0 }, T(2, 2), new AiControlled("brute"))
            .Party("kara", TestSheets.Fighter with { MaxHp = 40, Move = 0 }, T(2, 1));
        var state = battle.Start().State;

        var plan = Ai.Plan(state, U("brute"), battle.Rules);

        Assert.True(UseOf(plan, "common.shove")?.Target.Unit == U("kara"), plan.Explain);
        Assert.Single(plan.Commands.OfType<Use>(), u => u.Target.Unit == U("kara"));
        ApplyAll(state, plan, battle.Rules);
    }

    [Fact]
    public void A_surged_attack_keeps_off_the_foe_a_multi_attack_may_move_on_to()
    {
        // Two attacks at Aldo (9 HP, three attacks of his own) may drop him, though not likely enough to split them; if they
        // do, the rest go to the hero in reach with the least HP, Bree (12 of 30). The surged attack mustn't count on her still
        // standing: it goes to Cass.
        var surge = A("fighter.action_surge");
        var battle = On(Open(3, 3)).With(surge)
            .Enemy("kara", First(TestSheets.Fighter.Knowing(surge)) with { Attacks = 2, Move = 0 }, T(1, 1), new AiControlled("brute"))
            .Party("aldo", TestSheets.Goblin with { MaxHp = 9, Attacks = 3, Move = 0 }, T(0, 1))
            .Party("bree", TestSheets.Goblin with { MaxHp = 30, Move = 0 }, T(2, 1))
            .Party("cass", TestSheets.Goblin with { MaxHp = 60, Move = 0 }, T(1, 2));
        var state = battle.Start().State.Change("bree", u => u with { Hp = 12 });

        var plan = Ai.Plan(state, U("kara"), battle.Rules);

        Assert.True(plan.Commands is [Use { Ability.Value: "common.attack" } first, Use { Ability.Value: "fighter.action_surge" },
            Use { Ability.Value: "common.attack" } second, EndTurn]
            && first.Target.Unit == U("aldo") && first.Targets.IsDefaultOrEmpty && second.Target.Unit == U("cass"), plan.Explain);
    }

    [Fact]
    public void A_rogue_dashes_with_its_bonus_action_to_reach_a_foe()
    {
        // 8 tiles to the foe and Move 4: the bonus-action Dash doubles it, and the action is left for the attack.
        var dash = A("rogue.cunning_dash");
        var battle = On(Open(10, 1)).With(dash)
            .Enemy("vex", First(TestSheets.Rogue.Knowing(dash)), T(0, 0), new AiControlled("skirmisher"))
            .Party("kara", TestSheets.Fighter with { Move = 0 }, T(8, 0));
        var state = battle.Start().State;

        var plan = Ai.Plan(state, U("vex"), battle.Rules);

        Assert.True(plan.Commands is [Use { Ability.Value: "rogue.cunning_dash" }, Move { Path: [.., var end] },
            Use { Ability.Value: "common.attack" }, EndTurn] && end == T(7, 0), plan.Explain);
        ApplyAll(state, plan, battle.Rules);
    }

    [Fact]
    public void A_rogue_withdraws_from_a_sturdy_foe_to_finish_a_far_one()
    {
        // Leaving the knight draws an opportunity attack, and the wounded archer is 8 tiles away with Move 4. Withdraw
        // (Disengage and Dash) does both: no attack of opportunity, and the Move to reach the archer.
        var withdraw = A("rogue.withdraw");
        var rogue = First(TestSheets.Rogue.Knowing(withdraw)) with { Resources = Ordinal.Map(("edge", 1)) };
        var battle = On(Open(12, 3)).With(withdraw).WithConditions(Base.Conditions[new ConditionId("disengaged")])
            .Enemy("vex", rogue, T(2, 1), new AiControlled("skirmisher"))
            .Party("knight", TestSheets.Fighter with { MaxHp = 60, Move = 0 }, T(1, 1))
            .Party("archer", TestSheets.Rogue with { Move = 0 }, T(10, 1));
        var state = battle.Start().State.Change("archer", u => u with { Hp = 2 });

        var plan = Ai.Plan(state, U("vex"), battle.Rules);

        Assert.True(plan.Commands is [Use { Ability.Value: "rogue.withdraw" }, Move, Use { Ability.Value: "common.attack" }, EndTurn]
            && UseOf(plan, "common.attack")!.Target.Unit == U("archer"), plan.Explain);
        var after = ApplyAll(state, plan, battle.Rules);
        Assert.Equal(TestSheets.Rogue.MaxHp, after.Units[U("vex")].Hp);
    }
}
