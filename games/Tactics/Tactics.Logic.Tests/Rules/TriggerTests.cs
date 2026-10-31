using System.Collections.Immutable;

namespace Tactics.Logic.Tests;

/// <summary>Encounter triggers (spec §4.8): their conditions, their actions and when they are checked.</summary>
public sealed class TriggerTests
{
    private const string Field = """
        0. 0. 0. 0. 0. 0.
        0. 0. 0. 0. 0. 0.
        0. 0. 0. 0. 0. 0.
        """;

    /// <summary>A chapter monster with one phase, at 50% (the minimal base pack's monsters have none).</summary>
    private static readonly (string Path, string Json) Warboss = ("monsters/bosses.json", """
        [ { "id": "warboss", "name": "Warboss", "cr": "1", "xp": 200, "level": 2,
            "scores": { "str": 12, "dex": 12, "con": 12, "int": 10, "wis": 10, "cha": 10 }, "hp": 20, "ac": 13,
            "weapon": { "id": "club", "damage": "1d6", "damageType": "bludgeoning", "stat": "str", "finesse": false, "ranged": false,
                        "range": 1, "reach": false, "tags": [] },
            "abilities": ["test.strike"], "passives": [], "tags": ["humanoid"], "aiProfile": "brute", "sprite": "beasts.wolf.grey",
            "phases": [ { "hpBelow": 50, "abilities": ["test.strike"], "passives": [] } ] } ]
        """);

    private static UnitId U(string id) => new(id);

    private static Tile T(int x, int y) => new(x, y);

    private static UnitSheet First(UnitSheet sheet) => sheet with { InitiativeBonus = 20 };

    private static UnitSheet Tagged(UnitSheet sheet, string tag) => sheet with { Tags = sheet.Tags.Add(tag) };

    private static TriggerDef Trigger(string id, TriggerWhen when, params TriggerAction[] actions)
        => new(id, when, Once: true, [.. actions]);

    /// <summary>Ends turns until round <paramref name="round"/> has started (or the battle is over).</summary>
    private static (BattleState State, ImmutableArray<BattleEvent> Events) PlayTo(TestBattle battle, BattleState state, int round)
    {
        var events = ImmutableArray.CreateBuilder<BattleEvent>();
        while (state.Round < round && state.Active is { } active)
        {
            var (next, more) = battle.Apply(state, new EndTurn(active));
            state = next;
            events.AddRange(more);
        }
        return (state, events.ToImmutable());
    }

    [Fact]
    public void Round_3_spawns_and_says()
    {
        var map = AsciiMap.Parse(Field, Ordinal.Map(("ridge", ImmutableArray.Create(T(5, 0), T(5, 1), T(5, 2)))));
        var battle = TestBattle.On(map)
            .Party("kara", First(TestSheets.Fighter), T(0, 0))
            .Enemy("gob", TestSheets.Goblin, T(3, 2))
            .WithMonsters(("archer", TestSheets.Goblin with { Name = "Archer" }))
            .WithTriggers(Trigger("ambush", new TriggerWhen(Round: 3),
                new TriggerAction(Say: new SayAction("gob", "Archers, to the ridge!")),
                new TriggerAction(Spawn: new SpawnAction("archer", new PlaceRef("ridge", null), Count: 2, Tag: "archers"))));
        var (state, _) = battle.Start(seed: 1);

        var (round2, early) = PlayTo(battle, state, 2);
        Assert.DoesNotContain(early, e => e is TriggerFired);

        var (round3, events) = PlayTo(battle, round2, 3);
        Assert.Equal(new TriggerFired("ambush"), Assert.Single(events.OfType<TriggerFired>()));
        Assert.Equal(new Said("gob", "Archers, to the ridge!"), Assert.Single(events.OfType<Said>()));
        Assert.Equal([U("archer.1"), U("archer.2")], events.OfType<Spawned>().Select(s => s.Unit));
        Assert.Equal([T(5, 0), T(5, 1)], events.OfType<Spawned>().Select(s => s.At));
        var archer = round3.Units[U("archer.1")];
        Assert.Equal(Side.Enemy, archer.Sheet.Side);
        Assert.Contains("archers", archer.Sheet.Tags);
        Assert.IsType<AiControlled>(archer.Controller);
        Assert.Contains(U("archer.1"), round3.Joining);   // they act from the next round

        var (round4, later) = PlayTo(battle, round3, 4);
        Assert.DoesNotContain(later, e => e is TriggerFired);   // once
        Assert.Contains(U("archer.2"), round4.Order);
    }

