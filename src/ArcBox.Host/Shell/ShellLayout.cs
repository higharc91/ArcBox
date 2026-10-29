using Godot;

namespace ArcBox.Host.Shell;

/// <summary>Layout pieces every shell screen shares: background, safe margins and the slab + panel header.</summary>
public static class ShellLayout
{
    public const int HeaderHeight = 112;

    /// <summary>Adds the backdrop and returns the 1920×1080-safe content column (64 px sides, 52 px top/bottom).</summary>
    public static VBoxContainer Frame(Control screen, int separation = 26)
    {
        screen.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        screen.AddChild(new BroadcastBackground());
        var column = ArcTheme.VBox(separation);
        var margin = ArcTheme.Margin(64, 52, 64, 52, column);
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        screen.AddChild(margin);
        return column;
    }

    /// <summary>[brand slab][glass panel: meta line + title][optional right box], like the mockups' <c>.head</c>.</summary>
    public static HBoxContainer Header(string brand, Label meta, Label title, Control? right = null)
    {
        var slab = new BrandSlab(brand) { CustomMinimumSize = new Vector2(0, HeaderHeight) };
        title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        title.ClipText = true;
        var text = ArcTheme.VBox(0, meta, title);
        text.Alignment = BoxContainer.AlignmentMode.Center;
        var panel = ArcTheme.Panel(ArcTheme.GlassBox(ArcTheme.Red, 28, 10), text);
        panel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        var row = ArcTheme.HBox(20, slab, panel);
        row.CustomMinimumSize = new Vector2(0, HeaderHeight);
        if (right is not null)
        {
            row.AddChild(right);
        }
        return row;
    }

    /// <summary>The small boxed stat at the right of a header ("TIME 0:09").</summary>
    public static PanelContainer StatBox(Label caption, Label value, Control? below = null, int width = 200)
    {
        caption.HorizontalAlignment = HorizontalAlignment.Center;
        value.HorizontalAlignment = HorizontalAlignment.Center;
        var box = ArcTheme.VBox(4, caption, value);
        box.Alignment = BoxContainer.AlignmentMode.Center;
        if (below is not null)
        {
            box.AddChild(below);
        }
        var panel = ArcTheme.Panel(ArcTheme.GlassBox(null, 18, 6), box);
        panel.CustomMinimumSize = new Vector2(width, 0);
        return panel;
    }

    /// <summary>A thin progress bar (track + red fill), like <c>.clock .bar</c>.</summary>
    public static (Control Bar, ColorRect Fill) Bar(float height = 5, Color? fill = null)
    {
        var track = new ColorRect { Color = new Color(1, 1, 1, 0.12f), CustomMinimumSize = new Vector2(0, height) };
        var bar = new ColorRect { Color = fill ?? ArcTheme.Red, MouseFilter = Control.MouseFilterEnum.Ignore };
        bar.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        track.AddChild(bar);
        return (track, bar);
    }

    /// <summary>
    /// Moves a fill's right edge to <paramref name="fraction"/> of its parent. Godot 4's anchor setters keep the
    /// current rect by default (they rewrite the offset), so this keeps the offset at 0 instead.
    /// </summary>
    public static void SetBar(Control fill, float fraction)
    {
        fill.SetAnchor(Side.Right, Mathf.Clamp(fraction, 0, 1), keepOffset: true);
        fill.OffsetRight = 0;
    }
}
