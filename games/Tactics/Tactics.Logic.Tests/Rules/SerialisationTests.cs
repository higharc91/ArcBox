using System.Collections.Immutable;
using System.Text.Json;
using ArcBox.Sdk;

namespace Tactics.Logic.Tests;

public sealed class SerialisationTests
{
    private const string Field = """
        0. 0. 0. 0.
        0. 0. 0. 0.
        """;

    private static UnitId U(string id) => new(id);

    /// <summary>A started battle with every part of the state filled in, including conditions, a queue entry and a Pending.</summary>
    private static BattleState RichState()
    {
        var (state, _) = TestBattle.On(Field)
            .Party("kara", TestSheets.Fighter, new Tile(0, 0))
            .Party("Brin", TestSheets.Cleric, new Tile(1, 0))
            .Enemy("gob", TestSheets.Goblin, new Tile(3, 1))
            .Start(seed: 42);

        var kara = state.Units[U("kara")];
        kara = kara with
        {
            Hp = 5,
            TempHp = 2,
            Conditions =
            [
                new ActiveCondition(new ConditionId("prone"), null, null, false, 0, Stat.Str, false),
                new ActiveCondition(new ConditionId("blessed"), U("Brin"), 10, false, 0, Stat.Wis, true),
                new ActiveCondition(new ConditionId("frightened"), U("gob"), null, true, 13, Stat.Wis, false),
            ],
            Economy = new TurnEconomy(2, false, true, true, 1),
            UsesLeft = Ordinal.Map(("second_wind", 0), ("Action_surge", 1)),
            Bag = [new ItemStack(new ItemId("potion.healing"), 2)],
        };
        var brin = state.Units[U("Brin")] with { Concentrating = new AbilityId("bless"), Resources = Ordinal.Map(("mana", 3), ("devotion", 1)) };
        var gob = state.Units[U("gob")] with { Life = LifeState.Downed, Countdown = 2, SummonedBy = U("Brin"), Protected = true };

        var objectives = new ObjectivesDef(
            Win: new ObjectiveExpr(null, [ObjectiveExpr.Of("defeat_all"), ObjectiveExpr.Of("survive:8")], []),
            Lose: new ObjectiveExpr(null, [], [ObjectiveExpr.Of("party_out"), ObjectiveExpr.Of("escort_dead:npc")]));

        return state with
        {
            Units = state.Units.SetItem(kara.Id, kara).SetItem(brin.Id, brin).SetItem(gob.Id, gob),
            Queue = [new EndTurnStep(U("kara")), new DelayStep(U("kara"), 1)],
            Pending = new ReactionOffer(U("gob"),
                [new ReactionChoice("pass", null, null), new ReactionChoice("opportunity", new AbilityId("common.attack"), U("kara"))],
                "leavesReach"),
            HeightDelta = ImmutableSortedDictionary<Tile, int>.Empty.Add(new Tile(1, 1), 2).Add(new Tile(0, 1), -1),
            Zones = [new Zone(3, U("Brin"), "spirit_guardians", [new Tile(0, 0), new Tile(1, 0)], 10, true, true, [new PassiveDef("zone.damage")])],
            Objectives = new ObjectiveState(objectives, Ordinal.Map(("survive", 2))),
            Flags = Ordinal.Set("bridge_down", "B", "a"),
            Counters = Ordinal.Map(("kills", 3), ("Waves", 1)),
            FiredTriggers = Ordinal.Set("t2", "T1"),
            Injuries = [new RecordedInjury(U("kara"), new InjuryId("cracked_rib"))],
            ItemsUsed = [new ItemUse(U("kara"), new ItemId("potion.healing"), false)],
            NextSerial = 4,
            StartedThisRound = [U("gob"), U("Brin")],
            DelayedThisRound = [U("Brin")],
            Xp = 300,
            Loot = [new LootTableId("goblin_cache"), new LootTableId("Coins")],
            Difficulty = Difficulty.Hard,
            Triggers =
            [
                new TriggerDef("reinforcements", Json("""{"round":3}"""), true, Json("""[{"spawn":{"monster":"goblin","at":[3,1]}}]""")),
            ],
        };
    }

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    [Fact]
    public void State_round_trips_byte_identical()
    {
        var state = RichState();

        var json = JsonSerializer.Serialize(state, ArcJson.Options);
        var back = JsonSerializer.Deserialize<BattleState>(json, ArcJson.Options)!;
        var again = JsonSerializer.Serialize(back, ArcJson.Options);

        Assert.Equal(json, again);
        Assert.Contains("\"$type\":\"EndTurnStep\"", json);
        Assert.Contains("\"$type\":\"ReactionOffer\"", json);
        Assert.Contains("\"$type\":\"PlayerControlled\"", json);
        Assert.Contains("\"1,1\":2", json);
        Assert.DoesNotContain("\"active\"", json);

        Assert.Contains("\"triggers\":[{\"id\":\"reinforcements\",\"when\":{\"round\":3},\"once\":true,", json);
        Assert.Contains("\"difficulty\":\"Hard\"", json);

        Assert.Equal(state.Active, back.Active);
        Assert.Equal(300, back.Xp);
        Assert.Equal(state.Loot, back.Loot);
        Assert.Equal(state.StartedThisRound, back.StartedThisRound);
        Assert.Equal(state.DelayedThisRound, back.DelayedThisRound);
        Assert.Equal(state.Rng, back.Rng);
        Assert.Equal(state.Queue, back.Queue);
        Assert.Equal(state.Units[U("kara")].Conditions, back.Units[U("kara")].Conditions);
        Assert.IsType<ReactionOffer>(back.Pending);
        Assert.Equal("leavesReach", ((ReactionOffer)back.Pending).Trigger);
    }

