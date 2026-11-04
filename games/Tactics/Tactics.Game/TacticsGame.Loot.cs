using System.Collections.Immutable;
using Tactics.Logic;

namespace Tactics.Game;

/// <summary>
/// Found items: the party picks whose bag (interactables design §1.1, USER DECISION 1, decided by the owner on 2026-11-03). When a container,
/// a cache, a pickup or a quest's reward gives an item, it waits in <see cref="WorldSave.Loot"/>, and once the scene that found it is over a
/// short party vote asks "Who takes the Potion of Healing?" on the 5d vote screen, between the players here who have a character. Nobody
/// answering gives it to whoever found it (else the leader), after 15 seconds; a tie goes to the leader, as any vote's. One vote per item, in
/// the order they were found. A party of one takes it at once, as before. Gold is never voted on: it's split across the purses.
/// </summary>
public sealed partial class TacticsGame
{
    /// <summary>The loot vote's scene id: no lore scene and no thing's has it.</summary>
    public const string LootVoteId = "loot_vote";

    /// <summary>How long the party has to say who takes a found item.</summary>
    internal static readonly TimeSpan LootVoteLength = TimeSpan.FromSeconds(15);

    /// <summary>True while the party votes on who takes a found item.</summary>
    private bool LootVote => _doc.World?.Loot is { Voting: true } && Cursor?.Scene == LootVoteId;

    /// <summary>True when found items wait for a vote that hasn't begun.</summary>
    private bool LootWaiting => _doc.World?.Loot is { Items.IsEmpty: false } && _region is not null;

    /// <summary>
    /// <paramref name="items"/>, found by <paramref name="by"/> in <paramref name="from"/>, go to the party (USER DECISION 1): straight into
    /// <paramref name="by"/>'s bag when only one player here has a character, as they always did; otherwise they wait for the party's vote,
    /// which begins when the party would walk again. Only the pack's items: gold and anything unknown aren't here. The caller saves.
    /// </summary>
    private void GiveFound(IEnumerable<(ItemId Item, int Count)> items, string? by, string from)
    {
        var found = items.Where(i => i.Count > 0 && Rules.Items.ContainsKey(i.Item)).ToList();
        if (found.Count == 0 || by is null || !_doc.Members.ContainsKey(by))
            return;
        if (LootChoices().Length <= 1 && _doc.World?.Loot is not { Items.IsEmpty: false })
        {
            Stack(by, found);
            return;
        }
        var world = _doc.World!;
        var loot = world.Loot ?? new LootSave([]);
        _doc = _doc with { World = world with { Loot = loot with { Items = loot.Items.AddRange(found.Select(i => new LootDue(i.Item.Value, i.Count, by, from))) } } };
    }

    /// <summary>Puts <paramref name="items"/> into <paramref name="member"/>'s bag, stacking with what's there.</summary>
    private void Stack(string member, IEnumerable<(ItemId Item, int Count)> items)
    {
        if (!_doc.Members.TryGetValue(member, out var record))
            return;
        var bag = StackLoot(record.Character.Bag, items.Select(i => new LootDef(i.Item, i.Count)));
        _doc = _doc with { Members = _doc.Members.SetItem(member, record with { Character = record.Character with { Bag = bag } }) };
    }

