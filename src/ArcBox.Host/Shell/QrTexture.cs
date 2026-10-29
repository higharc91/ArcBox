using Godot;
using QRCoder;

namespace ArcBox.Host.Shell;

/// <summary>QR code → PNG bytes (QRCoder) → <see cref="ImageTexture"/>.</summary>
public static class QrTexture
{
    /// <summary>Dark modules on white with a 4-module quiet zone; draw it with nearest filtering to stay crisp.</summary>
    public static ImageTexture Create(string text, int pixelsPerModule = 12)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(pixelsPerModule, [0x0b, 0x0b, 0x10], [0xff, 0xff, 0xff], drawQuietZones: true);

        var image = new Image();
        var err = image.LoadPngFromBuffer(png);
        if (err != Error.Ok)
        {
            throw new InvalidOperationException($"QR PNG failed to load: {err}");
        }
        return ImageTexture.CreateFromImage(image);
    }
}
