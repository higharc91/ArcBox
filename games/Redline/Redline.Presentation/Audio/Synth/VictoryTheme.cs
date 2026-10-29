namespace Redline.Presentation.Audio.Synth;

/// <summary>
/// REDLINE's own victory theme for the champagne, composed for the game (it imitates no broadcast theme): 128 BPM in
/// D major, eight bars over D–G–Bm–A | D–G–(G, A7)–D. The hook — a held D, a drop to A and a run back up through E to F♯
/// ("DAAA-da da-da DAA") — is stated in bars 1 and 5 and answered higher the second time, climbing to a top D on the
/// final hit. Brass (doubled an octave down, with a thin synth lead on top), off-beat synth-brass stabs, an
/// eighth-note synth bass, a pad, four-on-the-floor kick, snare and clap on 2 and 4, sixteenth hats, a snare build
/// into bar 5, a tom fill into the last bar, and crash, timpani and the full band on the final downbeat, which rings
/// out into silence (so the clip can loop without a click). 16 s.
/// </summary>
public static class VictoryTheme
{
    private const int Sr = Sfx.SampleRate;
    private const double TwoPi = 2 * Math.PI;

    public const double Bpm = 128;
    private const double Beat = 60 / Bpm;
    private const double Bar = 4 * Beat;
    private const double Seconds = 16.0;

    /// <summary>The hook and its answer, in the anthem score notation (D major, 4/4).</summary>
    private const string Lead =
        "D5:q. A4:e D5:e E5:e F#5:q | G5:q. F#5:e E5:q D5:q | F#5:q. E5:e D5:e B4:e D5:q | E5:h. r:q | " +
        "D5:q. A4:e D5:e E5:e F#5:q | B5:q. A5:e G5:q F#5:q | G5:e F#5:e E5:e F#5:e G5:q A5:q | D6:h.";

    private const string Harmony = "D:w | G:w | Bm:w | A:w | D:w | G:w | G:h A7:h | D:h.";

    /// <summary>The 16 s theme as 44.1 kHz mono, peaking at −1.2 dBFS.</summary>
    public static Pcm Render()
    {
        var x = new float[(int)(Seconds * Sr)];
        var rng = new Rng(2026);
        var melody = Anthems.Melody("4/4", Lead);
        var chords = Anthems.Chords("4/4", Harmony);
        var end = melody.Sum(n => n.Beats);

        // Lead: brass hook, an octave of horns under it and a thin synth lead on top.
        double at = 0;
        for (var i = 0; i < melody.Count; i++)
        {
            var (midi, beats) = (melody[i].Midi, melody[i].Beats);
            var t0 = at * Beat;
            at += beats;
            if (midi == 0) continue;
            var final = i == melody.Count - 1;
            var dur = final ? 2 * Beat : beats * Beat - 0.035;
            var accent = (at - beats) % 4 == 0 ? 1.08 : 1;
            Sfx.BrassNote(x, Dsp.Midi(midi), t0, dur, release: final ? 0.45 : 0.05, swell: false, 1.0 * accent, seed: 500 + i, brightness: 1.3, maxHarmonics: 20);
            Sfx.BrassNote(x, Dsp.Midi(midi - 12), t0, dur, release: final ? 0.45 : 0.05, swell: false, 0.5 * accent, seed: 600 + i, brightness: 0.85, vibrato: 0.003, maxHarmonics: 12);
            SynthLead(x, Dsp.Midi(midi), t0, dur, final ? 0.4 : 0.04, 0.16);
        }

        // Harmony: stabs, pad and bass per chord.
        at = 0;
        for (var c = 0; c < chords.Count; c++)
        {
            var chord = chords[c];
            var start = at;
            at += chord.Beats;
            var (bassPc, tones) = Anthems.ChordTones(chord.Symbol)!.Value;
            var final = c == chords.Count - 1;
            var t0 = start * Beat;
            if (final)
            {
                foreach (var pc in tones) Sfx.BrassNote(x, Dsp.Midi(Place(pc, 57)), t0, 2 * Beat, 0.45, swell: false, 0.45, seed: 700 + pc, brightness: 0.8, maxHarmonics: 12);
                Stab(x, Chord(tones, 62), t0, 0.9, 0.4);
                Pad(x, Chord(tones, 50), t0, 2 * Beat, 0.5, 0.18);
                SynthBass(x, Dsp.Midi(Place(bassPc, 38)), t0, 1.2, 0.6, 0.55);
                SynthBass(x, Dsp.Midi(Place(bassPc, 26)), t0, 1.2, 0.6, 0.35);
                continue;
            }

            Pad(x, Chord(tones, 50), t0, chord.Beats * Beat, 0.25, 0.1);
            // Stabs on the beat and on the "and" of 2 (and of 4 on the bar's last chord), pushing the groove.
            for (var b = 0.0; b < chord.Beats; b += 0.5)
            {
                var inBar = (start + b) % 4;
                if (inBar is 0 or 1.5 or 3.5) Stab(x, Chord(tones, 62), (start + b) * Beat, inBar == 0 ? 0.32 : 0.18, inBar == 0 ? 0.32 : 0.24);
            }
            // Driving eighth-note bass, jumping the octave on the off-beat of 4.
            for (var b = 0.0; b < chord.Beats; b += 0.5)
            {
                var inBar = (start + b) % 4;
                var octave = inBar == 3.5 ? 12 : 0;
                SynthBass(x, Dsp.Midi(Place(bassPc, 38) + octave), (start + b) * Beat, 0.42 * Beat, 0.04, inBar % 1 == 0 ? 0.5 : 0.4);
            }
        }

        Drums(x, end, ref rng);
        return Master(x, -1.2, fadeIn: 0.003, fadeOut: 0.6);
    }

