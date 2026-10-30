using System.Collections.Immutable;

namespace Tactics.Logic.Tests;

public sealed class UseCommandTests
{
    private const string Field = """
        0. 0. 0. 0. 0. 0. 0. 0.
        0. 0. 0. 0. 0. 0. 0. 0.
        0. 0. 0. 0. 0. 0. 0. 0.
        """;

    private static readonly AbilityId Bolt = TestAbilities.GuidingBolt.Id;
    private static readonly AbilityId Attack = new("common.attack");

    private static UnitId U(string id) => new(id);

    /// <summary>A cleric who knows Guiding Bolt and acts first (initiative 15).</summary>
    private static readonly UnitSheet Cleric = TestSheets.Cleric with { Abilities = [Bolt], InitiativeBonus = 5 };

    /// <summary>A goblin with 30 HP, so damage doesn't drop it.</summary>
    private static readonly UnitSheet Goblin = TestSheets.Goblin with { MaxHp = 30 };

    /// <summary>A fighter who acts first (initiative 19).</summary>
    private static readonly UnitSheet Fighter = TestSheets.Fighter with { InitiativeBonus = 10 };

    private static Rejection Rejected(TestBattle battle, BattleState state, Command command)
    {
        var resolution = Battle.Apply(state, command, battle.Rules);
        Assert.Same(state, resolution.State);
        Assert.Empty(resolution.Events);
        return Assert.IsType<Rejection>(resolution.Rejected);
    }

    [Fact]
    public void Guiding_bolt_hits_damages_and_applies_lit()
    {
        var battle = TestBattle.On(Field)
            .Party("cleric", Cleric, new Tile(0, 1))
            .Enemy("gob", Goblin, new Tile(5, 1))
            .With(TestAbilities.GuidingBolt);
        var (state, _) = battle.Start(seed: 1);
        Assert.Equal(U("cleric"), state.Active);

        // Seed 1: the attack d20 is 13; +3 WIS +2 proficiency = 18 against AC 15: a hit. Then 4d6 radiant (level 1).
        var dice = TestAbilities.Next(state.Rng, 20, 6, 6, 6, 6);
        Assert.Equal(13, dice[0]);
        var damage = dice[1..].Sum();

        var target = new Target(Unit: U("gob"));
        var (after, events) = battle.Apply(state, new Use(U("cleric"), Bolt, target));

        Assert.Collection(events,
            e => Assert.Equal(new AbilityUsed(U("cleric"), Bolt, target, TestAbilities.GuidingBolt.Presentation), e),
            e =>
            {
                var rolled = Assert.IsType<Rolled>(e);
                Assert.Equal(U("cleric"), rolled.Roller);
                Assert.Equal(RollKind.Attack, rolled.Kind);
                Assert.Equal([13], rolled.Result.D20s);
                Assert.Equal((13, 5, 18, 15), (rolled.Result.Kept, rolled.Result.Modifier, rolled.Result.Total, rolled.Result.Vs));
                Assert.True(rolled.Result.Success);
                Assert.False(rolled.Result.Crit);
            },
            e => Assert.Equal(new AttackResolved(U("cleric"), U("gob"), Hit: true, Crit: false), e),
            e => Assert.Equal(new Damaged(U("gob"), damage, "radiant", 30 - damage), e),
            e => Assert.Equal(new ConditionApplied(U("gob"), new ConditionId("lit")), e));

        var gob = after.Units[U("gob")];
        Assert.Equal(30 - damage, gob.Hp);
        var lit = Assert.Single(gob.Conditions);
        Assert.Equal(new ConditionId("lit"), lit.Id);
        Assert.Equal(U("cleric"), lit.Source);
        Assert.Equal(1, lit.RoundsLeft);
        Assert.False(lit.UntilSave);

        var cleric = after.Units[U("cleric")];
        Assert.False(cleric.Economy.Action);
        Assert.True(cleric.Economy.Bonus);
        Assert.Equal(0, cleric.Resources["devotion"]);
        Assert.Equal(4, cleric.Resources["mana"]);
        Assert.Empty(after.Queue);
        Assert.Equal(U("cleric"), after.Active);
    }

