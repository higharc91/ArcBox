using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArcBox.Sdk;

public sealed record PlayerInput(JsonElement Payload)
{
    public T Read<T>()
    {
        var result = Payload.Deserialize<T>(ArcJson.Options);
        if (result is null)
        {
            throw new JsonException($"Failed to deserialize payload to {typeof(T).Name}");
        }
        return result;
    }
}

public sealed record TextEntryInput(string Text);
/// <summary>
/// The <see cref="ChoiceView"/> reply. <see cref="ElapsedMs"/> is the phone's own stopwatch, from the options being painted
/// to the tap; optional (older phones omit it) and untrusted, so a game that uses it must clamp it.
/// </summary>
public sealed record ChoiceInput(IReadOnlyList<string> Selected, double? ElapsedMs = null);
public sealed record ButtonsInput(string Pressed);
public sealed record Stroke(string Color, float Width, IReadOnlyList<float> Points); // x,y pairs normalized 0..1
public sealed record DrawInput(IReadOnlyList<Stroke> Strokes);

/// <summary>
/// The <see cref="ShakeView"/> reply: the phone's current charge (0..1; the game clamps it) and whether the player fired.
/// A payload without a numeric <c>charge</c> is rejected with a <see cref="JsonException"/>; a missing <c>fire</c> is false.
/// </summary>
public sealed record ShakeInput([property: JsonRequired] double Charge, bool Fire);

/// <summary>The <see cref="CustomView"/> reply: a screen-defined action and its data.</summary>
public sealed record CustomInput(string Action, JsonElement? Data = null);
