using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Tactics.Logic;

public sealed record QuestTarget(string? Person = null, string? Thing = null, Tile? Tile = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Region = null);
public sealed record QuestStage(string Id, string Objective, string Log, ImmutableArray<string> When, QuestTarget? Target = null);
public sealed record QuestEnding(ImmutableArray<string> When, string Log);
public sealed record QuestItem(string Item, int Count);
public sealed record QuestRewards(int Gold, int Xp, ImmutableArray<QuestItem> Items, ImmutableArray<string> Sets,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ImmutableSortedDictionary<string, string>? Trinkets = null);
public sealed record QuestDef(string Id, string Name, string Giver, string Region, ImmutableArray<QuestStage> Stages,
    QuestEnding Done, QuestEnding? Failed, QuestRewards Rewards)
{
    public QuestState? State(IReadOnlySet<string> flags)
    {
        bool Holds(ImmutableArray<string> when) => !when.IsDefaultOrEmpty && when.All(flags.Contains);
        if (Holds(Done.When)) return new QuestState(this, "done", Stages.Length - 1);
        if (Failed is { } failed && Holds(failed.When)) return new QuestState(this, "failed", Stages.Length - 1);
        if (Stages.IsEmpty || !Holds(Stages[0].When)) return null;
        return new QuestState(this, "active", Enumerable.Range(0, Stages.Length).Last(i => Holds(Stages[i].When)));
    }

    public ImmutableArray<string> Flags(SceneQuest action)
        => action.Start == Id ? Stages[0].When : action.Complete == Id ? Done.When
            : action.Advance == Id && Stages.FirstOrDefault(s => s.Id == action.Stage) is { } stage ? stage.When : [];
}
public sealed record QuestState(QuestDef Def, string Status, int Stage)
{
    public QuestStage Current => Def.Stages[Stage];
    public string Log => Status == "done" ? Def.Done.Log : Status == "failed" ? Def.Failed!.Log : Current.Log;
}