    [Fact]
    public void Out_of_range_rejected()
    {
        var battle = TestBattle.On(Field)
            .Party("cleric", Cleric, new Tile(0, 1))
            .Enemy("gob", Goblin, new Tile(7, 1))                     // 7 tiles away; range 6
            .With(TestAbilities.GuidingBolt);
        var (state, _) = battle.Start();

        var rejection = Rejected(battle, state, new Use(U("cleric"), Bolt, new Target(Unit: U("gob"))));
        Assert.Equal(RejectCode.OutOfRange, rejection.Code);
        Assert.Equal("Out of range.", rejection.Reason);
    }

    [Fact]
    public void High_ground_extends_range()
    {
        const string hill = """
            4. 0. 0. 0. 0. 0. 0. 0.
            """;
        var battle = TestBattle.On(hill)
            .Party("cleric", Cleric, new Tile(0, 0))                  // 4 levels up: +2 tiles
            .Enemy("gob", Goblin, new Tile(7, 0))
            .With(TestAbilities.GuidingBolt);
        var (state, _) = battle.Start();

        var resolution = Battle.Apply(state, new Use(U("cleric"), Bolt, new Target(Unit: U("gob"))), battle.Rules);
        Assert.Null(resolution.Rejected);
        var rolled = Assert.IsType<Rolled>(resolution.Events[1]);
        Assert.Equal(7, rolled.Result.Modifier);                       // +3 WIS +2 prof +2 high ground
    }

    [Fact]
    public void High_ground_range_bonus_is_for_ranged_abilities_only()
    {
        const string hill = """
            4. 0. 0. 0. 0. 0. 0. 0. 0. 0.
            """;
        var touch = new AbilityDef(new AbilityId("test.touch"), "Touch", 1, new CostDef(Action: 1),
            new TargetDef(TargetKind.Enemy, Range: 1, Los: false, new AreaDef(AreaShape.Single)),
            [new DamageEffect(TestAbilities.Flat("1"), "radiant", [])]);
        var bolt = TestAbilities.GuidingBolt;

        Rejection? Try(AbilityDef ability, int x)
        {
            var battle = TestBattle.On(hill)
                .Party("cleric", Cleric with { Abilities = [ability.Id] }, new Tile(0, 0))   // 4 levels up
                .Enemy("gob", Goblin, new Tile(x, 0))
                .With(ability);
            var (state, _) = battle.Start();
            return Battle.Apply(state, new Use(U("cleric"), ability.Id, new Target(Unit: U("gob"))), battle.Rules).Rejected;
        }

        Assert.Null(Try(touch, 1));                                     // adjacent: fine
        Assert.Equal(RejectCode.OutOfRange, Try(touch, 2)?.Code);       // no +2 tiles for touch
        Assert.Equal(RejectCode.OutOfRange, Try(touch, 3)?.Code);
        Assert.Null(Try(bolt, 8));                                      // range 6 + ⌊4 / 2⌋ = 8
        Assert.Equal(RejectCode.OutOfRange, Try(bolt, 9)?.Code);
    }

    [Fact]
    public void No_los_rejected()
    {
        const string wall = """
            0. 0. 0. 5# 0. 0.
            0. 0. 0. 5# 0. 0.
            """;
        var battle = TestBattle.On(wall)
            .Party("cleric", Cleric, new Tile(0, 0))
            .Enemy("gob", Goblin, new Tile(5, 0))
            .With(TestAbilities.GuidingBolt);
        var (state, _) = battle.Start();

        var rejection = Rejected(battle, state, new Use(U("cleric"), Bolt, new Target(Unit: U("gob"))));
        Assert.Equal(RejectCode.NoLineOfSight, rejection.Code);
        Assert.Equal("No line of sight.", rejection.Reason);
    }

