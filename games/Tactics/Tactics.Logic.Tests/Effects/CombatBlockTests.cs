using System.Collections.Immutable;

namespace Tactics.Logic.Tests;

public sealed class CombatBlockTests
{
    private const string Field = """
        0. 0. 0. 0. 0. 0.
        0. 0. 0. 0. 0. 0.
        0. 0. 0. 0. 0. 0.
        """;

    private static UnitId U(string id) => new(id);

    private static Tile T(int x, int y) => new(x, y);

    private static readonly ConditionId Marked = new("marked");

    /// <summary>cleric (initiative 15, acts first) at (0, 1), kara at (1, 1), gob at (3, 1), hob at (5, 1).</summary>
    private static TestBattle Build()
        => TestBattle.On(Field)
            .Party("cleric", TestSheets.Cleric with { InitiativeBonus = 5 }, T(0, 1))
            .Party("kara", TestSheets.Fighter, T(1, 1))
            .Enemy("gob", TestSheets.Goblin with { MaxHp = 30 }, T(3, 1))
            .Enemy("hob", TestSheets.Goblin with { MaxHp = 30 }, T(5, 1))
            .WithConditions([.. TestConditions.All]);

    [Fact]
    public void TempHp_keeps_the_higher_value()
    {
        var battle = Build();
        var (state, _) = battle.Start();

        var die = TestAbilities.Next(state.Rng, 4)[0];
        var (after, events) = battle.Run(state, "cleric", "kara", null, new TempHpEffect(TestAbilities.Flat("1d4"), ["mod.wis"]));
        Assert.Equal<BattleEvent>([new TempHpGained(U("kara"), die + 3)], events);
        Assert.Equal(die + 3, after.Units[U("kara")].TempHp);

        // Temporary HP doesn't stack: a smaller grant changes nothing.
        (after, events) = battle.Run(after, "cleric", "kara", null, new TempHpEffect(TestAbilities.Flat("2"), []));
        Assert.Empty(events);
        Assert.Equal(die + 3, after.Units[U("kara")].TempHp);
    }

    [Fact]
    public void Revive_raises_a_downed_ally()
    {
        var battle = Build();
        var (state, _) = battle.Start();
        state = state.Change("kara", u => u with { Life = LifeState.Downed, Hp = 0, Countdown = 2 });

        var (after, events) = battle.Run(state, "cleric", "kara", null, new ReviveEffect(5));
        Assert.Equal<BattleEvent>([new Revived(U("kara"), 5)], events);
        Assert.Equal((LifeState.Up, 5), (after.Units[U("kara")].Life, after.Units[U("kara")].Hp));

        // A standing unit is not revived.
        (_, events) = battle.Run(after, "cleric", "kara", null, new ReviveEffect(5));
        Assert.Empty(events);
    }

    [Fact]
    public void Cleanse_removes_conditions_by_tag()
    {
        var battle = Build();
        var (state, _) = battle.Start();
        state = state.Give("kara", TestConditions.Lasting(TestConditions.Stunned), TestConditions.Lasting(TestConditions.Prone),
            TestConditions.Lasting(TestConditions.Burning));

        var (after, events) = battle.Run(state, "cleric", "kara", null, new CleanseEffect(Ordinal.Set("magical")));
        Assert.Equal<BattleEvent>([new ConditionRemoved(U("kara"), TestConditions.Stunned.Id)], events);

        (after, events) = battle.Run(after, "cleric", "kara", null, new CleanseEffect(Ordinal.Set("physical", "fear")));
        Assert.Equal<BattleEvent>(
            [new ConditionRemoved(U("kara"), TestConditions.Prone.Id), new ConditionRemoved(U("kara"), TestConditions.Burning.Id)], events);
        Assert.Empty(after.Units[U("kara")].Conditions);
    }

    [Fact]
    public void Dispel_ends_concentration_and_the_zones_at_the_aim()
    {
        var battle = Build();
        var (state, _) = battle.Start();
        state = state.Change("gob", u => u with { Concentrating = new AbilityId("test.hex") }) with
        {
            Zones =
            [
                new Zone(0, U("gob"), "fog", [T(2, 0), T(2, 1)], null, false, false, []),
                new Zone(1, U("hob"), "fire", [T(4, 1)], 3, false, false, []),
                new Zone(2, null, "web", [T(2, 1)], null, false, true, []),
            ],
            NextSerial = 3,
        };

        var (after, events) = battle.Run(state, "cleric", null, T(2, 1), new DispelEffect(Zones: true));
        Assert.Equal<BattleEvent>([new ZoneEnded(0), new ZoneEnded(2)], events);
        Assert.Equal([1], after.Zones.Select(z => z.Serial));

        // Concentration: of the target, or of the unit on the aimed tile.
        (after, events) = battle.Run(after, "cleric", null, T(3, 1), new DispelEffect(Concentration: true));
        Assert.Equal<BattleEvent>([new ConcentrationBroken(U("gob"), new AbilityId("test.hex"))], events);
        Assert.Null(after.Units[U("gob")].Concentrating);
    }

