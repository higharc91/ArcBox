using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Tactics.Logic;

/// <summary>
/// A dice expression such as <c>2d6+1</c>. A flat number like <c>3</c> has <c>Count</c> and <c>Sides</c> of 0.
/// Serialises as its canonical text.
/// </summary>
[JsonConverter(typeof(DiceJsonConverter))]
public readonly partial record struct Dice(int Count, int Sides, int Bonus)
{
    [GeneratedRegex(@"^(?:(?<count>\d+)d(?<sides>\d+)(?<bonus>[+-]\d+)?|(?<flat>[+-]?\d+))$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    /// <summary>Parses <c>"2d6+1"</c>, <c>"1d4-1"</c> or a flat <c>"3"</c>. Throws <see cref="FormatException"/> naming the text.</summary>
    public static Dice Parse(string text)
    {
        var match = text is null ? Match.Empty : Pattern().Match(text);
        if (match.Success)
        {
            if (match.Groups["flat"].Success)
            {
                if (TryInt(match.Groups["flat"].Value, out var flat))
                    return new Dice(0, 0, flat);
            }
            else if (TryInt(match.Groups["count"].Value, out var count)
                     && TryInt(match.Groups["sides"].Value, out var sides)
                     && count >= 1 && sides >= 1)
            {
                var bonus = 0;
                if (match.Groups["bonus"].Success && !TryInt(match.Groups["bonus"].Value, out bonus))
                    throw Bad(text);
                return new Dice(count, sides, bonus);
            }
        }
        throw Bad(text);
    }

    /// <summary>The mean roll: Count × (Sides + 1) / 2 + Bonus.</summary>
    public double Average => Count * (Sides + 1) / 2.0 + Bonus;

    /// <summary>The canonical text: <c>2d6+1</c>, <c>1d4-1</c>, <c>3</c>.</summary>
    public override string ToString()
    {
        if (Count == 0)
            return Bonus.ToString(CultureInfo.InvariantCulture);
        var text = $"{Count}d{Sides}";
        return Bonus == 0 ? text : text + (Bonus > 0 ? "+" : "-") + Math.Abs((long)Bonus).ToString(CultureInfo.InvariantCulture);
    }

    private static bool TryInt(string s, out int value)
        => int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);

    private static FormatException Bad(string? text) => new($"'{text}' is not a dice expression (expected e.g. '2d6+1', '1d4' or '3').");
}

/// <summary>Writes a <see cref="Dice"/> as its canonical text and parses it back.</summary>
public sealed class DiceJsonConverter : JsonConverter<Dice>
{
    /// <inheritdoc />
    public override Dice Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        try
        {
            return Dice.Parse(reader.GetString() ?? string.Empty);
        }
        catch (FormatException ex)
        {
            throw new JsonException(ex.Message, ex);
        }
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, Dice value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}
