namespace Redline.Presentation.Audio.Synth;

/// <summary>A music loop: <see cref="Bars"/> bars of 4/4 at <see cref="Bpm"/>, exactly, so it can loop end to start.</summary>
public sealed record MusicLoop(Pcm Pcm, double Bpm, int Bars);

/// <summary>
/// REDLINE's two music loops, synthesised from code like the sound effects (no samples, no licensing; the CC0 sources
/// in the audio spec were not reachable when this was made). Each note is rendered on its own into a buffer one tail
/// longer than the loop, and the tail is then folded onto the head, so notes still ringing at the end carry on into
/// the start exactly as they would if the loop played twice: the loop has no seam.
/// <list type="bullet">
/// <item><see cref="LobbyLoop"/>: an easy, warm groove for the lobby and Driver Select. 104 BPM, A minor,
/// Am–F–C–G twice: electric-piano chords, a round bass, a soft plucked arpeggio, light kick, clap and hats.</item>
/// <item><see cref="RaceBed"/>: a low bed under the race, ducked under the voice. 118 BPM, E minor, Em–C–Am–B two bars
/// each: a muted, low-passed eighth-note bass pulse, a dark sustained pad and a soft four-on-the-floor kick. No hats,
/// little top end, so it sits under the engines and the commentator.</item>
/// </list>
/// </summary>
public static class Music
{
    private const int Sr = Sfx.SampleRate;
    private const double TwoPi = 2 * Math.PI;
    private const double TailSeconds = 3;

    public static MusicLoop LobbyLoop()
    {
        const double bpm = 104;
        const int bars = 8;
        var beat = 60 / bpm;
        var song = new Song(bars * 4 * beat);

        // Voicings kept close for smooth movement: Am, F, C, G.
        int[][] chords = [[57, 60, 64], [57, 60, 65], [55, 60, 64], [55, 59, 62]];
        int[] roots = [45, 41, 36, 43];
        var rng = new Rng(104);
        for (var bar = 0; bar < bars; bar++)
        {
            var c = bar % 4;
            var t0 = bar * 4 * beat;
            var second = bar >= 4;

            // Electric piano: a stab on 1, a lighter one on the "and" of 2.
            foreach (var n in chords[c])
            {
                song.Add(t0, 3.2, (t, i) => EPiano(t, Dsp.Midi(n)) * 0.16);
                song.Add(t0 + 1.5 * beat, 1.8, (t, i) => EPiano(t, Dsp.Midi(n)) * 0.09);
            }

            // Bass: 1, the "and" of 2, 3, and a passing note on 4 (up to the fifth).
            (double At, int Note, double Len)[] bass =
                [(0, roots[c], 1.4), (1.5, roots[c], 0.45), (2, roots[c], 0.9), (3, roots[c] + 7, 0.9)];
            foreach (var (at, note, len) in bass)
            {
                song.Add(t0 + at * beat, len * beat + 0.3, (t, i) => Bass(t, Dsp.Midi(note), len * beat) * 0.34);
            }

            // Arpeggio: eighths over the chord, an octave up; the second half climbs a little higher.
            int[] pattern = second ? [0, 1, 2, 3, 2, 1, 2, 3] : [0, 1, 2, 1, 0, 1, 2, 1];
            for (var e = 0; e < 8; e++)
            {
                var k = pattern[e];
                var note = (k < 3 ? chords[c][k] : chords[c][0] + 12) + 12;
                var level = (e % 2 == 0 ? 0.07 : 0.05) * (second ? 1.1 : 1);
                song.Add(t0 + e * 0.5 * beat, 0.9, (t, i) => Pluck(t, Dsp.Midi(note)) * level);
            }

            // Drums.
            foreach (var b in new[] { 0, 2 })
            {
                song.Add(t0 + b * beat, 0.5, (t, i) => Kick(t) * 0.45);
            }
            foreach (var b in new[] { 1, 3 })
            {
                var seed = rng.NextDouble();
                song.Add(t0 + b * beat, 0.3, Noise(seed, (t, n) => n * Math.Exp(-t / 0.07) * 0.10, bandHz: 1800));
            }
            for (var e = 0; e < 8; e++)
            {
                var seed = rng.NextDouble();
                var level = e % 2 == 1 ? 0.045 : 0.028;
                song.Add(t0 + e * 0.5 * beat, 0.12, Noise(seed, (t, n) => n * Math.Exp(-t / 0.022) * level, bandHz: 7500));
            }
        }
        return new MusicLoop(song.Render(-3), bpm, bars);
    }

    public static MusicLoop RaceBed()
    {
        const double bpm = 118;
        const int bars = 8;
        var beat = 60 / bpm;
        var song = new Song(bars * 4 * beat);

        // Two bars per chord: Em, C, Am, B (the B major's D# pulls back to E at the loop).
        int[][] pads = [[52, 59, 67], [52, 60, 67], [52, 57, 64], [51, 59, 66]];
        int[] roots = [28, 24, 33, 35];
        for (var section = 0; section < 4; section++)
        {
            var t0 = section * 8 * beat;
            var length = 8 * beat;
            foreach (var n in pads[section])
            {
                song.Add(t0, length + 1.2, (t, i) => Pad(t, Dsp.Midi(n), length) * 0.07);
            }
            for (var e = 0; e < 16; e++)
            {
                var accent = e % 2 == 0 ? 1.0 : 0.7;
                var note = roots[section] + (e % 8 == 7 ? 12 : 0);
                song.Add(t0 + e * 0.5 * beat, 0.5, (t, i) => MutedBass(t, Dsp.Midi(note)) * 0.30 * accent);
            }
            for (var b = 0; b < 8; b++)
            {
                song.Add(t0 + b * beat, 0.5, (t, i) => Kick(t) * 0.34);
            }
        }
        return new MusicLoop(song.Render(-6), bpm, bars);
    }