    /// <summary>Who can take a found item: the players here who have a character, in member order.</summary>
    private ImmutableArray<string> LootChoices() => [.. Voters().Select(p => p.Profile!.Value.Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    /// <summary>"Potion of Healing", or "2 × Potion of Healing".</summary>
    private string LootLabel(LootDue item)
    {
        var name = ItemName(new ItemId(item.Item));
        return item.Count == 1 ? name : $"{item.Count} × {name}";
    }

    /// <summary>
    /// The vote on the first item waiting: one narrated line that asks it, and an option for each member it's between, labelled with their
    /// character's name (and their own, when it's another). Null when there's no vote.
    /// </summary>
    private SceneDef? LootScene()
    {
        if (_doc.World?.Loot is not { Voting: true, Items.IsEmpty: false } loot)
            return null;
        var item = loot.Items[0];
        var question = $"Who takes the {LootLabel(item)}?";
        var options = loot.Choices.Where(_doc.Members.ContainsKey).Select(m =>
        {
            var character = _doc.Members[m].Character.Name;
            var player = PlayerName(m);
            return new SceneOption(m, player == character || player == m ? character : $"{character} ({player})", "taken", null, [], [], OptionTags.None, null);
        });
        var line = new SceneLine($"{LootVoteId}.{loot.Seq}", SceneDef.Narrator, $"From the {item.From}: {LootLabel(item)}. {question}", SceneLine.DefaultMood);
        return new SceneDef(LootVoteId, "", null, question, [], [], [SceneDef.Narrator], "ask",
            [new SceneNode("ask", [line], [], [.. options], true, null, false)], false);
    }

    /// <summary>
    /// The vote on the first item waiting begins: between the players here with a character now, saved with the cursor it sets aside (on the
    /// first item; the next keeps it). With one or none left to choose, the items go at once, to them or else whoever found each.
    /// </summary>
    /// <returns>
    /// False only when no vote was under way and the items went at once: the caller carries on as it would have. True when the vote began,
    /// or it was under way and the party has gone back to walking.
    /// </returns>
    private bool BeginLootVote()
    {
        var world = _doc.World!;
        var loot = world.Loot!;
        var choices = LootChoices();
        if (choices.Length <= 1)
        {
            foreach (var item in loot.Items)
                Stack(choices.FirstOrDefault() ?? item.By, [(new ItemId(item.Item), item.Count)]);
            _doc = _doc with { World = _doc.World! with { Loot = null, Story = loot.Voting ? loot.Under : _doc.World.Story } };
            KeepWorld();
            Save();
            if (!loot.Voting)
                return false;
            ToExploration();
            return true;
        }
        if (_phase == TacticsPhase.Exploration)
            LeaveExploration();
        EndTelling();
        _toasts.Clear();
        _phase = TacticsPhase.Story;
        var under = loot.Voting ? loot.Under : world.Story;
        _doc = _doc with
        {
            World = world with
            {
                Loot = loot with { Choices = choices, Under = under, Voting = true },
                Story = new StorySave("", StorySave.Lead, LootVoteId, null, 0, _storySeq),
            },
        };
        EnterNode(LootScene()!, "ask");
        return true;
    }

    /// <summary>Who takes the item nobody voted on: whoever found it, if they're one of the choices, else the leader, else the first.</summary>
    private string LootUnanswered(ImmutableArray<string> options)
    {
        var item = _doc.World!.Loot!.Items[0];
        return options.Contains(item.By) ? item.By : _lead is { } lead && options.Contains(lead) ? lead : options[0];
    }

    /// <summary>
    /// The vote is decided: <paramref name="member"/> takes the item, which leaves the waiting list in the same save that puts it in their
    /// bag, and the TV says so. Then the next item's vote, or the party walks on with the cursor the vote set aside.
    /// </summary>
    private void TakeLoot(string member)
    {
        CloseQuestion();
        EndTelling();
        var loot = _doc.World!.Loot!;
        var item = loot.Items[0];
        var taker = _doc.Members.ContainsKey(member) ? member : item.By;
        Stack(taker, [(new ItemId(item.Item), item.Count)]);
        var rest = loot.Items.RemoveAt(0);
        _doc = _doc with { World = _doc.World! with { Loot = loot with { Items = rest, Seq = loot.Seq + 1 } } };
        var line = $"{_doc.Members.GetValueOrDefault(taker)?.Character.Name ?? taker} takes the {LootLabel(item)}.";
        if (!rest.IsEmpty)
        {
            BeginLootVote();       // the next item's question is saved with its first line, with this one given
            Say(line);
            return;
        }
        _doc = _doc with { World = _doc.World! with { Loot = null, Story = loot.Under } };
        KeepWorld();
        Save();
        ToExploration();
        Say(line);
    }

    /// <summary>Back into the loot vote after a restart, Muster or a new session: the same item's question, asked again.</summary>
    private void ResumeLoot()
    {
        _phase = TacticsPhase.Story;
        if (!BeginLootVote())
            ToExploration();
    }
}
