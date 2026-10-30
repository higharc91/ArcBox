using System.Collections.Immutable;

namespace Tactics.Logic.Tests;

public sealed class ConcentrationTests
{
    private const string Field = """
        0. 0. 0. 0. 0. 0. 0. 0.
        0. 0. 0. 0. 0. 0. 0. 0.
        0. 0. 0. 0. 0. 0. 0. 0.
        """;

    private static readonly ConditionId Held = new("held");
    private static readonly ConditionId Slowed = new("slowed");
    private static readonly ConditionId Marked = new("marked");

    private static UnitId U(string id) => new(id);

    private static Tile T(int x, int y) => new(x, y);

    private static AbilityDef Apply(string id, ConditionId condition, CostDef cost, AreaDef? area = null) => new(
        new AbilityId(id), id, 1, cost, new TargetDef(TargetKind.Enemy, Range: 6, Los: false, area ?? new AreaDef(AreaShape.Single)),
        [new ApplyEffect(condition, Rounds: 10)]);

    /// <summary>Action, concentration: holds one enemy.</summary>
    private static readonly AbilityDef Hold = Apply("test.hold", Held, new CostDef(Action: 1, Concentration: true));

    /// <summary>Bonus action, concentration: slows one enemy.</summary>
    private static readonly AbilityDef Slow = Apply("test.slow", Slowed, new CostDef(Bonus: 1, Concentration: true));

    /// <summary>Bonus action, no concentration: marks one enemy.</summary>
    private static readonly AbilityDef Mark = Apply("test.mark", Marked, new CostDef(Bonus: 1));

    /// <summary>Action, concentration: holds every enemy within 1 tile of the aimed tile.</summary>
    private static readonly AbilityDef HoldAll = Apply("test.hold_all", Held, new CostDef(Action: 1, Concentration: true),
        new AreaDef(AreaShape.Burst, Size: 1));

    /// <summary>A goblin spell: fixed damage to an enemy, free to use.</summary>
    private static AbilityDef Zap(int damage) => new(new AbilityId("test.zap" + damage), "Zap", 0, new CostDef(),
        new TargetDef(TargetKind.Enemy, Range: 6, Los: false, new AreaDef(AreaShape.Single)),
        [new DamageEffect(TestAbilities.Flat(damage.ToString()), "force", [])]);

    private static readonly AbilityDef[] Zaps = [Zap(6), Zap(30), Zap(50)];

    /// <summary>A cleric (CON 14: +2, not proficient) with 100 HP who acts first (15), and goblins (12) who know the zaps.</summary>
    private static TestBattle Build(params string[] goblins)
    {
        var battle = TestBattle.On(Field)
            .Party("cleric", TestSheets.Cleric with
            {
                Abilities = [Hold.Id, Slow.Id, Mark.Id, HoldAll.Id], MaxHp = 100, InitiativeBonus = 5,
            }, T(0, 1))
            .With([Hold, Slow, Mark, HoldAll, .. Zaps]);
        foreach (var (id, i) in goblins.Select((g, i) => (g, i)))
            battle.Enemy(id, TestSheets.Goblin with { MaxHp = 30, Abilities = [.. Zaps.Select(z => z.Id)] }, T(4, i));
        return battle;
    }

    [Fact]
    public void Second_concentration_ends_first()
    {
        var battle = Build("gob");
        var (state, _) = battle.Start();
        var target = new Target(Unit: U("gob"));

        (state, _) = battle.Apply(state, new Use(U("cleric"), Hold.Id, target));
        Assert.Equal(Hold.Id, state.Units[U("cleric")].Concentrating);
        var held = Assert.Single(state.Units[U("gob")].Conditions);
        Assert.Equal((Held, U("cleric"), true), (held.Id, held.Source, held.Concentration));

        var (after, events) = battle.Apply(state, new Use(U("cleric"), Slow.Id, target));

        Assert.Equal<BattleEvent>(
        [
            new ConcentrationBroken(U("cleric"), Hold.Id),
            new ConditionRemoved(U("gob"), Held),
            new AbilityUsed(U("cleric"), Slow.Id, target, null),
            new ConditionApplied(U("gob"), Slowed),
        ], events);
        Assert.Equal(Slow.Id, after.Units[U("cleric")].Concentrating);
        Assert.Equal([Slowed], after.Units[U("gob")].Conditions.Select(c => c.Id));
    }

