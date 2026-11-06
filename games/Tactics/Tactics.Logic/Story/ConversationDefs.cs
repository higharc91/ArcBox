using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>The first eligible entry wins, so a hand-in can precede the ordinary greeting.</summary>
public sealed record ConversationEntry(string Node, ImmutableArray<string> Requires, ImmutableArray<string> Unless)
{
    public bool Matches(IReadOnlySet<string> flags) => Requires.All(flags.Contains) && !Unless.Any(flags.Contains);
}

/// <summary>A named person's repeatable interaction scene.</summary>
public sealed record ConversationDef(string Character, SceneDef Scene, ImmutableArray<ConversationEntry> Entries)
{
    public ConversationEntry? Entry(IReadOnlySet<string> flags) => Entries.FirstOrDefault(e => e.Matches(flags));
}

/// <summary>A declarative quest transition; Advance names the quest and Stage names its next stage.</summary>
public sealed record SceneQuest(string? Start, string? Advance, string? Complete, string? Stage);
