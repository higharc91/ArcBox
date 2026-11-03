using System.Collections.Immutable;
using Tactics.Logic;

namespace Tactics.Game;

public sealed partial class TacticsGame
{
    private ConversationDef? ConversationFor(RegionPerson person)
        => person.Character is { } character && Lore.Conversation(character) is { } conversation
            && SceneRun.Plays(conversation.Scene, StoryFlags()) && conversation.Entry(StoryFlags()) is not null ? conversation : null;

    private void BeginConversation(RegionPerson person, int index, ConversationDef conversation)
    {
        var entry = conversation.Entry(StoryFlags())!;
        var under = Cursor;
        LeaveExploration();
        EndTelling();
        _bark = null;
        _toasts.Clear();
        _phase = TacticsPhase.Story;
        SetCursor(new StorySave("", StorySave.Lead, conversation.Scene.Id, null, 0, _storySeq,
            Person: new PersonCursor(RegionPeople.Id(person, index), _lead!, entry.Node, under)));
        EnterNode(conversation.Scene, entry.Node);
    }

    private void FinishConversation()
    {
        var person = Cursor!.Person!;
        EndTelling();
        SetCursor(person.Under);
        KeepWorld();
        Save();
        ToExploration();
        if (person.Service is { } service && RegionShops.FirstOrDefault(s => s.Id == service && ShopRules.Present(s, StoryFlags())) is { } shop)
            StartShop(shop);
    }

    private void ResumeConversation(StorySave cursor)
    {
        var scene = Lore.Scene(cursor.Scene!);
        var node = cursor.Node is { } id ? scene?.Node(id) : null;
        if (scene is null || (cursor.Node is not null && (node is null || cursor.Line < 0 || cursor.Line >= node.Lines.Length)))
        {
            Note(StoryChanged);
            FinishConversation();
            return;
        }
        _phase = TacticsPhase.Story;
        if (node is null) EnterNode(scene, cursor.Person!.Entry);
        else if (cursor.Roll is not null) ShowRoll();
        else PlayLine(scene, node, cursor.Line);
    }

    private void ConversationAction(SceneOption option)
    {
        if (option.Quest is { } action)
        {
            var quest = Lore.Quests.First(q => q.Id == (action.Start ?? action.Advance ?? action.Complete));
            AddFlags(quest.Flags(action));
        }
        if (option.Give is { } item && Rules.Items.ContainsKey(new ItemId(item)) && RewardMember() is { } by)
        {
            var member = _doc.Members[by];
            _doc = _doc with { Members = _doc.Members.SetItem(by, member with
            {
                Character = member.Character with { Bag = StackLoot(member.Character.Bag, [new LootDef(new ItemId(item), 1)]) },
            }) };
        }
        if (option.Service is { } service && Cursor?.Person is { } person)
            SetCursor(Cursor with { Person = person with { Service = service } });
    }

    private string? RewardMember()
    {
        var starter = Cursor?.Person?.By ?? Cursor?.Thing?.By ?? _lead;
        return starter is not null && _doc.Members.ContainsKey(starter) && Party().Any(p => p.Member == starter && p.Here)
            ? starter : Party().FirstOrDefault(p => p.Here).Member;
    }
}