    [Fact]
    public void GrantResource_adds_up_to_the_sheet_maximum()
    {
        var battle = Build();
        var (state, _) = battle.Start();
        state = state.Change("cleric", u => u with { Resources = u.Resources.SetItem("mana", 1) });

        var (after, _) = battle.Run(state, "cleric", "cleric", null, new GrantResourceEffect("mana", 2));
        Assert.Equal(3, after.Units[U("cleric")].Resources["mana"]);
        (after, _) = battle.Run(after, "cleric", "cleric", null, new GrantResourceEffect("mana", 5));
        Assert.Equal(4, after.Units[U("cleric")].Resources["mana"]);
        (after, _) = battle.Run(after, "cleric", "cleric", null, new GrantResourceEffect("mana", -10));
        Assert.Equal(0, after.Units[U("cleric")].Resources["mana"]);
    }

    [Fact]
    public void ExtraAction_and_refreshReaction_restore_the_economy()
    {
        var battle = Build();
        var (state, _) = battle.Start();
        state = state.Change("cleric", u => u with { Economy = u.Economy with { Action = false, Bonus = false, Reaction = false } });

        var (after, _) = battle.Run(state, "cleric", "cleric", null,
            new ExtraActionEffect("action"), new ExtraActionEffect("bonus"), new RefreshReactionEffect());
        var economy = after.Units[U("cleric")].Economy;
        Assert.Equal((false, 1, true, true), (economy.Action, economy.ExtraActions, economy.Bonus, economy.Reaction));

        // The extra action pays for an Action ability.
        (after, _) = battle.Apply(after, new Use(U("cleric"), RulesPack.CommonHelp.Id, new Target(Unit: U("kara"))));
        Assert.Equal(0, after.Units[U("cleric")].Economy.ExtraActions);
    }

    [Fact]
    public void ExtraAction_move_gives_the_units_move_again()
    {
        var battle = Build();
        var (state, _) = battle.Start();
        state = state.Change("cleric", u => u with { Economy = u.Economy with { MoveLeft = 1 } });

        var (after, events) = battle.Run(state, "cleric", "cleric", null, new ExtraActionEffect("move"));
        Assert.Empty(events);
        Assert.Equal(1 + TestSheets.Cleric.Move, after.Units[U("cleric")].Economy.MoveLeft);

        // A unit that can't move gains nothing.
        state = state.Change("kara", u => u with { Economy = u.Economy with { MoveLeft = 0 } });
        (after, _) = battle.Run(state, "cleric", "kara", null, new StatModEffect(new StaticMods(CantMove: true), 1),
            new ExtraActionEffect("move"));
        Assert.Equal(0, after.Units[U("kara")].Economy.MoveLeft);
    }

    [Fact]
    public void GrantResource_primary_names_the_first_resource_and_overMax_passes_the_pool()
    {
        var battle = Build();
        var (state, _) = battle.Start();
        Assert.Equal(1, state.Units[U("cleric")].Resources["devotion"]);   // devotion sorts before mana

        // Capped at the sheet's pool unless overMax.
        var (after, _) = battle.Run(state, "cleric", "cleric", null, new GrantResourceEffect(GrantResourceEffect.Primary, 1));
        Assert.Equal((1, 4), (after.Units[U("cleric")].Resources["devotion"], after.Units[U("cleric")].Resources["mana"]));
        (after, _) = battle.Run(state, "cleric", "cleric", null, new GrantResourceEffect(GrantResourceEffect.Primary, 1, OverMax: true));
        Assert.Equal((2, 4), (after.Units[U("cleric")].Resources["devotion"], after.Units[U("cleric")].Resources["mana"]));

        // A unit with no resources gains nothing.
        (after, _) = battle.Run(state, "cleric", "gob", null, new GrantResourceEffect(GrantResourceEffect.Primary, 1, OverMax: true));
        Assert.Empty(after.Units[U("gob")].Resources);

        // A sheet's declared primary resource wins over the ordinal-first one.
        var mana = state.Change("cleric", u => u with { Sheet = u.Sheet with { PrimaryResource = "mana" } });
        (after, _) = battle.Run(mana, "cleric", "cleric", null, new GrantResourceEffect(GrantResourceEffect.Primary, 1, OverMax: true));
        Assert.Equal((1, 5), (after.Units[U("cleric")].Resources["devotion"], after.Units[U("cleric")].Resources["mana"]));
    }