    [Fact]
    public void Turn_handoff_is_carried_by_the_queue_and_survives_a_save()
    {
        var battle = TestBattle.On(Field)
            .Party("kara", TestSheets.Rogue, new Tile(0, 0))
            .Enemy("gob", TestSheets.Goblin, new Tile(3, 1));
        var (state, _) = battle.Start(seed: 9);

        // Resolve EndTurn's first step by hand: the turn has ended but not yet been handed on.
        var w = new BattleWriter(state, battle.Rules);
        w.PushFront(new EndTurnStep(U("kara")));
        w.PopFront()!.Run(w, battle.Rules);
        Assert.Equal<BattleEvent>([new TurnEnded(U("kara"))], w.Events);

        // Pause there, as a turn-end reaction would, and save.
        var paused = w.Freeze() with
        {
            Pending = new ReactionOffer(U("gob"), [new ReactionChoice("pass", null, null)], "turnEnd"),
        };
        Assert.Equal([new StartNextTurnStep()], paused.Queue);
        Assert.Equal(U("kara"), paused.Active);

        var json = JsonSerializer.Serialize(paused, ArcJson.Options);
        var loaded = JsonSerializer.Deserialize<BattleState>(json, ArcJson.Options)!;
        Assert.Equal(json, JsonSerializer.Serialize(loaded, ArcJson.Options));
        Assert.Equal(U("kara"), loaded.Active);

        // Answering the pause resumes the hand-off identically from the saved and the in-memory state.
        var (original, originalEvents) = Resume(paused with { Pending = null });
        var (resumed, resumedEvents) = Resume(loaded with { Pending = null });
        Assert.Equal<BattleEvent>([new TurnStarted(U("gob"), 1)], originalEvents);
        Assert.Equal(originalEvents, resumedEvents);
        Assert.Equal(U("gob"), resumed.Active);
        Assert.Equal(JsonSerializer.Serialize(original, ArcJson.Options), JsonSerializer.Serialize(resumed, ArcJson.Options));

        (BattleState, ImmutableArray<BattleEvent>) Resume(BattleState s)
        {
            var writer = new BattleWriter(s, battle.Rules);
            while (writer.PopFront() is { } step)
                step.Run(writer, battle.Rules);
            return (writer.Freeze(), writer.Events);
        }
    }

