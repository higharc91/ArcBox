using System.Collections.Immutable;
using System.Text.Json;
using ArcBox.Sdk;
using Tactics.Logic;

namespace Tactics.Game;

public sealed partial class TacticsGame
{
    private string? _questLogLeader;
    private string? _questSelection;
    private bool QuestLogOpen => _phase == TacticsPhase.Exploration && _questLogLeader is not null && _lead == _questLogLeader
        && Leader is { } leader && _online.Contains(leader.Id);

    private ImmutableArray<QuestState> KnownQuests()
        => [.. RegionQuests.Select(q => q.State(StoryFlags())).OfType<QuestState>()];

    private QuestLogView Journal(bool canPin)
    {
        var flags = StoryFlags();
        var states = KnownQuests();
        var entries = states.Select(s => new QuestEntryView(s.Def.Id, s.Def.Name, s.Status,
            Lore.Character(s.Def.Giver)?.Name ?? s.Def.Giver, Lore.Character(s.Def.Giver)?.Preset,
            s.Status == "active" ? s.Current.Objective : s.Status == "done" ? "Quest complete" : "Quest failed",
            [.. s.Def.Stages.Select((stage, i) => (Stage: stage, Index: i)).Where(x => x.Stage.When.All(flags.Contains))
                .Select(x => new QuestHistoryView(x.Stage.Log, x.Index < s.Stage || s.Status != "active", x.Index == s.Stage ? x.Stage.Objective : null))],
            s.Status == "active" ? null : s.Log, s.Status == "done" ? new QuestRewardView(s.Def.Rewards.Gold, s.Def.Rewards.Xp,
                [.. s.Def.Rewards.Items.Select(i => new QuestItemView(ItemName(new ItemId(i.Item)), i.Count))]) : null)).ToImmutableArray();
        var selected = entries.Any(q => q.Id == _questSelection) ? _questSelection : entries.FirstOrDefault(q => q.Id == _doc.World?.PinnedQuest)?.Id ?? entries.FirstOrDefault()?.Id;
        return new QuestLogView(entries, selected, _doc.World?.PinnedQuest, canPin, QuestLogOpen ? Leader?.Name : null);
    }

    private CustomView QuestLogScreen(Player player, ProfileId profile)
        => Custom("questLog", JsonSerializer.SerializeToNode(Journal(profile.Value == _lead), ArcJson.Options)!);

    private void QuestLogInput(Player player, ProfileId profile, JsonElement? data)
    {
        if (_phase != TacticsPhase.Exploration || profile.Value != _lead || !RegionQuests.Any()) return;
        var action = Str(data, "action");
        var quest = Str(data, "quest");
        var state = KnownQuests().FirstOrDefault(q => q.Def.Id == quest);
        switch (action)
        {
            case "open":
                Halt();
                _questLogLeader = _lead;
                _questSelection = state?.Def.Id ?? _doc.World?.PinnedQuest ?? KnownQuests().FirstOrDefault()?.Def.Id;
                break;
            case "close": _questLogLeader = null; break;
            case "select" when QuestLogOpen && state is not null: _questSelection = state.Def.Id; break;
            case "pin" when state?.Status == "active":
                _doc = _doc with { World = _doc.World! with { PinnedQuest = state.Def.Id } };
                Save();
                break;
            case "unpin":
                _doc = _doc with { World = _doc.World! with { PinnedQuest = null } };
                Save();
                break;
        }
    }

    private QuestTrackerView? QuestTracker()
    {
        if (_doc.World?.PinnedQuest is not { } id || RegionQuests.FirstOrDefault(q => q.Id == id)?.State(StoryFlags()) is not { } state) return null;
        Tile? target = null;
        if (state.Status == "active" && state.Current.Target is { } waypoint)
        {
            var region = waypoint.Region ?? state.Def.Region;
            if (region != _region!.Def.Id)
            {
                var mouths = AllThings.Where(t => t.To?.Region == region).Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
                target = ThingViews()?.Where(t => mouths.Contains(t.Id)).OrderBy(t => Math.Abs(t.At.X - _at.X) + Math.Abs(t.At.Y - _at.Y))
                    .ThenBy(t => t.Id, StringComparer.Ordinal).Select(t => (Tile?)t.At).FirstOrDefault();
            }
            else if (waypoint.Person is { } person)
                target = PresentPeople().Where(p => RegionPeople.Id(p.Person, p.Index) == person).Select(p => (Tile?)p.Person.At).FirstOrDefault();
            else if (waypoint.Thing is { } thing)
                target = ThingViews()?.Where(t => t.Id == thing).Select(t => (Tile?)t.At).FirstOrDefault();
            else target = waypoint.Tile;
        }
        return new QuestTrackerView(id, state.Def.Name, state.Status == "active" ? state.Current.Objective : state.Status == "done" ? "Quest complete" : "Quest failed", state.Status, target);
    }

    private ImmutableArray<QuestPersonMarker>? QuestPersonMarkers()
    {
        var flags = StoryFlags();
        var result = ImmutableArray.CreateBuilder<QuestPersonMarker>();
        foreach (var (person, _) in PresentPeople())
        {
            if (ConversationFor(person) is not { } conversation || conversation.Entry(flags) is not { } entry) continue;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var todo = new Queue<string>(); todo.Enqueue(entry.Node);
            bool give = false, turnIn = false;
            while (todo.TryDequeue(out var id))
            {
                if (!seen.Add(id) || conversation.Scene.Node(id) is not { } node) continue;
                if (node.Next is { } next) todo.Enqueue(next);
                foreach (var option in SceneRun.Offered(node, flags, PartyTags.Of(_doc.Members.Values.Select(m => m.Character)), ImmutableHashSet<string>.Empty))
                {
                    if (option.Quest is { } action)
                    {
                        if (action.Start is { } start && Lore.Quests.First(q => q.Id == start).State(flags) is null) give = true;
                        if (action.Complete is { } complete && Lore.Quests.First(q => q.Id == complete).State(flags)?.Status == "active") turnIn = true;
                    }
                    foreach (var target in option.Targets) todo.Enqueue(target);
                }
            }
            if (give || turnIn) result.Add(new QuestPersonMarker(person.At, turnIn));
        }
        return result.Count == 0 ? null : result.ToImmutable();
    }
}
