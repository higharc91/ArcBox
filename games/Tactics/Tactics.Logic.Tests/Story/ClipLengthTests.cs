using System.Buffers.Binary;

namespace Tactics.Logic.Tests;

public sealed class ClipLengthTests
{
    private const uint Serial = 0x3D9BE0D9;

    /// <summary>One Ogg page, with its checksum: the header, one lacing value per 255 bytes of the body, then the body.</summary>
    internal static byte[] Page(ReadOnlySpan<byte> body, long granule, byte type = 0, uint serial = Serial, int sequence = 0)
    {
        var laces = new List<byte>();
        var left = body.Length;
        do
        {
            laces.Add((byte)Math.Min(left, 255));
            left -= 255;
        } while (left >= 0);

        var page = new byte[27 + laces.Count + body.Length];
        "OggS"u8.CopyTo(page);
        page[5] = type;
        BinaryPrimitives.WriteInt64LittleEndian(page.AsSpan(6), granule);
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(14), serial);
        BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(18), sequence);
        page[26] = (byte)laces.Count;
        laces.CopyTo(page, 27);
        body.CopyTo(page.AsSpan(27 + laces.Count));
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(22), Crc(page));
        return page;
    }

    /// <summary>Ogg's CRC-32, written out bit by bit, apart from the one under test.</summary>
    private static uint Crc(byte[] page)
    {
        var crc = 0u;
        foreach (var b in page)
        {
            crc ^= (uint)b << 24;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 0x80000000) != 0 ? (crc << 1) ^ 0x04C11DB7 : crc << 1;
        }
        return crc;
    }

    /// <summary>A Vorbis identification header at <paramref name="rate"/> Hz, mono.</summary>
    internal static byte[] VorbisHead(uint rate)
    {
        var head = new byte[30];
        head[0] = 1;
        "vorbis"u8.CopyTo(head.AsSpan(1));
        head[11] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(12), rate);
        head[29] = 1;
        return head;
    }

    /// <summary>A stream of the id header's page, a middle page of 600 bytes and a last page with <paramref name="samples"/>.</summary>
    internal static byte[] Clip(uint rate, long samples, byte[]? head = null)
        =>
        [
            .. Page(head ?? VorbisHead(rate), 0, type: 2),
            .. Page(new byte[600], samples / 2, sequence: 1),
            .. Page(new byte[40], samples, type: 4, sequence: 2),
        ];

    private static double? Of(byte[] bytes) => ClipLength.Of(new MemoryStream(bytes));

    [Fact]
    public void A_clip_length_is_read_from_its_last_page()
    {
        Assert.Equal(1.5, Of(Clip(24000, 36000)));
        Assert.Equal(2.0, Of(Clip(48000, 96000)));

        // A body over 255 bytes takes a second lacing value, and "OggS" inside a body isn't mistaken for a page.
        byte[] decoy = [.. "OggS"u8, .. new byte[400]];
        Assert.Equal(0.5, Of([.. Page(VorbisHead(8000), 0, type: 2), .. Page(decoy, 4000, type: 4, sequence: 1)]));

        var real = Path.Combine(ContentPaths.ContentBase, "lore", "audio", "ch1_ford_vote_2.1ee49baf.ogg");
        using var file = File.OpenRead(real);
        Assert.Equal(74401 / 24000.0, ClipLength.Of(file)!.Value, 6);
    }

    [Fact]
    public void A_bad_clip_has_no_length()
    {
        var good = Clip(24000, 36000);
        var badCrc = (byte[])good.Clone();
        badCrc[^1] ^= 0xFF;
        byte[] opus = [.. "OpusHead"u8, 1, 1, 0x38, 1, 0x80, 0xBB, 0, 0, 0, 0, 0];
        byte[] otherStream = [.. Page(VorbisHead(24000), 0, type: 2), .. Page(new byte[40], 36000, type: 4, serial: 7, sequence: 1)];

        Assert.Null(Of([]));
        Assert.Null(Of("not even an ogg file, just words"u8.ToArray()));
        Assert.Null(Of(Clip(24000, 36000, head: opus)));
        Assert.Null(Of(Clip(0, 36000)));
        Assert.Null(Of(badCrc));
        Assert.Null(Of(good[..^10]));
        Assert.Null(Of([.. good, 0, 0, 0]));
        Assert.Null(Of(Clip(24000, -1)));
        Assert.Null(Of(otherStream));
        Assert.Null(Of(Page(VorbisHead(24000), 0, type: 2)[..20]));
        Assert.Null(ClipLength.Of(new OneWay(good)));
    }

    /// <summary>A stream that can't seek, as a network read would be.</summary>
    private sealed class OneWay(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
}
