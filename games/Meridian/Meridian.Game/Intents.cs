using System.Text.Json;
using ArcBox.Sdk;

namespace Meridian.Game;

/// <summary>Reads a phone's <see cref="CustomInput"/>. Malformed input is dropped, never thrown.</summary>
internal static class Intents
{
    public static bool TryRead(PlayerInput input, out string action, out JsonElement data)
    {
        action = "";
        data = default;
        try
        {
            var custom = input.Read<CustomInput>();
            action = custom.Action ?? "";
            data = custom.Data ?? default;
            return action.Length > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>A string property of the data, or null.</summary>
    public static string? Text(JsonElement data, string name)
        => data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    public static int? Number(JsonElement data, string name)
        => data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out var p) && p.TryGetInt32(out var n) ? n : null;

    public static bool? Flag(JsonElement data, string name)
        => data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out var p)
           && p.ValueKind is JsonValueKind.True or JsonValueKind.False ? p.GetBoolean() : null;
}
