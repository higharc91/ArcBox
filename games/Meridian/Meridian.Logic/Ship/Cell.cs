using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meridian.Logic;

/// <summary>A square on one deck. y grows to the south. JSON: <c>["habitat", 3, -1]</c>.</summary>
[JsonConverter(typeof(CellJsonConverter))]
public readonly record struct Cell(DeckId Deck, int X, int Y) : IComparable<Cell>
{
    public Cell Step(Side side)
    {
        var (dx, dy) = side.Offset();
        return this with { X = X + dx, Y = Y + dy };
    }

    public bool InBounds(int radius) => Math.Abs(X) <= radius && Math.Abs(Y) <= radius;

    public int CompareTo(Cell other)
        => (Deck, Y, X).CompareTo((other.Deck, other.Y, other.X));

    public override string ToString() => $"{Deck}({X},{Y})";
}

public sealed class CellJsonConverter : JsonConverter<Cell>
{
    public override Cell Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("A cell is [deck, x, y].");
        }
        reader.Read();
        var deck = JsonSerializer.Deserialize<DeckId>(ref reader, options);
        reader.Read();
        var x = reader.GetInt32();
        reader.Read();
        var y = reader.GetInt32();
        reader.Read();
        return reader.TokenType == JsonTokenType.EndArray ? new Cell(deck, x, y) : throw new JsonException("A cell is [deck, x, y].");
    }

    public override void Write(Utf8JsonWriter writer, Cell value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        JsonSerializer.Serialize(writer, value.Deck, options);
        writer.WriteNumberValue(value.X);
        writer.WriteNumberValue(value.Y);
        writer.WriteEndArray();
    }
}
