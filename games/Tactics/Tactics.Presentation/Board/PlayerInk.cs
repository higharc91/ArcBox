using Tactics.Game;

namespace Tactics.Presentation.Board;

/// <summary>
/// How the TV draws a player's own colour (the user's answer 14, 2026-11-01; <see cref="PlayerColors"/>): their walker's ring and their hero's
/// in a fight, their pips on the vote panel and the edge of their card at a skill check. The server sends each colour already made readable
/// on grass, stone, ash and the panels, so this only reads it and says what goes with it: the fallback when there's none, and the ink an
/// initial is written in on a pip.
/// </summary>
public static class PlayerInk
{
    /// <summary>The dark an initial is written in on a light pip: the ring's outline.</summary>
    public static readonly Rgb Dark = UnitPlan.RingOutline;

    /// <summary>The light an initial is written in on a dark pip.</summary>
    public static readonly Rgb Light = Rgb.Hex("#ffffff");

    /// <summary>A voter's pip with no colour: the panel's gold (HudLayer.Gold), as every pip was before players had colours.</summary>
    public static readonly Rgb PipFallback = Rgb.Hex("#e6bd5c");

    /// <summary>A colour as the server sends it, "#rrggbb"; null for none, or anything that isn't one (<see cref="PlayerColors.Parse"/>).</summary>
    public static Rgb? Of(string? hex) => PlayerColors.Parse(hex) is { } colour ? Rgb.Hex(colour) : null;

    /// <summary>
    /// The ink for an initial on <paramref name="fill"/>: <see cref="Dark"/> or <see cref="Light"/>, whichever stands further off it by
    /// WCAG's contrast, dark on a tie. Every palette colour is light, so its initials are dark; a dark colour still gets white.
    /// </summary>
    public static Rgb InkOn(Rgb fill) => Contrast(fill, Dark) >= Contrast(fill, Light) ? Dark : Light;

    /// <summary>WCAG's relative luminance, 0 (black) to 1 (white).</summary>
    public static double Luminance(Rgb c) => 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);

    /// <summary>WCAG's contrast ratio of two colours, 1 to 21.</summary>
    public static double Contrast(Rgb a, Rgb b)
    {
        var (x, y) = (Luminance(a), Luminance(b));
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }

    private static double Linear(float c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
}
