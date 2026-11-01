using System.Text.Json;
using ArcBox.Sdk;

namespace Tactics.Logic.Tests;

/// <summary>
/// A present player's 60 s ran out (spec §5.5): the adapter has the AI finish the turn with the companion profile, from the
/// Move, Action and Bonus not yet used, with no items and no Delay, and every event it causes carries <c>AiFinished</c>.
/// </summary>
public sealed class TimeoutTests
{
    private static RulesPack Base => BasePackTests.Rules;

    private static UnitId U(string id) => new(id);

    private static AbilityDef A(string id) => Base.Abilities[new AbilityId(id)];

    private static Tile T(int x, int y) => new(x, y);

    /// <summary>The options the adapter passes for a timed-out turn.</summary>
    private static readonly AiOptions TimedOut = new(AllowItems: false, AllowDelay: false, RemainingOnly: true, ProfileOverride: "companion");

    /// <summary>
    /// A player's cleric (healing word, two potions, <paramref name="hp"/> of 10 HP) acts first, 9 tiles from Kara, who is
    /// down to 2 HP; a goblin waits at the far end. The cleric's player has already spent the Action.
    /// </summary>
    private static (TestBattle Battle, BattleState State) ClericOutOfTime(int hp = 10)
    {
        var word = A("cleric.healing_word");
        var battle = TestBattle.On("""
            0. 0. 0. 0. 0. 0. 0. 0. 0. 0. 0. 0.
            0. 0. 0. 0. 0. 0. 0. 0. 0. 0. 0. 0.
            0. 0. 0. 0. 0. 0. 0. 0. 0. 0. 0. 0.
            """).WithAiProfiles(Base.AiProfiles.Values)
            .With(word, A("common.use_item"), A("item.potion_of_healing"), A("common.dash"))
            .WithItems(Base.Items[new ItemId("potion_of_healing")])
            .Party("cleric", TestSheets.Cleric.Knowing(word) with { InitiativeBonus = 30 }, T(0, 1), bag: [new ItemStack(new ItemId("potion_of_healing"), 2)])
            .Party("kara", TestSheets.Fighter, T(9, 1))
            .Enemy("gob", TestSheets.Goblin, T(11, 0));
        var state = battle.Start().State
            .Change("kara", u => u with { Hp = 2 })
            .Change("cleric", u => u with { Hp = hp, Economy = u.Economy with { Action = false } });
        return (battle, state);
    }

    [Fact]
    public void Finishes_only_remaining_economy()
    {
        var (battle, state) = ClericOutOfTime();

        var plan = Ai.Plan(state, U("cleric"), battle.Rules, TimedOut);

        // Only a move and the bonus action are left: it walks into range and casts Healing Word on Kara.
        Assert.Collection(plan.Commands,
            c => Assert.IsType<Move>(c),
            c => Assert.Equal((new AbilityId("cleric.healing_word"), U("kara")), (((Use)c).Ability, ((Use)c).Target.Unit!.Value)),
            c => Assert.IsType<EndTurn>(c));
        Assert.All(plan.Commands.OfType<Use>(), u => Assert.Equal(0, battle.Rules.Abilities[u.Ability].Cost.Action));
        foreach (var command in plan.Commands)
        {
            var resolution = Battle.Apply(state, command, battle.Rules, aiFinished: true);
            Assert.Null(resolution.Rejected);
            state = resolution.State;
        }
        Assert.True(state.Units[U("kara")].Hp > 2);
    }

    [Fact]
    public void No_items_no_delay()
    {
        // Badly hurt, with potions in the bag and its Bonus free: a timed-out turn still never drinks, and never delays.
        var (battle, state) = ClericOutOfTime(hp: 2);
        var fresh = state.Change("cleric", u => u with { Economy = u.Economy with { Action = true } });

        foreach (var s in new[] { state, fresh })
        {
            var plan = Ai.Plan(s, U("cleric"), battle.Rules, TimedOut);

            Assert.True(!plan.Commands.OfType<Use>().Any(u => u.Item is not null || u.Ability == Battle.UseItem), plan.Explain);
            Assert.DoesNotContain(plan.Commands, c => c is Delay);
            Assert.IsType<EndTurn>(plan.Commands[^1]);
        }
    }

