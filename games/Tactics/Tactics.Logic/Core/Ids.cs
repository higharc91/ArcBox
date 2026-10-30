using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tactics.Logic;

/// <summary>Identifies a unit in a battle. Serialises as a plain JSON string.</summary>
[JsonConverter(typeof(UnitIdJsonConverter))]
public readonly record struct UnitId(string Value) : IComparable<UnitId>
{
    /// <summary>Ordinal ordering, so the order never depends on culture.</summary>
    public int CompareTo(UnitId other) => string.CompareOrdinal(Value, other.Value);

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>Identifies an ability. Serialises as a plain JSON string.</summary>
[JsonConverter(typeof(AbilityIdJsonConverter))]
public readonly record struct AbilityId(string Value) : IComparable<AbilityId>
{
    /// <summary>Ordinal ordering, so the order never depends on culture.</summary>
    public int CompareTo(AbilityId other) => string.CompareOrdinal(Value, other.Value);

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>Identifies a condition. Serialises as a plain JSON string.</summary>
[JsonConverter(typeof(ConditionIdJsonConverter))]
public readonly record struct ConditionId(string Value) : IComparable<ConditionId>
{
    /// <summary>Ordinal ordering, so the order never depends on culture.</summary>
    public int CompareTo(ConditionId other) => string.CompareOrdinal(Value, other.Value);

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>Identifies an injury. Serialises as a plain JSON string.</summary>
[JsonConverter(typeof(InjuryIdJsonConverter))]
public readonly record struct InjuryId(string Value) : IComparable<InjuryId>
{
    /// <summary>Ordinal ordering, so the order never depends on culture.</summary>
    public int CompareTo(InjuryId other) => string.CompareOrdinal(Value, other.Value);

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>Identifies an item. Serialises as a plain JSON string.</summary>
[JsonConverter(typeof(ItemIdJsonConverter))]
public readonly record struct ItemId(string Value) : IComparable<ItemId>
{
    /// <summary>Ordinal ordering, so the order never depends on culture.</summary>
    public int CompareTo(ItemId other) => string.CompareOrdinal(Value, other.Value);

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>Identifies a loot table. Serialises as a plain JSON string.</summary>
[JsonConverter(typeof(LootTableIdJsonConverter))]
public readonly record struct LootTableId(string Value) : IComparable<LootTableId>
{
    /// <summary>Ordinal ordering, so the order never depends on culture.</summary>
    public int CompareTo(LootTableId other) => string.CompareOrdinal(Value, other.Value);

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>A grid position. Serialises as <c>[x, y]</c>. Orders by X, then Y.</summary>
[JsonConverter(typeof(TileJsonConverter))]
public readonly record struct Tile(int X, int Y) : IComparable<Tile>
{
    /// <summary>Orders by X, then Y.</summary>
    public int CompareTo(Tile other)
    {
        var byX = X.CompareTo(other.X);
        return byX != 0 ? byX : Y.CompareTo(other.Y);
    }

    /// <inheritdoc />
    public override string ToString() => $"({X}, {Y})";
}

/// <summary>Shared reading and writing for the id types: a plain string, also usable as a dictionary key.</summary>
public abstract class StringIdJsonConverter<T> : JsonConverter<T> where T : struct
{
    /// <summary>Wraps a string in the id type.</summary>
    protected abstract T Create(string value);

    /// <summary>Unwraps the id type to its string.</summary>
    protected abstract string Unwrap(T id);

    /// <inheritdoc />
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => Create(reader.GetString() ?? string.Empty);

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        => writer.WriteStringValue(Unwrap(value));

    /// <inheritdoc />
    public override T ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => Create(reader.GetString() ?? string.Empty);

    /// <inheritdoc />
    public override void WriteAsPropertyName(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        => writer.WritePropertyName(Unwrap(value));
}

/// <summary>JSON converter for <see cref="UnitId"/>.</summary>
public sealed class UnitIdJsonConverter : StringIdJsonConverter<UnitId>
{
    /// <inheritdoc />
    protected override UnitId Create(string value) => new(value);

    /// <inheritdoc />
    protected override string Unwrap(UnitId id) => id.Value;
}

/// <summary>JSON converter for <see cref="AbilityId"/>.</summary>
public sealed class AbilityIdJsonConverter : StringIdJsonConverter<AbilityId>
{
    /// <inheritdoc />
    protected override AbilityId Create(string value) => new(value);

    /// <inheritdoc />
    protected override string Unwrap(AbilityId id) => id.Value;
}

/// <summary>JSON converter for <see cref="ConditionId"/>.</summary>
public sealed class ConditionIdJsonConverter : StringIdJsonConverter<ConditionId>
{
    /// <inheritdoc />
    protected override ConditionId Create(string value) => new(value);

    /// <inheritdoc />
    protected override string Unwrap(ConditionId id) => id.Value;
}

/// <summary>JSON converter for <see cref="InjuryId"/>.</summary>
public sealed class InjuryIdJsonConverter : StringIdJsonConverter<InjuryId>
{
    /// <inheritdoc />
    protected override InjuryId Create(string value) => new(value);

    /// <inheritdoc />
    protected override string Unwrap(InjuryId id) => id.Value;
}

/// <summary>JSON converter for <see cref="ItemId"/>.</summary>
public sealed class ItemIdJsonConverter : StringIdJsonConverter<ItemId>
{
    /// <inheritdoc />
    protected override ItemId Create(string value) => new(value);

    /// <inheritdoc />
    protected override string Unwrap(ItemId id) => id.Value;
}

/// <summary>JSON converter for <see cref="LootTableId"/>.</summary>
public sealed class LootTableIdJsonConverter : StringIdJsonConverter<LootTableId>
{
    /// <inheritdoc />
    protected override LootTableId Create(string value) => new(value);

    /// <inheritdoc />
    protected override string Unwrap(LootTableId id) => id.Value;
}

/// <summary>Writes a <see cref="Tile"/> as <c>[x, y]</c> and reads one back. As a dictionary key it is <c>"x,y"</c>.</summary>
public sealed class TileJsonConverter : JsonConverter<Tile>
{
    /// <inheritdoc />
    public override Tile Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("A tile must be a two-number array [x, y].");
        reader.Read();
        var x = reader.GetInt32();
        reader.Read();
        var y = reader.GetInt32();
        reader.Read();
        if (reader.TokenType != JsonTokenType.EndArray)
            throw new JsonException("A tile must be a two-number array [x, y].");
        return new Tile(x, y);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, Tile value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(value.X);
        writer.WriteNumberValue(value.Y);
        writer.WriteEndArray();
    }

    /// <inheritdoc />
    public override Tile ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString() ?? string.Empty;
        var comma = text.IndexOf(',');
        if (comma > 0
            && int.TryParse(text.AsSpan(0, comma), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var x)
            && int.TryParse(text.AsSpan(comma + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var y))
            return new Tile(x, y);
        throw new JsonException($"'{text}' is not a tile key (expected \"x,y\").");
    }

    /// <inheritdoc />
    public override void WriteAsPropertyName(Utf8JsonWriter writer, Tile value, JsonSerializerOptions options)
        => writer.WritePropertyName(string.Create(CultureInfo.InvariantCulture, $"{value.X},{value.Y}"));
}
