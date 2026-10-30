using System.Collections.Immutable;
using System.Text.Json;
using ArcBox.Sdk;

namespace Tactics.Logic.Tests;

public sealed class MotionTests
{
    private const string Field = """
        0. 0. 0. 0. 0. 0.
        0. 0. 0. 0. 0. 0.
        0. 0. 0. 0. 0. 0.
        """;

    private static readonly ConditionId Prone = new("prone");

    private static UnitId U(string id) => new(id);

    private static Tile T(int x, int y) => new(x, y);

    /// <summary>A goblin with 30 HP, so knocks don't matter.</summary>
    private static readonly UnitSheet Goblin = TestSheets.Goblin with { MaxHp = 30 };

    private static string Json(ImmutableArray<BattleEvent> events) => JsonSerializer.Serialize(events, ArcJson.Options);

    /// <summary>Runs one effect from <paramref name="source"/> on <paramref name="target"/> aimed at <paramref name="aim"/>, draining the queue.</summary>
    private static (BattleState State, ImmutableArray<BattleEvent> Events) Run(TestBattle battle, BattleState state, string source,
        string? target, Tile? aim, EffectDef effect)
    {
        var w = new BattleWriter(state, battle.Rules);
        w.PushFront(new RunEffectsStep(U(source), target is null ? null : U(target), aim, null, [effect]));
        while (w.PopFront() is { } step)
            step.Run(w, battle.Rules);
        return (w.Freeze(), w.Events);
    }

    [Fact]
    public void Push_2_moves_two_tiles_away()
    {
        var battle = TestBattle.On(Field)
            .Party("kara", TestSheets.Fighter, T(0, 1))
            .Enemy("gob", Goblin, T(1, 1));
        var (state, _) = battle.Start();

        var (after, events) = Run(battle, state, "kara", "gob", T(1, 1), new PushEffect(2));

        Assert.Equal(Json([new Pushed(U("gob"), T(1, 1), T(2, 1)), new Pushed(U("gob"), T(2, 1), T(3, 1))]), Json(events));
        Assert.Equal(T(3, 1), after.Units[U("gob")].Pos);
        Assert.Equal(30, after.Units[U("gob")].Hp);
        Assert.Equal(state.Rng, after.Rng);                            // no collision, no roll

        // Diagonal source: the dominant axis decides, and a tie goes to X.
        var diagonal = TestBattle.On(Field)
            .Party("kara", TestSheets.Fighter, T(0, 0))
            .Enemy("gob", Goblin, T(1, 1));
        (state, _) = diagonal.Start();
        (after, events) = Run(diagonal, state, "kara", "gob", T(1, 1), new PushEffect(2));
        Assert.Equal(Json([new Pushed(U("gob"), T(1, 1), T(2, 1)), new Pushed(U("gob"), T(2, 1), T(3, 1))]), Json(events));
    }

    [Fact]
    public void Push_into_wall_1d4_both()
    {
        const string walled = """
            0. 0. 0. 0# 0.
            0. 0. 0. 0. 0.
            """;
        var battle = TestBattle.On(walled)
            .Party("kara", TestSheets.Fighter, T(0, 0))
            .Enemy("gob", Goblin, T(1, 0))
            .Enemy("bob", Goblin, T(3, 1));
        var (state, _) = battle.Start();

        // An impassable tile: the push stops at (2, 0) and only the pushed unit takes 1d4.
        var wallDie = TestAbilities.Next(state.Rng, 4)[0];
        var (after, events) = Run(battle, state, "kara", "gob", T(1, 0), new PushEffect(3));
        Assert.Equal(Json(
        [
            new Pushed(U("gob"), T(1, 0), T(2, 0)),
            new Collided(U("gob"), null, wallDie),
            new Damaged(U("gob"), wallDie, "bludgeoning", 30 - wallDie),
        ]), Json(events));
        Assert.Equal(T(2, 0), after.Units[U("gob")].Pos);

        // A unit in the way: both take 1d4.
        var crowded = TestBattle.On(walled)
            .Party("kara", TestSheets.Fighter, T(0, 1))
            .Enemy("gob", Goblin, T(1, 1))
            .Enemy("bob", Goblin, T(3, 1));
        (state, _) = crowded.Start();
        var dice = TestAbilities.Next(state.Rng, 4, 4);
        (after, events) = Run(crowded, state, "kara", "gob", T(1, 1), new PushEffect(3));
        Assert.Equal(Json(
        [
            new Pushed(U("gob"), T(1, 1), T(2, 1)),
            new Collided(U("gob"), U("bob"), dice[0]),
            new Damaged(U("gob"), dice[0], "bludgeoning", 30 - dice[0]),
            new Collided(U("bob"), U("gob"), dice[1]),
            new Damaged(U("bob"), dice[1], "bludgeoning", 30 - dice[1]),
        ]), Json(events));
        Assert.Equal(T(2, 1), after.Units[U("gob")].Pos);
        Assert.Equal(T(3, 1), after.Units[U("bob")].Pos);
    }

