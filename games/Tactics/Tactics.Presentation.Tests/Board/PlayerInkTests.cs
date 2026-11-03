using Tactics.Game;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.Tests.Board;

/// <summary><see cref="PlayerInk"/>: a player's colour as the TV draws it, the fallbacks, and the ink an initial is written in on a pip.</summary>
public sealed class PlayerInkTests
{
    [Theory]
    [InlineData("#7ccbff", "#7ccbff")]
    [InlineData("#FFD23F", "#ffd23f")]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("7ccbff", null)]
    [InlineData("#7ccbf", null)]
    [InlineData("#7ccbffaa", null)]
    [InlineData("#7ccbfg", null)]
    [InlineData("teal", null)]
    public void A_colour_is_read_only_as_the_server_writes_it(string? hex, string? read)
        => Assert.Equal(read is null ? null : Rgb.Hex(read), PlayerInk.Of(hex));

    // Every colour the palette hands out is light, so an initial on it is dark, and reads as text should (WCAG's 4.5:1).
    [Fact]
    public void An_initial_on_any_palette_colour_is_dark_and_reads()
    {
        foreach (var colour in PlayerColors.Palette)
        {
            var fill = Rgb.Hex(colour.Hex);
            Assert.Equal(PlayerInk.Dark, PlayerInk.InkOn(fill));
            Assert.True(PlayerInk.Contrast(fill, PlayerInk.InkOn(fill)) >= 4.5, $"{colour.Name}: {PlayerInk.Contrast(fill, PlayerInk.Dark):0.0}:1");
        }
        Assert.Equal(PlayerInk.Dark, PlayerInk.InkOn(PlayerInk.PipFallback));
    }

    [Theory]
    [InlineData("#000000")]
    [InlineData("#1a1e3a")]      // the panels' navy
    [InlineData("#5a1010")]
    [InlineData("#2040a0")]
    public void An_initial_on_a_dark_colour_is_white(string hex) => Assert.Equal(PlayerInk.Light, PlayerInk.InkOn(Rgb.Hex(hex)));

    // A model test: over many colours, the ink chosen is never the one that reads worse. The better of the two is at worst about 4.4:1 (where
    // both are equal, at a luminance near 0.19), so an initial reads at the pip's bold size on any colour at all.
    [Fact]
    public void The_ink_is_always_the_better_of_black_and_white()
    {
        var random = new Random(14);
        for (var i = 0; i < 5000; i++)
        {
            var fill = new Rgb(random.Next(256) / 255f, random.Next(256) / 255f, random.Next(256) / 255f);
            var ink = PlayerInk.InkOn(fill);
            var other = ink == PlayerInk.Dark ? PlayerInk.Light : PlayerInk.Dark;
            Assert.True(PlayerInk.Contrast(fill, ink) >= PlayerInk.Contrast(fill, other), $"{fill}");
            Assert.True(PlayerInk.Contrast(fill, ink) >= 4.2, $"{fill}: {PlayerInk.Contrast(fill, ink):0.00}:1");
        }
    }

    // The TV's sums are the server's, so a colour the server made readable is judged the same on the TV.
    [Fact]
    public void Its_contrast_is_the_servers()
    {
        foreach (var colour in PlayerColors.Palette)
            foreach (var ground in PlayerColors.Grounds)
                Assert.Equal(PlayerColors.Contrast(colour.Hex, ground.Hex), PlayerInk.Contrast(Rgb.Hex(colour.Hex), Rgb.Hex(ground.Hex)), 3);
    }

    // Each palette colour reads against its ring's outline far beyond WCAG's 3:1 for graphics, so whatever ground is under it, it stands off.
    [Fact]
    public void Each_palette_colour_stands_well_off_the_rings_outline()
    {
        foreach (var colour in PlayerColors.Palette)
        {
            var contrast = PlayerInk.Contrast(Rgb.Hex(colour.Hex), UnitPlan.RingOutline);
            Assert.True(contrast >= 7, $"{colour.Name} is {contrast:0.0}:1");
        }
    }

    // The ring the review found lost in the grass was the party's one green. Every palette colour is brighter than any grass, stone or ash
    // in the pack, measured from the art itself, so it stays a light ring on darker ground.
    [Theory]
    [InlineData("grass_lush")]
    [InlineData("grass_green")]
    [InlineData("cobble_grey")]
    [InlineData("flagstone_grey")]
    [InlineData("ash_grey")]
    public void Each_palette_colour_is_brighter_than_the_ground(string top)
    {
        var ground = MeanOf(Path.Combine(Pack.Root, "terrain", "top", $"{top}.png"));
        foreach (var colour in PlayerColors.Palette)
        {
            var fill = Rgb.Hex(colour.Hex);
            Assert.True(PlayerInk.Luminance(fill) > PlayerInk.Luminance(ground) && PlayerInk.Contrast(fill, ground) >= 1.35,
                $"{colour.Name} on {top}: {PlayerInk.Contrast(fill, ground):0.00}:1");
        }
    }

    private static Rgb MeanOf(string png)
    {
        var (rgba, _, _) = PngTexels.Read(png);
        var (r, g, b, n) = (0.0, 0.0, 0.0, 0);
        for (var i = 0; i + 3 < rgba.Length; i += 4)
        {
            if (rgba[i + 3] < 200)
                continue;
            (r, g, b, n) = (r + rgba[i], g + rgba[i + 1], b + rgba[i + 2], n + 1);
        }
        return new Rgb((float)(r / n / 255), (float)(g / n / 255), (float)(b / n / 255));
    }
}
