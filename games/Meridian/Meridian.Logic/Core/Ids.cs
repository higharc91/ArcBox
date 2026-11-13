using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meridian.Logic;

/// <summary>
/// A string-backed id. The <c>static abstract</c> factory lets one generic converter serve every id type, instead of a
/// hand-written converter per id.
/// </summary>
public interface IStringId<TSelf> where TSelf : struct, IStringId<TSelf>
{
    string Value { get; }
    static abstract TSelf From(string value);
}

/// <summary>Writes an id as a plain JSON string, both as a value and as a dictionary key.</summary>
public sealed class StringIdJsonConverter<T> : JsonConverter<T> where T : struct, IStringId<T>
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => T.From(reader.GetString() ?? throw new JsonException($"Expected a string for {typeof(T).Name}."));

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);

    public override T ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => T.From(reader.GetString()!);

    public override void WriteAsPropertyName(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        => writer.WritePropertyName(value.Value);
}

/// <summary>A player's seat in this game. The game adapter maps platform player ids to seats.</summary>
[JsonConverter(typeof(StringIdJsonConverter<SeatId>))]
public readonly record struct SeatId(string Value) : IStringId<SeatId>, IComparable<SeatId>
{
    public static SeatId From(string value) => new(value);
    public int CompareTo(SeatId other) => string.CompareOrdinal(Value, other.Value);
    public override string ToString() => Value;
}

/// <summary>A crew member's definition in <c>crew.json</c>.</summary>
[JsonConverter(typeof(StringIdJsonConverter<CrewId>))]
public readonly record struct CrewId(string Value) : IStringId<CrewId>, IComparable<CrewId>
{
    public static CrewId From(string value) => new(value);
    public int CompareTo(CrewId other) => string.CompareOrdinal(Value, other.Value);
    public override string ToString() => Value;
}

/// <summary>A tile's definition in <c>tiles.json</c>, e.g. <c>med_bay</c>. Several compartments can share one.</summary>
[JsonConverter(typeof(StringIdJsonConverter<TileId>))]
public readonly record struct TileId(string Value) : IStringId<TileId>, IComparable<TileId>
{
    public static TileId From(string value) => new(value);
    public int CompareTo(TileId other) => string.CompareOrdinal(Value, other.Value);
    public override string ToString() => Value;
}

/// <summary>A tile placed on the ship, e.g. <c>c12</c>. Numbered in placement order, so ids are stable.</summary>
[JsonConverter(typeof(StringIdJsonConverter<CompartmentId>))]
public readonly record struct CompartmentId(string Value) : IStringId<CompartmentId>, IComparable<CompartmentId>
{
    public static CompartmentId From(string value) => new(value);
    public static CompartmentId Numbered(int n) => new($"c{n}");
    public int CompareTo(CompartmentId other) => string.CompareOrdinal(Value, other.Value);
    public override string ToString() => Value;
}

/// <summary>A card's definition in one of the card files.</summary>
[JsonConverter(typeof(StringIdJsonConverter<CardId>))]
public readonly record struct CardId(string Value) : IStringId<CardId>, IComparable<CardId>
{
    public static CardId From(string value) => new(value);
    public int CompareTo(CardId other) => string.CompareOrdinal(Value, other.Value);
    public override string ToString() => Value;
}

// Enums carry their lower-case wire names explicitly: ArcJson.Options writes enum member names as declared, and the
// content files and phones use lower case. JsonStringEnumMemberName (.NET 9+) is honoured by that converter.

[JsonConverter(typeof(JsonStringEnumConverter<DeckId>))]
public enum DeckId
{
    [JsonStringEnumMemberName("command")] Command,
    [JsonStringEnumMemberName("habitat")] Habitat,
    [JsonStringEnumMemberName("engineering")] Engineering,
}

[JsonConverter(typeof(JsonStringEnumConverter<Trait>))]
public enum Trait
{
    [JsonStringEnumMemberName("brawn")] Brawn,
    [JsonStringEnumMemberName("agility")] Agility,
    [JsonStringEnumMemberName("tech")] Tech,
    [JsonStringEnumMemberName("nerve")] Nerve,
}

/// <summary>A compass side of a cell. Declared clockwise, so rotating by a quarter turn is <c>+1 mod 4</c>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<Side>))]
public enum Side
{
    [JsonStringEnumMemberName("n")] N,
    [JsonStringEnumMemberName("e")] E,
    [JsonStringEnumMemberName("s")] S,
    [JsonStringEnumMemberName("w")] W,
}

[JsonConverter(typeof(JsonStringEnumConverter<CardKind>))]
public enum CardKind
{
    [JsonStringEnumMemberName("incident")] Incident,
    [JsonStringEnumMemberName("salvage")] Salvage,
    [JsonStringEnumMemberName("anomaly")] Anomaly,
}

public static class Sides
{
    public static readonly EqArray<Side> All = [Side.N, Side.E, Side.S, Side.W];

    /// <summary>The side rotated clockwise by <paramref name="quarterTurns"/>.</summary>
    public static Side Rotate(this Side side, int quarterTurns) => (Side)((((int)side + quarterTurns) % 4 + 4) % 4);

    public static Side Opposite(this Side side) => side.Rotate(2);

    public static (int Dx, int Dy) Offset(this Side side) => side switch
    {
        Side.N => (0, -1),
        Side.E => (1, 0),
        Side.S => (0, 1),
        Side.W => (-1, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(side)),
    };
}

public static class Traits
{
    public static readonly EqArray<Trait> All = [Trait.Brawn, Trait.Agility, Trait.Tech, Trait.Nerve];
    public static readonly EqArray<Trait> Physical = [Trait.Brawn, Trait.Agility];
    public static readonly EqArray<Trait> Mental = [Trait.Tech, Trait.Nerve];
}
