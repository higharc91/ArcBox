using Godot;

namespace ArcBox.Host.Shell;

/// <summary>
/// The broadcast look (docs/superpowers/mockups/trivia-racer/broadcast.css): Titillium Web, glass panels,
/// thin edges and the red brand slab. Everything is built in code so screens stay plain C#.
/// </summary>
public static class ArcTheme
{
    public static readonly Color Ink = Colors.White;
    public static readonly Color Dim = new("9a9aa8");
    public static readonly Color Glass = new(21 / 255f, 21 / 255f, 30 / 255f, 0.86f);
    public static readonly Color GlassSolid = new("15151e");
    public static readonly Color Edge = new(1, 1, 1, 0.09f);
    public static readonly Color Red = new("e10600");
    public static readonly Color Good = new("1ed760");
    public static readonly Color Bad = new("ff3b3b");
    public static readonly Color Gold = new("ffd400");
    public static readonly Color BgTop = new("1c1c26");
    public static readonly Color BgBottom = new("0b0b10");

    /// <summary>Player bar colours, in join order.</summary>
    public static readonly Color[] PlayerColours =
    [
        new("ff8000"), new("27f4d2"), new("3671c6"), new("e8002d"),
        new("52e252"), new("ffd400"), new("b6babd"), new("ff87bc"),
    ];

    private static readonly Dictionary<(string, int), FontVariation> Cache = new();
    private static Godot.Theme? _theme;

    public static Font Regular => Variant("Regular", 0);
    public static Font SemiBold => Variant("SemiBold", 0);
    public static Font Bold => Variant("Bold", 0);
    public static Font Black => Variant("Black", 0);

    /// <summary>The brand face: Black, slanted (Titillium ships no Black Italic).</summary>
    public static Font BlackItalic => Italic("Black");

    /// <summary>A weight with extra letter spacing in pixels (the mockups' small caps labels use 2–3 px).</summary>
    public static Font Spaced(string weight, int spacing) => Variant(weight, spacing);

    /// <summary>
    /// Display numerals and codes: a weight with glyph spacing and the line box trimmed by
    /// <paramref name="trimTop"/>/<paramref name="trimBottom"/> pixels so huge type doesn't carry huge empty margins.
    /// </summary>
    public static Font Display(string weight, int spacing, int trimTop, int trimBottom)
    {
        var key = ($"{weight}-display-{trimTop}-{trimBottom}", spacing);
        if (!Cache.TryGetValue(key, out var v))
        {
            v = new FontVariation { BaseFont = File(weight), SpacingGlyph = spacing, SpacingTop = -trimTop, SpacingBottom = -trimBottom };
            Cache[key] = v;
        }
        return v;
    }

    public static Godot.Theme Theme => _theme ??= BuildTheme();

    public static Color PlayerColour(int index) => PlayerColours[((index % PlayerColours.Length) + PlayerColours.Length) % PlayerColours.Length];

    public static StyleBoxFlat GlassBox(Color? leftAccent = null, int padH = 20, int padV = 12)
    {
        var sb = new StyleBoxFlat
        {
            BgColor = Glass,
            BorderColor = Edge,
            ContentMarginLeft = padH,
            ContentMarginRight = padH,
            ContentMarginTop = padV,
            ContentMarginBottom = padV,
        };
        sb.SetBorderWidthAll(1);
        if (leftAccent is { } accent)
        {
            // StyleBoxFlat has one border colour, so an accented panel has only its 4 px left edge.
            sb.BorderWidthLeft = 4;
            sb.BorderColor = accent;
            sb.BorderWidthTop = 0;
            sb.BorderWidthRight = 0;
            sb.BorderWidthBottom = 0;
        }
        return sb;
    }

    public static StyleBoxFlat Flat(Color bg, int pad = 0, Color? border = null, int borderWidth = 0)
    {
        var sb = new StyleBoxFlat
        {
            BgColor = bg,
            ContentMarginLeft = pad,
            ContentMarginRight = pad,
            ContentMarginTop = pad,
            ContentMarginBottom = pad,
        };
        if (border is { } b)
        {
            sb.BorderColor = b;
            sb.SetBorderWidthAll(borderWidth);
        }
        return sb;
    }

    public static Label Label(string text, Font font, int size, Color? colour = null)
    {
        var label = new Label { Text = text };
        Style(label, font, size, colour);
        return label;
    }

    public static void Style(Label label, Font font, int size, Color? colour = null)
    {
        label.AddThemeFontOverride("font", font);
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", colour ?? Ink);
    }

    /// <summary>Small upper-case meta label ("ROOM CODE"), like <c>.label</c> / <c>.th</c> in the mockups.</summary>
    public static Label Meta(string text, int size = 18, Color? colour = null) =>
        Label(text.ToUpperInvariant(), Spaced("Bold", 3), size, colour ?? Dim);

