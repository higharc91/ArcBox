using System.Buffers.Binary;
using System.Security.Cryptography;
using Meridian.Presentation.Art;

namespace Meridian.Presentation.Tests.Art;

/// <summary>The audio budget (M5 plan, Global constraints): Ogg Vorbis, mono or stereo, sounds short, tracks bounded.</summary>
public sealed class AudioCheckTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("meridian-audio-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>Just enough Ogg Vorbis for its headers: an identification page, then a last page with the granule.</summary>
    private static byte[] Ogg(int channels, int rate, long samples)
    {
        var first = new byte[27 + 1 + 30];
        "OggS"u8.CopyTo(first);
        first[26] = 1;    // one segment
        first[27] = 30;   // of 30 bytes
        first[28] = 1;
        "vorbis"u8.CopyTo(first.AsSpan(29));
        first[28 + 11] = (byte)channels;
        BinaryPrimitives.WriteInt32LittleEndian(first.AsSpan(28 + 12), rate);
        var last = new byte[27];
        "OggS"u8.CopyTo(last);
        BinaryPrimitives.WriteInt64LittleEndian(last.AsSpan(6), samples);
        return [.. first, .. new byte[64], .. last];
    }

    [Fact]
    public void Headers_give_channels_and_length()
    {
        Assert.Equal((2, 3.0), ArtCheck.Vorbis(Ogg(2, 48000, 144000)));
        Assert.Null(ArtCheck.Vorbis("not an ogg file at all, just some text bytes here........"u8.ToArray()));
    }

    private ArtCheck Check(params (string Id, byte[] Bytes)[] files)
    {
        Directory.CreateDirectory(Path.Combine(_root, "audio"));
        var entries = files.Select(f =>
        {
            File.WriteAllBytes(Path.Combine(_root, "audio", $"{f.Id}.ogg"), f.Bytes);
            return $"\"{f.Id}\": {{ \"file\": \"{f.Id}.ogg\", \"sha256\": \"{Convert.ToHexStringLower(SHA256.HashData(f.Bytes))}\" }}";
        });
        File.WriteAllText(Path.Combine(_root, "audio", "manifest.json"), $$"""{ "audio": { {{string.Join(", ", entries)}} } }""");
        return ArtCheck.Run(_root, BasePack.Pack);
    }

    [Fact]
    public void A_good_sound_and_a_good_track_pass()
    {
        var check = Check(("sfx.hit", Ogg(1, 44100, 44100)), ("music.explore", Ogg(2, 48000, 48000 * 120)));
        Assert.DoesNotContain(check.Problems, p => p.Error);
        Assert.Contains("audio/sfx.hit", check.Entries.Keys);
        Assert.Contains("audio/music.explore", check.Entries.Keys);
    }

    [Fact]
    public void A_long_sound_too_many_channels_or_not_vorbis_is_rejected()
    {
        var check = Check(("sfx.hit", Ogg(1, 44100, 44100 * 7)), ("sfx.miss", Ogg(6, 44100, 44100)),
                          ("music.explore", Ogg(2, 48000, 48000L * 400)), ("sfx.tick", new byte[80]));
        Assert.DoesNotContain(check.Entries.Keys, k => k.StartsWith("audio/", StringComparison.Ordinal));
        Assert.Contains(check.Problems, p => p.Where == "sfx.hit" && p.Message.Contains("at most 5 s"));
        Assert.Contains(check.Problems, p => p.Where == "sfx.miss" && p.Message.Contains("6 channels"));
        Assert.Contains(check.Problems, p => p.Where == "music.explore" && p.Message.Contains("a track"));
        Assert.Contains(check.Problems, p => p.Where == "sfx.tick" && p.Message.Contains("isn't Ogg Vorbis"));
    }
}
