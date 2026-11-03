using System.Buffers.Binary;

namespace Tactics.Logic;

/// <summary>
/// How long a voice clip plays, read from the file itself so a line's timer never cuts its speech (5d plan, flag 5). A clip is Ogg
/// Vorbis, as <c>tools/tactics-lore/voices.py</c> writes it: the sample rate is in the identification header on the first page, and the
/// last page's granule position is the number of samples. Only those two pages are read.
/// </summary>
public static class ClipLength
{
    /// <summary>The longest an Ogg page can be: a 27-byte header, 255 lacing values and 255 segments of 255 bytes.</summary>
    private const int MaxPage = 27 + 255 + 255 * 255;

    /// <summary>The header flag of a stream's last page.</summary>
    private const byte EndOfStream = 0x04;

    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>
    /// The clip's length in seconds, or null if <paramref name="ogg"/> isn't a whole Ogg Vorbis stream: not Ogg, not Vorbis, a page whose
    /// checksum is wrong, a file cut short (its last page must end the file and carry the end-of-stream flag, so a cut at a page
    /// boundary is caught too) or no last page with a sample count. The stream must
    /// be seekable; it's read from the start.
    /// </summary>
    public static double? Of(Stream ogg)
    {
        try
        {
            if (!ogg.CanRead || !ogg.CanSeek)
                return null;
            var length = ogg.Length;
            ogg.Position = 0;
            var head = ReadUpTo(ogg, (int)Math.Min(MaxPage, length));
            if (Page(head, 0) is not { } first || first.End > head.Length)
                return null;
            var body = head.AsSpan(first.Body, first.End - first.Body);
            if (body.Length < 16 || body[0] != 1 || !body[1..7].SequenceEqual("vorbis"u8))
                return null;
            var rate = BinaryPrimitives.ReadUInt32LittleEndian(body[12..16]);
            if (rate == 0)
                return null;

            var start = Math.Max(0, length - MaxPage);
            ogg.Position = start;
            var tail = ReadUpTo(ogg, (int)(length - start));
            for (var at = tail.Length - 27; at >= 0; at--)
                if (Page(tail, at) is { } last && last.End == tail.Length)
                    return last.Serial == first.Serial && last.Ends && last.Granule >= 0 ? last.Granule / (double)rate : null;
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>An Ogg page found at <paramref name="at"/>: where its body starts and ends in the buffer, its stream, its granule and whether it ends the stream.</summary>
    private readonly record struct OggPage(int Body, int End, uint Serial, long Granule, bool Ends);

    /// <summary>The page at <paramref name="at"/> if one starts there, lies wholly in the buffer and its checksum holds; otherwise null.</summary>
    private static OggPage? Page(byte[] buffer, int at)
    {
        var data = buffer.AsSpan(at);
        if (data.Length < 27 || !data[..4].SequenceEqual("OggS"u8) || data[4] != 0)
            return null;
        var segments = data[26];
        if (data.Length < 27 + segments)
            return null;
        var size = 27 + segments;
        foreach (var lace in data.Slice(27, segments))
            size += lace;
        if (data.Length < size)
            return null;

        var crc = 0u;
        for (var i = 0; i < size; i++)
            crc = (crc << 8) ^ CrcTable[(crc >> 24) ^ (i is >= 22 and < 26 ? 0u : data[i])];
        if (crc != BinaryPrimitives.ReadUInt32LittleEndian(data[22..26]))
            return null;
        return new OggPage(at + 27 + segments, at + size, BinaryPrimitives.ReadUInt32LittleEndian(data[14..18]),
            BinaryPrimitives.ReadInt64LittleEndian(data[6..14]), (data[5] & EndOfStream) != 0);
    }

    /// <summary>Ogg's CRC-32: polynomial 0x04C11DB7, not reflected, starting from 0.</summary>
    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (var i = 0u; i < 256; i++)
        {
            var r = i << 24;
            for (var bit = 0; bit < 8; bit++)
                r = (r & 0x80000000) != 0 ? (r << 1) ^ 0x04C11DB7 : r << 1;
            table[i] = r;
        }
        return table;
    }

    private static byte[] ReadUpTo(Stream stream, int count)
    {
        var buffer = new byte[count];
        var read = 0;
        while (read < count && stream.Read(buffer, read, count - read) is > 0 and var n)
            read += n;
        return read == count ? buffer : buffer[..read];
    }
}
