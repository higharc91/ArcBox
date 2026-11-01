using System.Runtime.InteropServices;

namespace Tactics.Presentation.Board;

/// <summary>
/// Keeps a top's checker dither a checker on the tile. The pack's tops are square, so they're laid on a tile turned by 45 degrees (x = U - V,
/// y = (U + V) / 2), and a checker's diagonals, which are all one colour, then run straight down the screen: at scale 2 every patch of
/// dither is a comb of vertical stripes two pixels wide. It's the art's own colours in the right place, so no filter or UV can tell the
/// comb from the art's outlines, which that same turn draws well.
/// <para>
/// So this works on the dither alone. A top goes to the TV at twice its size, where each texel is four, which samples exactly as it did at
/// any scale. A texel that's part of a dither gets a pattern on those four that lands as an upright checker of whole art pixels, in the same two
/// colours and shares. The art's files don't change. Its pattern is in the texture's own space, so it holds however the board is placed, as
/// long as that's on whole pixels.
/// </para>
/// </summary>
public static class DitherTexels
{
    /// <summary>How many times larger a top is drawn from.</summary>
    public const int Scale = 2;

    /// <summary>
    /// <paramref name="rgba"/>, one frame of a top that tiles, at <see cref="Scale"/> times its size with its dither turned upright; or null if
    /// it has no dither, so that it's drawn as it is.
    /// </summary>
    public static byte[]? Doubled(ReadOnlySpan<byte> rgba, int width, int height)
    {
        var art = MemoryMarshal.Cast<byte, uint>(rgba);
        var dither = new bool[art.Length];
        var any = false;
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                any |= dither[y * width + x] = IsDither(art, width, height, x, y);
        if (!any)
            return null;

        var doubled = new byte[rgba.Length * Scale * Scale];
        var texels = MemoryMarshal.Cast<byte, uint>(doubled.AsSpan());
        var wide = width * Scale;
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var own = art[y * width + x];
                var (even, odd) = (own, own);
                if (dither[y * width + x])
                    (even, odd) = ((x + y & 1) == 0 ? (own, At(art, width, height, x + 1, y)) : (At(art, width, height, x + 1, y), own));

                for (var j = 0; j < Scale; j++)
                    for (var i = 0; i < Scale; i++)
                    {
                        var (u, v) = (x * Scale + i, y * Scale + j);
                        texels[v * wide + u] = (Across(u, v) + Down(u, v) & 1) == 0 ? even : odd;
                    }
            }
        return doubled;
    }

    // Which art column and every-other-row a doubled texel lands in, at scale 2, from the projection's x = U - V and y = (U + V) / 2.
    private static int Across(int u, int v) => (u - v) >> 1;

    private static int Down(int u, int v) => (u + v) >> 2;

    /// <summary>
    /// A checker's texel: all four beside it are one other colour, and its corners are its own, at least on one side of it. A lone dot has no such
    /// corners, and a one-texel diagonal line has only two opposite ones, so an outline stays an outline.
    /// </summary>
    private static bool IsDither(ReadOnlySpan<uint> art, int width, int height, int x, int y)
    {
        var own = At(art, width, height, x, y);
        var beside = At(art, width, height, x + 1, y);
        if (beside == own || At(art, width, height, x - 1, y) != beside || At(art, width, height, x, y + 1) != beside || At(art, width, height, x, y - 1) != beside)
            return false;
        var (backLeft, backRight) = (At(art, width, height, x - 1, y - 1) == own, At(art, width, height, x + 1, y - 1) == own);
        var (frontLeft, frontRight) = (At(art, width, height, x - 1, y + 1) == own, At(art, width, height, x + 1, y + 1) == own);
        return (backLeft && backRight) || (frontLeft && frontRight) || (backLeft && frontLeft) || (backRight && frontRight);
    }

    private static uint At(ReadOnlySpan<uint> art, int width, int height, int x, int y) => art[(y + height) % height * width + (x + width) % width];
}
