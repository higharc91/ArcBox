using System.Collections.Immutable;
using System.Text.Json;
using ArcBox.Sdk;

namespace Tactics.Logic.Tests;

public sealed class UnitBlockTests
{
    private const string Field = """
        0. 0. 0. 0. 0. 0.
        0. 0. 0. 0. 0. 0.
        0. 0. 0. 0. 0. 0.
        """;

    private static UnitId U(string id) => new(id);

    private static Tile T(int x, int y) => new(x, y);

    private static readonly UnitId Wolf = new("wiz.wolf.0");

    private static readonly ConditionId Summoned = new("summoned");

    /// <summary>A wolf with a high initiative (22): it still acts right after its summoner.</summary>
    private static readonly UnitSheet FastWolf = TestSheets.Wolf with { InitiativeBonus = 10 };

    private static AbilityDef Summon(string id, SummonEffect summon, bool concentration)
        => new(new AbilityId(id), id, Tier: 0, new CostDef(Action: 1, Concentration: concentration),
            new TargetDef(TargetKind.Tile, Range: 4, Los: false, new AreaDef(AreaShape.Single)), [summon]);

    private static readonly AbilityDef CallWolf = Summon("test.call_wolf", new SummonEffect("wolf", Concentration: true), concentration: true);

    /// <summary>Another concentration ability: casting it ends the concentration on the wolf.</summary>
    private static readonly AbilityDef Focus = new(new AbilityId("test.focus"), "Focus", Tier: 0, new CostDef(Action: 1, Concentration: true),
        new TargetDef(TargetKind.Self, Range: 0, Los: false, new AreaDef(AreaShape.Self)),
        [new StatModEffect(new StaticMods(Ac: 1), 10, Concentration: true)]);

    /// <summary>Ends the target's concentration.</summary>
    private static readonly AbilityDef Dispel = new(new AbilityId("test.dispel"), "Dispel", Tier: 0, new CostDef(Action: 1),
        new TargetDef(TargetKind.Enemy, Range: 6, Los: false, new AreaDef(AreaShape.Single)), [new DispelEffect(Concentration: true)]);

    /// <summary>
    /// wiz (initiative 15) at (0, 1), gob (12) at (5, 1), kara (9) at (0, 2): round 1's order is wiz, gob, kara.
    /// </summary>
    private static TestBattle Build(params AbilityDef[] abilities)
        => TestBattle.On(Field)
            .Party("wiz", (TestSheets.Cleric with { InitiativeBonus = 5 }).Knowing([CallWolf, Focus, .. abilities]), T(0, 1))
            .Enemy("gob", (TestSheets.Goblin with { MaxHp = 30 }).Knowing(Dispel), T(5, 1))
            .Party("kara", TestSheets.Fighter, T(0, 2))
            .With([CallWolf, Focus, Dispel, .. abilities])
            .WithConditions([.. TestConditions.All])
            .WithMonsters(("wolf", FastWolf));

    /// <summary>Round 1: wiz calls the wolf at (1, 1), then everyone ends their turn. Returns round 2's first state.</summary>
    private static BattleState ToRoundTwo(TestBattle battle, BattleState state)
    {
        (state, _) = battle.Apply(state, new Use(U("wiz"), CallWolf.Id, new Target(Tile: T(1, 1))));
        (state, _) = battle.Apply(state, new EndTurn(U("wiz")));
        (state, _) = battle.Apply(state, new EndTurn(U("gob")));
        (state, _) = battle.Apply(state, new EndTurn(U("kara")));
        return state;
    }