    [Fact]
    public void Damage_forces_con_save_dc_max_10_half()
    {
        var battle = Build("gob");
        var (state, _) = battle.Start(seed: 9);
        (state, _) = battle.Apply(state, new Use(U("cleric"), Hold.Id, new Target(Unit: U("gob"))));
        (state, _) = battle.Apply(state, new EndTurn(U("cleric")));
        Assert.Equal(U("gob"), state.Active);

        RollResult Zapped(BattleState s, int damage, out ImmutableArray<BattleEvent> events, out BattleState after)
        {
            (after, events) = battle.Apply(s, new Use(U("gob"), new AbilityId("test.zap" + damage), new Target(Unit: U("cleric"))));
            Assert.Equal(new Damaged(U("cleric"), damage, "force", 100 - damage), events[1]);
            var save = Assert.IsType<Rolled>(events[2]);
            Assert.Equal((U("cleric"), RollKind.Save, 2), (save.Roller, save.Kind, save.Result.Modifier));
            return save.Result;
        }

        // 6 damage: DC max(10, 3) = 10. 30 damage: DC 15.
        var low = Zapped(state, 6, out var lowEvents, out var lowAfter);
        Assert.Equal(10, low.Vs);
        Assert.Equal(low.Success, lowAfter.Units[U("cleric")].Concentrating == Hold.Id);
        Assert.Equal(!low.Success, lowEvents.Contains(new ConcentrationBroken(U("cleric"), Hold.Id)));

        var high = Zapped(state, 30, out _, out _);
        Assert.Equal(15, high.Vs);

        // 50 damage: DC 25 can't be made (d20 + 2), so concentration ends; then more damage asks for no save.
        var hopeless = Zapped(state, 50, out var events, out var broken);
        Assert.Equal(25, hopeless.Vs);
        Assert.False(hopeless.Success);
        Assert.Equal(new ConcentrationBroken(U("cleric"), Hold.Id), events[3]);
        Assert.Null(broken.Units[U("cleric")].Concentrating);
        (_, events) = battle.Apply(broken, new Use(U("gob"), new AbilityId("test.zap6"), new Target(Unit: U("cleric"))));
        Assert.DoesNotContain(events, e => e is Rolled);
    }

    [Fact]
    public void Breaking_removes_linked_conditions()
    {
        var battle = Build("gob", "hob");
        var (state, _) = battle.Start();
        Assert.Equal(U("cleric"), state.Active);

        // Both goblins held (concentration), one also marked (no concentration); the cleric also keeps a concentration zone
        // and a plain one on the map.
        (state, _) = battle.Apply(state, new Use(U("cleric"), HoldAll.Id, new Target(Tile: T(4, 0))));
        (state, _) = battle.Apply(state, new Use(U("cleric"), Mark.Id, new Target(Unit: U("gob"))));
        state = state with
        {
            Zones =
            [
                new Zone(1, U("cleric"), "spirit_guardians", [T(1, 1)], 10, Concentration: true, Difficult: false, []),
                new Zone(2, U("cleric"), "grease", [T(2, 1)], 10, Concentration: false, Difficult: true, []),
            ],
        };
        Assert.Equal([Held, Marked], state.Units[U("gob")].Conditions.Select(c => c.Id));
        Assert.Equal([Held], state.Units[U("hob")].Conditions.Select(c => c.Id));
        (state, _) = battle.Apply(state, new EndTurn(U("cleric")));
        Assert.Equal(U("gob"), state.Active);

        var (after, events) = battle.Apply(state, new Use(U("gob"), new AbilityId("test.zap50"), new Target(Unit: U("cleric"))));

        Assert.Equal<BattleEvent>(
        [
            new ConcentrationBroken(U("cleric"), HoldAll.Id),
            new ConditionRemoved(U("gob"), Held),
            new ConditionRemoved(U("hob"), Held),
        ], events[3..]);
        Assert.Equal([Marked], after.Units[U("gob")].Conditions.Select(c => c.Id));
        Assert.Empty(after.Units[U("hob")].Conditions);
        Assert.Equal([2], after.Zones.Select(z => z.Serial));
    }
}
