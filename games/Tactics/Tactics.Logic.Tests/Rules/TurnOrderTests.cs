using System.Collections.Immutable;
using ArcBox.Sdk;

namespace Tactics.Logic.Tests;

public sealed class TurnOrderTests
{
    private const string Field = """
        0. 0. 0. 0. 0.
        0. 0. 0. 0. 0.
        0. 0. 0. 0. 0.
        """;

    private static UnitId U(string id) => new(id);

    private static ImmutableArray<UnitId> Ids(params string[] ids) => [.. ids.Select(U)];

    [Fact]
    public void Sorted_by_10_plus_dex_plus_bonuses()
    {
        var battle = TestBattle.On(Field)
            .Party("cleric", TestSheets.Cleric, new Tile(0, 0))                                      // 10 + 0
            .Party("rogue", TestSheets.Rogue, new Tile(1, 0))                                        // 10 + 3
            .Enemy("gob", TestSheets.Goblin, new Tile(4, 0))                                         // 10 + 2
            .Enemy("boss", TestSheets.Goblin with { InitiativeBonus = 5 }, new Tile(4, 1));          // 10 + 2 + 5
        var (state, events) = battle.Start();

        Assert.Equal(10, TurnOrder.Initiative(state, U("cleric"), battle.Rules));
        Assert.Equal(13, TurnOrder.Initiative(state, U("rogue"), battle.Rules));
        Assert.Equal(12, TurnOrder.Initiative(state, U("gob"), battle.Rules));
        Assert.Equal(17, TurnOrder.Initiative(state, U("boss"), battle.Rules));

        var expected = Ids("boss", "rogue", "gob", "cleric");
        Assert.Equal(expected, state.Order);
        Assert.Equal(1, state.Round);
        Assert.Equal(0, state.TurnIndex);
        Assert.Equal(U("boss"), state.Active);
        Assert.Collection(events,
            e => Assert.Equal(new RoundStarted(1, expected), e, EventEquality.Instance),
            e => Assert.Equal(new TurnStarted(U("boss"), 1), e));
    }

    [Fact]
    public void Heavy_armor_minus_2()
    {
        var battle = TestBattle.On(Field)
            .Party("heavy", TestSheets.Fighter, new Tile(0, 0))                                      // 10 + 1 − 2
            .Party("medium", TestSheets.Fighter with { Armor = ArmorKind.Medium }, new Tile(1, 0))   // 10 + 1
            .Party("cleric", TestSheets.Cleric, new Tile(2, 0));                                     // 10
        var (state, _) = battle.Start();

        Assert.Equal(9, TurnOrder.Initiative(state, U("heavy"), battle.Rules));
        Assert.Equal(11, TurnOrder.Initiative(state, U("medium"), battle.Rules));
        Assert.Equal(Ids("medium", "cleric", "heavy"), state.Order);
    }

    [Fact]
    public void Ties_players_then_dex_then_id()
    {
        // Everyone has initiative 12.
        var lowDex = TestSheets.Goblin.WithScore(Stat.Dex, 12) with { InitiativeBonus = 1 };    // DEX 12, +1 bonus
        var battle = TestBattle.On(Field)
            .Enemy("gob", TestSheets.Goblin, new Tile(4, 0))                                        // DEX 14
            .Enemy("Gob", TestSheets.Goblin, new Tile(4, 1))                                        // DEX 14, sorts before "gob"
            .Enemy("archer", lowDex, new Tile(4, 2))                                                // DEX 12
            .Party("kara", TestSheets.Rogue.WithScore(Stat.Dex, 12) with { InitiativeBonus = 1 }, new Tile(0, 0))
            .Party("companion", TestSheets.Rogue.WithScore(Stat.Dex, 14), new Tile(0, 1),
                new AiControlled("companion"));                                                     // party, but AI
        var (state, _) = battle.Start();

        foreach (var id in state.Units.Keys)
            Assert.Equal(12, TurnOrder.Initiative(state, id, battle.Rules));
        Assert.Equal(Ids("kara", "Gob", "companion", "gob", "archer"), state.Order);
    }

