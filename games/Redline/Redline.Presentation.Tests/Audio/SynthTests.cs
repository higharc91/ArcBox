using Redline.Presentation.Audio.Synth;

namespace Redline.Presentation.Tests.Audio;

public class SynthTests
{
    private const int Rate = 44100;
    private static readonly double MaxSample = short.MaxValue * Math.Pow(10, -1 / 20.0); // −1 dBFS

    public static TheoryData<string, double> Sounds => new()
    {
        { nameof(Sfx.StartBeep), 0.22 },
        { nameof(Sfx.LightsOut), 2.6 },
        { nameof(Sfx.Tick), 0.045 },
        { nameof(Sfx.Whoosh), 0.75 },
        { nameof(Sfx.CorrectSting), 0.9 },
        { nameof(Sfx.WrongBuzz), 0.42 },
        { nameof(Sfx.TyreScreech), 1.2 },
        { nameof(Sfx.Crunch), 0.65 },
        { nameof(Sfx.CrowdCheer), 3.2 },
        { nameof(Sfx.CrowdBed), 6.0 },
        { nameof(Sfx.Fanfare), 2.9 },
        { nameof(Sfx.LockClick), 0.06 },
        { nameof(Sfx.CountdownTick), 0.08 },
        { nameof(ChampagneSfx.CorkPop), 0.35 },
        { nameof(ChampagneSfx.SprayHiss), 2.6 },
    };