    [Fact]
    public void Cant_afford_names_resource()
    {
        var broke = Cleric with { Resources = Ordinal.Map(("devotion", 0), ("mana", 4)) };
        var battle = TestBattle.On(Field)
            .Party("cleric", broke, new Tile(0, 1))
            .Enemy("gob", Goblin, new Tile(3, 1))
            .With(TestAbilities.GuidingBolt);
        var (state, _) = battle.Start();

        var rejection = Rejected(battle, state, new Use(U("cleric"), Bolt, new Target(Unit: U("gob"))));
        Assert.Equal(RejectCode.CantAfford, rejection.Code);
        Assert.Equal("Not enough devotion.", rejection.Reason);
    }

    [Fact]
    public void Unknown_and_wrong_target_rejected()
    {
        var battle = TestBattle.On(Field)
            .Party("cleric", Cleric with { Abilities = [] }, new Tile(0, 1))
            .Party("ally", TestSheets.Fighter, new Tile(1, 1))
            .Enemy("gob", Goblin, new Tile(3, 1))
            .With(TestAbilities.GuidingBolt);
        var (state, _) = battle.Start();
        Assert.Equal(RejectCode.UnknownAbility,
            Rejected(battle, state, new Use(U("cleric"), Bolt, new Target(Unit: U("gob")))).Code);
        Assert.Equal(RejectCode.UnknownAbility,
            Rejected(battle, state, new Use(U("cleric"), new AbilityId("nope"), new Target(Unit: U("gob")))).Code);

        var knows = TestBattle.On(Field)
            .Party("cleric", Cleric, new Tile(0, 1))
            .Party("ally", TestSheets.Fighter, new Tile(1, 1))
            .Enemy("gob", Goblin, new Tile(3, 1))
            .With(TestAbilities.GuidingBolt);
        var (s2, _) = knows.Start();
        var rejection = Rejected(knows, s2, new Use(U("cleric"), Bolt, new Target(Unit: U("ally"))));
        Assert.Equal(RejectCode.InvalidTarget, rejection.Code);
        Assert.Equal("That's not a valid target.", rejection.Reason);
        Assert.Equal(RejectCode.InvalidTarget,
            Rejected(knows, s2, new Use(U("cleric"), Bolt, new Target(Unit: U("ghost")))).Code);
        Assert.Equal(RejectCode.InvalidTarget, Rejected(knows, s2, new Use(U("cleric"), Bolt, new Target())).Code);
    }

    [Fact]
    public void Action_spent_twice_rejected_NoAction()
    {
        var battle = TestBattle.On(Field)
            .Party("kara", Fighter, new Tile(2, 1))
            .Enemy("gob", Goblin, new Tile(3, 1));
        var (state, _) = battle.Start(seed: 3);

        var (after, events) = battle.Apply(state, new Use(U("kara"), Attack, new Target(Unit: U("gob"))));
        Assert.Contains(events, e => e is Rolled { Kind: RollKind.Attack });
        Assert.False(after.Units[U("kara")].Economy.Action);

        var rejection = Rejected(battle, after, new Use(U("kara"), Attack, new Target(Unit: U("gob"))));
        Assert.Equal(RejectCode.NoAction, rejection.Code);
        Assert.Equal("You've already used your action.", rejection.Reason);
    }