    /// <summary>A short brass hit for the moment a spray starts: a two-note pickup into a D major chord with crash and kick. 1.8 s.</summary>
    public static Pcm Sting()
    {
        var x = new float[(int)(1.8 * Sr)];
        var rng = new Rng(77);
        Sfx.BrassNote(x, Dsp.Midi(69), 0.0, 0.055, 0.03, false, 0.7, seed: 1, brightness: 1.2);
        Sfx.BrassNote(x, Dsp.Midi(74), 0.065, 0.055, 0.03, false, 0.8, seed: 2, brightness: 1.2);
        const double hit = 0.13;
        int[] chord = [62, 66, 69, 74, 78];
        foreach (var n in chord) Sfx.BrassNote(x, Dsp.Midi(n), hit, 0.45, 0.32, swell: false, 0.6, seed: n, brightness: 1.2);
        Sfx.BrassNote(x, Dsp.Midi(50), hit, 0.45, 0.32, swell: false, 0.5, seed: 3, brightness: 0.7);
        Stab(x, [62, 66, 69, 74], hit, 0.6, 0.45);
        SynthBass(x, Dsp.Midi(38), hit, 0.5, 0.3, 0.6);
        Kick(x, hit, 1.0);
        Snare(x, hit, 0.6, ref rng);
        AnthemSynth.Cymbal(x, hit, 0, 0.16, ref rng, decay: 0.9);
        AnthemSynth.Timpani(x, hit, Dsp.Midi(38), 0.8, ref rng);
        return Master(x, -1.5, fadeIn: 0.002, fadeOut: 0.35);
    }

    // ---- Drums -------------------------------------------------------------------------------------------------

    private static void Drums(float[] x, double endBeat, ref Rng rng)
    {
        var lastBar = (int)(endBeat / 4); // the final hit's bar
        for (var bar = 0; bar < lastBar; bar++)
        {
            var b0 = bar * 4.0;
            if (bar is 0 or 4) AnthemSynth.Cymbal(x, b0 * Beat, 0, 0.14, ref rng, decay: 1.2);
            for (var beat = 0; beat < 4; beat++)
            {
                var t = (b0 + beat) * Beat;
                var fill = (bar is 3 or 6) && beat == 3;
                var build = bar == 3 && beat >= 2;
                if (!(bar == 6 && beat == 3)) Kick(x, t, beat == 0 ? 1.0 : 0.85);
                if (beat is 1 or 3 && !build && !fill)
                {
                    Snare(x, t, 0.8, ref rng);
                    Clap(x, t, 0.35, ref rng);
                }
                if (build)
                {
                    for (var s = 0; s < 4; s++) Snare(x, t + s * Beat / 4, 0.35 + 0.15 * (beat - 2) + 0.06 * s, ref rng);
                }
                else if (fill)
                {
                    double[] toms = [196, 165, 131, 98];
                    for (var s = 0; s < 4; s++) Tom(x, t + s * Beat / 4, toms[s], 0.75 + 0.05 * s, ref rng);
                }
                if (!fill)
                {
                    for (var s = 0; s < 4; s++) Hat(x, t + s * Beat / 4, s % 2 == 0 ? (s == 0 ? 0.32 : 0.26) : 0.14, ref rng);
                }
            }
        }

        // The final downbeat: everything at once, then let it ring.
        var hit = lastBar * 4 * Beat;
        Kick(x, hit, 1.1);
        Snare(x, hit, 0.9, ref rng);
        AnthemSynth.Cymbal(x, hit, 0, 0.2, ref rng, decay: 1.0);
        AnthemSynth.Timpani(x, hit, Dsp.Midi(38), 1.0, ref rng);
        AnthemSynth.Timpani(x, hit + 0.004, Dsp.Midi(45), 0.6, ref rng);
    }

