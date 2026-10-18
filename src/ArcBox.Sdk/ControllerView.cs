using System.Text.Json.Serialization;

namespace ArcBox.Sdk;

public sealed record Option(string Id, string Label);

/// <summary>
/// Any change to a player's view gives it a new ViewId and invalidates input already in flight. Keep a view unchanged while it should accept repeated input.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(WaitView), "wait")]
[JsonDerivedType(typeof(TextEntryView), "textEntry")]
[JsonDerivedType(typeof(ChoiceView), "choice")]
[JsonDerivedType(typeof(ButtonsView), "buttons")]
[JsonDerivedType(typeof(DrawView), "draw")]
public abstract record ControllerView
{
    public string ViewId { get; init; } = "";          // assigned by the server; games leave it empty
    public DateTimeOffset? DeadlineUtc { get; init; }
    public string? Header { get; init; }
}

public sealed record WaitView(string Message) : ControllerView;
public sealed record TextEntryView(string Prompt, int MaxLength, string? Placeholder = null) : ControllerView;
public sealed record ChoiceView(string Prompt, IReadOnlyList<Option> Options, bool AllowMultiple = false, int? MaxSelections = null) : ControllerView;
public sealed record ButtonsView(string? Prompt, IReadOnlyList<Option> Buttons) : ControllerView;
public sealed record DrawView(string Prompt, IReadOnlyList<string>? Colors = null) : ControllerView;