    [Fact]
    public void Hp_below_50_triggers_phase_2_once()
    {
        var warcry = new AbilityId("boss.warcry");
        var phase = new PhaseDef(50, [warcry], ["boss.fury"], "boss_cruel");
        var battle = TestBattle.On(Field)
            .Party("kara", First(TestSheets.Fighter).Knowing(DownedTests.Smite), T(2, 1))
            .Enemy("boss", Tagged(TestSheets.Goblin with { MaxHp = 20 }, "boss"), T(3, 1))
            .With(DownedTests.Smite)
            .WithTriggers(Trigger("enraged", new TriggerWhen(HpBelow: new HpWatch("boss", 50)),
                new TriggerAction(Say: new SayAction("boss", "Enough!")),
                new TriggerAction(Phase: new PhaseAction("boss", 1, phase))));
        var (state, startEvents) = battle.Start(seed: 9);
        Assert.DoesNotContain(startEvents, e => e is TriggerFired);

        // 10 of 20 is not below half; 9 is.
        var (at10, none) = battle.Apply(state.Change("boss", u => u with { Hp = 10 }), new EndTurn(U("kara")));
        Assert.DoesNotContain(none, e => e is TriggerFired);
        var (after, events) = battle.Apply(at10.Change("boss", u => u with { Hp = 9 }), new EndTurn(U("boss")));

        Assert.Equal(new TriggerFired("enraged"), Assert.Single(events.OfType<TriggerFired>()));
        Assert.Equal(new PhaseEntered(U("boss"), 1), Assert.Single(events.OfType<PhaseEntered>()));
        var boss = after.Units[U("boss")];
        Assert.Contains(warcry, boss.Sheet.Abilities);
        Assert.Contains("boss.fury", boss.Sheet.Passives);
        Assert.Equal("boss_cruel", boss.Sheet.AiProfile);
        Assert.Equal(new AiControlled("boss_cruel"), boss.Controller);

        var (_, later) = PlayTo(battle, after.Change("boss", u => u with { Hp = 5 }), after.Round + 2);
        Assert.DoesNotContain(later, e => e is TriggerFired or PhaseEntered);
    }

    [Fact]
    public void Enter_zone_collapses_bridge_with_alterTerrain()
    {
        const string gorge = """
            2. 2. 2. 2. 2.
            2. 2. 2. 2. 2.
            2. 2. 2. 2. 2.
            """;
        var bridge = ImmutableArray.Create(T(2, 0), T(2, 1), T(2, 2));
        var map = AsciiMap.Parse(gorge, Ordinal.Map(("bridge", bridge)));
        var collapse = new AlterTerrainEffect(new AreaDef(AreaShape.Single), -9, Deep: true);
        var battle = TestBattle.On(map)
            .Party("kara", First(TestSheets.Fighter), T(1, 1))
            .Party("vex", TestSheets.Rogue, T(2, 0))
            .Party("bo", TestSheets.Cleric, T(0, 2))
            .Enemy("gob", TestSheets.Goblin, T(4, 2))
            .WithTriggers(Trigger("collapse", new TriggerWhen(UnitEntersZone: new ZoneWatch(Objectives.PartyTag, "bridge", 2)),
                new TriggerAction(Effects: [collapse], Zone: "bridge"),
                new TriggerAction(SetFlag: "bridge_down")));
        var (state, start) = battle.Start(seed: 3);
        Assert.DoesNotContain(start, e => e is TriggerFired);   // only vex is on the bridge

        var (after, events) = battle.Apply(state, new Move(U("kara"), [T(1, 1), T(2, 1)]));

        Assert.Equal(new TriggerFired("collapse"), Assert.Single(events.OfType<TriggerFired>()));
        Assert.Equal(3, events.OfType<TerrainAltered>().Count());
        Assert.Equal(bridge, after.Sunk!.ToImmutableArray());
        Assert.All(bridge, t => Assert.Equal(-2, after.HeightDelta[t]));
        Assert.Equal([U("kara"), U("vex")], events.OfType<Fell>().Where(f => f.IntoDeep).Select(f => f.Unit).Order());
        Assert.Equal(LifeState.Downed, after.Units[U("kara")].Life);
        Assert.Equal(LifeState.Downed, after.Units[U("vex")].Life);
        Assert.Contains("bridge_down", after.Flags);
        Assert.Null(after.Outcome);   // bo still stands

        // The chasm can't be walked into any more.
        var w = new BattleWriter(after, battle.Rules);
        Assert.True(w.Terrain.Deep(T(2, 2)));
        Assert.False(w.Terrain.Walkable(T(2, 2)));
    }