    /// <summary>A punchy kick: a sine sweeping 160 → 48 Hz with a click on top.</summary>
    private static void Kick(float[] x, double at, double velocity)
    {
        var start = (int)Math.Round(at * Sr);
        var n = Math.Min(x.Length - start, (int)(0.45 * Sr));
        double phase = 0;
        for (var i = 0; i < n; i++)
        {
            var t = i / (double)Sr;
            phase += (48 + 112 * Math.Exp(-t / 0.035)) / Sr;
            var body = Math.Sin(TwoPi * phase) * Dsp.AttackDecay(t, 0.0015, 0.17);
            var click = Math.Sin(TwoPi * 3200 * t) * Math.Exp(-t / 0.002) * 0.3;
            x[start + i] += (float)((body + click) * velocity * 0.9);
        }
    }

    /// <summary>A tight snare: two damped shell modes and a band of rattling wires.</summary>
    private static void Snare(float[] x, double at, double velocity, ref Rng rng)
    {
        var start = (int)Math.Round(at * Sr);
        var n = Math.Min(x.Length - start, (int)(0.35 * Sr));
        var bp = new Svf(3200, 0.8, Sr);
        var hp = new Svf(900, 0.7, Sr);
        for (var i = 0; i < n; i++)
        {
            var t = i / (double)Sr;
            var shell = (Math.Sin(TwoPi * 185 * t) + 0.6 * Math.Sin(TwoPi * 330 * t)) * Dsp.AttackDecay(t, 0.001, 0.05);
            var w = rng.Bipolar();
            var wires = (bp.BandPass(w) * 0.9 + hp.HighPass(w) * 0.5) * Dsp.AttackDecay(t, 0.001, 0.11);
            x[start + i] += (float)((shell * 0.5 + wires) * velocity * 0.55);
        }
    }

    /// <summary>A clap: three quick noise bursts then a short tail, band-passed around 1.4 kHz.</summary>
    private static void Clap(float[] x, double at, double velocity, ref Rng rng)
    {
        var start = (int)Math.Round(at * Sr);
        var n = Math.Min(x.Length - start, (int)(0.25 * Sr));
        var bp = new Svf(1400, 1.2, Sr);
        for (var i = 0; i < n; i++)
        {
            var t = i / (double)Sr;
            var env = t < 0.024 ? Math.Exp(-(t % 0.008) / 0.002) : Math.Exp(-(t - 0.024) / 0.06);
            x[start + i] += (float)(bp.BandPass(rng.Bipolar()) * env * velocity * 1.2);
        }
    }

    /// <summary>A closed hi-hat tick: very high-passed noise, 30 ms.</summary>
    private static void Hat(float[] x, double at, double velocity, ref Rng rng)
    {
        var start = (int)Math.Round(at * Sr);
        var n = Math.Min(x.Length - start, (int)(0.08 * Sr));
        var hp = new Svf(8500, 0.8, Sr);
        for (var i = 0; i < n; i++)
        {
            var t = i / (double)Sr;
            x[start + i] += (float)(hp.HighPass(rng.Bipolar()) * Math.Exp(-t / 0.018) * velocity * 0.5);
        }
    }

    /// <summary>A tom: a sine dropping a fifth onto <paramref name="hz"/> with a little stick noise.</summary>
    private static void Tom(float[] x, double at, double hz, double velocity, ref Rng rng)
    {
        var start = (int)Math.Round(at * Sr);
        var n = Math.Min(x.Length - start, (int)(0.5 * Sr));
        var lp = new Svf(2500, 0.7, Sr);
        double phase = 0;
        for (var i = 0; i < n; i++)
        {
            var t = i / (double)Sr;
            phase += hz * (1 + 0.5 * Math.Exp(-t / 0.03)) / Sr;
            var body = Math.Sin(TwoPi * phase) * Dsp.AttackDecay(t, 0.001, 0.2);
            var stick = lp.LowPass(rng.Bipolar()) * Math.Exp(-t / 0.01) * 0.4;
            x[start + i] += (float)((body + stick) * velocity * 0.75);
        }
    }

    // ---- Synths ------------------------------------------------------------------------------------------------

