namespace Tactics.Presentation.World;

/// <summary>
/// Draws a strip of pixel art smaller, as whole pixels, for art made bigger than the world it stands in (the props manifest's <c>scale</c>).
/// Each frame is shrunk on its own, so no frame bleeds into the next. An output pixel is the average of the opaque source pixels its box
/// covers, and opaque only if they cover at least <see cref="Coverage"/> of it: alpha stays 0 or 255, as the pack's art is. Pixels in the
/// cut-out's matte colour (magenta fringe left by the generator's key) count as clear.
/// </summary>
public static class SpriteShrink
{
    /// <summary>How much of an output pixel's box must be opaque for it to be drawn.</summary>
    public const double Coverage = 0.4;

    /// <summary>The shrunk strip: RGBA bytes, row-major, and the size of one frame.</summary>
    public sealed record Strip(byte[] Rgba, int FrameWidth, int Height, int Frames)
    {
        /// <summary>The whole strip's width.</summary>
        public int Width => FrameWidth * Frames;
    }

    /// <summary>One frame's size at <paramref name="scale"/>: at least a pixel each way.</summary>
    public static (int Width, int Height) Size(int frameWidth, int height, double scale)
        => (Math.Max(1, (int)Math.Round(frameWidth * scale)), Math.Max(1, (int)Math.Round(height * scale)));

    /// <summary>
    /// The anchor (the ground contact pixel) at <paramref name="scale"/>: the pixel whose box holds the old one's. So an anchor on the
    /// bottom row stays on the bottom row, and the feet stay on the ground.
    /// </summary>
    public static (int X, int Y) Anchor(int x, int y, int frameWidth, int height, double scale)
    {
        var (w, h) = Size(frameWidth, height, scale);
        return (Math.Clamp((int)((x + 0.5) * w / frameWidth), 0, w - 1), Math.Clamp((int)((y + 0.5) * h / height), 0, h - 1));
    }

    /// <summary>
    /// <paramref name="rgba"/> (a strip <paramref name="width"/> by <paramref name="height"/> of <paramref name="frames"/> frames) at
    /// <paramref name="scale"/>. A scale of 1 or more returns a copy as it is.
    /// </summary>
    public static Strip Shrink(ReadOnlySpan<byte> rgba, int width, int height, int frames, double scale)
    {
        if (frames < 1 || width % frames != 0 || rgba.Length != width * height * 4)
            throw new ArgumentException($"A {width}x{height} strip of {frames} frames needs {width * height * 4} bytes and a width the frames divide; got {rgba.Length}.", nameof(rgba));
        var frameWidth = width / frames;
        if (scale >= 1)
            return new Strip(rgba.ToArray(), frameWidth, height, frames);

        var (w, h) = Size(frameWidth, height, scale);
        var (sx, sy) = ((double)frameWidth / w, (double)height / h);
        var output = new byte[w * frames * h * 4];
        for (var f = 0; f < frames; f++)
            for (var oy = 0; oy < h; oy++)
                for (var ox = 0; ox < w; ox++)
                {
                    double x0 = ox * sx, x1 = (ox + 1) * sx, y0 = oy * sy, y1 = (oy + 1) * sy;
                    double area = 0, opaque = 0, r = 0, g = 0, b = 0;
                    for (var y = (int)y0; y < Math.Min(height, (int)Math.Ceiling(y1)); y++)
                    {
                        var wy = Math.Min(y1, y + 1) - Math.Max(y0, y);
                        for (var x = (int)x0; x < Math.Min(frameWidth, (int)Math.Ceiling(x1)); x++)
                        {
                            var weight = (Math.Min(x1, x + 1) - Math.Max(x0, x)) * wy;
                            area += weight;
                            var i = (y * width + f * frameWidth + x) * 4;
                            if (rgba[i + 3] < 128 || Matte(rgba[i], rgba[i + 1], rgba[i + 2]))
                                continue;
                            opaque += weight;
                            r += rgba[i] * weight;
                            g += rgba[i + 1] * weight;
                            b += rgba[i + 2] * weight;
                        }
                    }
                    if (opaque <= 0 || opaque < Coverage * area)
                        continue;
                    var o = (oy * w * frames + f * w + ox) * 4;
                    output[o] = (byte)Math.Round(r / opaque);
                    output[o + 1] = (byte)Math.Round(g / opaque);
                    output[o + 2] = (byte)Math.Round(b / opaque);
                    output[o + 3] = 255;
                }
        return new Strip(output, w, h, frames);
    }

    /// <summary>
    /// The magenta the art generator draws on, and its fringe: red and blue both bright and well above green. A dark fringe pixel reads as
    /// outline, so it stays.
    /// </summary>
    public static bool Matte(byte r, byte g, byte b) => r > 90 && b > 70 && g * 5 < Math.Min(r, b) * 2;
}
