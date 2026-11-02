using System.Collections.Immutable;
using System.Globalization;
using ArcBox.Sdk.Campaigns;

namespace Tactics.Game;

/// <summary>A colour on offer: its hex and the name a screen reader says for it.</summary>
public sealed record PaletteColor(string Hex, string Name);

/// <summary>A surface a player's colour is drawn on (a ground under a walker, or the panels the vote dots sit on), by its average colour.</summary>
public sealed record GroundColor(string Name, string Hex);

/// <summary>
/// Player colours (the user's answer 14, 2026-11-01): each player picks a colour at character creation, for their ring on the TV, their
/// vote dots and their phone. A colour has to stand out where it's drawn, so every colour is checked against grass, stone and ash (the
/// ring) and the navy panels (the dots): one that doesn't reach <see cref="MinContrast"/> on all four is lightened until it does
/// (<see cref="Readable"/>). Lightening, never darkening, because the panels are dark and the ring has a dark outline. A member
/// who never picked (a character from before colours, or one made at Muster) is given one once (<see cref="Assign"/>): the first palette
/// colour from their place in join order that nobody has, which is then kept with them, so it never shifts when someone joins or leaves.
/// </summary>
public static class PlayerColors
{
    /// <summary>
    /// The palette, in the order defaults are handed out. Each reaches <see cref="MinContrast"/> on every ground, and their names are
    /// distinct for a screen reader.
    /// </summary>
    public static ImmutableArray<PaletteColor> Palette { get; } =
    [
        new("#ffd23f", "Gold"), new("#7ccbff", "Sky"), new("#ffab94", "Coral"), new("#8ff0c4", "Mint"),
        new("#d2b4ff", "Lilac"), new("#f4f1ea", "Bone"), new("#ffa8dd", "Pink"), new("#b9f26b", "Lime"),
    ];

    /// <summary>
    /// The surfaces a colour must show on: the average colour of the pack's <c>terrain/top/grass_green.png</c>, <c>flagstone_grey.png</c>
    /// and <c>ash_grey.png</c>, measured when the palette was chosen (2026-11-01), under the ring; and the top of the Wardenflame glass
    /// panels (<c>--wf-glass-top</c> in tactics.css), under the vote dots.
    /// </summary>
    public static ImmutableArray<GroundColor> Grounds { get; } =
        [new("grass", "#59983f"), new("stone", "#817e8b"), new("ash", "#6c6166"), new("panel", "#1a1e3a")];

    /// <summary>
    /// The least contrast ratio (WCAG's, 1 to 21) a colour needs against every ground. The grounds are mid-tones, so even white only
    /// reaches 3.5 on grass; the TV draws the ring with a dark outline, which carries the rest.
    /// </summary>
    public const double MinContrast = 1.8;

    /// <summary>The palette's colour for the <paramref name="joinIndex"/>th member to join (from 0), round again after the last.</summary>
    public static string Default(int joinIndex) => Palette[((joinIndex % Palette.Length) + Palette.Length) % Palette.Length].Hex;

    /// <summary>
    /// The default for the <paramref name="joinIndex"/>th member: the first palette colour from <see cref="Default"/>(joinIndex) on, round
    /// the palette, that isn't <paramref name="taken"/>; with every one taken, <see cref="Default"/>(joinIndex).
    /// </summary>
    public static string FreeDefault(int joinIndex, IEnumerable<string?> taken)
    {
        var used = taken.Where(t => t is not null).ToHashSet(StringComparer.Ordinal);
        for (var k = 0; k < Palette.Length; k++)
            if (!used.Contains(Default(joinIndex + k)))
                return Default(joinIndex + k);
        return Default(joinIndex);
    }