    [Fact]
    public void Push_off_ledge_4_levels_deals_1d6_and_prone()
    {
        const string cliff = """
            4. 4. 0. 0. 0.
            4. 4. 1. 0. 0.
            """;
        var battle = TestBattle.On(cliff)
            .Party("kara", TestSheets.Fighter, T(0, 0))
            .Enemy("gob", Goblin, T(1, 0))
            .Enemy("bob", Goblin, T(1, 1));
        var (state, _) = battle.Start();

        // 4 levels down: 1d6 (one level beyond 3), prone, and the push ends there.
        var die = TestAbilities.Next(state.Rng, 6)[0];
        var (after, events) = Run(battle, state, "kara", "gob", T(1, 0), new PushEffect(2));
        Assert.Equal(Json(
        [
            new Pushed(U("gob"), T(1, 0), T(2, 0)),
            new Fell(U("gob"), 4, die, false),
            new Damaged(U("gob"), die, "bludgeoning", 30 - die),
            new ConditionApplied(U("gob"), Prone),
        ]), Json(events));
        var gob = after.Units[U("gob")];
        Assert.Equal(T(2, 0), gob.Pos);
        Assert.Contains(gob.Conditions, c => c.Id == Prone);

        // 3 levels down, then 1: no fall, and the push goes on (east: a diagonal tie goes to X).
        (after, events) = Run(battle, state, "kara", "bob", T(1, 1), new PushEffect(2));
        Assert.Equal(Json([new Pushed(U("bob"), T(1, 1), T(2, 1)), new Pushed(U("bob"), T(2, 1), T(3, 1))]), Json(events));
        Assert.Equal(30, after.Units[U("bob")].Hp);
        Assert.Empty(after.Units[U("bob")].Conditions);
    }

    [Fact]
    public void Push_off_map_edge_collides_like_wall()
    {
        var battle = TestBattle.On(Field)
            .Party("kara", TestSheets.Fighter, T(4, 1))
            .Enemy("gob", Goblin, T(5, 1));
        var (state, _) = battle.Start();

        var die = TestAbilities.Next(state.Rng, 4)[0];
        var (after, events) = Run(battle, state, "kara", "gob", T(5, 1), new PushEffect(2));

        Assert.Equal(Json(
        [
            new Collided(U("gob"), null, die),
            new Damaged(U("gob"), die, "bludgeoning", 30 - die),
        ]), Json(events));
        Assert.Equal(T(5, 1), after.Units[U("gob")].Pos);
    }

