using System.Collections.Immutable;
using Tactics.Logic;
using static Tactics.Game.Tests.TacticsGameTests;

namespace Tactics.Game.Tests;

public sealed class GriskBarkTests
{
    private static readonly Lazy<BarkBook> Book = new(() => LoreBook.Load(ContentBase()).Barks);
    private static BattleState State()
    {
        var hero = SheetBuilder.DefaultCharacter("Hero", "human", "fighter", null, 2, Rules);
        return Battle.Start(Encounters.Setup("hollow_diggings", [new(new("hero"), hero, new AiControlled("companion"))], Difficulty.Normal, Rules), Rules, 74).State;
    }

    [Theory]
    [InlineData("battle_start", "Bear’s awake! Dig or die!")]
    [InlineData("hit", "That was my good side!")]
    [InlineData("low_hp", "The market pays! I’ll split it!")]
    [InlineData("ally_down", "Up! I counted you in the wages!")]
    [InlineData("turn_start", "Dig faster!")]
    [InlineData("defeat", "Tell them… I kept the tally.")]
    public void Authored_events_add_one_cosmetic_line_without_changing_the_input(string moment, string text)
    {
        var state = State();
        var grisk = state.Units[new("grisk")];
        var kobold = state.Units.Values.First(u => u.Id != grisk.Id && u.Sheet.Tags.Contains("kobold"));
        ImmutableArray<BattleEvent> events = moment switch
        {
            "battle_start" => [new RoundStarted(1, state.Order)],
            "hit" => [new Damaged(grisk.Id, 1, "slashing", grisk.Hp - 1)],
            "low_hp" => [new Damaged(grisk.Id, grisk.Hp - 1, "slashing", 1)],
            "ally_down" => [new Died(kobold.Id)],
            "turn_start" => [new AbilityUsed(grisk.Id, new("grisk.dig_faster"), new Target(Unit: grisk.Id), null)],
            _ => [new Damaged(grisk.Id, grisk.Hp, "slashing", 0), new Died(grisk.Id)],
        };
        var result = GriskBarks.Enrich(state, events, Book.Value, 5);
        Assert.Equal(events, result.RemoveAt(result.Length - 1));
        Assert.Equal(new Said("grisk", text), Assert.Single(result.OfType<Said>()));
        Assert.Equal(result, GriskBarks.Enrich(state, events, Book.Value, 5));
    }

    [Fact]
    public void The_candle_aside_is_occasional_and_never_replaces_urgent_barks()
    {
        var state = State();
        var grisk = state.Units[new("grisk")];
        BarkSet Line(string text) => new("grisk", [new("test", text, "angry", null)]);
        var book = new BarkBook(ImmutableDictionary<string, BarkSet>.Empty,
            ImmutableDictionary<string, BarkSet>.Empty.Add("grisk:hit", new("grisk",
                [new("hit", "Hit!", "angry", null, Weight: 6), new("candle", "You no take candle!", "angry", null)]))
                .Add("grisk:low_hp", Line("Mercy!")));
        ImmutableArray<BattleEvent> hit = [new Damaged(grisk.Id, 1, "slashing", grisk.Hp - 1)];
        var lines = Enumerable.Range(0, 28).Select(beat => Assert.Single(GriskBarks.Enrich(state, hit, book, beat).OfType<Said>()).Line).ToArray();
        Assert.Equal(4, lines.Count(line => line == "You no take candle!"));
        Assert.Equal(24, lines.Count(line => line == "Hit!"));
        ImmutableArray<BattleEvent> low = [new Damaged(grisk.Id, grisk.Hp - 1, "slashing", 1)];
        Assert.Equal("Mercy!", Assert.Single(GriskBarks.Enrich(state, low, book, 3).OfType<Said>()).Line);
    }

    [Fact]
    public void Defeat_wins_over_other_events_and_dead_Grisk_does_not_keep_shouting()
    {
        var state = State();
        var grisk = state.Units[new("grisk")];
        var kobold = state.Units.Values.First(u => u.Id != grisk.Id && u.Sheet.Tags.Contains("kobold"));
        state = state with { Units = state.Units.SetItem(grisk.Id, grisk with { Hp = 0, Life = LifeState.Dead }) };
        ImmutableArray<BattleEvent> events = [new Died(grisk.Id), new Died(kobold.Id)];
        Assert.Equal("Tell them… I kept the tally.", Assert.Single(GriskBarks.Enrich(state, events, Book.Value, 5).OfType<Said>()).Line);
        events = [new Died(kobold.Id)];
        Assert.Equal(events, GriskBarks.Enrich(state, events, Book.Value, 6));
    }
}
