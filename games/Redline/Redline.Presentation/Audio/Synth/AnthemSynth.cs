namespace Redline.Presentation.Audio.Synth;

/// <summary>
/// Plays an <see cref="AnthemInfo"/> as a brass band: a one-second timpani roll (with a suspended-cymbal swell) on the
/// first chord's root, then cornets on the melody doubled an octave down by euphoniums, horns sustaining the anthem's
/// chord list in close position and a tuba on the chord's bass note; a slight crescendo and ritardando into the last
/// note, a timpani stroke and cymbal on the final chord, and a hall. Everything is our own synthesis (the brass voice
/// is <see cref="Sfx"/>'s fanfare brass) and our own setting of the public-domain melody.
/// </summary>
public static class AnthemSynth
{
    private const int Sr = Sfx.SampleRate;
    private const double TwoPi = 2 * Math.PI;

    /// <summary>Seconds of timpani roll before the band comes in.</summary>
    public const double LeadInSeconds = 1.0;

    /// <summary>Seconds after the last note's written end: its release and the hall.</summary>
    private const double TailSeconds = 1.4;

    /// <summary>Over the last bar, each beat stretches by up to this fraction.</summary>
    private const double Ritardando = 0.2;

    /// <summary>
    /// Renders the anthem's opening phrase as 44.1 kHz mono, peaking at −1.5 dBFS. If the phrase would run past
    /// <paramref name="maxSeconds"/>, it ends early on the last note that fits, slowed and faded as a phrase end.
    /// </summary>
    /// <exception cref="ArgumentException">The anthem is a fallback with no melody (play the victory theme instead).</exception>
    public static Pcm Render(AnthemInfo anthem, double maxSeconds = 16)
    {
        ArgumentNullException.ThrowIfNull(anthem);
        if (!anthem.HasMelody || anthem.Chords is null)
            throw new ArgumentException($"{anthem.Country} has no anthem melody ({anthem.Status}); play the victory theme instead.", nameof(anthem));
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSeconds, 4.0);

        var melody = anthem.Melody!;
        var spb = 60.0 / anthem.Bpm;
        var barBeats = BarBeats(anthem.Meter);

        // Play as many notes as fit, ending on a note (not a rest).
        var count = melody.Count;
        Timeline time;
        while (true)
        {
            while (count > 1 && melody[count - 1].Midi == 0) count--;
            var endBeat = melody.Take(count).Sum(n => n.Beats);
            time = new Timeline(spb, barBeats, endBeat);
            if (time.Seconds(endBeat) + TailSeconds <= maxSeconds || count == 1) break;
            count--;
        }

        var end = time.EndBeat;
        var x = new float[(int)Math.Round((time.Seconds(end) + TailSeconds) * Sr)];
        double Dynamic(double beat) => 0.85 + 0.25 * Dsp.SmoothStep(end - 2 * barBeats, end, beat);

        // Melody: cornets, doubled an octave down.
        double at = 0;
        for (var i = 0; i < count; i++)
        {
            var note = melody[i];
            var start = at;
            at += note.Beats;
            if (note.Midi == 0) continue;
            var final = i == count - 1;
            var t0 = time.Seconds(start);
            var length = time.Seconds(at) - t0;
            var dur = final ? length : length - Math.Min(0.045, 0.2 * length);
            var gain = Dynamic(start) * (final ? 1.05 : 1);
            Sfx.BrassNote(x, Dsp.Midi(note.Midi), t0, dur, release: final ? 0.5 : 0.06, swell: final, gain * 1.3, seed: 1000 + i, brightness: 0.95, maxHarmonics: 20);
            Sfx.BrassNote(x, Dsp.Midi(note.Midi - 12), t0, dur, release: final ? 0.5 : 0.07, swell: final, gain * 0.3, seed: 2000 + i, brightness: 0.7, vibrato: 0.003, maxHarmonics: 12);
        }

        // Accompaniment: horns in close position under the melody, tuba on the bass note.
        at = 0;
        var lastChordStart = 0.0;
        int? firstRoot = null;
        int? finalRoot = null;
        for (var c = 0; c < anthem.Chords.Count && at < end - 1e-9; c++)
        {
            var chord = anthem.Chords[c];
            var start = at;
            at = Math.Min(end, at + chord.Beats);
            var tones = Anthems.ChordTones(chord.Symbol);
            if (tones is not { } chordTones) continue;
            var final = at >= end - 1e-9;
            var t0 = time.Seconds(start);
            var length = time.Seconds(at) - t0;
            var dur = final ? length : length - Math.Min(0.05, 0.15 * length);
            var gain = Dynamic(start);
            foreach (var pc in chordTones.Tones)
            {
                var midi = Place(pc, 53);
                Sfx.BrassNote(x, Dsp.Midi(midi), t0, dur, release: final ? 0.6 : 0.09, swell: final, gain * 0.24, seed: 3000 + 16 * c + pc, brightness: 0.55, vibrato: 0, maxHarmonics: 10);
            }
            var bass = Place(chordTones.Bass, 36);
            Sfx.BrassNote(x, Dsp.Midi(bass), t0, dur, release: final ? 0.6 : 0.08, swell: final, gain * 0.5, seed: 4000 + c, brightness: 0.5, vibrato: 0, maxHarmonics: 10);
            firstRoot ??= chordTones.Tones[0];
            finalRoot = chordTones.Tones[0];
            lastChordStart = start;
        }