    [Fact]
    public void A_walk_stops_short_of_an_end_tile_taken_during_it()
    {
        // Stepping on the gate spawns a helper on the walk's last tile: the walker can pass allies, not stop on one.
        var map = AsciiMap.Parse("""
            0. 0. 0. 0. 0.
            """, Ordinal.Map(("gate", ImmutableArray.Create(T(1, 0))), ("end", ImmutableArray.Create(T(3, 0)))));
        var battle = TestBattle.On(map)
            .Party("kara", First(TestSheets.Fighter), T(0, 0))
            .Enemy("gob", TestSheets.Goblin, T(4, 0))
            .WithMonsters(("helper", TestSheets.Goblin with { Name = "Helper" }))
            .WithTriggers(Trigger("arrive", new TriggerWhen(UnitEntersZone: new ZoneWatch(Objectives.PartyTag, "gate", 1)),
                new TriggerAction(Spawn: new SpawnAction("helper", new PlaceRef("end", null), Ally: true))));
        var (state, _) = battle.Start(seed: 1);

        var (after, _) = battle.Apply(state, new Move(U("kara"), [T(0, 0), T(1, 0), T(2, 0), T(3, 0)]));

        Assert.Equal(T(2, 0), after.Units[U("kara")].Pos);
        Assert.Equal(after.Units.Count, after.Units.Values.Select(u => u.Pos).Distinct().Count());
    }

    [Fact]
    public void Collapse_drops_both_units_sharing_a_tile_mid_walk()
    {
        const string gorge = """
            2. 2. 2. 2. 2.
            2. 2. 2. 2. 2.
            2. 2. 2. 2. 2.
            """;
        var bridge = ImmutableArray.Create(T(2, 0), T(2, 1), T(2, 2));
        var map = AsciiMap.Parse(gorge, Ordinal.Map(("bridge", bridge)));
        var collapse = new AlterTerrainEffect(new AreaDef(AreaShape.Single), -9, Deep: true);
        var battle = TestBattle.On(map)
            .Party("kara", First(TestSheets.Fighter), T(1, 1))
            .Party("vex", TestSheets.Rogue, T(2, 1))
            .Party("bo", TestSheets.Cleric, T(0, 2))
            .Enemy("gob", TestSheets.Goblin, T(4, 2))
            .WithTriggers(Trigger("collapse", new TriggerWhen(UnitEntersZone: new ZoneWatch(Objectives.PartyTag, "bridge", 2)),
                new TriggerAction(Effects: [collapse], Zone: "bridge")));
        var (state, _) = battle.Start(seed: 3);

        // Kara's first step lands on vex's tile, and that makes two on the bridge.
        var (after, events) = battle.Apply(state, new Move(U("kara"), [T(1, 1), T(2, 1), T(3, 1)]));

        Assert.Equal([U("kara"), U("vex")], events.OfType<Fell>().Select(f => f.Unit).Order());
        Assert.Equal(LifeState.Downed, after.Units[U("kara")].Life);
        Assert.Equal(LifeState.Downed, after.Units[U("vex")].Life);
    }