    // ---- Instruments (t in seconds since the note started) ------------------------------------------------------

    /// <summary>A soft Rhodes-like tone: sine carrier with a decaying 1:1 FM "tine" and a little second harmonic.</summary>
    private static double EPiano(double t, double f)
    {
        var index = 1.6 * Math.Exp(-t / 0.35) + 0.25;
        var tine = Math.Sin(TwoPi * f * t + index * Math.Sin(TwoPi * f * t));
        var body = 0.25 * Math.Sin(TwoPi * 2 * f * t) * Math.Exp(-t / 0.5);
        return (tine + body) * Dsp.AttackDecay(t, 0.004, 1.1);
    }

    /// <summary>A round bass: fundamental and two soft harmonics, held for <paramref name="len"/> then released.</summary>
    private static double Bass(double t, double f, double len)
    {
        var tone = Math.Sin(TwoPi * f * t) + 0.3 * Math.Sin(TwoPi * 2 * f * t) + 0.08 * Math.Sin(TwoPi * 3 * f * t);
        var env = Dsp.AttackDecay(t, 0.006, 0.9) * (t < len ? 1 : Math.Exp(-(t - len) / 0.06));
        return tone * env;
    }

    /// <summary>A plucked triangle-ish tone with a quick decay.</summary>
    private static double Pluck(double t, double f)
    {
        var tone = Math.Sin(TwoPi * f * t) - Math.Sin(TwoPi * 3 * f * t) / 9 + Math.Sin(TwoPi * 5 * f * t) / 25 * Math.Exp(-t / 0.05);
        return tone * Dsp.AttackDecay(t, 0.003, 0.18);
    }

    /// <summary>A dark pad: two slightly detuned sines and an octave below, swelling in and fading after <paramref name="len"/>.</summary>
    private static double Pad(double t, double f, double len)
    {
        var tone = Math.Sin(TwoPi * f * 0.998 * t) + Math.Sin(TwoPi * f * 1.002 * t) + 0.5 * Math.Sin(TwoPi * f * 0.5 * t);
        var swell = Dsp.SmoothStep(0, 1.2, t) * (t < len ? 1 : Math.Exp(-(t - len) / 0.35));
        return tone * swell;
    }

    /// <summary>A palm-muted bass: a few harmonics with the upper ones decaying fast, so each pulse thumps then darkens.</summary>
    private static double MutedBass(double t, double f)
    {
        double tone = 0;
        for (var k = 1; k <= 6; k++)
        {
            tone += Math.Sin(TwoPi * k * f * t) / k * Math.Exp(-t * k * 9);
        }
        return tone * Dsp.AttackDecay(t, 0.004, 0.14);
    }

    /// <summary>A soft kick: a sine sweeping 110 → 45 Hz.</summary>
    private static double Kick(double t)
    {
        var f0 = 45.0;
        var phase = TwoPi * (f0 * t + (110 - f0) * 0.03 * (1 - Math.Exp(-t / 0.03)));
        return Math.Sin(phase) * Dsp.AttackDecay(t, 0.002, 0.16);
    }

    /// <summary>Seeded noise through a one-pole band (low-pass then DC-free), shaped by <paramref name="shape"/>(t, noise).</summary>
    private static Func<double, int, double> Noise(double seed, Func<double, double, double> shape, double bandHz)
    {
        var rng = new Rng((int)(seed * int.MaxValue));
        var a = Math.Exp(-TwoPi * bandHz / Sr);
        double low = 0, prev = 0;
        return (t, i) =>
        {
            var white = rng.Bipolar();
            low = a * low + (1 - a) * white;
            var band = white - low;           // high-passed above bandHz…
            var smooth = 0.5 * (band + prev); // …with the harshest top rolled off
            prev = band;
            return shape(t, smooth);
        };
    }

    // ---- Mixing ------------------------------------------------------------------------------------------------

    /// <summary>A loop being written: notes are summed into a buffer one tail longer, folded onto the head at the end.</summary>
    private sealed class Song(double seconds)
    {
        private readonly int _length = (int)Math.Round(seconds * Sr);
        private readonly float[] _mix = new float[(int)Math.Round(seconds * Sr) + (int)(TailSeconds * Sr)];

        /// <summary>Adds a note starting at <paramref name="at"/> seconds and lasting <paramref name="seconds"/>.</summary>
        /// <param name="voice">Sample at (seconds since the note started, sample index since it started), called in order.</param>
        public void Add(double at, double seconds, Func<double, int, double> voice)
        {
            var start = (int)Math.Round(at * Sr);
            var n = Math.Min((int)(seconds * Sr), _mix.Length - start);
            var fade = Math.Min(n, Sr / 50);   // 20 ms raised-cosine fade at the note's end
            for (var i = 0; i < n; i++)
            {
                var v = voice(i / (double)Sr, i);
                if (i >= n - fade)
                {
                    v *= 0.5 - 0.5 * Math.Cos(Math.PI * (n - i) / fade);
                }
                _mix[start + i] += (float)v;
            }
        }

        public Pcm Render(double peakDbfs)
        {
            var loop = new float[_length];
            for (var i = 0; i < _mix.Length; i++)
            {
                loop[i % _length] += _mix[i];
            }
            return Pcm.FromFloats(loop, Sr, peakDbfs);
        }
    }
}