        // Timpani roll into the band, and a stroke with a cymbal on the final chord.
        var rng = new Rng(anthem.Id.Aggregate(17, (h, ch) => h * 31 + ch));
        var timpHz = Dsp.Midi(Place(firstRoot ?? 7, 41));
        var strokes = (int)(LeadInSeconds * 15);
        for (var k = 0; k < strokes; k++)
        {
            var u = k / (double)strokes;
            var when = LeadInSeconds * u + rng.Range(-0.004, 0.004);
            Timpani(x, Math.Max(0, when), timpHz, 0.18 + 0.55 * u * u * (k % 2 == 0 ? 1 : 0.9), ref rng, ring: 0.6);
        }
        Timpani(x, LeadInSeconds, timpHz, 0.95, ref rng);
        Cymbal(x, 0, LeadInSeconds, 0.07, ref rng);
        var finalAt = time.Seconds(lastChordStart);
        Timpani(x, finalAt, Dsp.Midi(Place(finalRoot ?? 7, 41)), 0.9, ref rng);
        Cymbal(x, finalAt, 0, 0.11, ref rng);

        Reverb.Apply(x, Sr, wet: 0.28, room: 0.86, damp: 0.35);
        Dsp.Fade(x, Sr, 0.004, 0.35);
        return Pcm.FromFloats(x, Sr, -1.5);
    }

    private static double BarBeats(string meter)
    {
        var parts = meter.Split('/');
        return int.Parse(parts[0]) * 4.0 / int.Parse(parts[1]);
    }

    /// <summary>The MIDI note of pitch class <paramref name="pc"/> in the octave starting at <paramref name="low"/>.</summary>
    private static int Place(int pc, int low) => low + ((pc - low) % 12 + 12) % 12;

    /// <summary>Beat → seconds: the lead-in, then steady beats, with the last bar (or less) gradually slowing.</summary>
    private readonly struct Timeline(double secondsPerBeat, double barBeats, double endBeat)
    {
        private readonly double _ritBeats = Math.Min(barBeats, endBeat / 3);

        public double EndBeat => endBeat;

        public double Seconds(double beat)
        {
            var from = endBeat - _ritBeats;
            var u = _ritBeats > 0 ? Math.Clamp((beat - from) / _ritBeats, 0, 1) : 0;
            return LeadInSeconds + secondsPerBeat * (beat + Ritardando * _ritBeats * u * u / 2);
        }
    }

    // ---- Percussion (shared with the victory theme) ------------------------------------------------------------

    /// <summary>
    /// One timpani stroke: a membrane's inharmonic modes (1, 1.50, 1.74, 2.00, 2.24, 2.49 × the pitch) with a slight
    /// downward pitch settle and a felt-mallet thump.
    /// </summary>
    /// <param name="ring">Seconds until the stroke is damped to silence (strokes inside a roll mask each other, so they can stop early).</param>
    internal static void Timpani(float[] x, double at, double hz, double velocity, ref Rng rng, double ring = 2.5)
    {
        (double Ratio, double Amp, double Tau)[] modes =
            [(1.0, 1.0, 0.9), (1.504, 0.5, 0.5), (1.742, 0.32, 0.35), (2.0, 0.28, 0.4), (2.245, 0.15, 0.25), (2.494, 0.08, 0.2)];
        var start = (int)Math.Round(at * Sr);
        var n = Math.Min(x.Length - start, (int)(ring * Sr));
        var phases = new double[modes.Length];
        for (var m = 0; m < modes.Length; m++) phases[m] = rng.NextDouble();
        var lp = new Svf(900, 0.7, Sr);
        var bright = 0.5 + 0.5 * velocity;
        for (var i = 0; i < n; i++)
        {
            var t = i / (double)Sr;
            var settle = 1 + 0.012 * Math.Exp(-t / 0.04);
            double s = 0;
            for (var m = 0; m < modes.Length; m++)
            {
                phases[m] += hz * modes[m].Ratio * settle / Sr;
                var amp = modes[m].Amp * (m == 0 ? 1 : bright);
                s += amp * Math.Sin(TwoPi * phases[m]) * Math.Exp(-t / modes[m].Tau);
            }
            var thump = lp.LowPass(rng.Bipolar()) * Math.Exp(-t / 0.008) * 1.2;
            var damp = 1 - Dsp.SmoothStep(0.6 * ring, ring, t);
            x[start + i] += (float)((s * Dsp.SmoothStep(0, 0.002, t) + thump) * damp * velocity * 0.32);
        }
    }

    /// <summary>
    /// A suspended or crash cymbal: high-passed noise with a metallic band, swelling over <paramref name="swell"/>
    /// seconds (0 = struck) up to <paramref name="at"/> + <paramref name="swell"/>, then ringing away.
    /// </summary>
    internal static void Cymbal(float[] x, double at, double swell, double level, ref Rng rng, double decay = 1.3)
    {
        var start = (int)Math.Round(at * Sr);
        var peak = swell;
        var n = Math.Min(x.Length - start, (int)((swell + decay * 4) * Sr));
        var hp = new Svf(4500, 0.7, Sr);
        var ring = new Svf(7200, 3, Sr);
        var air = new Svf(11000, 0.7, Sr);
        for (var i = 0; i < n; i++)
        {
            var t = i / (double)Sr;
            var env = t < peak ? Math.Pow(t / peak, 2.2) : Math.Exp(-(t - peak) / decay) * (swell > 0 ? 1 : 1 + 1.5 * Math.Exp(-(t - peak) / 0.05));
            var w = rng.Bipolar();
            var s = hp.HighPass(w) * 0.6 + ring.BandPass(w) * 0.8 + air.HighPass(w) * 0.3;
            x[start + i] += (float)(s * env * level);
        }
    }
}
