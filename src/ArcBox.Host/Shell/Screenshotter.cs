using Godot;
using System.Buffers.Binary;
using System.IO.Compression;

namespace ArcBox.Host.Shell;

/// <summary>
/// <c>--shots=&lt;dir&gt;</c>: saves the viewport as <c>&lt;seq&gt;-&lt;label&gt;.png</c> after the next drawn frame.
/// Add it to the tree before calling <see cref="Capture"/>.
/// </summary>
public partial class Screenshotter : Node
{
    private int _seq;

    public Screenshotter()
    {
        Dir = "";
    }

    public Screenshotter(string dir)
    {
        Dir = ProjectSettings.GlobalizePath(dir);
        Name = "Screenshotter";
        System.IO.Directory.CreateDirectory(Dir);
    }

    public string Dir { get; }

    /// <summary>Performance passes retain the output directory for metadata without doing GPU readback or PNG encoding.</summary>
    public bool CaptureEnabled { get; set; } = true;

    /// <summary>Last saved file, for logs and tests.</summary>
    public string? LastPath { get; private set; }

    /// <summary>Saves the frame after this one (so layout changes made this frame are visible) as <c>NNN-label.png</c>.</summary>
    public async void Capture(string label)
    {
        if (!CaptureEnabled) return;
        var sequence = ++_seq;
        var name = $"{sequence:000}-{Sanitise(label)}.png";
        // Two frames: containers settle their sizes one frame after a screen is swapped in.
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        if (!IsInsideTree())
        {
            return;
        }

        using var image = GetViewport().GetTexture().GetImage();
        var path = System.IO.Path.Combine(Dir, name);
        var fast = System.Environment.GetEnvironmentVariable("ARCBOX_FX_FAST_PNG") == "1";
        var err = fast ? SaveFastPng(image, path) : image.SavePng(path);
        if (fast && sequence == 1 && System.Environment.GetEnvironmentVariable("ARCBOX_FX_VERIFY_PNG") == "1")
            image.SavePng(path + ".reference.png");
        if (err == Error.Ok)
        {
            LastPath = path;
            GD.Print($"SHOT {path}");
        }
        else
        {
            GD.PushWarning($"Screenshot {path} failed: {err}");
        }
    }

    // Native viewport pixels, lossless PNG with filter 0. Preview reels favour encoding speed over disk size.
    private static Error SaveFastPng(Image image, string path)
    {
        image.Convert(Image.Format.Rgba8);
        var pixels = image.GetData();
        using var compressed = new System.IO.MemoryStream();
        using (var zip = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            for (var row = 0; row < image.GetHeight(); row++)
            {
                zip.WriteByte(0);
                zip.Write(pixels.AsSpan(row * image.GetWidth() * 4, image.GetWidth() * 4));
            }
        using var file = System.IO.File.Create(path);
        file.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        Span<byte> header = stackalloc byte[13];
        header.Clear();
        BinaryPrimitives.WriteInt32BigEndian(header, image.GetWidth());
        BinaryPrimitives.WriteInt32BigEndian(header[4..], image.GetHeight());
        header[8] = 8; header[9] = 6;
        Chunk(file, "IHDR"u8, header);
        Chunk(file, "IDAT"u8, compressed.GetBuffer().AsSpan(0, (int)compressed.Length));
        Chunk(file, "IEND"u8, []);
        return Error.Ok;
    }

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(i =>
    {
        var value = (uint)i;
        for (var bit = 0; bit < 8; bit++) value = (value & 1) != 0 ? 0xedb88320U ^ (value >> 1) : value >> 1;
        return value;
    }).ToArray();

    private static void Chunk(System.IO.Stream file, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        file.Write(number); file.Write(type); file.Write(data);
        var crc = uint.MaxValue;
        foreach (var value in type) crc = CrcTable[(crc ^ value) & 255] ^ (crc >> 8);
        foreach (var value in data) crc = CrcTable[(crc ^ value) & 255] ^ (crc >> 8);
        BinaryPrimitives.WriteUInt32BigEndian(number, ~crc);
        file.Write(number);
    }

    private static string Sanitise(string label)
    {
        var chars = label.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) || c == '-' ? c : '-').ToArray();
        return new string(chars).Trim('-');
    }
}