    [Fact]
    public void Summon_acts_after_owner_next_round()
    {
        var battle = Build();
        var (state, _) = battle.Start();
        Assert.Equal([U("wiz"), U("gob"), U("kara")], state.Order);

        var (after, events) = battle.Apply(state, new Use(U("wiz"), CallWolf.Id, new Target(Tile: T(1, 1))));

        Assert.Equal(new Spawned(Wolf, T(1, 1), "beasts.wolf"), events[^1]);
        var wolf = after.Units[Wolf];
        Assert.Equal(U("wiz"), wolf.SummonedBy);
        Assert.Equal(state.Units[U("wiz")].Controller, wolf.Controller);
        Assert.Equal((Side.Party, 11, LifeState.Up, T(1, 1)), (wolf.Sheet.Side, wolf.Hp, wolf.Life, wolf.Pos));
        Assert.Equal(1, after.NextSerial);
        var marker = Assert.Single(wolf.Conditions);
        Assert.Equal((Summoned, U("wiz"), (int?)null, true), (marker.Id, marker.Source, marker.RoundsLeft, marker.Concentration));

        // Not in this round's order; it waits to join the next one.
        Assert.Equal([U("wiz"), U("gob"), U("kara")], after.Order);
        Assert.Equal([Wolf], after.Joining);

        (state, _) = battle.Apply(after, new EndTurn(U("wiz")));
        Assert.Equal(U("gob"), state.Active);
        (state, _) = battle.Apply(state, new EndTurn(U("gob")));
        (state, events) = battle.Apply(state, new EndTurn(U("kara")));

        // Round 2: right after its owner, though its own initiative (22) is the highest.
        Assert.Contains(new RoundStarted(2, [U("wiz"), Wolf, U("gob"), U("kara")]), events, new JsonEquality<BattleEvent>());
        Assert.Empty(state.Joining);
        (state, events) = battle.Apply(state, new EndTurn(U("wiz")));
        Assert.Equal(new TurnStarted(Wolf, 2), events[^1]);
        Assert.Equal(Wolf, state.Active);

        // Its owner's player commands it.
        (state, _) = battle.Apply(state, new Move(Wolf, [T(1, 1), T(2, 1)]));
        (state, _) = battle.Apply(state, new EndTurn(Wolf));
        Assert.Equal(U("gob"), state.Active);
    }

    [Fact]
    public void Summons_go_on_the_aim_then_the_nearest_free_tiles_or_a_named_map_zone()
    {
        var pack = Summon("test.pack", new SummonEffect("wolf", Count: 2), concentration: false);
        var battle = Build(pack);
        var (state, _) = battle.Start();

        var (after, events) = battle.Apply(state, new Use(U("wiz"), pack.Id, new Target(Tile: T(1, 1))));
        Assert.Equal<BattleEvent>(
            [new Spawned(Wolf, T(1, 1), "beasts.wolf"), new Spawned(U("wiz.wolf.1"), T(1, 0), "beasts.wolf")], events[1..]);
        Assert.Empty(after.Units[Wolf].Conditions);   // no duration, no concentration: no marker
        Assert.Equal([Wolf, U("wiz.wolf.1")], after.Joining);

        // With a map zone: its tiles in order, skipping taken ones.
        var map = AsciiMap.Parse(Field, Ordinal.Map(("den", ImmutableArray.Create(T(5, 1), T(5, 0), T(4, 0)))));
        var den = Summon("test.den", new SummonEffect("wolf", Count: 2, Zone: "den"), concentration: false);
        var zoned = TestBattle.On(map)
            .Party("wiz", (TestSheets.Cleric with { InitiativeBonus = 5 }).Knowing(den), T(0, 1))
            .Enemy("gob", TestSheets.Goblin, T(5, 1))
            .With(den)
            .WithMonsters(("wolf", FastWolf));
        (state, _) = zoned.Start();
        (_, events) = zoned.Apply(state, new Use(U("wiz"), den.Id, new Target(Tile: T(1, 1))));
        Assert.Equal<BattleEvent>(
            [new Spawned(Wolf, T(5, 0), "beasts.wolf"), new Spawned(U("wiz.wolf.1"), T(4, 0), "beasts.wolf")], events[1..]);
    }

    [Fact]
    public void Summon_rounds_run_out_at_the_owner_turn_end()
    {
        var brief = Summon("test.brief", new SummonEffect("wolf", Rounds: 1), concentration: false);
        var battle = Build(brief);
        var (state, _) = battle.Start();

        (state, _) = battle.Apply(state, new Use(U("wiz"), brief.Id, new Target(Tile: T(1, 1))));
        (state, _) = battle.Apply(state, new EndTurn(U("wiz")));       // the turn it was summoned in doesn't count
        Assert.Equal(LifeState.Up, state.Units[Wolf].Life);
        (state, _) = battle.Apply(state, new EndTurn(U("gob")));
        (state, _) = battle.Apply(state, new EndTurn(U("kara")));
        Assert.Equal([U("wiz"), Wolf, U("gob"), U("kara")], state.Order);

        // Round 2: the summoner's turn end is the wolf's last; it leaves the order (it sat after the current index), and the
        // next turn is the goblin's.
        var (after, events) = battle.Apply(state, new EndTurn(U("wiz")));
        var gone = events.IndexOf(new Despawned(Wolf));
        Assert.True(gone >= 0);
        Assert.Equal(new ConditionRemoved(Wolf, Summoned), events[gone - 1]);
        Assert.Equal(new TurnStarted(U("gob"), 2), events[^1]);
        Assert.Equal(LifeState.Dead, after.Units[Wolf].Life);
        Assert.Equal([U("wiz"), U("gob"), U("kara")], after.Order);
        Assert.Equal(U("gob"), after.Active);
    }