    [Fact]
    public void String_keyed_collections_stay_ordinal_after_deserialising()
    {
        var json = JsonSerializer.Serialize(RichState(), ArcJson.Options);
        var back = JsonSerializer.Deserialize<BattleState>(json, ArcJson.Options)!;

        Assert.Equal(["B", "a", "bridge_down"], back.Flags);
        Assert.Same(StringComparer.Ordinal, back.Flags.KeyComparer);
        Assert.Same(StringComparer.Ordinal, back.Counters.KeyComparer);
        Assert.Same(StringComparer.Ordinal, back.FiredTriggers.KeyComparer);
        Assert.Same(StringComparer.Ordinal, back.Objectives.Progress.KeyComparer);
        var kara = back.Units[U("kara")];
        Assert.Same(StringComparer.Ordinal, kara.UsesLeft.KeyComparer);
        Assert.Same(StringComparer.Ordinal, kara.Resources.KeyComparer);
        Assert.Same(StringComparer.Ordinal, kara.Sheet.Skills.KeyComparer);
        Assert.Same(StringComparer.Ordinal, kara.Sheet.Resources.KeyComparer);
        Assert.Same(StringComparer.Ordinal, kara.Sheet.Tags.KeyComparer);
        Assert.Same(StringComparer.Ordinal, kara.Sheet.Resist.KeyComparer);
        Assert.Same(StringComparer.Ordinal, kara.Sheet.Weapon.Tags.KeyComparer);
        Assert.Equal(["Action_surge", "second_wind"], kara.UsesLeft.Keys);
    }

    [Fact]
    public void Commands_and_events_round_trip()
    {
        ImmutableArray<Command> commands =
        [
            new Move(U("kara"), [new Tile(0, 0), new Tile(1, 0)]),
            new Use(U("kara"), new AbilityId("common.attack"), new Target(Unit: U("gob"))),
            new Use(U("Brin"), new AbilityId("fireball"), new Target(Tile: new Tile(3, 1))),
            new Delay(U("kara"), 2),
            new React(U("gob"), new ReactionChoice("pass", null, null)),
            new EndTurn(U("kara")),
        ];
        ImmutableArray<BattleEvent> events =
        [
            new RoundStarted(1, [U("kara"), U("gob")]),
            new TurnStarted(U("kara"), 1),
            new Delayed(U("kara"), 1),
            new TurnEnded(U("gob")),
        ];

        var commandJson = JsonSerializer.Serialize(commands, ArcJson.Options);
        var eventJson = JsonSerializer.Serialize(events, ArcJson.Options);

        Assert.StartsWith("[{\"$type\":\"Move\",\"actor\":\"kara\",\"path\":[[0,0],[1,0]]}", commandJson);
        Assert.Equal(commandJson,
            JsonSerializer.Serialize(JsonSerializer.Deserialize<ImmutableArray<Command>>(commandJson, ArcJson.Options), ArcJson.Options));
        Assert.Equal(eventJson,
            JsonSerializer.Serialize(JsonSerializer.Deserialize<ImmutableArray<BattleEvent>>(eventJson, ArcJson.Options), ArcJson.Options));
    }

    [Fact]
    public void Objective_leaf_is_a_bare_string()
    {
        Assert.Equal("\"defeat_all\"", JsonSerializer.Serialize(ObjectiveExpr.Of("defeat_all"), ArcJson.Options));

        var json = """{"any":["defeat_all",{"all":["survive:8","flag:bridge_down"]}]}""";
        var expr = JsonSerializer.Deserialize<ObjectiveExpr>(json, ArcJson.Options)!;
        Assert.Null(expr.Leaf);
        Assert.Equal("defeat_all", expr.Any[0].Leaf);
        Assert.Equal("flag:bridge_down", expr.Any[1].All[1].Leaf);
        Assert.Equal(json, JsonSerializer.Serialize(expr, ArcJson.Options));

        Assert.Equal("""{"win":"defeat_all","lose":"party_out"}""", JsonSerializer.Serialize(ObjectivesDef.Default, ArcJson.Options));
        var bad = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ObjectiveExpr>("""{"some":[]}""", ArcJson.Options));
        Assert.Contains("some", bad.Message);
    }
}