    [Fact]
    public void Spawned_ally_is_not_a_party_member()
    {
        var battle = TestBattle.On(Field)
            .Party("kara", First(TestSheets.Fighter), T(0, 0))
            .Enemy("gob", TestSheets.Goblin, T(5, 2))
            .WithMonsters(("militia", TestSheets.Goblin with { Name = "Militia" }))
            .WithTriggers(Trigger("help", new TriggerWhen(Round: 2),
                new TriggerAction(Spawn: new SpawnAction("militia", new PlaceRef(null, T(0, 2)), Ally: true))));
        var (state, _) = battle.Start(seed: 1);

        var (round2, _) = PlayTo(battle, state, 2);
        var ally = Replay.RoundTrip(round2).Units[U("militia.1")];

        Assert.Equal(Side.Party, ally.Sheet.Side);
        Assert.False(Objectives.HasTag(ally, Objectives.PartyTag));
        Assert.False(DownedRules.GoesDown(ally));
        Assert.True(Objectives.Leaf(round2.Change("kara", u => u with { Life = LifeState.Downed, Hp = 0 }), Objectives.PartyOut));
        Assert.Equal([U("kara")], Battle.Result(round2 with { Outcome = BattleOutcome.Won }, battle.Rules)!.Xp.Keys);
    }

    [Fact]
    public void Round_1_spawn_joins_the_order_next_round()
    {
        var battle = TestBattle.On(Field)
            .Party("kara", First(TestSheets.Fighter), T(0, 0))
            .Enemy("gob", TestSheets.Goblin, T(3, 2))
            .WithMonsters(("archer", TestSheets.Goblin with { Name = "Archer", InitiativeBonus = 50 }))
            .WithTriggers(Trigger("ambush", new TriggerWhen(Round: 1),
                new TriggerAction(Spawn: new SpawnAction("archer", new PlaceRef(null, T(5, 0))))));

        var (state, events) = battle.Start(seed: 1);

        Assert.Contains(new TriggerFired("ambush"), events);
        Assert.Equal(U("kara"), state.Active);
        Assert.DoesNotContain(U("archer.1"), state.Order);
        Assert.Contains(U("archer.1"), state.Joining);

        var (round2, _) = PlayTo(battle, state, 2);
        Assert.Equal(U("archer.1"), round2.Order[0]);
    }

    [Fact]
    public void Validation_rejects_a_phase_on_a_built_in_tag()
    {
        var errors = ValidationTests.ErrorsOfChapter(Warboss, ("encounters/e.json", """
            { "id": "e", "name": "E", "map": "field", "party": { "min": 1, "max": 4 }, "enemies": [ { "unit": "warboss", "at": [5, 4] } ],
              "triggers": [ { "id": "t", "when": { "round": 2 }, "do": [ { "phase": { "tag": "enemy", "number": 1 } } ] } ] }
            """));

        Assert.Equal([new ContentError("chapter-01/encounters/e.json", "/triggers/0/do/0/phase/tag",
            "A phase needs the tag of a placed or spawned monster, not 'enemy'.")], errors);
    }

    [Fact]
    public void Validation_rejects_a_phase_tag_covering_two_monsters()
    {
        var errors = ValidationTests.ErrorsOfChapter(Warboss, ("encounters/e.json", """
            { "id": "e", "name": "E", "map": "field", "party": { "min": 1, "max": 4 },
              "enemies": [ { "unit": "warboss", "at": [5, 4], "tag": "pack" }, { "unit": "wolf", "at": [4, 4], "tag": "pack" } ],
              "triggers": [ { "id": "t", "when": { "round": 2 }, "do": [ { "phase": { "tag": "pack", "number": 1 } } ] } ] }
            """));

        Assert.Equal([new ContentError("chapter-01/encounters/e.json", "/triggers/0/do/0/phase/tag",
            "The tag 'pack' covers the monsters 'warboss' and 'wolf', but a phase comes from one monster.")], errors);
    }

    [Fact]
    public void Validation_rejects_a_trigger_percent_that_disagrees_with_the_phase()
    {
        var errors = ValidationTests.ErrorsOfChapter(Warboss, ("encounters/e.json", """
            { "id": "e", "name": "E", "map": "field", "party": { "min": 1, "max": 4 },
              "enemies": [ { "unit": "warboss", "at": [5, 4], "tag": "boss" } ],
              "triggers": [ { "id": "t", "when": { "hpBelow": { "tag": "boss", "percent": 40 } },
                              "do": [ { "phase": { "tag": "boss", "number": 1 } } ] } ] }
            """));

        Assert.Equal([new ContentError("chapter-01/encounters/e.json", "/triggers/0/when/hpBelow/percent",
            "This trigger watches 40%, but phase 1 of 'warboss' is at 50%.")], errors);
    }