    [Fact]
    public void Push_into_deep_downs_party_member_and_kills_enemy()
    {
        const string river = """
            0. 0. 0x 0x
            2. 2. 0x 0x
            """;
        var battle = TestBattle.On(river)
            .Party("kara", TestSheets.Fighter, T(1, 0))
            .Enemy("gob", Goblin, T(0, 0))
            .Party("brin", TestSheets.Cleric, T(0, 1))
            .Enemy("bob", Goblin, T(1, 1));
        var (state, _) = battle.Start();

        // An enemy pushes a party member into the river: Downed, countdown 3. The push stops in the water.
        var (after, events) = Run(battle, state, "gob", "kara", T(1, 0), new PushEffect(2));
        Assert.Equal(Json([new Pushed(U("kara"), T(1, 0), T(2, 0)), new Fell(U("kara"), 0, 0, true)]), Json(events));
        var kara = after.Units[U("kara")];
        Assert.Equal(T(2, 0), kara.Pos);
        Assert.Equal(LifeState.Downed, kara.Life);
        Assert.Equal(3, kara.Countdown);
        Assert.Equal(0, kara.Hp);

        // A party member pushes an enemy off a 2-level bank into the river: Dead.
        (after, events) = Run(battle, after, "brin", "bob", T(1, 1), new PushEffect(1));
        Assert.Equal(Json([new Pushed(U("bob"), T(1, 1), T(2, 1)), new Fell(U("bob"), 2, 0, true)]), Json(events));
        var bob = after.Units[U("bob")];
        Assert.Equal(T(2, 1), bob.Pos);
        Assert.Equal(LifeState.Dead, bob.Life);
        Assert.Equal(0, bob.Hp);
        Assert.Equal(state.Rng, after.Rng);

        // A party member already Downed keeps its countdown.
        var downed = state.Units[U("kara")] with { Hp = 0, Life = LifeState.Downed, Countdown = 1 };
        (after, _) = Run(battle, state with { Units = state.Units.SetItem(downed.Id, downed) }, "gob", "kara", T(1, 0), new PushEffect(1));
        Assert.Equal(T(2, 0), after.Units[U("kara")].Pos);
        Assert.Equal(LifeState.Downed, after.Units[U("kara")].Life);
        Assert.Equal(1, after.Units[U("kara")].Countdown);
    }

    [Fact]
    public void Pushes_never_provoke()
    {
        var battle = TestBattle.On(Field)
            .Party("kara", TestSheets.Fighter, T(0, 1))
            .Enemy("gob", Goblin, T(1, 1))
            .Enemy("bob", Goblin, T(1, 0));                             // bob's reach covers (1, 1), not (2, 1)
        var (state, _) = battle.Start();

        var (after, events) = Run(battle, state, "kara", "gob", T(1, 1), new PushEffect(1));

        Assert.Null(after.Pending);
        Assert.IsType<Pushed>(Assert.Single(events));
    }

    [Fact]
    public void Pull_draws_toward_the_source_and_stops_beside_it()
    {
        var battle = TestBattle.On(Field)
            .Party("kara", TestSheets.Fighter, T(0, 1))
            .Enemy("gob", Goblin, T(4, 1));
        var (state, _) = battle.Start();

        var (after, events) = Run(battle, state, "kara", "gob", T(4, 1), new PullEffect(5));

        Assert.Equal(Json(
        [
            new Pushed(U("gob"), T(4, 1), T(3, 1)),
            new Pushed(U("gob"), T(3, 1), T(2, 1)),
            new Pushed(U("gob"), T(2, 1), T(1, 1)),
        ]), Json(events));
        Assert.Equal(T(1, 1), after.Units[U("gob")].Pos);
        Assert.Equal(state.Rng, after.Rng);
    }

    [Fact]
    public void Teleport_needs_empty_walkable_tile()
    {
        const string map = """
            0. 0. 0# 0. 0x 0.
            0. 0. 0. 0. 0. 0.
            """;
        var battle = TestBattle.On(map)
            .Party("kara", TestSheets.Fighter, T(0, 0))
            .Enemy("gob", Goblin, T(1, 1));
        var (state, _) = battle.Start();

        Tile[] refused = [T(2, 0), T(4, 0), T(1, 1), T(5, 1), T(9, 0)];  // impassable, deep, occupied, out of range, off the map
        foreach (var aim in refused)
        {
            var (same, none) = Run(battle, state, "kara", null, aim, new TeleportEffect(4));
            Assert.Empty(none);
            Assert.Equal(T(0, 0), same.Units[U("kara")].Pos);
        }

        var (after, events) = Run(battle, state, "kara", null, T(3, 0), new TeleportEffect(4));
        Assert.Equal(Json([new Teleported(U("kara"), T(0, 0), T(3, 0))]), Json(events));
        Assert.Equal(T(3, 0), after.Units[U("kara")].Pos);

        // Self = false moves the target instead.
        (after, events) = Run(battle, state, "kara", "gob", T(3, 1), new TeleportEffect(4, Self: false));
        Assert.Equal(Json([new Teleported(U("gob"), T(1, 1), T(3, 1))]), Json(events));
        Assert.Equal(T(0, 0), after.Units[U("kara")].Pos);
    }

