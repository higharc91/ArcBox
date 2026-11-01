using System.Text.RegularExpressions;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.Tests.Board;

/// <summary><see cref="SeatColours"/>: one colour per player, the same on the phone and the TV, and readable on the ground.</summary>
public sealed partial class SeatColoursTests
{
    // The phone's lobby chips are where players first see their colour, so the TV must say the same.
    [Fact]
    public void They_are_the_phones_player_colours_in_the_same_order()
    {
        var css = File.ReadAllText(Path.Combine(Pack.Root, "..", "..", "..", "web", "play", "src", "games", "tactics", "lobby.css"));
        var first = FirstChip().Match(css).Groups[1].Value;
        var rest = NthChip().Matches(css).OrderBy(m => int.Parse(m.Groups[1].Value)).Select(m => m.Groups[2].Value);

        Assert.Equal([first, .. rest], SeatColours.Hex);
    }

    [Fact]
    public void They_go_round_again_past_the_last_and_never_throw()
    {
        var count = SeatColours.Hex.Length;
        Assert.Equal(SeatColours.Of(0), SeatColours.Of(count));
        Assert.Equal(SeatColours.Of(count - 1), SeatColours.Of(-1));
        Assert.Equal(count, Enumerable.Range(0, count).Select(SeatColours.Of).Distinct().Count());
    }

    // Each reads against its outline far beyond WCAG's 3:1 for graphics, so whatever ground is under the ring, the ring stands off it.
    [Fact]
    public void Each_stands_well_off_its_outline()
    {
        foreach (var hex in SeatColours.Hex)
            Assert.True(Contrast(Rgb.Hex(hex), SeatColours.Outline) >= 7, $"{hex} is {Contrast(Rgb.Hex(hex), SeatColours.Outline):0.0}:1");
    }

    // The ring the review found lost in the grass was the party's one green. A seat's colour is brighter than any grass, stone or ash in the
    // pack, by luminance, so it stays a light ring on darker ground.
    [Theory]
    [InlineData("grass_lush")]
    [InlineData("grass_green")]
    [InlineData("cobble_grey")]
    [InlineData("flagstone_grey")]
    [InlineData("ash_grey")]
    public void Each_is_brighter_than_the_ground(string top)
    {
        var ground = MeanOf(Path.Combine(Pack.Root, "terrain", "top", $"{top}.png"));
        foreach (var hex in SeatColours.Hex)
            Assert.True(Luminance(Rgb.Hex(hex)) > Luminance(ground) && Contrast(Rgb.Hex(hex), ground) >= 1.35,$"{hex} on {top}: {Contrast(Rgb.Hex(hex), ground):0.00}:1");
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

    private static double Contrast(Rgb a, Rgb b)
    {
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(Rgb c) => 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);

    private static double Linear(float c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

    [GeneratedRegex(@"\.wf-lobby \.player \{[^}]*--p:\s*(#[0-9a-f]{6})")]
    private static partial Regex FirstChip();

    [GeneratedRegex(@"\.wf-lobby \.player:nth-child\(6n \+ (\d)\) \{ --p: (#[0-9a-f]{6}); \}")]
    private static partial Regex NthChip();
}
