using ArcBox.Host.Shell;
using Godot;
using Tactics.Presentation.Audio;

namespace ArcBox.Host.Games.Tactics;

/// <summary>The title art's VICTORY or DEFEAT strip over the results: it plays once and holds its last frame, as the manifest asks.</summary>
public partial class ResultBanner : TextureRect
{
    private const int ArtScale = 3;
    private const int Room = 171;       // the taller of the strips at that scale

    private readonly AtlasTexture _frame = new();
    private TitleStrip? _strip;
    private double _clock;
    private int _shown = -1;

    /// <summary>Takes a shot when the strip has landed; null for none.</summary>
    public Screenshotter? Shots { get; set; }

    public ResultBanner()
    {
        ExpandMode = ExpandModeEnum.IgnoreSize;
        StretchMode = StretchModeEnum.KeepAspectCentered;
        TextureFilter = TextureFilterEnum.Nearest;
        CustomMinimumSize = new Vector2(0, Room);
        Texture = _frame;
        Visible = false;
        SetProcess(false);
    }

    /// <summary>Plays <paramref name="strip"/> from the start, or shows nothing for null. The strip already up carries on.</summary>
    public void Show(TitleStrip? strip, string? root)
    {
        if (strip == _strip)
        {
            return;
        }
        _strip = strip;
        var path = strip is not null && root is not null ? System.IO.Path.Combine(root, strip.Path) : null;
        var image = path is not null && System.IO.File.Exists(path) ? Image.LoadFromFile(path) : null;
        var ready = image is not null && !image.IsEmpty();
        Visible = ready;
        SetProcess(ready);
        if (ready)
        {
            _frame.Atlas = ImageTexture.CreateFromImage(image);
            (_clock, _shown) = (0, -1);
            ShowFrame(0);
        }
    }

    public override void _Process(double delta)
    {
        if (_strip is not { } strip)
        {
            return;
        }
        _clock += delta;
        var frame = strip.FrameAt(_clock);
        if (frame != _shown)
        {
            ShowFrame(frame);
            if (frame == strip.Frames - 1)
            {
                SetProcess(false);
                Shots?.Capture("tactics-results-banner");
            }
        }
    }

    private void ShowFrame(int frame)
    {
        _shown = frame;
        if (_strip is { } strip)
        {
            _frame.Region = new Rect2(frame * strip.Width, 0, strip.Width, strip.Height);
        }
    }
}
