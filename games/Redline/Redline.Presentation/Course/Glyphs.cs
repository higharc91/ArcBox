using System.Numerics;

namespace Redline.Presentation.Course;

/// <summary>
/// A blocky stencil font for painted grid numbers: each digit is straight strokes in a cell <see cref="CellWidth"/> wide and 1
/// tall (x right, y up), drawn <see cref="Weight"/> thick with square ends so the strokes join cleanly.
/// </summary>
public static class Glyphs
{
    public const double CellWidth = 0.55;
    public const double Spacing = 0.13;
    public const double Weight = 0.13;
    public static double Advance => CellWidth + Spacing;

    private const float W = (float)(Weight / 2);
    private const float L = W, R = (float)CellWidth - W, B = W, T = 1 - W, M = 0.5f;

    private static readonly Dictionary<char, (float, float, float, float)[]> Digits = new()
    {
        ['0'] = [(L, B, L, T), (L, T, R, T), (R, T, R, B), (R, B, L, B)],
        ['1'] = [(0.33f, B, 0.33f, T), (0.12f, 0.76f, 0.33f, T), (0.1f, B, 0.5f, B)],
        ['2'] = [(L, T, R, T), (R, T, R, M), (R, M, L, M), (L, M, L, B), (L, B, R, B)],
        ['3'] = [(L, T, R, T), (R, T, R, B), (0.16f, M, R, M), (L, B, R, B)],
        ['4'] = [(L, T, L, M), (L, M, R, M), (0.4f, T, 0.4f, B)],
        ['5'] = [(R, T, L, T), (L, T, L, M), (L, M, R, M), (R, M, R, B), (R, B, L, B)],
        ['6'] = [(R, T, L, T), (L, T, L, B), (L, B, R, B), (R, B, R, M), (R, M, L, M)],
        ['7'] = [(L, T, R, T), (R, T, 0.2f, B)],
        ['8'] = [(L, B, L, T), (L, T, R, T), (R, T, R, B), (R, B, L, B), (L, M, R, M)],
        ['9'] = [(R, M, L, M), (L, M, L, T), (L, T, R, T), (R, T, R, B), (R, B, L, B)],
    };

    /// <summary>The digit's strokes, each extended by half the weight at both ends (square caps).</summary>
    public static IEnumerable<(Vector2 From, Vector2 To)> Strokes(char digit)
    {
        if (!Digits.TryGetValue(digit, out var strokes))
            throw new ArgumentOutOfRangeException(nameof(digit), digit, "Only digits are painted.");
        foreach (var (x0, y0, x1, y1) in strokes)
        {
            var a = new Vector2(x0, y0);
            var b = new Vector2(x1, y1);
            var d = Vector2.Normalize(b - a) * W;
            yield return (a - d, b + d);
        }
    }
}
