using Tactics.Presentation.Board;

namespace Tactics.Presentation.Tests.Board;

/// <summary><see cref="DitherTexels"/>: a top's checker dither stays a checker when the top is laid on a tile, and nothing else moves.</summary>
public sealed class DitherTexelsTests
{
    private const int Size = 32;
    private const uint Light = 0xFFC0C0C0;
    private const uint Dark = 0xFF404040;
    private const uint Dot = 0xFF2020E0;

    private static byte[] Bytes(uint[] texels)
    {
        var bytes = new byte[texels.Length * 4];
        Buffer.BlockCopy(texels, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static uint[] Texels(byte[] bytes)
    {
        var texels = new uint[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, texels, 0, bytes.Length);
        return texels;
    }

    private static uint[] Painted(Func<int, int, uint> at)
    {
        var texels = new uint[Size * Size];
        for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
                texels[y * Size + x] = at(x, y);
        return texels;
    }

    private static uint Checker(int x, int y) => ((x + y) & 1) == 0 ? Light : Dark;

    /// <summary>What the screen shows at a pixel of a board at <paramref name="scale"/>, sampled as the GPU samples a top's texture: U and V from the 2:1 projection, nearest.</summary>
    private static uint Screen(uint[] texels, int size, int scalePerTexel, int scale, int x, int y)
    {
        var (artX, artY) = ((x + 0.5) / scale, (y + 0.5) / scale);
        var (u, v) = ((artY + artX / 2) * scalePerTexel, (artY - artX / 2) * scalePerTexel);
        return texels[Wrap((int)Math.Floor(v), size) * size + Wrap((int)Math.Floor(u), size)];
    }

    private static int Wrap(int value, int size) => (value % size + size) % size;

    [Fact]
    public void Art_with_no_dither_is_left_alone()
    {
        // Lines of one texel, along the axes and the diagonals, and a lone dot: what a stone's outline or an ember is made of.
        var art = Painted((x, y) => (x == 5 && y < 8) || (y == 28 && x > 16) || (x == y && x is >= 12 and < 20) || (x + y == 30 && x >= 22) ? Dark : x == 25 && y == 25 ? Dot : Light);

        Assert.Null(DitherTexels.Doubled(Bytes(art), Size, Size));
    }

    [Fact]
    public void A_checker_laid_on_a_tile_doesnt_turn_into_stripes()
    {
        var art = Painted(Checker);
        Assert.True(Enumerable.Range(0, 40).All(x =>                         // what the bug is: down each column of the screen, the colour never changes
            Enumerable.Range(0, 40).All(y => Screen(art, Size, 1, 2, x, y) == Screen(art, Size, 1, 2, x, 0))));

        var doubled = Texels(DitherTexels.Doubled(Bytes(art), Size, Size)!);

        for (var y = 0; y < 40; y += 2)
            for (var x = 0; x < 40; x += 2)
            {
                var here = Screen(doubled, Size * 2, 2, 2, x + 1, y);
                Assert.Equal(here, Screen(doubled, Size * 2, 2, 2, x + 1, y + 1));   // cells two pixels tall
                Assert.Equal(here, Screen(doubled, Size * 2, 2, 2, x + 2, y));       // and two pixels wide
                Assert.NotEqual(here, Screen(doubled, Size * 2, 2, 2, x + 1, y + 2));
                Assert.NotEqual(here, Screen(doubled, Size * 2, 2, 2, x + 3, y));
            }
    }

    [Fact]
    public void A_dither_shows_half_of_each_colour_as_the_art_has_it()
    {
        var doubled = Texels(DitherTexels.Doubled(Bytes(Painted(Checker)), Size, Size)!);

        Assert.Equal(doubled.Length / 2, doubled.Count(t => t == Light));
        Assert.Equal(doubled.Length / 2, doubled.Count(t => t == Dark));
    }

    [Fact]
    public void What_isnt_dither_samples_as_it_did_at_any_scale()
    {
        // A patch of dither in a field with a line through it: everything off the patch must land where it landed.
        var art = Painted((x, y) => x is >= 8 and < 16 && y is >= 8 and < 16 ? Checker(x, y) : y == 24 || x == 28 ? Dark : Light);
        var doubled = Texels(DitherTexels.Doubled(Bytes(art), Size, Size)!);

        foreach (var scale in new[] { 1, 2, 3, 4 })
            for (var y = 0; y < 200; y++)
                for (var x = 0; x < 200; x++)
                {
                    var was = Screen(art, Size, 1, scale, x, y);
                    var (u, v) = (((y + 0.5) / scale + (x + 0.5) / scale / 2), ((y + 0.5) / scale - (x + 0.5) / scale / 2));
                    if (Wrap((int)Math.Floor(u), Size) is < 7 or > 16 || Wrap((int)Math.Floor(v), Size) is < 7 or > 16)
                        Assert.Equal(was, Screen(doubled, Size * 2, 2, scale, x, y));
                }
    }

    [Fact]
    public void A_dither_runs_on_across_the_textures_edge()
    {
        // The art tiles, so its neighbours wrap, and the pattern must too, or tiles would show a seam.
        var doubled = Texels(DitherTexels.Doubled(Bytes(Painted(Checker)), Size, Size)!);

        for (var y = 0; y < 64; y++)
            for (var x = 0; x < 64; x++)
                Assert.Equal(Screen(doubled, Size * 2, 2, 2, x, y), Screen(doubled, Size * 2, 2, 2, x + Size * 2, y + Size));   // one texture on along U
    }

    [Fact]
    public void A_dot_with_a_friend_on_one_corner_isnt_a_dither()
    {
        var art = Painted((x, y) => (x, y) is (10, 10) or (11, 11) ? Dot : Light);

        Assert.Null(DitherTexels.Doubled(Bytes(art), Size, Size));
    }
}
