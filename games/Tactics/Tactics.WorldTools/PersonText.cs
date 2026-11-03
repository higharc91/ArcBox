using System.Collections.Immutable;
using System.Globalization;
using Tactics.Logic;

namespace Tactics.WorldTools;

/// <summary>A person's walk as a marker in a scene holds it, a line a painter can type: <c>31,59 32,59 32,60</c>.</summary>
public static class PersonText
{
    /// <summary>What tells two people apart: one standing on a tile, doing one thing.</summary>
    public static string Key(RegionPerson person) => $"{person.Preset} {person.At} {person.Face} {Walk(person.Walk)}"
        + (person.Id is null && person.Character is null && person.Requires.IsDefault && person.Unless.IsDefault && person.Bark is null && person.Voice is null
            ? "" : $" {person.Id} {person.Character} {Flags(person.Requires)} {Flags(person.Unless)} {person.Bark} {person.Voice}");

    public static ImmutableArray<string> Flags(string text)
    {
        var flags = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).ToImmutableArray();
        return flags.IsEmpty ? default : flags;
    }

    public static string Flags(ImmutableArray<string> flags) => flags.IsDefaultOrEmpty ? "" : string.Join(' ', flags);

    public static string? Optional(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>The tiles of <paramref name="walk"/> as text, nothing for no walk.</summary>
    public static string Walk(ImmutableArray<Tile> walk)
        => walk.IsDefaultOrEmpty ? "" : string.Join(' ', walk.Select(t => string.Create(CultureInfo.InvariantCulture, $"{t.X},{t.Y}")));

    /// <summary>The tiles of a line of text, or default for none. What can't be read is left out, and the walk that's left fails the pack's checks with its tiles named.</summary>
    public static ImmutableArray<Tile> Walk(string text)
    {
        var tiles = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Tile).OfType<Tile>().ToImmutableArray();
        return tiles.IsEmpty ? default : tiles;
    }

    private static Tile? Tile(string text)
    {
        var parts = text.Split(',');
        return parts.Length == 2 && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y)
            ? new Tile(x, y)
            : null;
    }
}
