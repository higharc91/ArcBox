using System.Collections.Immutable;

namespace Tactics.Logic.Tests;

public sealed class QuestStateTests
{
    private static readonly QuestDef Quest = new("q", "A quest", "mara", "r",
        [new("asked", "Ask at the gate", "Asked", ["asked"]), new("clue", "Follow the trail", "A clue", ["clue"])],
        new(["done"], "Finished"), new(["failed"], "Declined"), new(30, 100, [], []));

    [Theory]
    [InlineData("", null, -1)]
    [InlineData("clue", null, -1)]
    [InlineData("asked", "active", 0)]
    [InlineData("asked,clue", "active", 1)]
    [InlineData("asked,clue,done", "done", 1)]
    [InlineData("asked,failed", "failed", 1)]
    public void State_is_derived_only_from_flags(string flags, string? status, int stage)
    {
        var state = Quest.State(flags.Split(',').ToImmutableHashSet());
        Assert.Equal(status, state?.Status);
        if (state is not null) Assert.Equal(stage, state.Stage);
    }

    [Fact]
    public void Conversation_entries_are_ordered_not_merged()
    {
        var entries = new ConversationDef("mara", null!, [new("turnin", ["clue"], ["done"]), new("greet", [], [])]);
        Assert.Equal("turnin", entries.Entry(ImmutableHashSet.Create("clue"))!.Node);
        Assert.Equal("greet", entries.Entry(ImmutableHashSet.Create("clue", "done"))!.Node);
    }
}
