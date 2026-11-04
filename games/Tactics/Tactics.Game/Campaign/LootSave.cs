using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Tactics.Game;

/// <summary>
/// A found item waiting for its owner (interactables design §1.1, USER DECISION 1, decided by the owner on 2026-11-03): <paramref name="Item"/>
/// and how many, the member who found it (<paramref name="By"/>: who opened it, or for a quest's reward who turned it in), who takes it if
/// nobody votes, and what it came from, for the TV's line.
/// </summary>
public sealed record LootDue(string Item, int Count, string By, string From);

/// <summary>
/// The found items the party is deciding on, in the order they were found: the first is being voted on while <paramref name="Voting"/>.
/// An item leaves <paramref name="Items"/> in the same save that puts it in its taker's bag, so a restart can neither give it twice nor lose
/// it. <paramref name="Choices"/> are the members the vote on the first is between (the players here with a character as it opened), and
/// <paramref name="Under"/> the story cursor the vote set aside (a battle beat waiting at its fight), which it puts back when it ends.
/// <paramref name="Seq"/> counts the votes decided, so each item's question is a new one on the phones. One more optional field of
/// <see cref="WorldSave"/> and no version bump: a document from before reads with none. <paramref name="Service"/> is the shop, inn or
/// temple a conversation opened as it gave an item: it opens once the last item is given.
/// </summary>
public sealed record LootSave(ImmutableArray<LootDue> Items,
    ImmutableArray<string> Choices = default,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] StorySave? Under = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Voting = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int Seq = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Service = null)
{
    /// <summary>The items waiting; never a default array.</summary>
    public ImmutableArray<LootDue> Items { get => field.IsDefault ? [] : field; init; } = Items;

    /// <summary>Who the vote is between; never a default array.</summary>
    public ImmutableArray<string> Choices { get => field.IsDefault ? [] : field; init; } = Choices;
}
