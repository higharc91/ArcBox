using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tactics.Logic;

/// <summary>
/// String-keyed sorted collections with ordinal ordering. <c>ImmutableSortedSet&lt;string&gt;.Empty</c> and collection
/// expressions use the culture-sensitive default comparer, so build every string set or map in the state through here.
/// </summary>
public static class Ordinal
{
    /// <summary>An ordinal set of the given strings.</summary>
    public static ImmutableSortedSet<string> Set(params IEnumerable<string> items)
        => ImmutableSortedSet.CreateRange(StringComparer.Ordinal, items);

    /// <summary>An ordinal map of the given entries. Throws <see cref="ArgumentException"/> on a duplicate key.</summary>
    public static ImmutableSortedDictionary<string, T> Map<T>(params IEnumerable<(string Key, T Value)> entries)
    {
        var builder = ImmutableSortedDictionary.CreateBuilder<string, T>(StringComparer.Ordinal);
        foreach (var (key, value) in entries)
            builder.Add(key, value);
        return builder.ToImmutable();
    }
}

/// <summary>
/// Reads and writes <c>ImmutableSortedSet&lt;string&gt;</c> and <c>ImmutableSortedDictionary&lt;string, T&gt;</c> so that a
/// deserialised collection keeps ordinal ordering (System.Text.Json would otherwise rebuild it with the culture-sensitive
/// default comparer). Put <c>[JsonConverter(typeof(OrdinalJsonConverter))]</c> on every such property.
/// </summary>
public sealed class OrdinalJsonConverter : JsonConverterFactory
{
    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert)
        => typeToConvert == typeof(ImmutableSortedSet<string>)
           || (typeToConvert.IsGenericType
               && typeToConvert.GetGenericTypeDefinition() == typeof(ImmutableSortedDictionary<,>)
               && typeToConvert.GetGenericArguments()[0] == typeof(string));

    /// <inheritdoc />
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        => typeToConvert == typeof(ImmutableSortedSet<string>)
            ? new SetConverter()
            : (JsonConverter)Activator.CreateInstance(
                typeof(MapConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[1]))!;

    private sealed class SetConverter : JsonConverter<ImmutableSortedSet<string>>
    {
        public override ImmutableSortedSet<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => Ordinal.Set(JsonSerializer.Deserialize<string[]>(ref reader, options) ?? []);

        public override void Write(Utf8JsonWriter writer, ImmutableSortedSet<string> value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (var item in value.WithComparer(StringComparer.Ordinal))
                writer.WriteStringValue(item);
            writer.WriteEndArray();
        }
    }

    private sealed class MapConverter<T> : JsonConverter<ImmutableSortedDictionary<string, T>>
    {
        public override ImmutableSortedDictionary<string, T> Read(ref Utf8JsonReader reader, Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException("Expected an object.");
            var builder = ImmutableSortedDictionary.CreateBuilder<string, T>(StringComparer.Ordinal);
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                var key = reader.GetString()!;
                reader.Read();
                if (!builder.TryAdd(key, JsonSerializer.Deserialize<T>(ref reader, options)!))
                    throw new JsonException($"Duplicate key '{key}'.");
            }
            return builder.ToImmutable();
        }

        public override void Write(Utf8JsonWriter writer, ImmutableSortedDictionary<string, T> value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            foreach (var (key, item) in value.WithComparers(StringComparer.Ordinal))
            {
                writer.WritePropertyName(key);
                JsonSerializer.Serialize(writer, item, options);
            }
            writer.WriteEndObject();
        }
    }
}