    /// <summary>
    /// Every member with a colour: one who has a readable colour keeps it; each without, taken in <paramref name="joinOrder"/> (then any
    /// member it doesn't name, in document order), gets <see cref="FreeDefault"/> for their place. Members are returned unchanged when
    /// all have one, so a caller can tell whether there's anything to save.
    /// </summary>
    public static ImmutableSortedDictionary<string, MemberRecord> Assign(ImmutableSortedDictionary<string, MemberRecord> members,
        IEnumerable<string> joinOrder)
    {
        var order = joinOrder.Where(members.ContainsKey).Distinct().ToList();
        order.AddRange(members.Keys.Where(k => !order.Contains(k)));
        var result = members;
        for (var i = 0; i < order.Count; i++)
        {
            var record = result[order[i]];
            if (Parse(record.Color) is not null)
                continue;
            result = result.SetItem(order[i], record with { Color = FreeDefault(i, result.Values.Select(r => Parse(r.Color))) });
        }
        return result;
    }

    /// <summary>A campaign's join order: its members as <paramref name="info"/> lists them.</summary>
    public static IEnumerable<string> JoinOrder(CampaignInfo? info) => (info?.Members ?? []).Select(m => m.Profile.Value);

    /// <summary>"#RRGGBB" or "#rrggbb" as lower-case "#rrggbb"; null for anything else.</summary>
    public static string? Parse(string? hex)
    {
        if (hex is not { Length: 7 } || hex[0] != '#')
            return null;
        for (var i = 1; i < 7; i++)
            if (!char.IsAsciiHexDigit(hex[i]))
                return null;
        return hex.ToLowerInvariant();
    }

    /// <summary>WCAG's relative luminance of a "#rrggbb", 0 (black) to 1 (white).</summary>
    public static double Luminance(string hex)
    {
        var (r, g, b) = Rgb(hex);
        return 0.2126 * Linear(r) + 0.7152 * Linear(g) + 0.0722 * Linear(b);
    }

    /// <summary>WCAG's contrast ratio of two colours: 1 (the same) to 21 (black on white).</summary>
    public static double Contrast(string a, string b)
    {
        var (x, y) = (Luminance(a), Luminance(b));
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }

    /// <summary>The least contrast of <paramref name="hex"/> against any of the <see cref="Grounds"/>.</summary>
    public static double WorstContrast(string hex) => Grounds.Min(g => Contrast(hex, g.Hex));

    /// <summary>
    /// <paramref name="hex"/> made readable on every ground: unchanged when it already reaches <see cref="MinContrast"/>, else its HSL
    /// lightness raised one percent at a time (hue and saturation kept) until it does. White always does, so this ends. Null for a
    /// colour <see cref="Parse"/> refuses.
    /// </summary>
    public static string? Readable(string? hex)
    {
        if (Parse(hex) is not { } colour)
            return null;
        if (WorstContrast(colour) >= MinContrast)
            return colour;
        var (h, s, l) = Hsl(colour);
        for (var step = (int)Math.Floor(l * 100) + 1; step <= 100; step++)
        {
            var tried = FromHsl(h, s, step / 100.0);
            if (WorstContrast(tried) >= MinContrast)
                return tried;
        }
        return "#ffffff";
    }

    private static (int R, int G, int B) Rgb(string hex)
        => (int.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            int.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            int.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));

    private static double Linear(int channel)
    {
        var c = channel / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    /// <summary>Hue in degrees, saturation and lightness 0 to 1.</summary>
    private static (double H, double S, double L) Hsl(string hex)
    {
        var (r8, g8, b8) = Rgb(hex);
        double r = r8 / 255.0, g = g8 / 255.0, b = b8 / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        if (max == min)
            return (0, 0, l);
        var d = max - min;
        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        var h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (h * 60, s, l);
    }

    private static string FromHsl(double h, double s, double l)
    {
        var c = (1 - Math.Abs(2 * l - 1)) * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = l - c / 2;
        var (r, g, b) = h switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        static int Byte(double v) => (int)Math.Clamp(Math.Round(v * 255), 0, 255);
        return $"#{Byte(r + m):x2}{Byte(g + m):x2}{Byte(b + m):x2}";
    }
}