    [Fact]
    public void A_normal_grant_never_takes_away_what_overMax_gave()
    {
        var battle = Build();
        var (state, _) = battle.Start();
        (state, _) = battle.Run(state, "cleric", "cleric", null, new GrantResourceEffect("mana", 2, OverMax: true));
        Assert.Equal(6, state.Units[U("cleric")].Resources["mana"]);   // the pool is 4

        var (after, _) = battle.Run(state, "cleric", "cleric", null, new GrantResourceEffect("mana", 1));
        Assert.Equal(6, after.Units[U("cleric")].Resources["mana"]);
        (after, _) = battle.Run(state, "cleric", "cleric", null, new GrantResourceEffect("mana", -1));
        Assert.Equal(5, after.Units[U("cleric")].Resources["mana"]);

        // The amount can scale by level: level 1 takes the first step.
        var table = new ByLevel<int>(ImmutableSortedDictionary<int, int>.Empty.Add(1, 3).Add(5, 9));
        (after, _) = battle.Run(state, "cleric", "kara", null, new GrantResourceEffect("second_wind", table, OverMax: true));
        Assert.Equal(4, after.Units[U("kara")].Resources["second_wind"]);
    }

    [Fact]
    public void StatMod_adds_timed_modifiers()
    {
        var battle = Build();
        var (state, _) = battle.Start();
        Assert.Equal(U("cleric"), state.Active);

        var (after, events) = battle.Run(state, "cleric", "kara", null, new StatModEffect(new StaticMods(Ac: 2, Move: 1), 1));
        var id = new ConditionId("statMod.effect");
        Assert.Equal<BattleEvent>([new ConditionApplied(U("kara"), id)], events);
        Assert.Equal((2, 1), (Modifiers.For(after, U("kara"), battle.Rules).Ac, Modifiers.For(after, U("kara"), battle.Rules).Move));

        // 1 round: until the end of the source's next turn.
        (after, _) = battle.Apply(after, new EndTurn(U("cleric")));
        Assert.Equal(2, Modifiers.For(after, U("kara"), battle.Rules).Ac);
        while (after.Active != U("cleric"))
            (after, _) = battle.Apply(after, new EndTurn(after.Active!.Value));
        (after, events) = battle.Apply(after, new EndTurn(U("cleric")));
        Assert.Contains(new ConditionRemoved(U("kara"), id), events);
        Assert.Equal(0, Modifiers.For(after, U("kara"), battle.Rules).Ac);

        // An incapacitating one ends the target's concentration.
        state = state.Change("gob", u => u with { Concentrating = new AbilityId("test.hex") });
        (_, events) = battle.Run(state, "cleric", "gob", null, new StatModEffect(new StaticMods(Incapacitated: true), 1));
        Assert.Contains(new ConcentrationBroken(U("gob"), new AbilityId("test.hex")), events);
    }

    [Fact]
    public void Mark_follows_the_latest_target()
    {
        var battle = Build();
        var (state, _) = battle.Start();

        var (after, events) = battle.Run(state, "kara", "gob", null, new MarkEffect(2));
        Assert.Equal<BattleEvent>([new ConditionApplied(U("gob"), Marked), new Marked(U("gob"), U("kara"))], events);
        var mark = Assert.Single(after.Units[U("gob")].Conditions);
        Assert.Equal((U("kara"), (int?)2), (mark.Source, mark.RoundsLeft));

        (after, events) = battle.Run(after, "kara", "hob", null, new MarkEffect(2));
        Assert.Equal<BattleEvent>(
            [new ConditionRemoved(U("gob"), Marked), new ConditionApplied(U("hob"), Marked), new Marked(U("hob"), U("kara"))], events);

        // Another unit's mark is its own.
        (after, _) = battle.Run(after, "cleric", "gob", null, new MarkEffect(2));
        Assert.Single(after.Units[U("hob")].Conditions);
        Assert.Single(after.Units[U("gob")].Conditions);
    }

    [Fact]
    public void Taunt_gives_disadvantage_against_anyone_else()
    {
        var battle = Build();
        var (state, _) = battle.Start();
        var attack = new AttackEffect("weapon", null, [], [], []);

        var (after, events) = battle.Run(state, "kara", "gob", null, new TauntEffect(1));
        Assert.Equal<BattleEvent>([new ConditionApplied(U("gob"), new ConditionId("taunted"))], events);

        (_, events) = battle.Run(after, "gob", "cleric", null, attack);
        Assert.Equal(2, TestConditions.FirstAttack(events).D20s.Length);
        (_, events) = battle.Run(after, "gob", "kara", null, attack);
        Assert.Single(TestConditions.FirstAttack(events).D20s);
    }

    [Fact]
    public void Reveal_ends_invisibility_on_enemies_in_the_area()
    {
        var battle = Build();
        var (state, _) = battle.Start();
        var invisible = TestConditions.Lasting(TestConditions.Invisible);
        state = state.Give("gob", invisible).Give("hob", invisible).Give("kara", invisible);
        state = state.Change("kara", u => u with { Pos = T(3, 0) });

        var (after, events) = battle.Run(state, "cleric", null, T(3, 1), new RevealEffect(new AreaDef(AreaShape.Burst, 1)));
        Assert.Equal<BattleEvent>([new ConditionRemoved(U("gob"), TestConditions.Invisible.Id)], events);
        Assert.Single(after.Units[U("hob")].Conditions);       // outside the area
        Assert.Single(after.Units[U("kara")].Conditions);      // an ally
    }
}
