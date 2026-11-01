using System.IO.Compression;

namespace Tactics.Presentation.Tests.Board;

/// <summary>Reads an 8-bit RGB or RGBA PNG from the pack, as Godot's <c>GetData</c> gives it: tests here have no image library.</summary>
internal static class PngTexels
{
    public static (byte[] Rgba, int Width, int Height) Read(string file)
    {
        var bytes = File.ReadAllBytes(file);
        var (at, width, height, type) = (8, 0, 0, 0);
        using var data = new MemoryStream();
        byte[] palette = [];
        byte[] alpha = [];
        while (at < bytes.Length)
        {
            var length = Int(bytes, at);
            var kind = System.Text.Encoding.ASCII.GetString(bytes, at + 4, 4);
            var start = at + 8;
            switch (kind)
            {
                case "IHDR":
                    (width, height, type) = (Int(bytes, start), Int(bytes, start + 4), bytes[start + 9]);
                    Assert.Equal(8, bytes[start + 8]);
                    break;
                case "IDAT":
                    data.Write(bytes, start, length);
                    break;
                case "PLTE":
                    palette = bytes[start..(start + length)];
                    break;
                case "tRNS":
                    alpha = bytes[start..(start + length)];
                    break;
            }
            at = start + length + 4;
        }

        data.Position = 0;
        using var inflated = new ZLibStream(data, CompressionMode.Decompress);
        var channels = type switch { 6 => 4, 2 => 3, 3 => 1, _ => throw new InvalidDataException($"{file}: colour type {type}") };
        var stride = width * channels;
        var raw = new byte[(stride + 1) * height];
        inflated.ReadExactly(raw);

        var rgba = new byte[width * height * 4];
        var (last, row) = (new byte[stride], new byte[stride]);
        for (var y = 0; y < height; y++)
        {
            var filter = raw[y * (stride + 1)];
            Array.Copy(raw, y * (stride + 1) + 1, row, 0, stride);
            for (var i = 0; i < stride; i++)
            {
                var (a, b, c) = (i >= channels ? row[i - channels] : 0, last[i], i >= channels ? last[i - channels] : 0);
                row[i] += (byte)(filter switch { 1 => a, 2 => b, 3 => (a + b) / 2, 4 => Paeth(a, b, c), _ => 0 });
            }
            for (var x = 0; x < width; x++)
            {
                var to = (y * width + x) * 4;
                if (type == 3)
                {
                    var index = row[x];
                    (rgba[to], rgba[to + 1], rgba[to + 2], rgba[to + 3]) = (palette[index * 3], palette[index * 3 + 1], palette[index * 3 + 2], index < alpha.Length ? alpha[index] : (byte)255);
                }
                else
                    (rgba[to], rgba[to + 1], rgba[to + 2], rgba[to + 3]) = (row[x * channels], row[x * channels + 1], row[x * channels + 2], channels == 4 ? row[x * 4 + 3] : (byte)255);
            }
            (last, row) = (row, last);
        }
        return (rgba, width, height);
    }

    private static int Int(byte[] bytes, int at) => bytes[at] << 24 | bytes[at + 1] << 16 | bytes[at + 2] << 8 | bytes[at + 3];

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var (pa, pb, pc) = (Math.Abs(p - a), Math.Abs(p - b), Math.Abs(p - c));
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}
