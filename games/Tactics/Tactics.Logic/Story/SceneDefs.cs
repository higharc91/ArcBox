using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>
/// A voiced line of a scene. <see cref="Speaker"/> is a character id or <see cref="SceneDef.Narrator"/>, and <see cref="Id"/> is stable:
/// the clip and any translation key on it. <see cref="Mood"/> picks the speaker's portrait, <c>idle</c> when the line gives none.
/// </summary>
public sealed record SceneLine(string Id, string Speaker, string Text, string Mood)
{
    /// <summary>The mood of a line that doesn't name one.</summary>
    public const string DefaultMood = "idle";
}

/// <summary>A skill check on an option: the party picks who rolls d20 + their bonus for <see cref="Skill"/> against <see cref="Dc"/>.</summary>
public sealed record SceneCheck(string Skill, int Dc, string Pass, string Fail, bool Group = false);

/// <summary>
/// The tags that gate an option: it's offered only when someone in the party has one of them, of any kind. An option with no tags is
/// always offered.
/// </summary>
public sealed record OptionTags(ImmutableArray<string> Background, ImmutableArray<string> Race, ImmutableArray<string> Class)
{
    /// <summary>No tags: offered to any party.</summary>
    public static OptionTags None { get; } = new([], [], []);

    /// <summary>True when there's at least one tag.</summary>
    public bool Any => Background.Length + Race.Length + Class.Length > 0;
}

/// <summary>
/// Something the party can say or do at a node. It leads <see cref="To"/> a node, or a <see cref="Check"/> decides where. In a vote,
/// <see cref="Choice"/> names the <c>arcs.json</c> choice it stands for. <see cref="Requires"/> are tested as the node shows.
/// </summary>
public sealed record SceneOption(string Id, string Label, string? To, string? Choice, ImmutableArray<string> Sets,
    ImmutableArray<string> Requires, OptionTags Tags, SceneCheck? Check, int Gold = 0, SceneQuest? Quest = null, string? Give = null, string? Service = null, ImmutableArray<string> Unless = default)
{
    /// <summary>
    /// Every node this option names: its <see cref="To"/>, then its check's pass and fail. A check decides where it goes (<see
    /// cref="SceneRun.Target"/>), but a <see cref="To"/> written beside one is still a target here, as <c>check.py</c> counts it (review
    /// A6): it must be a node of the scene, an option naming one already played isn't offered, and one naming its own node is warned of.
    /// </summary>
    public IEnumerable<string> Targets
        => [.. To is { } to ? [to] : Array.Empty<string>(), .. Check is { } check ? [check.Pass, check.Fail] : Array.Empty<string>()];
}

/// <summary>
/// One node of a scene: its lines, the flags it sets as it plays, then exactly one of <see cref="Options"/> (the party picks, by a
/// vote when <see cref="Vote"/>), <see cref="Next"/>, or <see cref="End"/>.
/// </summary>
public sealed record SceneNode(string Id, ImmutableArray<SceneLine> Lines, ImmutableArray<string> Sets, ImmutableArray<SceneOption> Options,
    bool Vote, string? Next, bool End, int Gold = 0)
{
    /// <summary>Every node this one can lead to, in the order it's written.</summary>
    public IEnumerable<string> Targets => Next is { } next ? [next] : Options.SelectMany(o => o.Targets);
}

/// <summary>
/// A scene of <c>lore/scenes</c>: the dialogue of an optional <see cref="Beat"/>, as a graph of <see cref="Nodes"/> from <see cref="Start"/>.
/// <see cref="Place"/> is where it's set, null for the beat's own place. It plays only when every flag of <see cref="Requires"/> is set
/// and none of <see cref="Unless"/>, tested as it starts. <see cref="After"/> marks a battle beat's aftermath, which plays after the fight;
/// the others are its lead-in.
/// </summary>
public sealed record SceneDef(string Id, string? Beat, string? Place, string Title, ImmutableArray<string> Requires, ImmutableArray<string> Unless,
    ImmutableArray<string> Cast, string Start, ImmutableArray<SceneNode> Nodes, bool After, bool Camp = false)
{
    /// <summary>The speaker who isn't a character.</summary>
    public const string Narrator = "narrator";

    /// <summary>The node with this id, or null.</summary>
    public SceneNode? Node(string id) => Nodes.FirstOrDefault(n => n.Id == id);

    /// <summary>Every line of the scene, node by node.</summary>
    public IEnumerable<SceneLine> Lines => Nodes.SelectMany(n => n.Lines);
}

/// <summary>A character of <c>lore/characters.json</c>, as a scene shows them: their name, the portrait preset and their role.</summary>
public sealed record LoreCharacter(string Id, string Name, string Preset, string Role, string? Sprite = null, string? Portrait = null)
{
    public string SpriteKey => Sprite ?? $"characters.{Preset}";
    public string PortraitKey => Portrait ?? Preset;
}
