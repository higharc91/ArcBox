using ArcBox.Host.Shell;
using Godot;
using Redline.Presentation.Hud;

namespace ArcBox.Host.Games.Redline.Hud;

/// <summary>
/// REDLINE broadcast HUD constants, copied from the mockups' <c>broadcast.css</c> / <c>hud3d.css</c> (1280×720 there,
/// ×1.5 here for the 1920×1080 base the window scales from), plus small drawing helpers shared by the HUD pieces.
/// </summary>
public static class HudStyle
{
    // broadcast.css :root
    public static readonly Color Ink = Colors.White;
    public static readonly Color Dim = new("9a9aa8");
    public static readonly Color Glass = new(21 / 255f, 21 / 255f, 30 / 255f, 0.86f);
    public static readonly Color Edge = new(1, 1, 1, 0.09f);
    public static readonly Color Red = new("e10600");
    public static readonly Color Good = new("1ed760");
    public static readonly Color GoodInk = new("04220f");
    public static readonly Color Bad = new("ff3b3b");
    /// <summary>Qualifying's fastest lap (qualifying spec §6), the F1 timing-screen purple.</summary>
    public static readonly Color Purple = new("a020f0");

    /// <summary>hud3d.css: panels over the 3D view are a touch more transparent (rgba(14,14,20,.78–.82)).</summary>
    public static readonly Color Over3D = new(14 / 255f, 14 / 255f, 20 / 255f, 0.82f);
    /// <summary>Player names in the tower and on tags (plan Task 6).</summary>
    public static readonly Color Player = new("ffd400");
    public static readonly Color Dark = new("111111");
    /// <summary>AI rows in the tower are drawn at 55 % opacity.</summary>
    public const float AiAlpha = 0.55f;

    // Layout of the 1920×1080 base (hud3d.css .hud padding 24/28 px × 1.5).
    public const float MarginX = 42;
    public const float MarginY = 36;
    public const float Gap = 18;
    public const float HeaderHeight = 100;
    public const float BottomBarHeight = 78;

    public static Font Regular => ArcTheme.Regular;
    public static Font SemiBold => ArcTheme.SemiBold;
    public static Font Bold => ArcTheme.Bold;
    public static Font Black => ArcTheme.Black;
    public static Font BlackItalic => ArcTheme.BlackItalic;

    public static Color Hex(string hex) => new(hex);

    public static Color Faded(Color c, float alpha) => new(c, c.A * alpha);

    /// <summary>Background and ink of a chip (broadcast.css <c>.chip.*</c>, plus qualifying's purple); Wait and Bad also draw an outline.</summary>
    public static (Color Bg, Color Ink, Color? Outline) Chip(ChipKind kind) => kind switch
    {
        ChipKind.Cruise => (new Color(1, 1, 1, 0.1f), Dim, null),
        ChipKind.Push => (Colors.White, Dark, null),
        ChipKind.Redline => (Red, Colors.White, null),
        ChipKind.Wait => (Colors.Transparent, Dim, new Color(1, 1, 1, 0.3f)),
        ChipKind.Locked => (new Color(1, 1, 1, 0.1f), Colors.White, null),
        ChipKind.Pit => (Colors.White, Dark, null),
        ChipKind.Good => (Good, new Color("006622"), null),
        ChipKind.Bad => (new Color(1, 59 / 255f, 59 / 255f, 0.18f), Bad, Bad),
        ChipKind.Purple => (Purple, Colors.White, null),
        _ => (Bad, Colors.White, null),
    };

    /// <summary>Draws a chip centred in <paramref name="rect"/>, text in Black with letter spacing.</summary>
    public static void DrawChip(CanvasItem ci, Rect2 rect, Chip chip, int fontSize, float alpha = 1)
    {
        var (bg, ink, outline) = Chip(chip.Kind);
        if (bg.A > 0)
        {
            ci.DrawRect(rect, Faded(bg, alpha));
        }
        if (outline is { } o)
        {
            if (chip.Kind == ChipKind.Wait)
            {
                DrawDashedRect(ci, rect, Faded(o, alpha));
            }
            else
            {
                ci.DrawRect(rect, Faded(o, alpha), false, 1.5f);
            }
        }
        var font = ArcTheme.Spaced("Black", 2);
        var text = Fit(chip.Text, font, fontSize, rect.Size.X - 6);
        DrawCentered(ci, font, text, rect, fontSize, Faded(ink, alpha));
    }

    public static void DrawDashedRect(CanvasItem ci, Rect2 r, Color c)
    {
        const float dash = 4, gap = 3;
        for (var x = r.Position.X; x < r.End.X; x += dash + gap)
        {
            var x2 = Mathf.Min(x + dash, r.End.X);
            ci.DrawLine(new Vector2(x, r.Position.Y + 0.5f), new Vector2(x2, r.Position.Y + 0.5f), c, 1);
            ci.DrawLine(new Vector2(x, r.End.Y - 0.5f), new Vector2(x2, r.End.Y - 0.5f), c, 1);
        }
        for (var y = r.Position.Y; y < r.End.Y; y += dash + gap)
        {
            var y2 = Mathf.Min(y + dash, r.End.Y);
            ci.DrawLine(new Vector2(r.Position.X + 0.5f, y), new Vector2(r.Position.X + 0.5f, y2), c, 1);
            ci.DrawLine(new Vector2(r.End.X - 0.5f, y), new Vector2(r.End.X - 0.5f, y2), c, 1);
        }
    }

    /// <summary>Text width at <paramref name="size"/> px.</summary>
    public static float Width(Font font, string text, int size) =>
        font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;