    [Fact]
    public void Weapon_attack_repeats_for_each_sheet_attack_and_adds_the_stat_to_damage()
    {
        var battle = TestBattle.On(Field)
            .Party("kara", Fighter with { Attacks = 2 }, new Tile(2, 1))
            .Enemy("gob", Goblin, new Tile(3, 1));
        var (state, _) = battle.Start(seed: 5);

        // Seed 5: d20 13 (+5 = 18, a hit on AC 15), 1d8; then d20 16 (a hit), 1d8. Each 1d8 adds +3 STR.
        var dice = TestAbilities.Next(state.Rng, 20, 8, 20, 8);
        Assert.Equal((13, 16), (dice[0], dice[2]));

        var (after, events) = battle.Apply(state, new Use(U("kara"), Attack, new Target(Unit: U("gob"))));
        var rolls = events.OfType<Rolled>().ToArray();
        Assert.Equal([13, 16], rolls.Select(r => r.Result.Kept));
        Assert.All(rolls, r => Assert.Equal(5, r.Result.Modifier));    // +3 STR +2 proficiency
        Assert.All(rolls, r => Assert.True(r.Result.Success));

        var damaged = events.OfType<Damaged>().ToArray();
        Assert.Equal([dice[1] + 3, dice[3] + 3], damaged.Select(d => d.Amount));
        Assert.All(damaged, d => Assert.Equal("slashing", d.Type));
        Assert.Single(events.OfType<AbilityUsed>());
        Assert.Equal(30 - damaged.Sum(d => d.Amount), after.Units[U("gob")].Hp);
    }

    [Fact]
    public void Weapon_crit_doubles_weapon_dice_not_the_stat()
    {
        var battle = TestBattle.On(Field)
            .Party("kara", Fighter, new Tile(2, 1))
            .Enemy("gob", Goblin, new Tile(3, 1));
        var (state, _) = battle.Start(seed: 3);

        var dice = TestAbilities.Next(state.Rng, 20, 8, 8);            // seed 3: a natural 20, then 2d8
        Assert.Equal(20, dice[0]);

        var (_, events) = battle.Apply(state, new Use(U("kara"), Attack, new Target(Unit: U("gob"))));
        Assert.True(Assert.Single(events.OfType<Rolled>()).Result.Crit);
        Assert.Equal(dice[1] + dice[2] + 3, Assert.Single(events.OfType<Damaged>()).Amount);
    }

    [Fact]
    public void Burst_hits_each_unit_in_sorted_order()
    {
        var burst = new AbilityDef(new AbilityId("test.burst"), "Burst", 1, new CostDef(Action: 1),
            new TargetDef(TargetKind.Enemy, Range: 6, Los: true, new AreaDef(AreaShape.Burst, 1)),
            [new DamageEffect(TestAbilities.Flat("3"), "fire", [])]);
        var battle = TestBattle.On(Field)
            .Party("cleric", Cleric with { Abilities = [burst.Id] }, new Tile(0, 1))
            .Party("pal", TestSheets.Fighter, new Tile(3, 1))           // ally in the area: no friendly fire
            .Enemy("z", Goblin, new Tile(4, 0))
            .Enemy("a", Goblin, new Tile(4, 1))
            .Enemy("M", Goblin, new Tile(5, 1))
            .Enemy("far", Goblin, new Tile(7, 1))                       // outside the area
            .With(burst);
        var (state, _) = battle.Start();

        var target = new Target(Tile: new Tile(4, 1));
        var (after, events) = battle.Apply(state, new Use(U("cleric"), burst.Id, target));

        Assert.Equal<BattleEvent>(
        [
            new AbilityUsed(U("cleric"), burst.Id, target, null),
            new Damaged(U("M"), 3, "fire", 27),
            new Damaged(U("a"), 3, "fire", 27),
            new Damaged(U("z"), 3, "fire", 27),
        ], events);
        Assert.Equal(12, after.Units[U("pal")].Hp);
        Assert.Equal(30, after.Units[U("far")].Hp);
    }

    [Fact]
    public void Friendly_fire_and_include_self_widen_the_area()
    {
        var nova = new AbilityDef(new AbilityId("test.nova"), "Nova", 1, new CostDef(Action: 1),
            new TargetDef(TargetKind.Self, Range: 0, Los: false, new AreaDef(AreaShape.Burst, 1, IncludeSelf: true, FriendlyFire: true)),
            [new DamageEffect(TestAbilities.Flat("2"), "thunder", [])]);
        var battle = TestBattle.On(Field)
            .Party("cleric", Cleric with { Abilities = [nova.Id] }, new Tile(2, 1))
            .Party("pal", TestSheets.Fighter, new Tile(1, 1))
            .Enemy("gob", Goblin, new Tile(3, 1))
            .Enemy("far", Goblin, new Tile(5, 1))
            .With(nova);
        var (state, _) = battle.Start();

        var (_, events) = battle.Apply(state, new Use(U("cleric"), nova.Id, new Target()));
        Assert.Equal([U("cleric"), U("gob"), U("pal")], events.OfType<Damaged>().Select(d => d.Unit));
    }