    /// <summary>A synth-brass stab: two detuned saws per note through a low-pass that snaps shut.</summary>
    private static void Stab(float[] x, int[] notes, double at, double length, double level)
    {
        var start = (int)Math.Round(at * Sr);
        var n = Math.Min(x.Length - start, (int)((length + 0.3) * Sr));
        foreach (var note in notes)
        {
            var hz = Dsp.Midi(note);
            var a = new BlepOsc { Phase = (note * 0.137) % 1 };
            var b = new BlepOsc { Phase = (note * 0.311) % 1 };
            var lp = new Svf(6000, 0.9, Sr);
            for (var i = 0; i < n; i++)
            {
                var t = i / (double)Sr;
                if (i % 16 == 0) lp.Set(900 + 5500 * Math.Exp(-t / 0.09), 0.9, Sr);
                var s = a.Saw(hz * 0.9965, Sr) + b.Saw(hz * 1.0035, Sr);
                var env = Dsp.SmoothStep(0, 0.004, t) * (t < length ? Math.Exp(-t / (length * 1.5)) : Math.Exp(-length / (length * 1.5)) * Math.Exp(-(t - length) / 0.06));
                x[start + i] += (float)(lp.LowPass(s) * env * level * 0.22);
            }
        }
    }

    /// <summary>A warm pad: saws through a fixed low-pass, slow attack, held for <paramref name="length"/>.</summary>
    private static void Pad(float[] x, int[] notes, double at, double length, double release, double level)
    {
        var start = (int)Math.Round(at * Sr);
        var n = Math.Min(x.Length - start, (int)((length + release * 4) * Sr));
        foreach (var note in notes)
        {
            var hz = Dsp.Midi(note);
            var a = new BlepOsc { Phase = (note * 0.71) % 1 };
            var b = new BlepOsc { Phase = (note * 0.29) % 1 };
            var lp = new Svf(1800, 0.7, Sr);
            for (var i = 0; i < n; i++)
            {
                var t = i / (double)Sr;
                var s = a.Saw(hz * 0.997, Sr) + b.Saw(hz * 1.003, Sr);
                var env = Dsp.SmoothStep(0, 0.25, t) * (t < length ? 1 : Math.Exp(-(t - length) / release));
                x[start + i] += (float)(lp.LowPass(s) * env * level * 0.1);
            }
        }
    }

    /// <summary>A synth bass: saw plus a sub sine through a low-pass with a quick envelope.</summary>
    private static void SynthBass(float[] x, double hz, double at, double length, double release, double level)
    {
        var start = (int)Math.Round(at * Sr);
        var n = Math.Min(x.Length - start, (int)((length + release * 5) * Sr));
        var osc = new BlepOsc();
        var lp = new Svf(1200, 1.1, Sr);
        for (var i = 0; i < n; i++)
        {
            var t = i / (double)Sr;
            if (i % 16 == 0) lp.Set(250 + 1400 * Math.Exp(-t / 0.06), 1.1, Sr);
            var s = lp.LowPass(osc.Saw(hz, Sr)) + 0.8 * Math.Sin(TwoPi * hz * t);
            var env = Dsp.SmoothStep(0, 0.003, t) * (t < length ? 1 : Math.Exp(-(t - length) / release));
            x[start + i] += (float)(s * env * level * 0.6);
        }
    }

    /// <summary>A thin square-wave lead an octave above the brass, for a modern edge.</summary>
    private static void SynthLead(float[] x, double hz, double at, double length, double release, double level)
    {
        var start = (int)Math.Round(at * Sr);
        var n = Math.Min(x.Length - start, (int)((length + release * 5) * Sr));
        var osc = new BlepOsc();
        var lp = new Svf(3800, 0.8, Sr);
        for (var i = 0; i < n; i++)
        {
            var t = i / (double)Sr;
            var vib = 1 + 0.004 * Dsp.SmoothStep(0.2, 0.4, t) * Math.Sin(TwoPi * 5.5 * t);
            var s = lp.LowPass(osc.Square(2 * hz * vib, Sr));
            var env = Dsp.SmoothStep(0, 0.01, t) * (t < length ? 1 : Math.Exp(-(t - length) / release));
            x[start + i] += (float)(s * env * level * 0.25);
        }
    }

    // ---- Helpers -----------------------------------------------------------------------------------------------

    private static int Place(int pc, int low) => low + ((pc - low) % 12 + 12) % 12;

    private static int[] Chord(int[] pcs, int low) => [.. pcs.Select(pc => Place(pc, low)).Order()];

    /// <summary>Hall, a gentle tanh bus saturation for density, fades, then normalise.</summary>
    private static Pcm Master(float[] x, double peakDbfs, double fadeIn, double fadeOut)
    {
        Reverb.Apply(x, Sr, wet: 0.16, room: 0.8, damp: 0.4);
        double peak = 0;
        foreach (var v in x) peak = Math.Max(peak, Math.Abs(v));
        if (peak > 0)
        {
            const double drive = 1.8;
            var norm = Math.Tanh(drive);
            for (var i = 0; i < x.Length; i++) x[i] = (float)(Math.Tanh(drive * x[i] / peak) / norm);
        }
        Dsp.Fade(x, Sr, fadeIn, fadeOut);
        return Pcm.FromFloats(x, Sr, peakDbfs);
    }
}