    private static Pcm Make(string name) => name switch
    {
        nameof(Sfx.StartBeep) => Sfx.StartBeep(),
        nameof(Sfx.LightsOut) => Sfx.LightsOut(),
        nameof(Sfx.Tick) => Sfx.Tick(),
        nameof(Sfx.Whoosh) => Sfx.Whoosh(),
        nameof(Sfx.CorrectSting) => Sfx.CorrectSting(),
        nameof(Sfx.WrongBuzz) => Sfx.WrongBuzz(),
        nameof(Sfx.TyreScreech) => Sfx.TyreScreech(),
        nameof(Sfx.Crunch) => Sfx.Crunch(),
        nameof(Sfx.CrowdCheer) => Sfx.CrowdCheer(),
        nameof(Sfx.CrowdBed) => Sfx.CrowdBed(6.0),
        nameof(Sfx.Fanfare) => Sfx.Fanfare(),
        nameof(Sfx.LockClick) => Sfx.LockClick(),
        nameof(Sfx.CountdownTick) => Sfx.CountdownTick(),
        nameof(ChampagneSfx.CorkPop) => ChampagneSfx.CorkPop(),
        nameof(ChampagneSfx.SprayHiss) => ChampagneSfx.SprayHiss(),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    private static double Rms(ReadOnlySpan<short> s)
    {
        double sum = 0;
        foreach (var v in s) sum += (double)v * v;
        return Math.Sqrt(sum / s.Length) / short.MaxValue;
    }

    [Theory]
    [MemberData(nameof(Sounds))]
    public void Every_sound_is_audible_44k_mono_and_the_expected_length(string name, double seconds)
    {
        var pcm = Make(name);

        Assert.Equal(Rate, pcm.SampleRate);
        Assert.NotEmpty(pcm.Samples);
        Assert.InRange(pcm.Seconds, seconds * 0.9, seconds * 1.1);
        Assert.True(Rms(pcm.Samples) > 0.003, $"{name} is near-silent");
    }

    [Theory]
    [MemberData(nameof(Sounds))]
    public void Every_sound_peaks_at_or_below_minus_1_dBFS(string name, double _)
    {
        var pcm = Make(name);

        var peak = pcm.Samples.Max(s => Math.Abs((int)s));
        Assert.True(peak <= MaxSample, $"{name} peak {pcm.PeakDbfs:F2} dBFS");
        Assert.True(pcm.PeakDbfs <= -1.0);
        Assert.True(pcm.PeakDbfs > -30.0, $"{name} peak {pcm.PeakDbfs:F2} dBFS is too quiet");
    }

    [Theory]
    [MemberData(nameof(Sounds))]
    public void Every_sound_is_deterministic(string name, double _)
    {
        Assert.Equal(Make(name).Samples, Make(name).Samples);
    }

    [Theory]
    [MemberData(nameof(Sounds))]
    public void One_shots_start_and_end_near_silence(string name, double _)
    {
        if (name == nameof(Sfx.CrowdBed)) return; // a loop, tested below
        var s = Make(name).Samples;

        Assert.True(Math.Abs((int)s[0]) < 0.02 * short.MaxValue, $"{name} starts with a click");
        Assert.True(Math.Abs((int)s[^1]) < 0.02 * short.MaxValue, $"{name} ends with a click");
    }

    [Fact]
    public void Float_conversion_drops_NaN_and_normalises_to_the_requested_peak()
    {
        var pcm = Pcm.FromFloats([0f, float.NaN, 2f, -3f, 0.5f], Rate, peakDbfs: -1);

        Assert.Equal(5, pcm.Samples.Length);
        Assert.Equal(0, pcm.Samples[1]);
        Assert.InRange(pcm.PeakDbfs, -1.01, -1.0);
        Assert.Equal(-pcm.Samples[3] * 2 / 3.0, pcm.Samples[2], 0);
    }

    [Fact]
    public void Float_conversion_never_exceeds_minus_1_dBFS_even_when_asked_louder()
    {
        var pcm = Pcm.FromFloats([0f, 1f, -1f], Rate, peakDbfs: 0);

        Assert.True(pcm.PeakDbfs <= -1.0);
    }

    [Fact]
    public void Silence_converts_to_silence()
    {
        Assert.All(Pcm.FromFloats(new float[10], Rate, peakDbfs: -3).Samples, s => Assert.Equal(0, s));
    }

    [Fact]
    public void Pcm_bytes_are_little_endian_16_bit()
    {
        var bytes = new Pcm([1, -2, 0x1234], Rate).ToBytes();

        Assert.Equal([0x01, 0x00, 0xFE, 0xFF, 0x34, 0x12], bytes);
    }

    [Theory]
    [InlineData(3.0)]
    [InlineData(6.0)]
    [InlineData(10.0)]
    public void Crowd_bed_loops_seamlessly(double seconds)
    {
        var s = Sfx.CrowdBed(seconds).Samples;
        var w = Rate / 100; // 10 ms

        var head = Rms(s.AsSpan(0, w));
        var tail = Rms(s.AsSpan(s.Length - w, w));
        Assert.InRange(head / tail, 0.8, 1.25);

        // The wrap point is no louder a step than the signal's own typical sample step.
        double meanStep = 0;
        for (var i = 1; i < s.Length; i++) meanStep += Math.Abs(s[i] - s[i - 1]);
        meanStep /= s.Length - 1;
        Assert.True(Math.Abs(s[0] - s[^1]) < 6 * meanStep, "loop seam clicks");
    }

    [Fact]
    public void Engine_voice_is_continuous_across_fill_calls()
    {
        var voice = new EngineVoice(Rate);
        var a = new float[512];
        var b = new float[512];
        voice.Fill(a, 11000, 0.9);
        voice.Fill(b, 11500, 1.0);

        Assert.True(Math.Abs(b[0] - a[^1]) <= 0.2, $"step {Math.Abs(b[0] - a[^1]):F3} at the boundary");
        Assert.All(a.Concat(b), v => Assert.True(float.IsFinite(v) && Math.Abs(v) <= 1f));
    }

    [Fact]
    public void Engine_voice_split_fills_match_one_big_fill()
    {
        var whole = new float[3000];
        new EngineVoice(Rate).Fill(whole, 9000, 0.7);

        var split = new float[3000];
        var voice = new EngineVoice(Rate);
        voice.Fill(split.AsSpan(0, 1), 9000, 0.7);
        voice.Fill(split.AsSpan(1, 1000), 9000, 0.7);
        voice.Fill(split.AsSpan(1001, 37), 9000, 0.7);
        voice.Fill(split.AsSpan(1038), 9000, 0.7);

        for (var i = 0; i < whole.Length; i++) Assert.Equal(whole[i], split[i], 5);
    }

    [Fact]
    public void Engine_voice_fill_boundaries_are_seamless_through_gear_changes_and_lifts()
    {
        var voice = new EngineVoice(Rate);
        var buf = new float[256];
        float last = 0;
        double maxInside = 0, maxBoundary = 0;
        for (var block = 0; block < 400; block++)
        {
            var rpm = block % 40 < 2 ? 9000 : 12000 - 30 * (block % 40); // upshift drops
            var load = block % 50 < 40 ? 1.0 : 0.0; // lifts and re-applies throttle
            voice.Fill(buf, rpm, load);
            if (block > 0) maxBoundary = Math.Max(maxBoundary, Math.Abs(buf[0] - last));
            for (var i = 1; i < buf.Length; i++) maxInside = Math.Max(maxInside, Math.Abs(buf[i] - buf[i - 1]));
            Assert.All(buf, v => Assert.True(float.IsFinite(v) && Math.Abs(v) <= 1f));
            last = buf[^1];
        }

        Assert.True(maxBoundary <= maxInside, $"boundary step {maxBoundary:F3} > in-buffer max {maxInside:F3}");
        Assert.True(maxInside < 0.5, $"step {maxInside:F3}");
    }

    [Theory]
    [InlineData(0.2)]
    [InlineData(0.6)]
    [InlineData(1.0)]
    public void Engine_pitch_rises_with_rpm(double load)
    {
        var low = ZeroCrossingRate(6000, load);
        var mid = ZeroCrossingRate(9000, load);
        var high = ZeroCrossingRate(12000, load);

        Assert.True(low < mid && mid < high, $"zcr {low:F0} / {mid:F0} / {high:F0}");
    }

    [Fact]
    public void Engine_is_brighter_under_load()
    {
        static double Brightness(double load)
        {
            var voice = new EngineVoice(Rate);
            var buf = new float[Rate];
            voice.Fill(buf, 10000, load);
            // Ratio of first-difference energy to signal energy: a crude spectral centroid.
            double d = 0, e = 0;
            for (var i = Rate / 4 + 1; i < buf.Length; i++)
            {
                d += (buf[i] - buf[i - 1]) * (buf[i] - buf[i - 1]);
                e += buf[i] * buf[i];
            }
            return d / e;
        }

        Assert.True(Brightness(1.0) > Brightness(0.1) * 1.2);
    }

    [Fact]
    public void Engine_voices_with_different_seeds_differ()
    {
        var a = new float[2048];
        var b = new float[2048];
        new EngineVoice(Rate, seed: 1).Fill(a, 10000, 0.8);
        new EngineVoice(Rate, seed: 2).Fill(b, 10000, 0.8);

        Assert.NotEqual(a, b);
    }

    private static double ZeroCrossingRate(double rpm, double load)
    {
        var voice = new EngineVoice(Rate);
        var buf = new float[Rate];
        voice.Fill(buf, rpm, load);
        var crossings = 0;
        for (var i = Rate / 4 + 1; i < buf.Length; i++)
            if (buf[i - 1] < 0 != buf[i] < 0) crossings++;
        return crossings / 0.75;
    }
}
