using ArcBox.Host.Shell;
using Godot;
using Tactics.Presentation.Board;
using Tactics.Presentation.Hud;

namespace ArcBox.Host.Games.Tactics;

/// <summary>
/// What the story's panels draw with: the pack's panel pieces (the HUD's navy glass and gold, at twice the art), faces, the d20, and the
/// type. Loaded once, shared by <see cref="StoryPanel"/>, <see cref="VoteBoard"/>, <see cref="CheckBoard"/> and <see cref="ChapterBoard"/>.
/// </summary>
public sealed class StoryArt
{
    /// <summary>The layout's size: the scene stretches it to the window.</summary>
    public static readonly Vector2 Layout = new(1920, 1080);

    /// <summary>The screen's margin, as the world's own panels have it.</summary>
    public const int Margin = 36;

    /// <summary>The check's success green.</summary>
    public static readonly Color Pass = new("6fdc8c");

    /// <summary>The check's failure red.</summary>
    public static readonly Color Fail = new("ef6a5a");

    /// <summary>A check option's detail, as mockup 02 has it.</summary>
    public static readonly Color Sky = new("8fb8ff");

    private readonly Dictionary<string, Texture2D?> _textures = [];
    private readonly Texture2D? _panels;

    public StoryArt(string packRoot, HudArt hud, UnitArt units)
    {
        (Root, Hud, Units) = (packRoot, hud, units);
        _panels = HudLayer.Scaled(packRoot, hud.PanelsPath);
    }

    public string Root { get; }

    public HudArt Hud { get; }

    public UnitArt Units { get; }

    /// <summary>A panel piece of the pack's, as a style box.</summary>
    public StyleBoxTexture Box(string piece) => HudLayer.Box(_panels, Hud.Panel(piece));

    /// <summary>An image of the pack's, by its path from the pack's root; null (and a warning, once) if it can't be read.</summary>
    public Texture2D? Load(string? file)
    {
        if (file is null)
        {
            return null;
        }
        if (_textures.TryGetValue(file, out var known))
        {
            return known;
        }
        var path = System.IO.Path.Combine(Root, file);
        var image = System.IO.File.Exists(path) ? Image.LoadFromFile(path) : null;
        if (image is null || image.IsEmpty())
        {
            GD.PushWarning($"Tactics story: can't load {path}");
            return _textures[file] = null;
        }
        return _textures[file] = ImageTexture.CreateFromImage(image);
    }

    /// <summary>A label in the story's type: Titillium, in the HUD's ink unless said.</summary>
    public static Label Text(string text, Font font, int size, Color? colour = null) => ArcTheme.Label(text, font, size, colour ?? HudLayer.Ink);

    /// <summary>A rectangle of the art, for drawing.</summary>
    public static Rect2 Region(ArtRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);
}

/// <summary>A face in the pack's portrait style: its backdrop, the portrait over it, sharp at any size, and a gold edge.</summary>
public sealed partial class StoryFace : Control
{
    private readonly StoryArt _art;
    private HudPortrait? _face;
    private bool _bright = true;

    public StoryFace(StoryArt art, Vector2 size)
    {
        _art = art;
        CustomMinimumSize = size;
        TextureFilter = TextureFilterEnum.Nearest;
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
    }

    /// <summary>Shows <paramref name="face"/>, or nothing. <paramref name="bright"/> false greys it, for someone away.</summary>
    public void Show(HudPortrait? face, bool bright = true)
    {
        if (face == _face && bright == _bright)
        {
            return;
        }
        (_face, _bright) = (face, bright);
        Visible = face is not null;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_face is not { } face)
        {
            return;
        }
        var box = new Rect2(Vector2.Zero, Size);
        var tint = _bright ? Colors.White : new Color(0.55f, 0.55f, 0.6f);
        if (_art.Load(face.Backdrop) is { } backdrop)
        {
            DrawTextureRect(backdrop, box, false, tint);
        }
        if (_art.Load(face.Path) is { } picture)
        {
            DrawTextureRectRegion(picture, box, StoryArt.Region(face.Source), tint);
        }
        DrawRect(box, HudLayer.Gold with { A = 0.85f }, false, 3);
    }
}
