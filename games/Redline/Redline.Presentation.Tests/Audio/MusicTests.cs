using Redline.Presentation.Audio.Synth;

namespace Redline.Presentation.Tests.Audio;

public class MusicTests
{
    public static TheoryData<string> Loops => [nameof(Music.LobbyLoop), nameof(Music.RaceBed)];

    private static MusicLoop Make(string name) => name == nameof(Music.LobbyLoop) ? Music.LobbyLoop() : Music.RaceBed();

    private static double Rms(ReadOnlySpan<short> s)
    {
        double sum = 0;
        foreach (var v in s) sum += (double)v * v;
        return Math.Sqrt(sum / s.Length) / short.MaxValue;
    }

    [Theory]
    [MemberData(nameof(Loops))]
    public void Loops_are_a_whole_number_of_bars_at_44k_mono(string name)
    {
        var loop = Make(name);

        Assert.Equal(Sfx.SampleRate, loop.Pcm.SampleRate);
        var barSeconds = 4 * 60.0 / loop.Bpm;
        Assert.Equal(loop.Bars * barSeconds, loop.Pcm.Seconds, 2);
        Assert.InRange(loop.Pcm.Seconds, 8, 40);
    }

    [Theory]
    [MemberData(nameof(Loops))]
    public void Loops_peak_at_or_below_minus_1_dBFS_and_are_audible(string name)
    {
        var pcm = Make(name).Pcm;
        Assert.True(pcm.PeakDbfs <= -1.0, $"{name} peak {pcm.PeakDbfs:F2} dBFS");
        Assert.True(Rms(pcm.Samples) > 0.02, $"{name} is near-silent");
    }

    [Theory]
    [MemberData(nameof(Loops))]
    public void Loops_are_deterministic(string name) => Assert.Equal(Make(name).Pcm.Samples, Make(name).Pcm.Samples);

    [Theory]
    [MemberData(nameof(Loops))]
    public void Loops_wrap_without_a_click(string name)
    {
        var s = Make(name).Pcm.Samples;
        double meanStep = 0;
        for (var i = 1; i < s.Length; i++) meanStep += Math.Abs(s[i] - s[i - 1]);
        meanStep /= s.Length - 1;

        Assert.True(Math.Abs(s[0] - s[^1]) < 6 * meanStep + 50, $"{name} seam {Math.Abs(s[0] - s[^1])} vs step {meanStep:F0}");
        // Notes ring on across the seam: neither end of the loop drops out to near-silence.
        var w = Sfx.SampleRate / 20;
        var whole = Rms(s);
        Assert.True(Rms(s.AsSpan(0, w)) > 0.25 * whole, $"{name} head is near-silent");
        Assert.True(Rms(s.AsSpan(s.Length - w, w)) > 0.25 * whole, $"{name} tail is near-silent");
    }

    [Fact]
    public void The_race_bed_sits_lower_and_darker_than_the_lobby_loop()
    {
        var lobby = Music.LobbyLoop().Pcm.Samples;
        var bed = Music.RaceBed().Pcm.Samples;

        Assert.True(Rms(bed) < Rms(lobby), "the race bed is quieter");
        Assert.True(Brightness(bed) < Brightness(lobby), "the race bed has less top end");

        static double Brightness(short[] s)
        {
            // Mean absolute first difference over RMS: a rough high-frequency measure.
            double diff = 0, sum = 0;
            for (var i = 1; i < s.Length; i++)
            {
                diff += Math.Abs(s[i] - s[i - 1]);
                sum += Math.Abs((double)s[i]);
            }
            return diff / sum;
        }
    }
}