    [Fact]
    public void Nothing_left_returns_single_EndTurn()
    {
        var (battle, state) = ClericOutOfTime();
        var spent = state.Change("cleric", u => u with { Economy = u.Economy with { MoveLeft = 0, Action = false, Bonus = false } });

        var plan = Ai.Plan(spent, U("cleric"), battle.Rules, TimedOut);

        Assert.Equal([new EndTurn(U("cleric"))], plan.Commands);
    }

    [Fact]
    public void Events_flagged_AiFinished()
    {
        var (battle, state) = ClericOutOfTime();
        var plan = Ai.Plan(state, U("cleric"), battle.Rules, TimedOut);

        var events = new List<BattleEvent>();
        foreach (var command in plan.Commands)
        {
            var resolution = Battle.Apply(state, command, battle.Rules, aiFinished: true);
            Assert.Null(resolution.Rejected);
            state = resolution.State;
            events.AddRange(resolution.Events);
        }

        // Everything the finished turn caused, up to and including its TurnEnded, is flagged; the next turn's events aren't.
        var ended = events.FindIndex(e => e is TurnEnded { Unit.Value: "cleric" });
        Assert.True(ended > 0);
        Assert.Contains(events.Take(ended), e => e is AbilityUsed { Ability.Value: "cleric.healing_word" });
        Assert.All(events.Take(ended + 1), e => Assert.True(e.AiFinished, e.ToString()));
        Assert.Contains(events.Skip(ended + 1), e => e is TurnStarted);
        Assert.All(events.Skip(ended + 1), e => Assert.False(e.AiFinished, e.ToString()));

        // The flag is in the events' JSON (and survives a round trip), and only there when set.
        var json = JsonSerializer.Serialize(events[0], ArcJson.Options);
        Assert.Contains("\"aiFinished\":true", json);
        var back = JsonSerializer.Deserialize<BattleEvent>(json, ArcJson.Options)!;
        Assert.True(back.AiFinished);
        Assert.Equal(json, JsonSerializer.Serialize(back, ArcJson.Options));

        // Without the flag, nothing is stamped.
        var (_, again) = ClericOutOfTime();
        var plain = Battle.Apply(again, plan.Commands[0], battle.Rules);
        Assert.All(plain.Events, e => Assert.False(e.AiFinished));
        Assert.DoesNotContain("aiFinished", JsonSerializer.Serialize(plain.Events, ArcJson.Options));
    }

    [Fact]
    public void An_item_used_in_a_finished_turn_counts_as_the_AI_s()
    {
        // The adapter never lets a timed-out turn use items, but if a command with an item is applied as AI-finished, the
        // use is the AI's: recorded with ByAi and counted against the battle's one AI item.
        var (battle, state) = ClericOutOfTime();
        var drink = new Use(U("cleric"), Battle.UseItem, new Target(Unit: U("cleric")), new ItemId("potion_of_healing"));

        var finished = Battle.Apply(state, drink, battle.Rules, aiFinished: true).State;
        var played = Battle.Apply(state, drink, battle.Rules).State;

        Assert.Equal([new ItemUse(U("cleric"), new ItemId("potion_of_healing"), ByAi: true)], finished.ItemsUsed);
        Assert.Equal(1, finished.Units[U("cleric")].ItemsUsedByAi);
        Assert.Equal([new ItemUse(U("cleric"), new ItemId("potion_of_healing"), ByAi: false)], played.ItemsUsed);
        Assert.Equal(0, played.Units[U("cleric")].ItemsUsedByAi);
    }
}
