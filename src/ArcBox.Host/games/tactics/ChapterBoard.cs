using ArcBox.Host.Shell;
using Godot;
using Tactics.Presentation.Story;

namespace ArcBox.Host.Games.Tactics;

/// <summary>The card at the end of a chapter (5d default 13): the world dimmed nearly to black, and the chapter's end in the middle.</summary>
public sealed partial class ChapterBoard : Control
{
    private readonly Label _kicker;
    private readonly Label _chapter;
    private readonly Label _title;
    private readonly Control _words;
    private ChapterCard? _shown;

    public ChapterBoard()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        var dim = new ColorRect { Color = new Color(0.01f, 0.01f, 0.03f, 0.82f), MouseFilter = MouseFilterEnum.Ignore };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(dim);

        _kicker = ArcTheme.Meta("", 24, HudLayer.Muted);
        _chapter = StoryArt.Text("", ArcTheme.Spaced("Black", 12), 110, HudLayer.Gold);
        _title = StoryArt.Text("", ArcTheme.Spaced("Bold", 4), 44, HudLayer.Ink);
        foreach (var label in new[] { _kicker, _chapter, _title })
        {
            label.HorizontalAlignment = HorizontalAlignment.Center;
        }
        _words = ArcTheme.VBox(10, _kicker, _chapter, _title);
        AddChild(_words);
        Visible = false;
    }

    /// <summary>Shows <paramref name="card"/>, or hides it for null.</summary>
    public void Show(ChapterCard? card)
    {
        if (card == _shown)
        {
            return;
        }
        _shown = card;
        Visible = card is not null;
        if (card is null)
        {
            return;
        }
        _kicker.Text = card.Kicker.ToUpperInvariant();
        _chapter.Text = card.Chapter.ToUpperInvariant();
        _title.Text = card.Title;
        _words.Size = new Vector2(StoryArt.Layout.X, _words.GetCombinedMinimumSize().Y);
        _words.Position = new Vector2(0, (StoryArt.Layout.Y - _words.Size.Y) / 2);
    }
}