    [Fact]
    public void Self_cone_points_at_the_target_tile()
    {
        var cone = new AbilityDef(new AbilityId("test.cone"), "Cone", 1, new CostDef(Action: 1),
            new TargetDef(TargetKind.Self, Range: 0, Los: false, new AreaDef(AreaShape.Cone, 2)),
            [new DamageEffect(TestAbilities.Flat("1"), "fire", [])]);
        var battle = TestBattle.On(Field)
            .Party("cleric", Cleric with { Abilities = [cone.Id] }, new Tile(1, 1))
            .Enemy("near", Goblin, new Tile(2, 1))                      // step 1: the tile ahead
            .Enemy("wide", Goblin, new Tile(3, 0))                      // step 2: three tiles wide
            .Enemy("behind", Goblin, new Tile(0, 1))
            .With(cone);
        var (state, _) = battle.Start();

        var (_, events) = battle.Apply(state, new Use(U("cleric"), cone.Id, new Target(Tile: new Tile(7, 1))));
        Assert.Equal([U("near"), U("wide")], events.OfType<Damaged>().Select(d => d.Unit));
    }

    [Fact]
    public void Save_half_on_success()
    {
        var blast = new AbilityDef(new AbilityId("test.blast"), "Blast", 1, new CostDef(Action: 1),
            new TargetDef(TargetKind.Enemy, Range: 6, Los: true, new AreaDef(AreaShape.Single)),
            [
                new SaveEffect(Stat.Dex, Dc: 1,
                    OnFail: [new DamageEffect(TestAbilities.Flat("4d6"), "fire", []), new ApplyEffect(new ConditionId("prone"), null)],
                    OnSuccess: [], HalfOnSuccess: true),
            ]);
        var battle = TestBattle.On(Field)
            .Party("cleric", Cleric with { Abilities = [blast.Id] }, new Tile(0, 1))
            .Enemy("gob", Goblin, new Tile(4, 1))
            .With(blast);
        var (state, _) = battle.Start(seed: 5);

        // The goblin saves first (d20 + 2 DEX against DC 1 always succeeds), then 4d6 halved, rounded down.
        var dice = TestAbilities.Next(state.Rng, 20, 6, 6, 6, 6);
        var half = dice[1..].Sum() / 2;

        var (after, events) = battle.Apply(state, new Use(U("cleric"), blast.Id, new Target(Unit: U("gob"))));

        Assert.Equal(3, events.Length);
        var rolled = Assert.IsType<Rolled>(events[1]);
        Assert.Equal(U("gob"), rolled.Roller);
        Assert.Equal(RollKind.Save, rolled.Kind);
        Assert.Equal((dice[0], 2, 1), (rolled.Result.Kept, rolled.Result.Modifier, rolled.Result.Vs));
        Assert.True(rolled.Result.Success);
        Assert.Equal(new Damaged(U("gob"), half, "fire", 30 - half), events[2]);
        Assert.Empty(after.Units[U("gob")].Conditions);                 // only the damage carries over, halved
    }

