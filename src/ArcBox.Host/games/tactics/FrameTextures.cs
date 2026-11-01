using Godot;
using Tactics.Presentation.Board;

namespace ArcBox.Host.Games.Tactics;

/// <summary>
/// What moves in a world drawn once: one single-frame texture per animated image, rewritten as its frame changes, so water and flames go on
/// moving in blocks that are never recorded again. A separate layer for them would break the painter's order. A glow is a strip of its
/// own, under its own key.
/// </summary>
internal sealed class FrameTextures
{
    private sealed class Strip(ArtImage image, Image[] frames, ImageTexture texture)
    {
        public ArtImage Image { get; } = image;
        public Image[] Frames { get; } = frames;
        public ImageTexture Texture { get; } = texture;
        public int Shown { get; set; }
    }

    private readonly Dictionary<string, Strip> _strips = [];

    /// <summary>The texture showing <paramref name="key"/>, or null if it hasn't been added.</summary>
    public Texture2D? Of(string key) => _strips.TryGetValue(key, out var strip) ? strip.Texture : null;

    /// <summary>Cuts <paramref name="pixels"/>, laid out as <paramref name="image"/> says, into frames, and gives back the texture that will show them.</summary>
    public Texture2D Add(string key, ArtImage image, Image pixels)
    {
        var (width, height) = (pixels.GetWidth() / image.Frames, pixels.GetHeight());
        var frames = new Image[image.Frames];
        for (var i = 0; i < frames.Length; i++)
        {
            frames[i] = pixels.GetRegion(new Rect2I(i * width, 0, width, height));
        }
        var strip = new Strip(image, frames, ImageTexture.CreateFromImage(frames[0]));
        _strips[key] = strip;
        return strip.Texture;
    }

    /// <summary>Lets go of every texture and frame.</summary>
    public void Release()
    {
        foreach (var strip in _strips.Values)
        {
            strip.Texture.Dispose();
            foreach (var frame in strip.Frames)
            {
                frame.Dispose();
            }
        }
        _strips.Clear();
    }

    /// <summary>Shows what each image shows at <paramref name="clock"/>. Only what changed costs anything.</summary>
    public void Step(double clock)
    {
        foreach (var strip in _strips.Values)
        {
            var frame = strip.Image.FrameAt(clock);
            if (frame != strip.Shown)
            {
                strip.Shown = frame;
                strip.Texture.Update(strip.Frames[frame]);
            }
        }
    }
}
