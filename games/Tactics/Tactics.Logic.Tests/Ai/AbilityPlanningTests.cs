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
        // As above (with Move enough to step back past the web's difficult ground, and a swordsman nearly dead, so the shot is
        // worth taking even from a tile in their reach), but the tiles it shoots from are webbed: a web it walks into may hold
        // it (Restrained), so it shoots and plans no second walk.
        var shortbow = TestConditions.Longbow with { Id = "shortbow", Range = 6 };
        var battle = On(Open(14, 2)).With(A("wizard.web")).WithConditions(Base.Conditions[new ConditionId("restrained")])
            .Enemy("archer", First(TestSheets.Fighter with { Weapon = shortbow, Ac = 12, Move = 8 }), T(2, 0), new AiControlled("archer"))
            .Party("a", TestSheets.Fighter with { Move = 6 }, T(12, 0))
            .Party("b", TestSheets.Fighter with { Move = 6 }, T(12, 1));
        var web = A("wizard.web").Effects.OfType<ZoneEffect>().Single();
        var state = battle.Start().State.Change("a", u => u with { Hp = 2 }) with
        {
            Zones = [new Zone(1, null, web.Kind, [T(6, 0), T(6, 1)], 10, false, web.Difficult, web.Passives)],
            NextSerial = 2,
        };

        var plan = Ai.Plan(state, U("archer"), battle.Rules);

        Assert.True(plan.Commands is [Move, Use { Ability.Value: "common.attack" }, EndTurn], plan.Explain);
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
        Assert.True(plan.Commands is [Use { Ability.Value: "wizard.misty_step" }, Use { Ability.Value: "wizard.fire_bolt" }, EndTurn], plan.Explain);
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
    public void A_teleport_on_its_own_only_escapes_and_never_lands_where_the_foes_would_Down_the_unit()
    {
        // Out of everyone's reach, Fey Step would only close in a little sooner; the walk does that, and the step is kept.
        var fey = A("elf.fey_step");
        var fighter = First(TestSheets.Fighter.Knowing(fey)) with { MaxHp = 20 };
        var far = On(Open(16, 1)).With(fey)
            .Enemy("kara", fighter, T(0, 0), new AiControlled("brute"))
            .Party("ogre", TestSheets.Fighter with { Move = 0 }, T(15, 0));
        var farPlan = Ai.Plan(far.Start().State, U("kara"), far.Rules);
        Assert.True(UseOf(farPlan, "elf.fey_step") is null, farPlan.Explain);

        // As the Shadowstep test, with the foe nearly dead, but four swordsmen stand round it: landing beside it, the rogue
        // would be Downed next round, so it doesn't step in.
        var shadowstep = A("shadow.shadowstep");
        var rogue = First(TestSheets.Rogue.Knowing(shadowstep)) with { Resources = Ordinal.Map(("edge", 1)) };
        var guarded = On(Open(13, 3)).With(shadowstep)
            .Enemy("vex", rogue, T(0, 1), new AiControlled("skirmisher"))
            .Party("kara", TestSheets.Fighter with { Move = 0 }, T(9, 1))
            .Party("a", TestSheets.Fighter, T(10, 0))
            .Party("b", TestSheets.Fighter, T(10, 2))
            .Party("c", TestSheets.Fighter, T(11, 1))
            .Party("d", TestSheets.Fighter, T(12, 1));
        var state = guarded.Start().State.Change("kara", u => u with { Hp = 1 });
        Assert.True(ThreatMap.For(state, Side.Enemy, guarded.Rules).At(T(8, 1)) >= state.Units[U("vex")].Hp);
        var plan = Ai.Plan(state, U("vex"), guarded.Rules);
        Assert.True(UseOf(plan, "shadow.shadowstep") is null, plan.Explain);
    }

    [Fact]
    public void A_sorcerer_twins_a_single_target_spell_when_a_second_foe_is_in_range()
    {
        // Twinned Spell (2 Sorcery) makes Fire Bolt hit a second foe in range too: the planner casts the metamagic first, then
        // the spell, when the second hit is worth more than the Sorcery.
        var (bolt, twin) = (A("sorcerer.fire_bolt"), A("sorcerer.metamagic_twinned"));
        var battle = On(Open(8, 3)).With(bolt, twin)
            .Enemy("sora", Mage(bolt, twin) with { Move = 0, Resources = Ordinal.Map(("sorcery", 6)) }, T(1, 1), new AiControlled("caster"))
            .Party("aldo", TestSheets.Fighter with { Move = 0 }, T(5, 1))
            .Party("bree", TestSheets.Fighter with { Move = 0 }, T(5, 2));
        var state = battle.Start().State;

        var plan = Ai.Plan(state, U("sora"), battle.Rules);

        Assert.True(plan.Commands is [Use { Ability.Value: "sorcerer.metamagic_twinned" }, Use { Ability.Value: "sorcerer.fire_bolt" }, EndTurn],
            plan.Explain);
        var after = ApplyAll(state, plan, battle.Rules);
        Assert.Equal(4, after.Units[U("sora")].Resources["sorcery"]);
    }

    [Fact]
    public void With_no_second_foe_in_range_a_sorcerer_has_nothing_to_twin()
    {
        var (bolt, twin) = (A("sorcerer.fire_bolt"), A("sorcerer.metamagic_twinned"));
        var alone = On(Open(8, 3)).With(bolt, twin)
            .Enemy("sora", Mage(bolt, twin) with { Move = 0, Resources = Ordinal.Map(("sorcery", 6)) }, T(1, 1), new AiControlled("caster"))
            .Party("aldo", TestSheets.Fighter with { Move = 0 }, T(5, 1));
        Assert.Null(UseOf(Ai.Plan(alone.Start().State, U("sora"), alone.Rules), "sorcerer.metamagic_twinned"));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(5, true)]
    public void A_sorcerer_spends_on_a_resisted_twin_only_when_the_second_hit_is_worth_the_sorcery(int level, bool worthIt)
    {
        // Fire Bolt now shares the wizard's level-5 scaling. A resisted level-1 bolt is below the 2-Sorcery price;
        // a resisted level-5 bolt is worth it. Keep both sides of the planner's resource decision covered.
        var (bolt, twin) = (A("sorcerer.fire_bolt"), A("sorcerer.metamagic_twinned"));
        var battle = On(Open(8, 3)).With(bolt, twin)
            .Enemy("sora", Mage(bolt, twin) with { Move = 0, Level = level, Resources = Ordinal.Map(("sorcery", 6)) }, T(1, 1), new AiControlled("caster"))
            .Party("aldo", TestSheets.Fighter with { Move = 0 }, T(5, 1))
            .Party("bree", TestSheets.Fighter with { Move = 0, Resist = Ordinal.Set("fire") }, T(5, 2));
        var state = battle.Start().State;

        var plan = Ai.Plan(state, U("sora"), battle.Rules);

        Assert.True(UseOf(plan, "sorcerer.fire_bolt") is not null && (UseOf(plan, "sorcerer.metamagic_twinned") is not null) == worthIt, plan.Explain);
    }

    [Fact]
    public void A_sorcerer_casts_a_careful_fireball_over_an_ally()
    {
        // Every blast that catches the three swordsmen catches the goblin among them too. Careful Spell (1 Sorcery) makes
        // the sorcerer's allies pass the save: it spares the goblin half the damage.
        var (fireball, careful) = (A("sorcerer.fireball"), A("sorcerer.metamagic_careful"));
        var sorcerer = Mage(fireball, careful) with { Move = 0, Resources = Ordinal.Map(("mana", 6), ("sorcery", 4)) };
        var battle = On(Open(10, 5)).With(fireball, careful)
            .Enemy("sora", sorcerer, T(6, 4), new AiControlled("caster"))
            .Enemy("gob", TestSheets.Goblin with { MaxHp = 40, Move = 0 }, T(6, 3))
            .Party("a", TestSheets.Fighter with { Move = 0 }, T(5, 2))
            .Party("b", TestSheets.Fighter with { Move = 0 }, T(6, 2))
            .Party("c", TestSheets.Fighter with { Move = 0 }, T(7, 2));
        var state = battle.Start().State;

        var plan = Ai.Plan(state, U("sora"), battle.Rules);

        Assert.True(plan.Commands is [Use { Ability.Value: "sorcerer.metamagic_careful" }, Use { Ability.Value: "sorcerer.fireball" } cast, EndTurn]
            && cast.Target.Tile is { } aim && Reach.Distance(aim, T(6, 3)) <= 2, plan.Explain);
        ApplyAll(state, plan, battle.Rules);
    }

    [Fact]
    public void After_holding_a_foe_the_bonus_action_still_heals_an_ally()
    {
        // Hold Person paralyses the foe but moves nobody and changes no ground, so Healing Word on the hurt ally still holds
        // after it (only a push, a teleport, a summon, a zone or new ground limits the bonus action to the unit itself).
        var (hold, word) = (A("cleric.hold_person"), A("cleric.healing_word"));
        var priest = First(TestSheets.Cleric.Knowing(hold, word)) with { Level = 5, Move = 0, Resources = Ordinal.Map(("devotion", 6)) };
        var battle = On(Open(8, 3)).With(hold, word).WithConditions([.. Base.Conditions.Values])
            .Enemy("priest", priest, T(1, 1), new AiControlled("healer"))
            .Enemy("aldo", TestSheets.Fighter with { Move = 0 }, T(2, 1))
            .Party("brute", TestSheets.Fighter with { MaxHp = 40, Move = 0, Attacks = 3 }, T(3, 1));
        var state = battle.Start().State.Change("aldo", u => u with { Hp = 4 });

        var plan = Ai.Plan(state, U("priest"), battle.Rules);

        Assert.True(UseOf(plan, "cleric.hold_person")?.Target.Unit == U("brute") && UseOf(plan, "cleric.healing_word")?.Target.Unit == U("aldo"),
            plan.Explain);
        ApplyAll(state, plan, battle.Rules);
    }

    [Fact]
    public void The_bonus_action_keeps_off_a_foe_the_main_action_may_push_out_of_reach()
    {
        // Repelling Blast pushes a foe hit by a spell attack 2 tiles: Eldritch Blast from 6 tiles may leave Kara 8 away, out of
        // Hex's range, so Hex (worth its extra damage on the warlock's later hits) isn't planned on her after the blast.
        var (blast, hex) = (A("warlock.eldritch_blast"), A("warlock.hex"));
        var (repelling, hexDamage) = (Base.Passives["warlock.repelling_blast"], Base.Passives["warlock.hex_damage"]);
        var warlock = Mage(blast, hex) with
        {
            Level = 1, CastingStat = "Cha", Scores = TestSheets.Scores(8, 14, 12, 10, 10, 16), Move = 0, Resources = Ordinal.Map(("pact", 2)),
            Passives = [repelling.Id, hexDamage.Id],
        };
        var battle = On(Open(10, 1)).With(blast, hex).WithPassives(repelling, hexDamage).WithConditions([.. Base.Conditions.Values])
            .Enemy("wren", warlock, T(0, 0), new AiControlled("caster"))
            .Party("kara", TestSheets.Fighter with { Move = 0, MaxHp = 40 }, T(6, 0));
        var state = battle.Start().State;

        var plan = Ai.Plan(state, U("wren"), battle.Rules);

        Assert.True(UseOf(plan, "warlock.eldritch_blast")?.Target.Unit == U("kara") && UseOf(plan, "warlock.hex") is null, plan.Explain);
    }

    [Fact]
    public void A_warlock_blasts_a_foe_and_still_hexes_her_when_the_push_keeps_her_in_range()
    {
        // From 4 tiles, Repelling Blast's 2-tile push leaves Kara at most 6 away, still in Hex's range: Hex follows the blast.
        var (blast, hex) = (A("warlock.eldritch_blast"), A("warlock.hex"));
        var (repelling, hexDamage) = (Base.Passives["warlock.repelling_blast"], Base.Passives["warlock.hex_damage"]);
        var warlock = Mage(blast, hex) with
        {
            Level = 1, CastingStat = "Cha", Scores = TestSheets.Scores(8, 14, 12, 10, 10, 16), Move = 0, Resources = Ordinal.Map(("pact", 2)),
            Passives = [repelling.Id, hexDamage.Id],
        };
        var battle = On(Open(10, 1)).With(blast, hex).WithPassives(repelling, hexDamage).WithConditions([.. Base.Conditions.Values])
            .Enemy("wren", warlock, T(0, 0), new AiControlled("caster"))
            .Party("kara", TestSheets.Fighter with { Move = 0, MaxHp = 40 }, T(4, 0));
        var state = battle.Start().State;

        var plan = Ai.Plan(state, U("wren"), battle.Rules);

        Assert.True(plan.Commands is [Use { Ability.Value: "warlock.eldritch_blast" } shot, Use { Ability.Value: "warlock.hex" } hexed, EndTurn]
            && shot.Target.Unit == U("kara") && hexed.Target.Unit == U("kara"), plan.Explain);
        ApplyAll(state, plan, battle.Rules);
    }

    [Fact]
    public void The_bonus_action_keeps_off_a_foe_the_main_action_may_push_out_of_sight()
    {
        // Kara is 3 tiles off and to the side: the blast pushes her 2 tiles along the row, still in Hex's range of 6 but
        // behind the block at (3, 0) from where the warlock stands, so Hex couldn't reach her there.
        var (blast, hex) = (A("warlock.eldritch_blast"), A("warlock.hex"));
        var (repelling, hexDamage) = (Base.Passives["warlock.repelling_blast"], Base.Passives["warlock.hex_damage"]);
        var warlock = Mage(blast, hex) with
        {
            Level = 1, CastingStat = "Cha", Scores = TestSheets.Scores(8, 14, 12, 10, 10, 16), Move = 0, Resources = Ordinal.Map(("pact", 2)),
            Passives = [repelling.Id, hexDamage.Id],
        };
        var battle = On("""
            0. 0. 0. 0C 0. 0. 0. 0.
            0. 0. 0. 0. 0. 0. 0. 0.
            0. 0. 0. 0. 0. 0. 0. 0.
            """).With(blast, hex).WithPassives(repelling, hexDamage).WithConditions([.. Base.Conditions.Values])
            .Enemy("wren", warlock, T(0, 0), new AiControlled("caster"))
            .Party("kara", TestSheets.Fighter with { Move = 0, MaxHp = 40 }, T(3, 1));
        var state = battle.Start().State;

        var plan = Ai.Plan(state, U("wren"), battle.Rules);

        Assert.True(UseOf(plan, "warlock.eldritch_blast")?.Target.Unit == U("kara") && UseOf(plan, "warlock.hex") is null, plan.Explain);
        // Where the push leaves her, the warlock has no line of sight to her.
        var pushed = state.Change("kara", u => u with { Pos = T(5, 1) });
        Assert.Equal(RejectCode.NoLineOfSight,
            Battle.Check(pushed, new Use(U("wren"), hex.Id, new Target(Unit: U("kara"))), battle.Rules)?.Code);
    }

    [Fact]
    public void Repelling_blast_pushes_only_on_a_spell_so_a_crossbow_shot_leaves_the_foe_in_hex_range()
    {
        // Repelling Blast pushes on a spell attack only: a crossbow bolt from 6 tiles leaves Kara where she is, in Hex's range.
        var hex = A("warlock.hex");
        var (repelling, hexDamage) = (Base.Passives["warlock.repelling_blast"], Base.Passives["warlock.hex_damage"]);
        var warlock = Mage(hex) with
        {
            Level = 1, CastingStat = "Cha", Scores = TestSheets.Scores(8, 14, 12, 10, 10, 16), Move = 0, Resources = Ordinal.Map(("pact", 2)),
            Passives = [repelling.Id, hexDamage.Id],
            Weapon = new WeaponDef("light_crossbow", Dice.Parse("1d8"), "piercing", Stat.Dex, Finesse: false, Ranged: true, Range: 8,
                Reach: false, Tags: Ordinal.Set()),
        };
        var battle = On(Open(10, 1)).With(hex).WithPassives(repelling, hexDamage).WithConditions([.. Base.Conditions.Values])
            .Enemy("wren", warlock, T(0, 0), new AiControlled("caster"))
            .Party("kara", TestSheets.Fighter with { Move = 0, MaxHp = 40 }, T(6, 0));
        var state = battle.Start().State;

        var plan = Ai.Plan(state, U("wren"), battle.Rules);

        Assert.True(plan.Commands is [Use { Ability.Value: "common.attack" } shot, Use { Ability.Value: "warlock.hex" } hexed, EndTurn]
            && shot.Target.Unit == U("kara") && hexed.Target.Unit == U("kara"), plan.Explain);
        ApplyAll(state, plan, battle.Rules);
    }

    [Fact]
    public void The_bonus_action_keeps_off_a_summon_the_main_action_dismisses()
    {
        // The druid holds Conjure Animals; Hold Person is a new concentration, which ends it and sends the wolf away, so the
        // Healing Word after it isn't planned on the wolf.
        var (hold, word) = (A("cleric.hold_person"), A("cleric.healing_word"));
        var druid = First(TestSheets.Cleric.Knowing(hold, word)) with { Level = 5, Move = 0, Resources = Ordinal.Map(("devotion", 6)) };
        var battle = On(Open(8, 3)).With(hold, word).WithConditions([.. Base.Conditions.Values])
            .Enemy("druid", druid, T(1, 1), new AiControlled("healer"))
            .Enemy("wolf", TestSheets.Wolf with { Move = 0 }, T(1, 0))
            .Party("brute", TestSheets.Fighter with { MaxHp = 40, Move = 0, Attacks = 3 }, T(3, 1));
        var marker = new ActiveCondition(new ConditionId("summoned"), U("druid"), RoundsLeft: null, UntilSave: false, SaveDc: 0, Stat.Con,
            Concentration: true);
        var state = battle.Start().State
            .Change("druid", u => u with { Concentrating = new AbilityId("druid.conjure_animals") })
            .Change("wolf", u => u with { Hp = 3, SummonedBy = U("druid"), Conditions = u.Conditions.Add(marker) });

        var plan = Ai.Plan(state, U("druid"), battle.Rules);

        Assert.True(UseOf(plan, "cleric.hold_person") is not null && UseOf(plan, "cleric.healing_word")?.Target.Unit != U("wolf"), plan.Explain);
        ApplyAll(state, plan, battle.Rules);
    }

    [Fact]
    public void A_cleric_wards_the_ally_the_foes_are_about_to_drop()
    {
        // Death Ward does nothing until its holder is Downed: it is worth the chance the foes drop it next round (the threat on
        // its tile over its HP) times what losing it costs. Aldo has three goblins on him; Bree has none.
        var ward = A("cleric.death_ward");
        var priest = First(TestSheets.Cleric.Knowing(ward)) with { Level = 7, Resources = Ordinal.Map(("devotion", 8)), Move = 0 };
        var goblin = TestSheets.Goblin with { Move = 0 };
        var battle = On(Open(6, 3)).With(ward).WithConditions(Base.Conditions[new ConditionId("death_ward")])
            .Enemy("priest", priest, T(2, 1), new AiControlled("healer"))
            .Enemy("aldo", TestSheets.Fighter, T(3, 1))
            .Enemy("bree", TestSheets.Fighter, T(1, 1))
            .Party("g1", goblin, T(4, 1)).Party("g2", goblin, T(3, 0)).Party("g3", goblin, T(3, 2));
        var state = battle.Start().State;

        var plan = Ai.Plan(state, U("priest"), battle.Rules);

        Assert.True(UseOf(plan, "cleric.death_ward")?.Target.Unit == U("aldo"), plan.Explain);
        var after = ApplyAll(state, plan, battle.Rules);
        Assert.Contains(after.Units[U("aldo")].Conditions, c => c.Id.Value == "death_ward");
    }

    /// <summary>
    /// The reviewer's scenario (#110, R21): Aldo at 1 HP with three goblins on him, the priest beside him with Death Ward and
    /// Cure Wounds. <paramref name="spentWard"/> gives the ward's passive a use a battle that Aldo has already spent.
    /// </summary>
    private static (TestBattle Battle, BattleState State) Endangered(bool spentWard)
    {
        var (ward, cure) = (A("cleric.death_ward"), A("cleric.cure_wounds"));
        var condition = Base.Conditions[new ConditionId("death_ward")];
        if (spentWard)
            condition = condition with { Passives = [.. condition.Passives.Select(p => p with { UsesPerBattle = 1 })] };
        var priest = First(TestSheets.Cleric.Knowing(ward, cure)) with { Level = 7, Resources = Ordinal.Map(("devotion", 8)), Move = 0 };
        var goblin = TestSheets.Goblin with { Move = 0 };
        var battle = On(Open(6, 3)).With(ward, cure).WithConditions(condition)
            .Enemy("priest", priest, T(2, 1), new AiControlled("healer"))
            .Enemy("aldo", TestSheets.Fighter with { Attacks = 3 }, T(3, 1))
            .Party("g1", goblin, T(4, 1)).Party("g2", goblin, T(3, 0)).Party("g3", goblin, T(3, 2));
        var state = battle.Start().State.Change("aldo", u => u with { Hp = 1, UsesLeft = u.UsesLeft.SetItem("death_ward.save", 0) });
        return (battle, state);
    }

    [Fact]
    public void A_ward_that_cannot_fire_is_worth_nothing_and_the_cleric_heals_instead()
    {
        var (battle, state) = Endangered(spentWard: true);

        var plan = Ai.Plan(state, U("priest"), battle.Rules);

        Assert.True(UseOf(plan, "cleric.death_ward") is null && UseOf(plan, "cleric.cure_wounds")?.Target.Unit == U("aldo"), plan.Explain);
    }

    [Fact]
    public void Once_a_ward_has_fired_and_ended_a_fresh_one_is_worth_casting_again()
    {
        // The ward ends when it fires (the condition goes, and nothing else is spent), so the endangered Aldo is worth
        // warding again.
        var (battle, state) = Endangered(spentWard: false);

        var plan = Ai.Plan(state, U("priest"), battle.Rules);

        Assert.True(UseOf(plan, "cleric.death_ward")?.Target.Unit == U("aldo"), plan.Explain);
        var after = ApplyAll(state, plan, battle.Rules);
        Assert.Contains(after.Units[U("aldo")].Conditions, c => c.Id.Value == "death_ward");
    }

    [Fact]
    public void A_druid_conjures_animals_beside_its_foes()
    {
        var conjure = A("druid.conjure_animals");
        var druid = First(TestSheets.Cleric.Knowing(conjure)) with { Level = 5, Resources = Ordinal.Map(("mana", 6)) };
        var battle = On(Open(10, 3)).With(conjure).WithMonsters(("summon_wolf", SheetBuilder.ForMonster("summon_wolf", Base)))
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
    public void A_hero_surges_to_strike_a_lone_monster_twice()
    {
        // The ogre can't drop to one swing, so the second action may go at it again: a monster that dies at 0 HP is only off
        // limits to a later action when the first may kill it.
        var surge = A("fighter.action_surge");
        var battle = On(Open(4, 1)).With(surge)
            .Party("kara", First(TestSheets.Fighter.Knowing(surge)) with { Move = 0 }, T(0, 0), new AiControlled("brute"))
            .Enemy("ogre", TestSheets.Fighter with { MaxHp = 80, Move = 0 }, T(1, 0));
        var state = battle.Start().State;

        var plan = Ai.Plan(state, U("kara"), battle.Rules);

        Assert.True(plan.Commands is [Use { Ability.Value: "common.attack" } first, Use { Ability.Value: "fighter.action_surge" },
            Use { Ability.Value: "common.attack" } second, EndTurn] && first.Target.Unit == U("ogre") && second.Target.Unit == U("ogre"),
            plan.Explain);
        ApplyAll(state, plan, battle.Rules);
    }

    [Fact]
    public void The_bonus_action_keeps_off_a_foe_the_main_and_surged_attacks_may_drop_together()
    {
        // One longsword hit can't drop the ogre (20 HP), two can: after the attack and the surged attack at it, the ogre may
        // be dead, so the Spiritual Weapon goes at the troll.
        var (surge, weapon) = (A("fighter.action_surge"), A("cleric.spiritual_weapon"));
        var fighter = First(TestSheets.Fighter.Knowing(surge, weapon)) with
        {
            Move = 0, CastingStat = "Wis", Resources = Ordinal.Map(("devotion", 4)),
        };
        var battle = On(Open(3, 1)).With(surge, weapon)
            .Party("kara", fighter, T(1, 0), new AiControlled("brute"))
            .Enemy("ogre", TestSheets.Fighter with { MaxHp = 20, Move = 0 }, T(0, 0))
            .Enemy("troll", TestSheets.Fighter with { MaxHp = 60, Move = 0 }, T(2, 0));
        var state = battle.Start().State;

        var plan = Ai.Plan(state, U("kara"), battle.Rules);

        Assert.True(plan.Commands is [Use { Ability.Value: "common.attack" } first, Use { Ability.Value: "fighter.action_surge" },
            Use { Ability.Value: "common.attack" } second, Use { Ability.Value: "cleric.spiritual_weapon" } bonus, EndTurn]
            && first.Target.Unit == U("ogre") && second.Target.Unit == U("ogre") && bonus.Target.Unit == U("troll"), plan.Explain);
        ApplyAll(state, plan, battle.Rules);
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
    public void A_monk_flurries_rather_than_spending_its_bonus_action_on_stepping_away()
    {
        // Two swordsmen beside the monk (Move 2, then a step). Stepping out of their reach after the strike, with Step of the
        // Wind so they get no opportunity attack, is worth a little more than standing there; but the bonus action it spends
        // is worth far more as Flurry of Blows. A plan is ranked with the bonus action it leaves for the end.
        var (flurry, step) = (A("monk.flurry_of_blows"), A("monk.step_of_the_wind"));
        var monk = First(TestSheets.Rogue.Knowing(flurry, step)) with { MaxHp = 12, Move = 6, Resources = Ordinal.Map(("ki", 10)) };
        var swordsman = TestSheets.Fighter with { MaxHp = 40, Move = 2 };
        var battle = On(Open(12, 3)).With(flurry, step).WithConditions(Base.Conditions[new ConditionId("disengaged")])
            .Enemy("li", monk, T(2, 1), new AiControlled("skirmisher"))
            .Party("kara", swordsman, T(3, 1))
            .Party("bree", swordsman, T(2, 0));
        var state = battle.Start().State;

        var plan = Ai.Plan(state, U("li"), battle.Rules);

        Assert.True(UseOf(plan, "monk.flurry_of_blows")?.Target.Unit is { } target && (target == U("kara") || target == U("bree"))
            && UseOf(plan, "monk.step_of_the_wind") is null, plan.Explain);
        ApplyAll(state, plan, battle.Rules);
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
    public void With_its_action_spent_a_rogue_dashes_with_its_bonus_action_to_close_in()
    {
        // A timed-out turn finishing what the player left: the action is gone, so Cunning Action's Dash is the way to walk
        // twice as far toward the foe.
        var dash = A("rogue.cunning_dash");
        var battle = On(Open(16, 1)).With(dash)
            .Enemy("vex", First(TestSheets.Rogue.Knowing(dash)), T(0, 0), new AiControlled("skirmisher"))
            .Party("kara", TestSheets.Fighter with { Move = 0 }, T(15, 0));
        var state = battle.Start().State.Change("vex", u => u with { Economy = u.Economy with { Action = false } });

        var plan = Ai.Plan(state, U("vex"), battle.Rules);

        Assert.True(plan.Commands is [Use { Ability.Value: "rogue.cunning_dash" }, Move { Path: [.., var end] }, EndTurn] && end.X > 4,
            plan.Explain);
        ApplyAll(state, plan, battle.Rules);
    }

    [Fact]
    public void A_bonus_action_keeps_off_the_foe_a_multi_attack_may_move_on_to()
    {
        // As with Action Surge below: if the two attacks drop Aldo, the rest go to Bree, so the Spiritual Weapon goes to Cass.
        var weapon = A("cleric.spiritual_weapon");
        var priest = First(TestSheets.Fighter.Knowing(weapon)) with
        {
            Attacks = 2, Move = 0, CastingStat = "Wis", Resources = Ordinal.Map(("devotion", 4)),
        };
        var battle = On(Open(3, 3)).With(weapon)
            .Enemy("kara", priest, T(1, 1), new AiControlled("brute"))
            .Party("aldo", TestSheets.Goblin with { MaxHp = 9, Attacks = 3, Move = 0 }, T(0, 1))
            .Party("bree", TestSheets.Goblin with { MaxHp = 30, Move = 0 }, T(2, 1))
            .Party("cass", TestSheets.Goblin with { MaxHp = 60, Move = 0 }, T(1, 2));
        var state = battle.Start().State.Change("bree", u => u with { Hp = 12 });

        var plan = Ai.Plan(state, U("kara"), battle.Rules);

        Assert.True(UseOf(plan, "common.attack") is { Target.Unit.Value: "aldo", Targets.IsDefaultOrEmpty: true }
            && UseOf(plan, "cleric.spiritual_weapon")?.Target.Unit == U("cass"), plan.Explain);
    }

    [Fact]
    public void A_mage_caught_in_melee_dimension_doors_clear_with_its_action()
    {
        // Dimension Door is an action: walking away draws three opportunity attacks; the door draws none and lands out of
        // the swordsmen's reach.
        var door = A("warlock.dimension_door");
        var swordsman = TestSheets.Fighter with { Move = 2 };
        var battle = On(Open(14, 9)).With(door)
            .Enemy("mage", Mage(door) with { Resources = Ordinal.Map(("pact", 2)) }, T(5, 2), new AiControlled("caster"))
            .Party("a", swordsman, T(4, 2))
            .Party("b", swordsman, T(6, 2))
            .Party("c", swordsman, T(5, 1));
        var state = battle.Start().State;
        var threat = ThreatMap.For(state, Side.Enemy, battle.Rules);

        var plan = Ai.Plan(state, U("mage"), battle.Rules);

        Assert.True(UseOf(plan, "warlock.dimension_door")?.Target.Tile is { } landing && threat.At(landing) == 0, plan.Explain);
        var after = ApplyAll(state, plan, battle.Rules);
        Assert.Equal(20, after.Units[U("mage")].Hp);
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
