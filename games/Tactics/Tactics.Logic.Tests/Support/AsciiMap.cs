using System.Collections.Immutable;

namespace Tactics.Logic.Tests;

/// <summary>
/// Builds a <see cref="BattleMap"/> from a text picture. Rows are lines, cells are separated by spaces, and each cell is
/// one height digit (<c>0</c>-<c>9</c>) followed by one marker: <c>.</c> plain, <c>~</c> water (difficult), <c>#</c> impassable,
/// <c>^</c> difficult, <c>x</c> deep, <c>c</c> half-cover crate (walkable), <c>C</c> full-cover blocking prop.
/// </summary>
public static class AsciiMap
{
    /// <summary>The material list every ASCII map uses; every tile has material 0.</summary>
    public static readonly ImmutableArray<string> Materials = ["ground"];

    /// <summary>Parses the picture into a map with id <c>"ascii"</c>.</summary>
    public static BattleMap Parse(string text)
    {
        var rows = text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (rows.Length == 0)
            throw new FormatException("The map has no rows.");

        var cells = rows.Select(r => r.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToArray();
        var width = cells[0].Length;
        var height = cells.Length;
        var bytes = new byte[width * height * 3];
        var props = ImmutableArray.CreateBuilder<PropPlacement>();

        for (var y = 0; y < height; y++)
        {
            if (cells[y].Length != width)
                throw new FormatException($"Row {y} has {cells[y].Length} cells, expected {width}.");
            for (var x = 0; x < width; x++)
            {
                var cell = cells[y][x];
                if (cell.Length != 2 || cell[0] < '0' || cell[0] > '9')
                    throw new FormatException($"Cell ({x}, {y}) '{cell}' must be a height digit and a marker.");

                var flags = TileFlags.None;
                switch (cell[1])
                {
                    case '.': break;
                    case '~': flags = TileFlags.Water | TileFlags.Difficult; break;
                    case '#': flags = TileFlags.Impassable; break;
                    case '^': flags = TileFlags.Difficult; break;
                    case 'x': flags = TileFlags.Deep; break;
                    case 'c': props.Add(new PropPlacement("crate", new Tile(x, y), false, Cover.Half)); break;
                    case 'C': props.Add(new PropPlacement("block", new Tile(x, y), true, Cover.Total)); break;
                    default: throw new FormatException($"Cell ({x}, {y}) has unknown marker '{cell[1]}'.");
                }

                var i = (y * width + x) * 3;
                bytes[i] = (byte)(cell[0] - '0');
                bytes[i + 1] = 0;
                bytes[i + 2] = (byte)flags;
            }
        }

        return BattleMap.FromBytes("ascii", width, height, bytes, Materials, props.ToImmutable(),
            ImmutableSortedDictionary<string, ImmutableArray<Tile>>.Empty.WithComparers(StringComparer.Ordinal));
    }
}