    [Fact]
    public void Order_resorts_each_round()
    {
        var battle = TestBattle.On(Field)
            .Party("rogue", TestSheets.Rogue, new Tile(0, 0))      // 13
            .Enemy("gob", TestSheets.Goblin, new Tile(4, 0));      // 12
        var (state, _) = battle.Start();
        Assert.Equal(Ids("rogue", "gob"), state.Order);

        // Mid-round the goblin becomes much faster: this round's order is unchanged.
        var gob = state.Units[U("gob")];
        state = state with { Units = state.Units.SetItem(gob.Id, gob with { Sheet = gob.Sheet with { InitiativeBonus = 10 } }) };

        (state, var events) = battle.Apply(state, new EndTurn(U("rogue")));
        Assert.Equal(Ids("rogue", "gob"), state.Order);
        Assert.Equal(U("gob"), state.Active);
        Assert.Equal<BattleEvent>([new TurnEnded(U("rogue")), new TurnStarted(U("gob"), 1)], events);

        (state, events) = battle.Apply(state, new EndTurn(U("gob")));
        Assert.Equal(2, state.Round);
        Assert.Equal(Ids("gob", "rogue"), state.Order);
        Assert.Equal(U("gob"), state.Active);
        Assert.Collection(events,
            e => Assert.Equal(new TurnEnded(U("gob")), e),
            e => Assert.Equal(new RoundStarted(2, Ids("gob", "rogue")), e, EventEquality.Instance),
            e => Assert.Equal(new TurnStarted(U("gob"), 2), e));
    }

    [Fact]
    public void Delay_moves_turn_later_this_round_only()
    {
        var battle = TestBattle.On(Field)
            .Party("rogue", TestSheets.Rogue, new Tile(0, 0))      // 13
            .Enemy("gob", TestSheets.Goblin, new Tile(4, 0))       // 12
            .Party("cleric", TestSheets.Cleric, new Tile(1, 0));   // 10
        var (state, _) = battle.Start();

        (state, var events) = battle.Apply(state, new Delay(U("rogue"), 1));
        Assert.Equal(Ids("gob", "rogue", "cleric"), state.Order);
        Assert.Equal(0, state.TurnIndex);
        Assert.Equal(U("gob"), state.Active);
        Assert.Equal<BattleEvent>([new Delayed(U("rogue"), 1), new TurnStarted(U("gob"), 1)], events);

        (state, events) = battle.Apply(state, new EndTurn(U("gob")));
        Assert.Equal(U("rogue"), state.Active);
        Assert.Equal<BattleEvent>([new TurnEnded(U("gob")), new TurnStarted(U("rogue"), 1)], events);

        // Delaying past the end of the order goes last.
        (state, events) = battle.Apply(state, new Delay(U("rogue"), 5));
        Assert.Equal(Ids("gob", "cleric", "rogue"), state.Order);
        Assert.Equal<BattleEvent>([new Delayed(U("rogue"), 2), new TurnStarted(U("cleric"), 1)], events);

        (state, _) = battle.Apply(state, new EndTurn(U("cleric")));
        Assert.Equal(U("rogue"), state.Active);

        // Next round the delay is forgotten: initiative order again.
        (state, events) = battle.Apply(state, new EndTurn(U("rogue")));
        Assert.Equal(2, state.Round);
        Assert.Equal(Ids("rogue", "gob", "cleric"), state.Order);
        Assert.Equal(U("rogue"), state.Active);
        Assert.Equal(new TurnStarted(U("rogue"), 2), events[^1]);
    }

    [Fact]
    public void Delay_with_no_later_slot_is_rejected()
    {
        var battle = TestBattle.On(Field)
            .Party("rogue", TestSheets.Rogue, new Tile(0, 0))
            .Enemy("gob", TestSheets.Goblin, new Tile(4, 0));
        var (state, _) = battle.Start();

        foreach (var slots in new[] { 0, -1 })
        {
            var result = Battle.Apply(state, new Delay(U("rogue"), slots), battle.Rules);
            Assert.Equal(RejectCode.InvalidTarget, result.Rejected?.Code);
            Assert.Same(state, result.State);
        }

        (state, _) = battle.Apply(state, new EndTurn(U("rogue")));
        var last = Battle.Apply(state, new Delay(U("gob"), 1), battle.Rules);
        Assert.Equal(RejectCode.InvalidTarget, last.Rejected?.Code);
        Assert.Same(state, last.State);
    }

    [Fact]
    public void EndTurn_by_wrong_unit_is_rejected_NotYourTurn()
    {
        var battle = TestBattle.On(Field)
            .Party("rogue", TestSheets.Rogue, new Tile(0, 0))
            .Enemy("gob", TestSheets.Goblin, new Tile(4, 0));
        var (state, _) = battle.Start();

        foreach (var actor in new[] { "gob", "nobody" })
        {
            var result = Battle.Apply(state, new EndTurn(U(actor)), battle.Rules);

            Assert.Same(state, result.State);
            Assert.Empty(result.Events);
            Assert.NotNull(result.Rejected);
            Assert.Equal(RejectCode.NotYourTurn, result.Rejected.Code);
            Assert.Equal("It's not your turn.", result.Rejected.Reason);
        }
    }

