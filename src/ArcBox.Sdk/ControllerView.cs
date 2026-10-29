using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArcBox.Sdk;

public sealed record Option(
    string Id,
    string Label,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Hint = null)
{
    /// <summary>The original two-argument constructor, kept for binary compatibility with games built before Hint.</summary>
    /// <remarks>Marked as the JSON constructor because two constructors are ambiguous to System.Text.Json; Hint is then read through its init accessor.</remarks>
    [JsonConstructor]
    public Option(string id, string label) : this(id, label, null) { }
}

/// <summary>
/// Any change to a player's view gives it a new ViewId and invalidates input already in flight. Keep a view unchanged while it should accept repeated input.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(WaitView), "wait")]
[JsonDerivedType(typeof(TextEntryView), "textEntry")]
[JsonDerivedType(typeof(ChoiceView), "choice")]
[JsonDerivedType(typeof(ButtonsView), "buttons")]
[JsonDerivedType(typeof(DrawView), "draw")]
[JsonDerivedType(typeof(ShakeView), "shake")]
[JsonDerivedType(typeof(CustomView), "custom")]
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

/// <summary>
/// "Shake to charge, tap to fire": the phone shows a charge that builds as it is shaken (or tapped, without motion sensors)
/// and sends <see cref="ShakeInput"/>. With <paramref name="CanFire"/> it also offers a fire button. The charge is kept on
/// the phone, so the game can leave the view unchanged while the player shakes; <paramref name="InitialCharge"/> (0..1)
/// seeds it when a new view arrives.
/// </summary>
/// <param name="Color">An accent for the widget (<c>#rrggbb</c>), or null for the default.</param>
public sealed record ShakeView(string Prompt, string? Color = null, bool CanFire = false, double InitialCharge = 0) : ControllerView;

/// <summary>
/// A screen defined by a game module on the phone (web/play/src/games/), for games whose screens don't fit the generic
/// widgets. A phone that doesn't know the module or screen shows a "needs a newer app" card. Keep a view unchanged while
/// it should accept repeated input (a d-pad cursor); the phone renders the cursor locally.
/// </summary>
/// <param name="Module">A game module in web/play/src/games/ (normally the game id).</param>
/// <param name="Screen">A screen that module exports.</param>
/// <param name="Data">Screen-specific JSON. Kept under 64 KB (logged at Warning above that).</param>
public sealed record CustomView(string Module, string Screen, JsonElement Data) : ControllerView;
