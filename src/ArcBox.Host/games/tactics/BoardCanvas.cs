using Godot;
using Tactics.Presentation.Board;

namespace ArcBox.Host.Games.Tactics;

/// <summary>
/// Draws a <see cref="BoardPlan"/>: each item in order, in art pixels, with the node's position and scale from
/// <see cref="BoardFit"/>. Everything it draws is worked out in <c>Tactics.Presentation.Board</c>. The textures live in the
/// downloaded pack, outside <c>res://</c>, so they're loaded from files, once each.
/// </summary>
public partial class BoardCanvas : Node2D
{
    /// <summary>How often animated art redraws: fast enough for its fastest strip (10 fps).</summary>
    private const double TicksPerSecond = 20;

    private sealed record DrawnFace(Face Face, Vector2[] Points, Vector2[] Outline, Vector2[] Uvs, Vector2[] FrameUvs, Color[] Colours, Texture2D? Texture);

    private sealed record DrawnSprite(Sprite Sprite, Rect2 Target, Vector2 Size, Texture2D? Texture);

    private static readonly Color GridLine = new(0, 0, 0, 0.22f);

    private readonly Dictionary<string, Texture2D?> _textures = [];
    private readonly List<object> _items = [];
    private bool _animated;
    private double _clock;
    private long _tick;

    public BoardCanvas()
    {
        TextureFilter = TextureFilterEnum.Nearest;
        TextureRepeat = TextureRepeatEnum.Enabled;   // a side deeper than its texture repeats
    }

    /// <summary>Shows <paramref name="plan"/>, loading its art from <paramref name="packRoot"/>, placed by <paramref name="fit"/>.</summary>
    public void SetPlan(BoardPlan plan, BoardFit fit, string packRoot)
    {
        _items.Clear();
        foreach (var item in plan.Items)
        {
            _items.Add(item switch
            {
                Face face => Prepare(face, packRoot),
                Sprite sprite => Prepare(sprite, packRoot),
                _ => throw new InvalidOperationException($"Unknown board item {item.GetType().Name}."),
            });
        }
        _animated = plan.Animated;
        Position = new Vector2(fit.Origin.X, fit.Origin.Y);
        Scale = new Vector2(fit.Scale, fit.Scale);
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (!_animated)
        {
            return;
        }
        _clock += delta;
        var tick = (long)(_clock * TicksPerSecond);
        if (tick != _tick)
        {
            _tick = tick;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        foreach (var item in _items)
        {
            if (item is DrawnFace face)
            {
                DrawFace(face);
            }
            else if (item is DrawnSprite sprite)
            {
                DrawSprite(sprite);
            }
        }
    }

    private void DrawFace(DrawnFace drawn)
    {
        if (drawn.Texture is null)
        {
            DrawColoredPolygon(drawn.Points, drawn.Colours[0]);
        }
        else
        {
            DrawPrimitive(drawn.Points, drawn.Colours, FrameUvs(drawn), drawn.Texture);
        }
        if (drawn.Face.Grid)
        {
            DrawPolyline(drawn.Outline, GridLine, 0.5f);
        }
    }

    private Vector2[] FrameUvs(DrawnFace drawn)
    {
        if (drawn.Face.Image is not { Frames: > 1 } image)
        {
            return drawn.Uvs;
        }
        var frame = image.FrameAt(_clock);
        for (var i = 0; i < drawn.Uvs.Length; i++)
        {
            drawn.FrameUvs[i] = new Vector2(image.FrameU(drawn.Uvs[i].X, frame), drawn.Uvs[i].Y);
        }
        return drawn.FrameUvs;
    }

    private void DrawSprite(DrawnSprite drawn)
    {
        if (drawn.Texture is not null)
        {
            var source = new Rect2(new Vector2(drawn.Sprite.Image.FrameAt(_clock) * drawn.Size.X, 0), drawn.Size);
            DrawTextureRectRegion(drawn.Texture, drawn.Target, source);
        }
    }

    private DrawnFace Prepare(Face face, string packRoot)
    {
        var fill = face.Fill ?? new Rgb(1, 1, 1);
        var colour = new Color(fill.R * face.Shade, fill.G * face.Shade, fill.B * face.Shade, face.Alpha);
        Vector2[] points = [.. face.Points.Select(p => new Vector2(p.X, p.Y))];
        Vector2[] uvs = [.. face.Uvs.Select(p => new Vector2(p.X, p.Y))];
        return new DrawnFace(face, points, [.. points, points[0]], uvs, new Vector2[uvs.Length], [colour, colour, colour, colour], Load(face.Image, packRoot));
    }

    private DrawnSprite Prepare(Sprite sprite, string packRoot)
    {
        var size = new Vector2(sprite.Size.X, sprite.Size.Y);
        return new DrawnSprite(sprite, new Rect2(new Vector2(sprite.TopLeft.X, sprite.TopLeft.Y), size), size, Load(sprite.Image, packRoot));
    }

    private Texture2D? Load(ArtImage? image, string packRoot)
    {
        if (image is null)
        {
            return null;
        }
        if (_textures.TryGetValue(image.Path, out var known))
        {
            return known;
        }

        var path = System.IO.Path.Combine(packRoot, image.Path);
        var loaded = Image.LoadFromFile(path);
        if (loaded is null || loaded.IsEmpty())
        {
            GD.PushWarning($"Tactics board: can't load {path}");
            return _textures[image.Path] = null;
        }
        return _textures[image.Path] = ImageTexture.CreateFromImage(loaded);
    }
}