    [Fact]
    public void Pending_reaction_rejects_other_commands_and_outcome_rejects_everything()
    {
        var battle = TestBattle.On(Field)
            .Party("rogue", TestSheets.Rogue, new Tile(0, 0))
            .Enemy("gob", TestSheets.Goblin, new Tile(4, 0));
        var (state, _) = battle.Start();

        var paused = state with
        {
            Pending = new ReactionOffer(U("gob"), [new ReactionChoice("pass", null, null)], "leavesReach"),
        };
        var pending = Battle.Apply(paused, new EndTurn(U("rogue")), battle.Rules);
        Assert.Same(paused, pending.State);
        Assert.Equal(new Rejection(RejectCode.ReactionPending, "Answer the reaction first."), pending.Rejected);

        var over = state with { Outcome = BattleOutcome.Won };
        Assert.Null(over.Active);
        var ended = Battle.Apply(over, new EndTurn(U("rogue")), battle.Rules);
        Assert.Same(over, ended.State);
        Assert.Equal(new Rejection(RejectCode.BattleOver, "The battle is over."), ended.Rejected);
    }

    [Fact]
    public void Turn_start_resets_the_economy()
    {
        var battle = TestBattle.On(Field)
            .Party("rogue", TestSheets.Rogue, new Tile(0, 0))
            .Enemy("gob", TestSheets.Goblin with { Move = 5 }, new Tile(4, 0));
        var (state, _) = battle.Start();
        Assert.Equal(new TurnEconomy(4, true, true, true, 0), state.Units[U("rogue")].Economy);

        // The goblin spent everything last round (e.g. an opportunity attack).
        var gob = state.Units[U("gob")];
        state = state with { Units = state.Units.SetItem(gob.Id, gob with { Economy = new TurnEconomy(0, false, false, false, 1) }) };

        (state, _) = battle.Apply(state, new EndTurn(U("rogue")));
        Assert.Equal(new TurnEconomy(5, true, true, true, 0), state.Units[U("gob")].Economy);
    }

    [Fact]
    public void Rejection_texts_are_exact()
    {
        Assert.Equal("You've already used your bonus action.", Rejection.Of(RejectCode.NoBonus).Reason);
        Assert.Equal("Not enough devotion.", Rejection.Of(RejectCode.CantAfford, "devotion").Reason);
        Assert.Equal("You don't have that ability.", Rejection.Of(RejectCode.UnknownAbility).Reason);
        foreach (var code in Enum.GetValues<RejectCode>())
            Assert.False(string.IsNullOrEmpty(Rejection.Of(code, "x").Reason));
    }

    [Fact]
    public void Start_refills_resources_and_fills_unit_state()
    {
        var battle = TestBattle.On(Field).Party("cleric", TestSheets.Cleric, new Tile(2, 1));
        var (state, _) = battle.Start(seed: 7);

        var cleric = state.Units[U("cleric")];
        Assert.Equal(new Tile(2, 1), cleric.Pos);
        Assert.Equal(10, cleric.Hp);
        Assert.Equal(LifeState.Up, cleric.Life);
        Assert.Equal(4, cleric.Resources["mana"]);
        Assert.Equal(1, cleric.Resources["devotion"]);
        Assert.Equal(new PlayerControlled(new ProfileId("pr_cleric")), cleric.Controller);
        Assert.Equal("test", state.ContentHash);
        Assert.Equal("ascii", state.MapId);
        Assert.Equal(Rng.Seeded(7), state.Rng);
        Assert.Empty(state.Queue);
    }

    /// <summary>Compares events structurally, looking inside <see cref="ImmutableArray{T}"/> members.</summary>
    private sealed class EventEquality : IEqualityComparer<BattleEvent>
    {
        public static readonly EventEquality Instance = new();

        public bool Equals(BattleEvent? x, BattleEvent? y) => (x, y) switch
        {
            (RoundStarted a, RoundStarted b) => a.Round == b.Round && a.Order.SequenceEqual(b.Order),
            _ => object.Equals(x, y),
        };

        public int GetHashCode(BattleEvent obj) => 0;
    }
}