    /// <summary><paramref name="text"/> trimmed with an ellipsis until it fits <paramref name="maxWidth"/>.</summary>
    public static string Fit(string text, Font font, int size, float maxWidth)
    {
        if (Width(font, text, size) <= maxWidth || text.Length == 0)
        {
            return text;
        }
        for (var n = text.Length - 1; n > 0; n--)
        {
            var t = text[..n].TrimEnd() + "…";
            if (Width(font, t, size) <= maxWidth)
            {
                return t;
            }
        }
        return "…";
    }

    /// <summary>Greedy word wrap into two lines; the second is trimmed with an ellipsis if the text is longer still.</summary>
    public static (string First, string Second) TwoLines(string text, Font font, int size, float maxWidth)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var first = "";
        var k = 0;
        for (; k < words.Length; k++)
        {
            var candidate = first.Length == 0 ? words[k] : $"{first} {words[k]}";
            if (Width(font, candidate, size) > maxWidth && first.Length > 0)
            {
                break;
            }
            first = candidate;
        }
        var second = string.Join(' ', words.Skip(k));
        return (Fit(first, font, size, maxWidth), Fit(second, font, size, maxWidth));
    }

    /// <summary>The largest size from <paramref name="max"/> down to <paramref name="min"/> at which the text fits.</summary>
    public static int FitSize(string text, Font font, int max, int min, float maxWidth)
    {
        for (var s = max; s > min; s--)
        {
            if (Width(font, text, s) <= maxWidth)
            {
                return s;
            }
        }
        return min;
    }

    /// <summary>Baseline y that centres a line of <paramref name="font"/> at <paramref name="size"/> on <paramref name="centreY"/>.</summary>
    public static float Baseline(Font font, int size, float centreY) =>
        centreY + ((font.GetAscent(size) - font.GetDescent(size)) / 2) - 1;

    public static void DrawLeft(CanvasItem ci, Font font, string text, float x, float centreY, int size, Color colour) =>
        ci.DrawString(font, new Vector2(x, Baseline(font, size, centreY)), text, HorizontalAlignment.Left, -1, size, colour);

    public static void DrawRight(CanvasItem ci, Font font, string text, float right, float centreY, int size, Color colour) =>
        ci.DrawString(font, new Vector2(right - Width(font, text, size), Baseline(font, size, centreY)), text,
            HorizontalAlignment.Left, -1, size, colour);

    public static void DrawCentered(CanvasItem ci, Font font, string text, Rect2 rect, int size, Color colour)
    {
        var w = Width(font, text, size);
        ci.DrawString(font, new Vector2(rect.Position.X + ((rect.Size.X - w) / 2), Baseline(font, size, rect.GetCenter().Y)), text,
            HorizontalAlignment.Left, -1, size, colour);
    }

    /// <summary>A small solid triangle (▲ when <paramref name="up"/>, ▼ otherwise) centred on <paramref name="c"/>.</summary>
    public static void DrawTriangle(CanvasItem ci, Vector2 c, float size, bool up, Color colour)
    {
        var h = size * 0.86f;
        Vector2[] pts = up
            ? [new(c.X, c.Y - (h / 2)), new(c.X + (size / 2), c.Y + (h / 2)), new(c.X - (size / 2), c.Y + (h / 2))]
            : [new(c.X - (size / 2), c.Y - (h / 2)), new(c.X + (size / 2), c.Y - (h / 2)), new(c.X, c.Y + (h / 2))];
        ci.DrawColoredPolygon(pts, colour);
    }

    /// <summary>A tick mark in a <paramref name="size"/> box centred on <paramref name="c"/>.</summary>
    public static void DrawCheck(CanvasItem ci, Vector2 c, float size, Color colour, float width = 2.5f)
    {
        var s = size / 2;
        ci.DrawPolyline([new(c.X - s, c.Y), new(c.X - (s * 0.25f), c.Y + (s * 0.7f)), new(c.X + s, c.Y - (s * 0.75f))], colour, width, true);
    }

    /// <summary>A right-pointing arrow from <paramref name="from"/> spanning <paramref name="length"/> px.</summary>
    public static void DrawArrow(CanvasItem ci, Vector2 from, float length, Color colour, float width = 4)
    {
        var tip = from + new Vector2(length, 0);
        var head = Mathf.Min(14, length * 0.45f);
        ci.DrawLine(from, tip - new Vector2(width * 0.6f, 0), colour, width);
        ci.DrawColoredPolygon([tip, tip + new Vector2(-head, -head * 0.6f), tip + new Vector2(-head, head * 0.6f)], colour);
    }

    /// <summary>A panel style: glass with the 1 px edge, optionally a coloured left bar.</summary>
    public static StyleBoxFlat Panel(Color? bg = null, Color? leftAccent = null, int accentWidth = 4, int padH = 0, int padV = 0)
    {
        var sb = new StyleBoxFlat
        {
            BgColor = bg ?? Over3D,
            BorderColor = Edge,
            ContentMarginLeft = padH,
            ContentMarginRight = padH,
            ContentMarginTop = padV,
            ContentMarginBottom = padV,
        };
        sb.SetBorderWidthAll(1);
        if (leftAccent is { } a)
        {
            sb.BorderWidthLeft = accentWidth;
            sb.BorderWidthTop = sb.BorderWidthRight = sb.BorderWidthBottom = 0;
            sb.BorderColor = a;
            sb.ContentMarginLeft = padH + accentWidth;
        }
        return sb;
    }

    /// <summary>Draws the glass panel background with its 1 px edge.</summary>
    public static void DrawPanel(CanvasItem ci, Rect2 r, Color? bg = null)
    {
        ci.DrawRect(r, bg ?? Over3D);
        ci.DrawRect(r, Edge, false, 1);
    }
}
