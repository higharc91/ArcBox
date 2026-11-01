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
/// <para>
/// A top is only worth it if it has dither to speak of, because the doubled texture costs four times the memory. Sand, flagstone, ice, lava and the dungeon's
/// floors have a few texels that happen to pass for a checker, at most 7 together, and the crypt's runes are made of them. The art's real dither comes in patches of 80
/// and up. So a frame with no patch of <see cref="LeastPatch"/> texels has no dither.
/// </para>
/// </summary>
public static class DitherTexels
{
    /// <summary>How many times larger a top is drawn from.</summary>
    public const int Scale = 2;

    /// <summary>The fewest texels of dither touching each other that make a frame dithered: one more than the largest speck in the pack.</summary>
    public const int LeastPatch = 8;

    /// <summary>
    /// <paramref name="rgba"/>, a top that tiles, as a strip of <paramref name="frames"/> side by side, at <see cref="Scale"/> times its size with
    /// its dither turned upright; or null if no frame has any dither, so that it's drawn as it is.
    /// </summary>
    public static byte[]? Doubled(ReadOnlySpan<byte> rgba, int width, int height, int frames = 1)
    {
        var strip = MemoryMarshal.Cast<byte, uint>(rgba);
        var frameWidth = width / frames;
        var art = new uint[frameWidth * height];
        var doubled = default(byte[]);
        for (var frame = 0; frame < frames; frame++)
        {
            for (var y = 0; y < height; y++)
                strip.Slice(y * width + frame * frameWidth, frameWidth).CopyTo(art.AsSpan(y * frameWidth));
            if (!AnyDither(art, frameWidth, height))
                continue;
            doubled ??= Plain(strip, width, height);
            Pattern(art, frameWidth, height, MemoryMarshal.Cast<byte, uint>(doubled.AsSpan()), width * Scale, frame * frameWidth * Scale);
        }
        return doubled;
    }

    /// <summary><paramref name="rgba"/>, a top, at <see cref="Scale"/> times its size whether or not it has dither, for what has to be masked in those texels.</summary>
    public static byte[] Always(ReadOnlySpan<byte> rgba, int width, int height) => Doubled(rgba, width, height) ?? Plain(MemoryMarshal.Cast<byte, uint>(rgba), width, height);

    private static bool AnyDither(uint[] art, int width, int height)
    {
        var (seen, waiting) = (new bool[art.Length], new Stack<(int X, int Y)>());
        for (var i = 0; i < art.Length; i++)
        {
            if (seen[i] || !IsDither(art, width, height, i % width, i / width))
                continue;
            seen[i] = true;
            waiting.Push((i % width, i / width));
            var patch = 0;
            while (waiting.TryPop(out var at))
            {
                if (++patch >= LeastPatch)
                    return true;
                for (var dy = -1; dy <= 1; dy++)
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var (x, y) = ((at.X + dx + width) % width, (at.Y + dy + height) % height);
                        if (!seen[y * width + x] && IsDither(art, width, height, x, y))
                        {
                            seen[y * width + x] = true;
                            waiting.Push((x, y));
                        }
                    }
            }
        }
        return false;
    }

    /// <summary>The strip with each texel made four.</summary>
    private static byte[] Plain(ReadOnlySpan<uint> strip, int width, int height)
    {
        var doubled = new byte[strip.Length * sizeof(uint) * Scale * Scale];
        var texels = MemoryMarshal.Cast<byte, uint>(doubled.AsSpan());
        for (var y = 0; y < height * Scale; y++)
            for (var x = 0; x < width * Scale; x++)
                texels[y * width * Scale + x] = strip[y / Scale * width + x / Scale];
        return doubled;
    }

    /// <summary>Lays the pattern over the dither of one frame, which starts <paramref name="from"/> texels into the doubled strip's rows.</summary>
    private static void Pattern(uint[] art, int width, int height, Span<uint> texels, int wide, int from)
    {
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                if (!IsDither(art, width, height, x, y))
                    continue;
                var (own, beside) = (art[y * width + x], At(art, width, height, x + 1, y));
                var (even, odd) = (x + y & 1) == 0 ? (own, beside) : (beside, own);
                for (var j = 0; j < Scale; j++)
                    for (var i = 0; i < Scale; i++)
                    {
                        var (u, v) = (x * Scale + i, y * Scale + j);
                        texels[v * wide + from + u] = (Across(u, v) + Down(u, v) & 1) == 0 ? even : odd;
                    }
            }
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