    [Fact]
    public void Swap_exchanges_positions()
    {
        var battle = TestBattle.On(Field)
            .Party("kara", TestSheets.Fighter, T(0, 0))
            .Enemy("gob", Goblin, T(3, 2));
        var (state, _) = battle.Start();

        var (after, events) = Run(battle, state, "kara", "gob", T(3, 2), new SwapEffect());

        Assert.Equal(Json([new Teleported(U("kara"), T(0, 0), T(3, 2)), new Teleported(U("gob"), T(3, 2), T(0, 0))]), Json(events));
        Assert.Equal(T(3, 2), after.Units[U("kara")].Pos);
        Assert.Equal(T(0, 0), after.Units[U("gob")].Pos);
    }

    [Fact]
    public void Dash_moves_self_toward_the_aim_until_blocked()
    {
        var battle = TestBattle.On(Field)
            .Party("kara", TestSheets.Fighter, T(0, 1))
            .Enemy("gob", Goblin, T(4, 1));
        var (state, _) = battle.Start();

        // Toward the goblin: stops on the tile before it, spending no Move and provoking nothing.
        var (after, events) = Run(battle, state, "kara", null, T(4, 1), new DashEffect(6));
        Assert.Equal(Json([new Moved(U("kara"), [T(0, 1), T(1, 1), T(2, 1), T(3, 1)])]), Json(events));
        Assert.Equal(T(3, 1), after.Units[U("kara")].Pos);
        Assert.Equal(4, after.Units[U("kara")].Economy.MoveLeft);

        // At most Tiles tiles.
        (after, _) = Run(battle, state, "kara", null, T(4, 1), new DashEffect(2));
        Assert.Equal(T(2, 1), after.Units[U("kara")].Pos);
    }

    [Fact]
    public void JumpTo_ignores_the_jump_limit()
    {
        const string tower = """
            0. 0. 6. 0.
            """;
        var battle = TestBattle.On(tower)
            .Party("kara", TestSheets.Fighter, T(0, 0))
            .Enemy("gob", Goblin, T(3, 0));
        var (state, _) = battle.Start();

        Assert.Empty(Run(battle, state, "kara", null, T(2, 0), new JumpToEffect(1)).Events);     // out of range

        var (after, events) = Run(battle, state, "kara", null, T(2, 0), new JumpToEffect(2));
        Assert.Equal(Json([new Moved(U("kara"), [T(0, 0), T(2, 0)])]), Json(events));
        Assert.Equal(T(2, 0), after.Units[U("kara")].Pos);
    }

    [Fact]
    public void Motion_blocks_round_trip_as_single_key_json()
    {
        string[] forms =
        [
            """{"push":{"distance":2}}""",
            """{"push":{"distance":1,"awayFromSource":false}}""",
            """{"pull":{"distance":3}}""",
            """{"teleport":{"range":4}}""",
            """{"teleport":{"range":4,"self":false}}""",
            """{"swap":{}}""",
            """{"dash":{"tiles":3}}""",
            """{"jumpTo":{"range":2}}""",
        ];
        foreach (var json in forms)
        {
            var effect = JsonSerializer.Deserialize<EffectDef>(json, ArcJson.Options)!;
            Assert.Equal(json, JsonSerializer.Serialize(effect, ArcJson.Options));
        }

        Assert.Equal(new PushEffect(1, AwayFromSource: false), JsonSerializer.Deserialize<EffectDef>(forms[1], ArcJson.Options));
    }
}