    [Fact]
    public void Trigger_set_flag_seen_next_checkpoint_not_same_step()
    {
        var battle = TestBattle.On(Field)
            .Party("kara", First(TestSheets.Fighter), T(0, 0))
            .Enemy("gob", TestSheets.Goblin, T(5, 2))
            .WithTriggers(
                Trigger("raise", new TriggerWhen(Round: 2), new TriggerAction(SetFlag: "alarm")),
                Trigger("answer", new TriggerWhen(Flag: "alarm"), new TriggerAction(Say: new SayAction("gob", "To arms!"))));
        var (state, _) = battle.Start(seed: 4);

        // One checkpoint: "raise" fires and queues its action; "answer" doesn't see a flag that isn't set yet.
        var w = new BattleWriter(state with { Round = 2 }, battle.Rules);
        Triggers.Check(w, battle.Rules);
        Assert.Equal([new TriggerFired("raise")], w.Events);
        Assert.IsType<TriggerActionStep>(w.State.Queue[0]);
        Assert.DoesNotContain("alarm", w.State.Flags);

        // The action runs as a step; the next checkpoint sees its flag.
        w.PopFront()!.Run(w, battle.Rules);
        Assert.Contains("alarm", w.State.Flags);
        Triggers.Check(w, battle.Rules);
        Assert.Equal([new TriggerFired("raise"), new TriggerFired("answer")], w.Events);

        // The same, played for real.
        var (_, events) = PlayTo(battle, state, 2);
        var fired = events.OfType<TriggerFired>().Select(f => f.Id).ToArray();
        Assert.Equal(["raise", "answer"], fired);
        Assert.Single(events.OfType<Said>());
    }

    [Fact]
    public void Repeatable_trigger_fires_again_once_its_condition_has_stopped_holding()
    {
        var battle = TestBattle.On(Field)
            .Party("kara", First(TestSheets.Fighter), T(0, 0))
            .Enemy("gob", TestSheets.Goblin, T(5, 2))
            .WithTriggers(new TriggerDef("hurt", new TriggerWhen(HpBelow: new HpWatch("goblinoid", 50)), Once: false,
                [new TriggerAction(AddCounter: new CounterAdd("hurt"))]));
        var (state, _) = battle.Start(seed: 5);

        var (low, _) = battle.Apply(state.Change("gob", u => u with { Hp = 1 }), new EndTurn(U("kara")));
        var (stillLow, _) = battle.Apply(low, new EndTurn(U("gob")));
        Assert.Equal(1, stillLow.Counters["hurt"]);   // held all along: once
        var (healed, _) = battle.Apply(stillLow.Change("gob", u => u with { Hp = u.Sheet.MaxHp }), new EndTurn(U("kara")));
        var (lowAgain, _) = battle.Apply(healed.Change("gob", u => u with { Hp = 1 }), new EndTurn(U("gob")));

        Assert.Equal(2, lowAgain.Counters["hurt"]);
    }

    [Fact]
    public void End_battle_and_set_objective_actions()
    {
        var battle = TestBattle.On(Field)
            .Party("kara", First(TestSheets.Fighter), T(0, 0))
            .Enemy("gob", TestSheets.Goblin, T(5, 2))
            .WithTriggers(
                Trigger("retarget", new TriggerWhen(Round: 2),
                    new TriggerAction(SetObjective: new ObjectivesPatch(Win: ObjectiveExpr.Of("survive:5"))),
                    new TriggerAction(CameraHint: new PlaceRef(null, T(5, 2)))),
                Trigger("flee", new TriggerWhen(Round: 3), new TriggerAction(EndBattle: BattleOutcome.Fled)));
        var (state, _) = battle.Start(seed: 6);

        var (round2, events) = PlayTo(battle, state, 2);
        Assert.Equal(ObjectiveExpr.Of("survive:5"), round2.Objectives.Current.Win);
        Assert.Equal(ObjectiveExpr.Of(Objectives.PartyOut), round2.Objectives.Current.Lose);
        Assert.Contains(events, e => e is ObjectiveChanged);
        Assert.Contains(new CameraHint(T(5, 2)), events);

        var (round3, ended) = PlayTo(battle, round2, 3);
        Assert.Equal(BattleOutcome.Fled, round3.Outcome);
        Assert.Equal(new BattleEnded(BattleOutcome.Fled), Assert.Single(ended.OfType<BattleEnded>()));
    }