    [Fact]
    public void Concentration_break_despawns_summon()
    {
        var battle = Build();
        var (state, _) = battle.Start();
        (state, _) = battle.Apply(state, new Use(U("wiz"), CallWolf.Id, new Target(Tile: T(1, 1))));
        (state, _) = battle.Apply(state, new EndTurn(U("wiz")));

        // The summoner drops: its concentration ends, and the wolf (still waiting to join the order) goes with it.
        var (after, events) = battle.Run(state, "gob", "wiz", null, new DamageEffect(TestAbilities.Flat("30"), "force", []));
        Assert.Equal(LifeState.Downed, after.Units[U("wiz")].Life);
        var broken = events.IndexOf(new ConcentrationBroken(U("wiz"), CallWolf.Id));
        Assert.Equal<BattleEvent>([new ConditionRemoved(Wolf, Summoned), new Despawned(Wolf)], events[(broken + 1)..(broken + 3)]);
        Assert.Equal(LifeState.Dead, after.Units[Wolf].Life);
        Assert.Empty(after.Joining);
        Assert.DoesNotContain(Wolf, after.Order);

        // Stunned (incapacitated) ends concentration too.
        (after, events) = battle.Run(state, "gob", "wiz", null, new ApplyEffect(TestConditions.Stunned.Id, 1));
        Assert.Contains(new ConcentrationBroken(U("wiz"), CallWolf.Id), events);
        Assert.Contains(new Despawned(Wolf), events);
        Assert.Equal(LifeState.Dead, after.Units[Wolf].Life);
    }

    [Fact]
    public void Removing_summon_before_current_index_keeps_next_turn_correct()
    {
        var battle = Build();
        var (state, _) = battle.Start();
        state = ToRoundTwo(battle, state);
        (state, _) = battle.Apply(state, new EndTurn(U("wiz")));
        (state, _) = battle.Apply(state, new EndTurn(Wolf));
        Assert.Equal([U("wiz"), Wolf, U("gob"), U("kara")], state.Order);
        Assert.Equal((2, U("gob")), (state.TurnIndex, state.Active));

        // The goblin ends the wizard's concentration: the wolf, before the current index, leaves; the index follows it down.
        var (after, events) = battle.Apply(state, new Use(U("gob"), Dispel.Id, new Target(Unit: U("wiz"))));
        Assert.Contains(new Despawned(Wolf), events);
        Assert.Equal([U("wiz"), U("gob"), U("kara")], after.Order);
        Assert.Equal((1, U("gob")), (after.TurnIndex, after.Active));

        // Nobody is skipped or repeated: kara is next, then round 3.
        (after, events) = battle.Apply(after, new EndTurn(U("gob")));
        Assert.Equal(new TurnStarted(U("kara"), 2), events[^1]);
        (after, events) = battle.Apply(after, new EndTurn(U("kara")));
        Assert.Contains(new RoundStarted(3, [U("wiz"), U("gob"), U("kara")]), events, new JsonEquality<BattleEvent>());
        Assert.Equal(U("wiz"), after.Active);
    }

    [Fact]
    public void Removing_unit_after_current_index_keeps_next_turn_correct()
    {
        var battle = Build();
        var (state, _) = battle.Start();
        state = ToRoundTwo(battle, state);
        Assert.Equal((0, U("wiz")), (state.TurnIndex, state.Active));

        // A new concentration spell ends the one on the wolf, which sits after the current index: the index stays.
        var (after, events) = battle.Apply(state, new Use(U("wiz"), Focus.Id, new Target()));
        Assert.Contains(new Despawned(Wolf), events);
        Assert.Equal([U("wiz"), U("gob"), U("kara")], after.Order);
        Assert.Equal((0, U("wiz")), (after.TurnIndex, after.Active));

        (after, events) = battle.Apply(after, new EndTurn(U("wiz")));
        Assert.Equal(new TurnStarted(U("gob"), 2), events[^1]);
    }

