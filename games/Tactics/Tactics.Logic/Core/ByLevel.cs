using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tactics.Logic;

/// <summary>Level-keyed table: the highest key ≤ level wins. JSON: <c>{"1": x, "5": y}</c>.</summary>
[JsonConverter(typeof(ByLevelJsonConverterFactory))]
public sealed record ByLevel<T>(ImmutableSortedDictionary<int, T> Steps)
{
    /// <summary>The value for the highest key at or below <paramref name="level"/>. Throws if there is none.</summary>
    public T At(int level)
    {
        var found = false;
        T best = default!;
        foreach (var (key, value) in Steps)
        {
            if (key > level)
                break;
            best = value;
            found = true;
        }
        if (!found)
            throw new ArgumentOutOfRangeException(nameof(level), level, "No step at or below this level.");
        return best;
    }

    /// <summary>A table that gives <paramref name="value"/> at every level.</summary>
    public static ByLevel<T> Constant(T value)
        => new(ImmutableSortedDictionary<int, T>.Empty.Add(1, value));

    /// <summary>A value that is the same at every level (<see cref="Constant"/>).</summary>
    public static implicit operator ByLevel<T>(T value) => Constant(value);
}

/// <summary>Creates the <see cref="ByLevel{T}"/> JSON converter for each element type.</summary>
public sealed class ByLevelJsonConverterFactory : JsonConverterFactory
{
    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert)
        => typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(ByLevel<>);

    /// <inheritdoc />
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        => (JsonConverter)Activator.CreateInstance(
            typeof(ByLevelJsonConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]))!;
}

/// <summary>Reads and writes a <see cref="ByLevel{T}"/> as an object keyed by level.</summary>
public sealed class ByLevelJsonConverter<T> : JsonConverter<ByLevel<T>>
{
    /// <inheritdoc />
    public override ByLevel<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            var single = JsonSerializer.Deserialize<T>(ref reader, options)
                ?? throw new JsonException("A by-level step value cannot be null.");
            return ByLevel<T>.Constant(single);
        }
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (root.EnumerateObject().Count() == 1 && root.TryGetProperty("byLevel", out var nested) && nested.ValueKind == JsonValueKind.Object)
            root = nested;
        var steps = ImmutableSortedDictionary.CreateBuilder<int, T>();
        foreach (var prop in root.EnumerateObject())
        {
            if (!int.TryParse(prop.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var level))
                throw new JsonException($"'{prop.Name}' is not a level.");
            var value = prop.Value.Deserialize<T>(options)!;
            if (!steps.TryAdd(level, value))
                throw new JsonException($"Level {level} appears twice.");
        }
        return new ByLevel<T>(steps.ToImmutable());
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, ByLevel<T> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var (level, step) in value.Steps)
        {
            writer.WritePropertyName(level.ToString(CultureInfo.InvariantCulture));
            JsonSerializer.Serialize(writer, step, options);
        }
        writer.WriteEndObject();
    }
}