    public static PanelContainer Panel(StyleBox box, Control? child = null)
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", box);
        if (child is not null)
        {
            panel.AddChild(child);
        }
        return panel;
    }

    public static VBoxContainer VBox(int separation, params Control[] children)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", separation);
        foreach (var c in children)
        {
            box.AddChild(c);
        }
        return box;
    }

    public static HBoxContainer HBox(int separation, params Control[] children)
    {
        var box = new HBoxContainer();
        box.AddThemeConstantOverride("separation", separation);
        foreach (var c in children)
        {
            box.AddChild(c);
        }
        return box;
    }

    public static Control Spacer(bool expandH = true, bool expandV = false)
    {
        var c = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        if (expandH)
        {
            c.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        }
        if (expandV)
        {
            c.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        }
        return c;
    }

    public static MarginContainer Margin(int left, int top, int right, int bottom, Control? child = null)
    {
        var m = new MarginContainer();
        m.AddThemeConstantOverride("margin_left", left);
        m.AddThemeConstantOverride("margin_top", top);
        m.AddThemeConstantOverride("margin_right", right);
        m.AddThemeConstantOverride("margin_bottom", bottom);
        if (child is not null)
        {
            m.AddChild(child);
        }
        return m;
    }

    private static FontFile File(string weight) => GD.Load<FontFile>($"res://fonts/TitilliumWeb-{weight}.ttf");

    private static FontVariation Variant(string weight, int spacing)
    {
        if (!Cache.TryGetValue((weight, spacing), out var v))
        {
            v = new FontVariation { BaseFont = File(weight), SpacingGlyph = spacing };
            Cache[(weight, spacing)] = v;
        }
        return v;
    }

    private static FontVariation Italic(string weight)
    {
        if (!Cache.TryGetValue((weight + "-italic", 0), out var v))
        {
            v = new FontVariation
            {
                BaseFont = File(weight),
                // Slant like the FontVariation docs' Transform2D(1, 0.2, 0, 1, 0, 0): the text server reads it
                // transposed with glyph y up, so this leans the tops right (checked in screenshots).
                VariationTransform = new Transform2D(new Vector2(1, 0.2f), new Vector2(0, 1), Vector2.Zero),
            };
            Cache[(weight + "-italic", 0)] = v;
        }
        return v;
    }

    private static Godot.Theme BuildTheme()
    {
        var t = new Godot.Theme { DefaultFont = SemiBold, DefaultFontSize = 26 };

        t.SetColor("font_color", "Label", Ink);

        var normal = Flat(new Color(1, 1, 1, 0.06f), 0, Edge, 1);
        var hover = Flat(new Color(1, 1, 1, 0.12f), 0, new Color(1, 1, 1, 0.25f), 1);
        var pressed = Flat(Red, 0, Red, 1);
        var disabled = Flat(new Color(1, 1, 1, 0.03f), 0, Edge, 1);
        var focus = Flat(Colors.Transparent, 0, Red, 2);
        foreach (var sb in new[] { normal, hover, pressed, disabled, focus })
        {
            sb.ContentMarginLeft = sb.ContentMarginRight = 22;
            sb.ContentMarginTop = sb.ContentMarginBottom = 10;
        }
        t.SetStylebox("normal", "Button", normal);
        t.SetStylebox("hover", "Button", hover);
        t.SetStylebox("pressed", "Button", pressed);
        t.SetStylebox("disabled", "Button", disabled);
        t.SetStylebox("focus", "Button", focus);
        t.SetFont("font", "Button", Bold);
        t.SetFontSize("font_size", "Button", 24);
        t.SetColor("font_color", "Button", Ink);
        t.SetColor("font_hover_color", "Button", Ink);
        t.SetColor("font_pressed_color", "Button", Ink);
        t.SetColor("font_focus_color", "Button", Ink);
        t.SetColor("font_disabled_color", "Button", new Color(1, 1, 1, 0.3f));

        var edit = Flat(new Color(0, 0, 0, 0.35f), 0, new Color(1, 1, 1, 0.18f), 1);
        edit.ContentMarginLeft = edit.ContentMarginRight = 18;
        edit.ContentMarginTop = edit.ContentMarginBottom = 12;
        var editFocus = Flat(Colors.Transparent, 0, Red, 2);
        t.SetStylebox("normal", "LineEdit", edit);
        t.SetStylebox("focus", "LineEdit", editFocus);
        t.SetFont("font", "LineEdit", SemiBold);
        t.SetFontSize("font_size", "LineEdit", 30);
        t.SetColor("font_color", "LineEdit", Ink);
        t.SetColor("caret_color", "LineEdit", Red);
        t.SetColor("selection_color", "LineEdit", new Color(Red, 0.45f));
        return t;
    }
}
