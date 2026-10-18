using System.Text.Json;

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
public sealed record ChoiceInput(IReadOnlyList<string> Selected);
public sealed record ButtonsInput(string Pressed);
public sealed record Stroke(string Color, float Width, IReadOnlyList<float> Points); // x,y pairs normalized 0..1
public sealed record DrawInput(IReadOnlyList<Stroke> Strokes);
