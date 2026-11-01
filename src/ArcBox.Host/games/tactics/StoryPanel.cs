using ArcBox.Host.Shell;
using Godot;
using Tactics.Presentation.Story;

namespace ArcBox.Host.Games.Tactics;

/// <summary>
/// The line showing, at the bottom left (mockup 02): the speaker's face in the line's mood beside the dialogue box, their name on its tab,
/// and the caption in the rows <see cref="StoryScreen"/> wrapped it to, with the question lit in gold. The narrator has no face and no
/// name, and reads in the muted ink. Between two scenes the next one's title shows in the middle instead. Everything said is the
/// screen's; this only draws it, and only when it changes.
/// </summary>
public sealed partial class StoryPanel : Control
{
    /// <summary>The dialogue box's width: mockup 02's, leaving the vote panel its corner.</summary>
    public const int Width = 1230;

    private const int FaceSize = 180;
    private const int TextSize = 30;
    private const int RowGap = 2;

    private readonly StoryArt _art;
    private readonly Control _box;
    private readonly StoryFace _face;
    private readonly Control _tab;
    private readonly Label _name;
    private readonly VBoxContainer _rows;
    private readonly Control _interlude;
    private readonly Label _interludeTitle;
    private StoryCaption? _shown;
    private string? _shownInterlude;

    public StoryPanel(StoryArt art)
    {
        _art = art;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _face = new StoryFace(art, new Vector2(FaceSize, FaceSize)) { SizeFlagsVertical = SizeFlags.ShrinkCenter };
        _rows = ArcTheme.VBox(RowGap);
        _rows.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _rows.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        var inside = ArcTheme.HBox(28, _face, _rows);
        inside.CustomMinimumSize = new Vector2(0, FaceSize);
        _box = ArcTheme.Panel(art.Box("dialogue"), ArcTheme.Margin(10, 14, 10, 10, inside));
        AddChild(_box);

        _name = StoryArt.Text("", ArcTheme.Spaced("Bold", 3), 26, HudLayer.Gold);
        _tab = ArcTheme.Panel(art.Box("dialogue_tab"), _name);
        AddChild(_tab);

        _interludeTitle = StoryArt.Text("", ArcTheme.Spaced("Bold", 4), 48, HudLayer.Ink);
        _interludeTitle.HorizontalAlignment = HorizontalAlignment.Center;
        var kicker = ArcTheme.Meta("Next", 18, HudLayer.Gold);
        kicker.HorizontalAlignment = HorizontalAlignment.Center;
        _interlude = ArcTheme.Panel(art.Box("main"), ArcTheme.Margin(40, 10, 40, 14, ArcTheme.VBox(4, kicker, _interludeTitle)));
        AddChild(_interlude);
        Visible = false;
    }

    /// <summary>Shows <paramref name="caption"/>, or between scenes <paramref name="interlude"/>; neither hides the panel.</summary>
    public void Show(StoryCaption? caption, string? interlude)
    {
        if (caption == _shown && interlude == _shownInterlude)
        {
            return;
        }
        (_shown, _shownInterlude) = (caption, interlude);
        Visible = caption is not null || interlude is not null;
        _box.Visible = caption is not null;
        _tab.Visible = caption?.Speaker is not null;
        _interlude.Visible = caption is null && interlude is not null;
        if (caption is not null)
        {
            _face.Show(caption.Portrait);
            _name.Text = caption.Speaker?.ToUpperInvariant() ?? "";
            Fill(caption);
        }
        if (interlude is not null)
        {
            _interludeTitle.Text = interlude.ToUpperInvariant();
        }
        Place();
    }

    private void Fill(StoryCaption caption)
    {
        foreach (var child in _rows.GetChildren())
        {
            _rows.RemoveChild(child);
            child.QueueFree();
        }
        foreach (var row in caption.Rows)
        {
            var line = ArcTheme.HBox(0);
            foreach (var run in row)
            {
                line.AddChild(run.Strong
                    ? StoryArt.Text(run.Text, ArcTheme.Bold, TextSize, HudLayer.Gold)
                    : StoryArt.Text(run.Text, ArcTheme.SemiBold, TextSize, caption.Narrator ? HudLayer.Muted : HudLayer.Ink));
            }
            _rows.AddChild(line);
        }
    }

    /// <summary>Placed by hand: the box sits on the bottom margin and grows upward; the tab straddles its top edge (ui/manifest.json).</summary>
    private void Place()
    {
        if (_box.Visible)
        {
            _box.Size = new Vector2(Width, _box.GetCombinedMinimumSize().Y);
            _box.Position = new Vector2(StoryArt.Margin, StoryArt.Layout.Y - StoryArt.Margin - _box.Size.Y);
            _tab.Size = _tab.GetCombinedMinimumSize();
            _tab.Position = new Vector2(_box.Position.X + (_face.Visible ? 28 + FaceSize + 12 : 12), _box.Position.Y - _tab.Size.Y + 14);
        }
        if (_interlude.Visible)
        {
            _interlude.Size = _interlude.GetCombinedMinimumSize();
            _interlude.Position = new Vector2((StoryArt.Layout.X - _interlude.Size.X) / 2, StoryArt.Layout.Y - 220 - _interlude.Size.Y);
        }
    }
}
