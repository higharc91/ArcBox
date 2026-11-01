using Tactics.Logic;

namespace Tactics.WorldTools;

/// <summary>Says where a set of tiles is, so one line covers a thousand tiles and a painter can still find them.</summary>
public static class TileSpread
{
    /// <summary>
    /// The start of a sentence about <paramref name="tiles"/>, ending in the right verb: <c>"The tile (4, 6) is"</c>, or
    /// <c>"600 tiles, x 40 to 44, y 0 to 119, first (40, 0), are"</c>. First goes by row, then column. There must be at least one.
    /// </summary>
    public static string Say(IReadOnlyCollection<Tile> tiles, string one, string many)
    {
        var first = tiles.OrderBy(t => t.Y).ThenBy(t => t.X).First();
        return tiles.Count == 1
            ? $"The tile {first} {one}"
            : $"{tiles.Count} tiles, x {tiles.Min(t => t.X)} to {tiles.Max(t => t.X)}, y {tiles.Min(t => t.Y)} to {tiles.Max(t => t.Y)}, first {first}, {many}";
    }

    /// <summary><c>"it"</c> or <c>"them"</c>, for the sentence after <see cref="Say"/>.</summary>
    public static string It(IReadOnlyCollection<Tile> tiles) => tiles.Count == 1 ? "it" : "them";
}