    [Fact]
    public void Triggers_survive_a_save_mid_battle()
    {
        var battle = TestBattle.On(Field)
            .Party("kara", First(TestSheets.Fighter), T(0, 0))
            .Enemy("gob", TestSheets.Goblin, T(5, 2))
            .WithTriggers(Trigger("t", new TriggerWhen(Any: [new TriggerWhen(Round: 2), new TriggerWhen(Flag: "x")]),
                new TriggerAction(Effects: [new DamageEffect(TestAbilities.Flat("1d4"), "fire", [])], Tag: "goblinoid"),
                new TriggerAction(SetFlag: "x")));
        var (state, _) = battle.Start(seed: 8);

        var back = Replay.RoundTrip(state);
        var (_, events) = PlayTo(battle, back, 2);

        Assert.Contains(events, e => e is Damaged { Unit.Value: "gob", Type: "fire" });
    }

    [Fact]
    public void Validation_rejects_flag_never_set()
    {
        var errors = ValidationTests.ErrorsOfChapter(("encounters/e.json", """
            { "id": "e", "name": "E", "map": "field", "party": { "min": 1, "max": 4 }, "enemies": [ { "unit": "wolf", "at": [5, 4] } ],
              "objectives": { "win": { "any": ["defeat_all", "flag:gate_open"] } },
              "triggers": [ { "id": "t", "when": { "flag": "bridge_down" }, "do": [ { "say": { "speaker": "wolf", "line": "Awoo." } } ] },
                            { "id": "c", "when": { "counter": { "name": "waves", "atLeast": 2 } }, "do": [ { "setFlag": "seen" } ] },
                            { "id": "o", "when": { "objective": { "name": "survive:3", "state": "met" } }, "do": [ { "setFlag": "late" } ] },
                            { "id": "p", "when": { "round": 2 }, "do": [ { "phase": { "tag": "wolf", "number": 1 } },
                                                                          { "setFlag": "x", "zone": "exit" },
                                                                          { "effects": [ { "damage": { "dice": "1d4", "type": "fire" } } ] } ] } ] }
            """));

        const string file = "chapter-01/encounters/e.json";
        Assert.Equal(
            [
                new ContentError(file, "/objectives/win/any/1", "No trigger in this encounter sets the flag 'gate_open'."),
                new ContentError(file, "/triggers/0/when/flag", "No trigger in this encounter sets the flag 'bridge_down'."),
                new ContentError(file, "/triggers/1/when/counter/name", "No trigger in this encounter adds to the counter 'waves'."),
                new ContentError(file, "/triggers/2/when/objective/name", "The objective 'survive:3' isn't one of this encounter's objectives."),
                new ContentError(file, "/triggers/3/do/0/phase/number", "The monster 'wolf' has 0 phases, so it has no phase 1."),
                new ContentError(file, "/triggers/3/do/1", "\"tag\", \"zone\", \"at\" and \"source\" go only with \"effects\"."),
                new ContentError(file, "/triggers/3/do/2", "Trigger effects need exactly one of \"tag\", \"zone\" or \"at\"."),
            ],
            errors.OrderBy(e => e.Pointer, StringComparer.Ordinal));
    }

    [Fact]
    public void Validation_checks_trigger_effect_blocks_fully()
    {
        var errors = ValidationTests.ErrorsOfChapter(("encounters/e.json", """
            { "id": "e", "name": "E", "map": "field", "party": { "min": 1, "max": 4 }, "enemies": [ { "unit": "wolf", "at": [5, 4] } ],
              "triggers": [ { "id": "t", "when": { "round": 2 },
                              "do": [ { "effects": [ { "apply": "no_such_condition", "rounds": 1 } ], "tag": "wolf" } ] } ] }
            """));

        Assert.Equal([new ContentError("chapter-01/encounters/e.json", "/triggers/0/do/0/effects/0/apply",
            "Unknown condition 'no_such_condition'.")], errors);
    }
}