    [Fact]
    public void Save_dc_is_8_plus_prof_plus_casting_mod_and_failure_runs_on_fail()
    {
        var blast = new AbilityDef(new AbilityId("test.blast"), "Blast", 1, new CostDef(Action: 1),
            new TargetDef(TargetKind.Enemy, Range: 6, Los: true, new AreaDef(AreaShape.Single)),
            [
                new SaveEffect(Stat.Wis, Dc: null,
                    OnFail: [new ApplyEffect(new ConditionId("frightened"), null, UntilSave: true)],
                    OnSuccess: [new HealEffect(TestAbilities.Flat("1"), ["prof"])]),
            ]);
        var battle = TestBattle.On(Field)
            .Party("cleric", Cleric with { Abilities = [blast.Id] }, new Tile(0, 1))
            .Enemy("gob", Goblin, new Tile(4, 1))
            .With(blast);
        var (state, _) = battle.Start(seed: 4);                          // the goblin rolls a 1: 1 − 1 WIS = 0

        var (after, events) = battle.Apply(state, new Use(U("cleric"), blast.Id, new Target(Unit: U("gob"))));

        var rolled = Assert.IsType<Rolled>(events[1]);
        Assert.Equal((1, -1, 13), (rolled.Result.Kept, rolled.Result.Modifier, rolled.Result.Vs)); // DC 8 + 2 + 3 WIS
        Assert.False(rolled.Result.Success);
        Assert.Equal(new ConditionApplied(U("gob"), new ConditionId("frightened")), events[2]);
        var frightened = Assert.Single(after.Units[U("gob")].Conditions);
        Assert.True(frightened.UntilSave);
        Assert.Equal((13, Stat.Wis), (frightened.SaveDc, frightened.SaveStat));
    }

    [Fact]
    public void A_sight_blocking_zone_stops_line_of_sight_through_it()
    {
        var battle = TestBattle.On(Field)
            .Party("cleric", Cleric, new Tile(0, 1))
            .Enemy("gob", Goblin, new Tile(5, 1))
            .With(TestAbilities.GuidingBolt);
        var (state, _) = battle.Start();
        var bolt = new Use(U("cleric"), Bolt, new Target(Unit: U("gob")));

        var (smoked, events) = battle.Run(state, "cleric", null, new Tile(3, 1),
            new ZoneEffect("smoke", new AreaDef(AreaShape.Single), 2, [], BlocksSight: true));
        Assert.True(Assert.IsType<ZoneCreated>(Assert.Single(events)).Zone.BlocksSight);

        Assert.Equal(RejectCode.NoLineOfSight, Rejected(battle, smoked, bolt).Code);
        Assert.Null(Battle.Apply(state, bolt, battle.Rules).Rejected);   // clear without the smoke
    }

    [Fact]
    public void Save_dc_stat_replaces_the_casting_modifier()
    {
        // The fighter has no casting stat (a casting modifier of 0); a shove's DC uses STR (+3) instead.
        var battle = TestBattle.On(Field)
            .Party("kara", Fighter, new Tile(0, 1))
            .Enemy("gob", Goblin, new Tile(1, 1));
        var (state, _) = battle.Start();

        var (_, events) = battle.Run(state, "kara", "gob", null, new SaveEffect(Stat.Str, null, [], [], DcStat: Stat.Str));
        Assert.Equal(13, Assert.IsType<Rolled>(events[0]).Result.Vs);   // 8 + 2 + 3 STR
        (_, events) = battle.Run(state, "kara", "gob", null, new SaveEffect(Stat.Str, null, [], []));
        Assert.Equal(10, Assert.IsType<Rolled>(events[0]).Result.Vs);   // 8 + 2 + 0
    }