    [Fact]
    public void Summon_dies_at_zero_hp()
    {
        var battle = Build();
        var (state, _) = battle.Start();
        state = ToRoundTwo(battle, state);

        var (after, events) = battle.Run(state, "gob", Wolf.Value, null, new DamageEffect(TestAbilities.Flat("30"), "slashing", []));
        Assert.Contains(new Died(Wolf), events);
        Assert.DoesNotContain(events, e => e is Downed);
        Assert.Equal([U("wiz"), U("gob"), U("kara")], after.Order);
        Assert.Equal(0, after.TurnIndex);
    }

    [Fact]
    public void Monster_sheets_come_from_the_pack()
    {
        var rules = RulesPack.Load([ContentPaths.Fixture("minimal", "base")]);
        var wolf = rules.MonsterSheet("wolf")!;
        Assert.Equal(("Wolf", 11, 13, "beasts.wolf.grey", "bite", "brute"),
            (wolf.Name, wolf.MaxHp, wolf.Ac, wolf.Sprite, wolf.Weapon.Id, wolf.AiProfile));
        Assert.Equal(15, wolf.Score(Stat.Dex));
        Assert.Equal([new AbilityId("test.strike")], wolf.Abilities);
        Assert.Null(rules.MonsterSheet("dragon"));

        var memory = RulesPack.InMemory("test", monsters: [("wolf", TestSheets.Wolf)]);
        Assert.Same(TestSheets.Wolf, memory.MonsterSheet("wolf"));
    }

    [Fact]
    public void State_with_zone_and_summon_round_trips()
    {
        var battle = Build();
        var (state, _) = battle.Start();
        (state, _) = battle.Apply(state, new Use(U("wiz"), CallWolf.Id, new Target(Tile: T(1, 1))));
        state = state with
        {
            Zones =
            [
                new Zone(5, U("wiz"), "fire", [T(3, 1)], 2, false, true,
                    [new PassiveDef("zone.fire", HookKind.OnEnterTile, null, [new DamageEffect(TestAbilities.Flat("1d6"), "fire", [])], null,
                        Target: PassiveTarget.Self)], AppliedThisTurn: true),
            ],
            Queue = [new RunEffectsStep(U("wiz"), U("gob"), T(5, 1), null, [new DamageEffect(TestAbilities.Flat("1d6"), "fire", [])],
                HookDepth: 1, Passive: new PassiveRef(U("gob"), "zone.fire", Zone: 5))],
        };
        state = state.Change("gob", u => u with { TrueSheet = u.Sheet, Sheet = TestSheets.Sheep with { Side = Side.Enemy } });

        var json = JsonSerializer.Serialize(state, ArcJson.Options);
        var back = JsonSerializer.Deserialize<BattleState>(json, ArcJson.Options)!;

        Assert.Equal(json, JsonSerializer.Serialize(back, ArcJson.Options));
        Assert.Equal([Wolf], back.Joining);
        Assert.Equal(U("wiz"), back.Units[Wolf].SummonedBy);
        Assert.Equal("Goblin", back.Units[U("gob")].TrueSheet!.Name);
        Assert.Contains("\"appliedThisTurn\":true", json);
        Assert.Contains("\"zone\":5", json);
        Assert.Equal("zone.fire", back.Zones[0].Passives[0].Id);
    }

    [Fact]
    public void New_events_round_trip()
    {
        ImmutableArray<BattleEvent> events =
        [
            new ZoneCreated(new Zone(1, null, "fire", [T(1, 1)], null, false, false, [])),
            new ZoneEnded(1),
            new TerrainAltered([T(1, 1), T(2, 1)], -2),
            new Spawned(Wolf, T(1, 1), "beasts.wolf"),
            new Spawned(U("x"), T(0, 0), null),
            new Despawned(Wolf),
            new Transformed(U("wiz"), "beasts.wolf"),
            new TurnOrderChanged([U("wiz"), U("gob")]),
            new Marked(U("gob"), U("wiz")),
            new TempHpGained(U("wiz"), 5),
            new Countered(U("gob"), new AbilityId("test.scorch"), U("wiz")),
        ];

        var json = JsonSerializer.Serialize(events, ArcJson.Options);
        Assert.Equal(json,
            JsonSerializer.Serialize(JsonSerializer.Deserialize<ImmutableArray<BattleEvent>>(json, ArcJson.Options), ArcJson.Options));
        foreach (var name in new[] { "ZoneCreated", "ZoneEnded", "TerrainAltered", "Spawned", "Despawned", "Transformed",
                     "TurnOrderChanged", "Marked", "TempHpGained", "Countered" })
            Assert.Contains($"\"$type\":\"{name}\"", json);
    }
}