    [Fact]
    public void Heal_adds_named_bonuses_and_remove_clears_condition()
    {
        var mend = new AbilityDef(new AbilityId("test.mend"), "Mend", 1, new CostDef(Bonus: 1, Resource: Ordinal.Map(("mana", 1))),
            new TargetDef(TargetKind.Ally, Range: 1, Los: false, new AreaDef(AreaShape.Single)),
            [new HealEffect(TestAbilities.Flat("1d4"), ["mod.cast", "level"]), new RemoveEffect(new ConditionId("prone"))]);
        var battle = TestBattle.On(Field)
            .Party("cleric", Cleric with { Abilities = [mend.Id] }, new Tile(0, 1))
            .Party("pal", TestSheets.Fighter, new Tile(1, 1))
            .Enemy("gob", Goblin, new Tile(6, 1))
            .With(mend);
        var (state, _) = battle.Start(seed: 2);
        var pal = state.Units[U("pal")];
        state = state with
        {
            Units = state.Units.SetItem(pal.Id, pal with
            {
                Hp = 3,
                Conditions = [new ActiveCondition(new ConditionId("prone"), null, null, false, 0, Stat.Str, false)],
            }),
        };
        var roll = TestAbilities.Next(state.Rng, 4)[0];

        var (after, events) = battle.Apply(state, new Use(U("cleric"), mend.Id, new Target(Unit: U("pal"))));

        var healed = 3 + roll + 3 + 1;                                  // 1d4 + 3 WIS + level 1
        Assert.Equal<BattleEvent>(
        [
            new AbilityUsed(U("cleric"), mend.Id, new Target(Unit: U("pal")), null),
            new Healed(U("pal"), healed - 3, healed),
            new ConditionRemoved(U("pal"), new ConditionId("prone")),
        ], events);
        Assert.Empty(after.Units[U("pal")].Conditions);
        var cleric = after.Units[U("cleric")];
        Assert.True(cleric.Economy.Action);
        Assert.False(cleric.Economy.Bonus);
        Assert.Equal(3, cleric.Resources["mana"]);

        var rejection = Rejected(battle, after, new Use(U("cleric"), mend.Id, new Target(Unit: U("pal"))));
        Assert.Equal(RejectCode.NoBonus, rejection.Code);
    }

    [Fact]
    public void Uses_per_battle_run_out()
    {
        var once = new AbilityDef(new AbilityId("test.once"), "Once", 1, new CostDef(Uses: 1),
            new TargetDef(TargetKind.Self, Range: 0, Los: false, new AreaDef(AreaShape.Self)),
            [new HealEffect(TestAbilities.Flat("1"), [])]);
        var battle = TestBattle.On(Field)
            .Party("cleric", Cleric with { Abilities = [once.Id] }, new Tile(0, 1))
            .Enemy("gob", Goblin, new Tile(6, 1))
            .With(once);
        var (state, _) = battle.Start();

        var (after, _) = battle.Apply(state, new Use(U("cleric"), once.Id, new Target()));
        Assert.Equal(0, after.Units[U("cleric")].UsesLeft["test.once"]);
        var rejection = Rejected(battle, after, new Use(U("cleric"), once.Id, new Target()));
        Assert.Equal(RejectCode.NoUses, rejection.Code);
        Assert.Equal("No uses left this battle.", rejection.Reason);
    }

    [Fact]
    public void Resistance_halves_and_temp_hp_absorbs_first()
    {
        var burn = new AbilityDef(new AbilityId("test.burn"), "Burn", 1, new CostDef(Action: 1),
            new TargetDef(TargetKind.Enemy, Range: 6, Los: false, new AreaDef(AreaShape.Single)),
            [new DamageEffect(TestAbilities.Flat("9"), "fire", [])]);
        var battle = TestBattle.On(Field)
            .Party("cleric", Cleric with { Abilities = [burn.Id] }, new Tile(0, 1))
            .Enemy("gob", Goblin with { Resist = Ordinal.Set("fire") }, new Tile(4, 1))
            .With(burn);
        var (state, _) = battle.Start();
        var gob = state.Units[U("gob")];
        state = state with { Units = state.Units.SetItem(gob.Id, gob with { TempHp = 2 }) };

        var (after, events) = battle.Apply(state, new Use(U("cleric"), burn.Id, new Target(Unit: U("gob"))));

        Assert.Equal(new Damaged(U("gob"), 4, "fire", 28), events[1]);  // 9 halved to 4: 2 temp HP, then 2 HP
        Assert.Equal(0, after.Units[U("gob")].TempHp);
    }
}
