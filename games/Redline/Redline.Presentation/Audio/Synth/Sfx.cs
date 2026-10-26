namespace Redline.Presentation.Audio.Synth;

/// <summary>
/// REDLINE's sound effects, synthesised from code (no samples, no licensing) as 44.1 kHz mono 16-bit clips.
/// Every clip is deterministic (seeded noise), peaks at or below −1 dBFS, and fades in and out to avoid clicks
/// (except <see cref="CrowdBed"/>, which loops seamlessly).
/// </summary>
public static class Sfx
{
    public const int SampleRate = 44100;
    private const int Sr = SampleRate;
    private const double TwoPi = 2 * Math.PI;

    private static float[] Buffer(double seconds) => new float[(int)Math.Round(seconds * Sr)];

    private static Pcm Finish(float[] x, double peakDbfs, double fadeIn = 0.001, double fadeOut = 0.004)
    {
        Dsp.Fade(x, Sr, fadeIn, fadeOut);
        return Pcm.FromFloats(x, Sr, peakDbfs);
    }

    /// <summary>One F1 start-light beep: a clean 880 Hz sine with a hint of octave, 0.22 s.</summary>
    public static Pcm StartBeep()
    {
        var x = Buffer(0.22);
        for (var i = 0; i < x.Length; i++)
        {
            var t = i / (double)Sr;
            var p = TwoPi * 880 * t;
            x[i] = (float)(Math.Sin(p) + 0.1 * Math.Sin(2 * p) + 0.03 * Math.Sin(3 * p));
        }
        return Finish(x, -7, fadeIn: 0.004, fadeOut: 0.04);
    }

    /// <summary>
    /// Lights out: six cars on the limiter launch a few tens of milliseconds apart, bog at the clutch drop, scream up
    /// through the gears with upshift cuts, and recede (falling level, closing low-pass, slight Doppler drop) over a
    /// swelling ground rumble. 2.6 s.
    /// </summary>
    public static Pcm LightsOut()
    {
        const double length = 2.6;
        const int chunk = 64;
        var x = Buffer(length);
        var tmp = new float[chunk];
        double[] gains = [1.0, 0.6, 0.5, 0.42, 0.36, 0.3];
        for (var v = 0; v < gains.Length; v++)
        {
            var voice = new EngineVoice(Sr, seed: 100 + v);
            var delay = 0.03 + v * 0.045;
            var detune = 1 + (v - 2.5) * 0.011;
            for (var start = 0; start < x.Length; start += chunk)
            {
                var n = Math.Min(chunk, x.Length - start);
                var t = (start + n / 2.0) / Sr;
                var (rpm, load) = Launch(t - delay);
                var doppler = 1 - 0.05 * Dsp.SmoothStep(1.2, 2.6, t);
                voice.Fill(tmp.AsSpan(0, n), rpm * detune * doppler, load);
                for (var i = 0; i < n; i++) x[start + i] += (float)(tmp[i] * gains[v] * 0.6);
            }
        }

        var brown = 0.0;
        var rng = new Rng(77);
        var air = new PinkNoise(78);
        var rumbleLp = new Svf(220, 0.7, Sr);
        var rumbleHp = new Svf(35, 0.7, Sr);
        var airBp = new Svf(900, 0.7, Sr);
        var recede = new Svf(14000, 0.7, Sr);
        for (var i = 0; i < x.Length; i++)
        {
            var t = i / (double)Sr;
            var swell = 0.6 + 0.4 * Dsp.SmoothStep(0, 0.4, t);
            var away = 1 - Dsp.SmoothStep(1.4, 2.6, t);
            brown = 0.998 * brown + 0.04 * rng.Bipolar();
            var rumble = rumbleHp.HighPass(rumbleLp.LowPass(brown)) * 0.35 * Dsp.SmoothStep(0, 0.5, t);
            var hiss = airBp.BandPass(air.Next()) * 0.25 * Dsp.SmoothStep(0.05, 0.7, t);
            if (i % 32 == 0) recede.Set(14000 * Math.Pow(1800 / 14000.0, Dsp.SmoothStep(1.2, 2.6, t)), 0.7, Sr);
            x[i] = (float)(recede.LowPass((x[i] + rumble + hiss) * swell) * away);
        }
        return Finish(x, -1.5, fadeIn: 0.012, fadeOut: 0.05);
    }

    /// <summary>Rpm and load of one launching car, <paramref name="t"/> seconds after its clutch drop.</summary>
    private static (double Rpm, double Load) Launch(double t)
    {
        const double limiter = 10800, shift = 12100, ratio = 0.8, bog = 8700, bite = 0.08;
        if (t < 0)
        {
            var bounce = Math.Abs(t * 9 % 1 - 0.5) * 2; // limiter bounce, ~9 Hz
            return (limiter - 500 * bounce, 0.6);
        }
        if (t < bite) return (limiter + (bog - limiter) * (t / bite), 1);
        double[] gears = [0.36, 0.52, 0.68, 0.9, 1.3, 2.0];
        var start = bite;
        var from = bog;
        foreach (var g in gears)
        {
            if (t < start + g)
            {
                var u = (t - start) / g;
                var load = u < 0.04 ? 0.35 : 1; // brief cut while the upshift completes
                return (from + (shift - from) * Math.Pow(u, 0.85), load);
            }
            start += g;
            from = shift * ratio;
        }
        return (shift, 1);
    }

    /// <summary>A soft woodblock tick for a locked answer: damped 1.25 kHz with an inharmonic overtone. 45 ms.</summary>
    public static Pcm Tick()
    {
        var x = Buffer(0.045);
        var rng = new Rng(11);
        var hp = new Svf(3000, 0.7, Sr);
        for (var i = 0; i < x.Length; i++)
        {
            var t = i / (double)Sr;
            var body = Math.Sin(TwoPi * 1250 * t) * Math.Exp(-t / 0.009)
                     + 0.35 * Math.Sin(TwoPi * 2890 * t) * Math.Exp(-t / 0.004);
            var click = hp.HighPass(rng.Bipolar()) * Math.Exp(-t / 0.0006) * 0.5;
            x[i] = (float)(body + click);
        }
        return Finish(x, -9, fadeIn: 0.0004, fadeOut: 0.008);
    }

    /// <summary>
    /// The question whoosh: two layers of noise through resonant band-passes that sweep 250 Hz → 3.6 kHz → 250 Hz,
    /// with a bell-shaped swell peaking just before the middle, over a low air body. 0.75 s.
    /// </summary>
    public static Pcm Whoosh()
    {
        const double length = 0.75;
        var x = Buffer(length);
        var a = new PinkNoise(21);
        var b = new Rng(22);
        var bpA = new Svf(250, 2.2, Sr);
        var bpB = new Svf(400, 2.6, Sr);
        var body = new Svf(150, 0.7, Sr);
        for (var i = 0; i < x.Length; i++)
        {
            var u = i / (double)x.Length;
            var bell = Math.Pow(Math.Sin(Math.PI * Math.Pow(u, 0.8)), 1.2);
            var f = 250 * Math.Pow(3600 / 250.0, bell);
            if (i % 16 == 0)
            {
                bpA.Set(f, 2.2, Sr);
                bpB.Set(f * 1.6, 2.6, Sr);
                body.Set(f * 0.4, 0.7, Sr);
            }
            var white = b.Bipolar();
            var pink = a.Next();
            var s = bpA.BandPass(pink) * 1.0 + bpB.BandPass(white) * 0.35 + body.LowPass(pink) * 0.5;
            x[i] = (float)(s * Math.Pow(bell, 1.6));
        }
        return Finish(x, -4, fadeIn: 0.01, fadeOut: 0.02);
    }

    /// <summary>
    /// Correct answer: C6 then E6 (a rising major third) as bright bells — harmonic partials that decay faster the
    /// higher they are, a soft 6 ms attack and a slightly detuned twin for shimmer. 0.9 s.
    /// </summary>
    public static Pcm CorrectSting()
    {
        var x = Buffer(0.9);
        (double Hz, double At, double Gain)[] notes = [(Dsp.Midi(84), 0, 0.75), (Dsp.Midi(88), 0.1, 1.0)];
        (double Ratio, double Amp)[] partials = [(1, 1), (2, 0.45), (3, 0.2), (4.2, 0.07)];
        foreach (var (hz, at, gain) in notes)
            for (var i = (int)(at * Sr); i < x.Length; i++)
            {
                var t = i / (double)Sr - at;
                double s = 0;
                foreach (var (ratio, amp) in partials)
                {
                    var env = Dsp.AttackDecay(t, 0.006, 0.42 / Math.Pow(ratio, 0.8));
                    s += amp * env * (Math.Sin(TwoPi * hz * ratio * t) + 0.3 * Math.Sin(TwoPi * hz * ratio * 1.0012 * t));
                }
                x[i] += (float)(s * gain);
            }
        return Finish(x, -3, fadeIn: 0.002, fadeOut: 0.12);
    }

    /// <summary>Wrong answer: two band-limited squares at 98 and 103.5 Hz beating against each other over a sub sine, low-passed, sagging slightly. 0.42 s.</summary>
    public static Pcm WrongBuzz()
    {
        const double length = 0.42;
        var x = Buffer(length);
        var a = new BlepOsc();
        var b = new BlepOsc { Phase = 0.37 };
        var lp = new Svf(1600, 0.8, Sr);
        for (var i = 0; i < x.Length; i++)
        {
            var t = i / (double)Sr;
            var sag = 1 - 0.04 * Dsp.SmoothStep(0.1, length, t);
            var s = a.Square(98 * sag, Sr) + b.Square(103.5 * sag, Sr) + 0.6 * Math.Sin(TwoPi * 49 * sag * t);
            var env = Dsp.SmoothStep(0, 0.006, t) * (t < 0.3 ? 1 : Math.Exp(-(t - 0.3) / 0.04));
            x[i] = (float)(lp.LowPass(s) * env);
        }
        return Finish(x, -6, fadeIn: 0.002, fadeOut: 0.01);
    }

    /// <summary>
    /// Tyre screech: very narrow band-passed noise (Q 30) plus a stick-slip tone around 2.4 kHz with its second
    /// harmonic, wobbling at ~6.5 Hz while the pitch falls ~22 %, over a little scrub hiss. 1.2 s.
    /// </summary>
    public static Pcm TyreScreech()
    {
        const double length = 1.2;
        var x = Buffer(length);
        var rng = new Rng(31);
        var flutter = new SmoothNoise(32, 18, Sr);
        var grip = new SmoothNoise(33, 40, Sr);
        var drift = new SmoothNoise(34, 3, Sr);
        var bp1 = new Svf(2400, 30, Sr);
        var bp2 = new Svf(4800, 25, Sr);
        var scrub = new Svf(3500, 0.7, Sr);
        double phase = 0;
        for (var i = 0; i < x.Length; i++)
        {
            var t = i / (double)Sr;
            var u = t / length;
            var wobble = 1 + 0.035 * Math.Sin(TwoPi * 6.5 * t + 0.8 * Math.Sin(TwoPi * 1.3 * t)) + 0.012 * drift.Next();
            var f = 2400 * (1 - 0.22 * u) * wobble;
            if (i % 8 == 0)
            {
                bp1.Set(f, 30, Sr);
                bp2.Set(2 * f, 25, Sr);
            }
            phase += f / Sr;
            phase -= Math.Floor(phase);
            var white = rng.Bipolar();
            var tone = Math.Sin(TwoPi * phase) * (0.6 + 0.4 * grip.Next());
            var s = bp1.BandPass(white) * 2.2 + bp2.BandPass(white) * 0.8 + tone * 0.35 + scrub.HighPass(white) * 0.04;
            var env = Dsp.SmoothStep(0, 0.05, t) * (1 - Dsp.SmoothStep(length - 0.35, length, t)) * (0.8 + 0.2 * flutter.Next());
            x[i] = (float)(s * env);
        }
        return Finish(x, -5, fadeIn: 0.003, fadeOut: 0.02);
    }

    /// <summary>
    /// Crunch: a low thump (95 → 42 Hz) under a broadband impact burst whose low-pass closes from 9 kHz, followed by
    /// scattered carbon-fibre debris clicks, all lightly saturated. 0.65 s.
    /// </summary>
    public static Pcm Crunch()
    {
        var x = Buffer(0.65);
        var rng = new Rng(41);
        var lp = new Svf(9000, 0.8, Sr);
        var crack = new Svf(4000, 0.8, Sr);
        double thumpPhase = 0;
        for (var i = 0; i < x.Length; i++)
        {
            var t = i / (double)Sr;
            var thumpHz = 42 + 53 * Math.Exp(-t / 0.06);
            thumpPhase += thumpHz / Sr;
            var thump = Math.Sin(TwoPi * thumpPhase) * Dsp.AttackDecay(t, 0.002, 0.075) * 0.8;
            if (i % 16 == 0) lp.Set(900 + 8100 * Math.Exp(-t / 0.08), 0.8, Sr);
            var white = rng.Bipolar();
            var impact = lp.LowPass(white) * Dsp.AttackDecay(t, 0.001, 0.09) * 1.5;
            var cracks = crack.HighPass(white) * Dsp.AttackDecay(t - 0.035, 0.001, 0.03) * 0.5;
            x[i] = (float)(thump + impact + cracks);
        }

        // Debris: short band-passed bursts, denser and louder early.
        var debris = new Rng(42);
        for (var d = 0; d < 16; d++)
        {
            var at = 0.02 + 0.43 * Math.Pow(debris.NextDouble(), 1.6);
            var hz = debris.Range(1500, 6000);
            var tau = debris.Range(0.002, 0.008);
            var amp = 0.45 * (1 - at / 0.5) * debris.Range(0.4, 1);
            var bp = new Svf(hz, 3, Sr);
            var start = (int)(at * Sr);
            for (var i = start; i < Math.Min(x.Length, start + (int)(tau * 6 * Sr)); i++)
                x[i] += (float)(bp.BandPass(debris.Bipolar()) * amp * Math.Exp(-(i - start) / (tau * Sr)) * 2);
        }
        for (var i = 0; i < x.Length; i++) x[i] = (float)Math.Tanh(x[i] * 1.4);
        return Finish(x, -1.5, fadeIn: 0.0008, fadeOut: 0.06);
    }

    private static readonly (double F1, double F2, double F3)[] Vowels =
    [
        (750, 1200, 2600), // "ah"
        (550, 1800, 2500), // "eh"
        (500, 900, 2400), // "oh"
        (650, 1650, 2600), // "ay"
        (350, 2100, 2800), // "ee"
    ];

    /// <summary>One shouting fan: a polyBLEP glottal saw through three vowel formants, with a "yeah!" pitch rise and fall.</summary>
    private sealed class Shouter
    {
        private BlepOsc _osc;
        private Svf _f1, _f2, _f3;
        private SmoothNoise _jitter;
        private readonly double _pitch;
        private readonly double _scale;

        public Shouter(ref Rng rng, double minHz, double maxHz)
        {
            _pitch = rng.Range(minHz, maxHz);
            var v = Vowels[(int)(rng.NextDouble() * Vowels.Length)];
            _scale = rng.Range(0.92, 1.12);
            _f1 = new Svf(v.F1 * _scale, 6, Sr);
            _f2 = new Svf(v.F2 * _scale, 8, Sr);
            _f3 = new Svf(v.F3 * _scale, 9, Sr);
            _osc = new BlepOsc { Phase = rng.NextDouble() };
            _jitter = new SmoothNoise((int)(rng.NextDouble() * int.MaxValue), 7, Sr);
        }

        public void SetVowel((double F1, double F2, double F3) v)
        {
            _f1.Set(v.F1 * _scale, 6, Sr);
            _f2.Set(v.F2 * _scale, 8, Sr);
            _f3.Set(v.F3 * _scale, 9, Sr);
        }

        public double Next(double pitchFactor)
        {
            var src = _osc.Saw(_pitch * pitchFactor * (1 + 0.025 * _jitter.Next()), Sr);
            return _f1.BandPass(src) + 0.6 * _f2.BandPass(src) + 0.25 * _f3.BandPass(src);
        }
    }

    /// <summary>
    /// A crowd cheer: 28 shouting voices (vowel formants, rising "yeah!" contours, staggered entries) over a
    /// formant-shaped roar of pink noise, swelling applause and a fan's two-note whistle; rises over 0.5 s, peaks
    /// around 1.2 s and dies away. 3.2 s.
    /// </summary>
    public static Pcm CrowdCheer()
    {
        const double length = 3.2;
        var x = Buffer(length);
        var rng = new Rng(51);

        for (var v = 0; v < 28; v++)
        {
            var shouter = new Shouter(ref rng, v % 3 == 0 ? 220 : 120, v % 3 == 0 ? 420 : 240);
            var onset = rng.Range(0, 0.55);
            var dur = rng.Range(1.0, 2.4);
            var amp = rng.Range(0.4, 1.0);
            var start = (int)(onset * Sr);
            var end = Math.Min(x.Length, start + (int)(dur * Sr));
            for (var i = start; i < end; i++)
            {
                var t = (i - start) / (double)Sr;
                var contour = 1 + 0.18 * Math.Sin(Math.PI * Math.Min(1, t / dur * 1.6)) - 0.1 * Dsp.SmoothStep(dur * 0.6, dur, t);
                var env = Dsp.SmoothStep(0, 0.15, t) * (1 - Dsp.SmoothStep(dur * 0.5, dur, t));
                x[i] += (float)(shouter.Next(contour) * env * amp * 0.12);
            }
        }

        var pink = new PinkNoise(52);
        var white = new Rng(53);
        var roar1 = new Svf(650, 2, Sr);
        var roar2 = new Svf(1150, 2.5, Sr);
        var roar3 = new Svf(2500, 3, Sr);
        var clapBp = new Svf(1800, 0.9, Sr);
        double clapEnv = 0;
        double whistle = 0;
        for (var i = 0; i < x.Length; i++)
        {
            var t = i / (double)Sr;
            var p = pink.Next();
            var roar = roar1.BandPass(p) + 0.7 * roar2.BandPass(p) + 0.3 * roar3.BandPass(p);

            var clapRate = 260 * Dsp.SmoothStep(0.2, 1.0, t);
            if (white.NextDouble() < clapRate / Sr) clapEnv = white.Range(0.3, 1);
            clapEnv *= 0.95;
            var clap = clapBp.BandPass(white.Bipolar() * clapEnv);

            double whistleOut = 0;
            if (t is > 0.7 and < 1.55)
            {
                var wt = t - 0.7;
                var hz = wt < 0.3 ? 2500 + 700 * Dsp.SmoothStep(0, 0.12, wt) : 2600 + 600 * Math.Exp(-(wt - 0.3) / 0.15);
                whistle += hz / Sr;
                whistleOut = Math.Sin(TwoPi * whistle) * 0.05 * Dsp.SmoothStep(0, 0.03, wt) * (1 - Dsp.SmoothStep(0.65, 0.85, wt))
                             * (wt is > 0.27 and < 0.33 ? 0.2 : 1);
            }

            var swell = Dsp.SmoothStep(0, 0.5, t) * (t < 1.2 ? 1 : Math.Exp(-(t - 1.2) / 0.9));
            x[i] = (float)((x[i] + roar * 0.35 + clap * 0.5) * swell + whistleOut);
        }
        return Finish(x, -3, fadeIn: 0.02, fadeOut: 0.3);
    }

    /// <summary>
    /// A stadium murmur that loops seamlessly: 24 distant talkers babbling vowel syllables over a slowly breathing
    /// formant-shaped pink-noise bed, softened by a 3.2 kHz low-pass. The loop end is picked (within 50 ms of
    /// <paramref name="seconds"/>) where the level is steadiest, and the material after it is equal-power crossfaded into the head.
    /// </summary>
    public static Pcm CrowdBed(double seconds)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(seconds, 1.0);
        const double crossfade = 0.6;
        var n = (int)Math.Round(seconds * Sr);
        var xf = (int)(crossfade * Sr);
        var y = new float[n + xf];
        var rng = new Rng(61);

        for (var v = 0; v < 24; v++)
        {
            var talker = new Shouter(ref rng, v % 3 == 0 ? 190 : 100, v % 3 == 0 ? 300 : 170);
            var level = rng.Range(0.3, 1.0);
            var i = (int)(rng.Range(0, 0.4) * Sr);
            while (i < y.Length)
            {
                talker.SetVowel(Vowels[(int)(rng.NextDouble() * Vowels.Length)]);
                var syl = (int)(rng.Range(0.09, 0.26) * Sr);
                var bend = rng.Range(-0.08, 0.08);
                for (var j = 0; j < syl && i + j < y.Length; j++)
                {
                    var u = j / (double)syl;
                    var env = Math.Sin(Math.PI * u);
                    y[i + j] += (float)(talker.Next(1 + bend * u) * env * level * 0.07);
                }
                i += syl + (int)(rng.Range(0.02, 0.3) * Sr);
            }
        }

        var pink = new PinkNoise(62);
        var breathe = new SmoothNoise(63, 0.4, Sr);
        var b1 = new Svf(500, 0.8, Sr);
        var b2 = new Svf(1300, 0.9, Sr);
        var lp = new Svf(3200, 0.7, Sr);
        for (var i = 0; i < y.Length; i++)
        {
            var p = pink.Next();
            var bed = (b1.BandPass(p) + 0.6 * b2.BandPass(p)) * 0.3 * (1 + 0.15 * breathe.Next());
            y[i] = (float)lp.LowPass(y[i] + bed);
        }

        // Loop point: of the ends within 50 ms of the requested length, take the one where the level is steadiest
        // (10 ms either side most alike), then equal-power crossfade the material after it into the head.
        var w = Sr / 100;
        var best = n;
        var bestMismatch = double.MaxValue;
        for (var end = n; end > n - Sr / 20; end -= 8)
        {
            var mismatch = Math.Abs(Math.Log(Rms(y, end - w, w) / Rms(y, end, w)));
            if (mismatch < bestMismatch) (best, bestMismatch) = (end, mismatch);
        }
        var x = new float[best];
        Array.Copy(y, x, best);
        for (var i = 0; i < xf; i++)
        {
            var u = (i + 0.5) / xf * Math.PI / 2;
            x[i] = (float)(y[i] * Math.Sin(u) + y[best + i] * Math.Cos(u));
        }
        return Pcm.FromFloats(x, Sr, -8);
    }

    private static double Rms(float[] x, int start, int count)
    {
        double sum = 1e-12;
        for (var i = start; i < start + count; i++) sum += x[i] * (double)x[i];
        return Math.Sqrt(sum / count);
    }

    /// <summary>
    /// The chequered-flag fanfare in C: G, G, C, then the heroic ♭VI–♭VII–I (A♭, B♭, C) cadence, voiced as brass
    /// (additive saw-like harmonics whose brightness follows loudness, a pitch scoop into each note, ensemble detune,
    /// delayed vibrato on long notes) over a bass line, with a soft cymbal and kick on the final chord. 2.9 s.
    /// </summary>
    public static Pcm Fanfare()
    {
        const double length = 2.9;
        var x = Buffer(length);
        (double At, double Dur, int[] Notes)[] chords =
        [
            (0.00, 0.12, [43, 55, 59, 62, 67]),
            (0.16, 0.12, [43, 55, 59, 62, 67]),
            (0.32, 0.42, [48, 60, 64, 67, 72]),
            (0.80, 0.28, [44, 56, 60, 63, 68]),
            (1.12, 0.28, [46, 58, 62, 65, 70]),
            (1.44, 1.0, [36, 48, 60, 64, 67, 72, 76]),
        ];
        foreach (var (at, dur, notes) in chords)
        {
            var final = at > 1.4;
            foreach (var note in notes)
            {
                var gain = note < 50 ? 0.7 : note > 74 ? 0.55 : 1.0;
                BrassNote(x, Dsp.Midi(note), at, dur, release: final ? 0.45 : 0.07, swell: final, gain, seed: note * 13 + (int)(at * 100));
            }
        }

        // Cymbal and kick on the final chord.
        var rng = new Rng(71);
        var hp = new Svf(6000, 0.7, Sr);
        var start = (int)(1.44 * Sr);
        for (var i = start; i < x.Length; i++)
        {
            var t = (i - start) / (double)Sr;
            var cymbal = hp.HighPass(rng.Bipolar()) * Dsp.AttackDecay(t, 0.002, 0.5) * 0.1;
            var kick = Math.Sin(TwoPi * (55 * t + 40 * 0.03 * (1 - Math.Exp(-t / 0.03)))) * Dsp.AttackDecay(t, 0.002, 0.12) * 0.5;
            x[i] += (float)(cymbal + kick);
        }
        return Finish(x, -2, fadeIn: 0.003, fadeOut: 0.08);
    }

    private static void BrassNote(float[] x, double hz, double at, double dur, double release, bool swell, double gain, int seed)
    {
        var start = (int)(at * Sr);
        var end = Math.Min(x.Length, (int)((at + dur + release * 5) * Sr));
        var harmonics = Math.Clamp((int)(9000 / hz), 1, 24);
        var rng = new Rng(seed);
        double[] detunes = [1 - 0.003, 1 + 0.0035];
        var phases = new[] { rng.NextDouble(), rng.NextDouble() };
        var weights = new double[harmonics + 1];
        for (var i = start; i < end; i++)
        {
            var t = (i - start) / (double)Sr;
            var attack = Dsp.SmoothStep(0, 0.03, t) * (1 + 0.15 * Math.Exp(-Math.Pow((t - 0.035) / 0.02, 2)));
            var sustain = swell ? 0.85 + 0.25 * Dsp.SmoothStep(0, 0.9, t) : 1;
            var rel = t < dur ? 1 : Math.Exp(-(t - dur) / release);
            var env = attack * sustain * rel;
            if ((i - start) % 16 == 0)
            {
                // Brass: the louder the note, the more harmonics speak.
                var bright = 0.8 + 9 * Math.Pow(Math.Min(env, 1), 1.5);
                for (var k = 1; k <= harmonics; k++) weights[k] = Math.Exp(-(k - 1) / bright) / Math.Pow(k, 0.6);
            }
            var vibrato = 1 + 0.005 * Dsp.SmoothStep(0.25, 0.5, t) * Math.Sin(TwoPi * 5.3 * t);
            var scoop = 1 - 0.02 * Math.Exp(-t / 0.03);
            double s = 0;
            for (var d = 0; d < 2; d++)
            {
                phases[d] += hz * detunes[d] * vibrato * scoop / Sr;
                phases[d] -= Math.Floor(phases[d]);
                var theta = TwoPi * phases[d];
                var s1 = Math.Sin(theta);
                var c2 = 2 * Math.Cos(theta);
                double prev = 0, cur = s1, sum = s1;
                for (var k = 2; k <= harmonics; k++)
                {
                    var next = c2 * cur - prev;
                    prev = cur;
                    cur = next;
                    sum += next * weights[k];
                }
                s += sum;
            }
            x[i] += (float)(s * env * gain * 0.08);
        }
    }

    /// <summary>The lock-in latch: a crisp double click ("click-CLACK") of high-passed noise and damped 3.2 / 2.6 kHz pings, 18 ms apart. 60 ms.</summary>
    public static Pcm LockClick()
    {
        var x = Buffer(0.06);
        var rng = new Rng(81);
        var hp = new Svf(2500, 0.8, Sr);
        (double At, double Hz, double Amp)[] hits = [(0.001, 3200, 0.6), (0.019, 2600, 1.0)];
        for (var i = 0; i < x.Length; i++)
        {
            var t = i / (double)Sr;
            var noise = hp.HighPass(rng.Bipolar());
            double s = 0;
            foreach (var (at, hz, amp) in hits)
            {
                var dt = t - at;
                if (dt < 0) continue;
                s += amp * (noise * Math.Exp(-dt / 0.0012) * 0.8 + Math.Sin(TwoPi * hz * dt) * Math.Exp(-dt / 0.004));
            }
            x[i] = (float)s;
        }
        return Finish(x, -6, fadeIn: 0.0004, fadeOut: 0.01);
    }

    /// <summary>A crisp countdown tick: a C7 (2.09 kHz) pip with a 14 ms decay and a short noise transient on top. 80 ms.</summary>
    public static Pcm CountdownTick()
    {
        var x = Buffer(0.08);
        var rng = new Rng(91);
        var hp = new Svf(4000, 0.7, Sr);
        for (var i = 0; i < x.Length; i++)
        {
            var t = i / (double)Sr;
            var pip = (Math.Sin(TwoPi * 2093 * t) + 0.2 * Math.Sin(TwoPi * 4186 * t)) * Dsp.AttackDecay(t, 0.0005, 0.014);
            var click = hp.HighPass(rng.Bipolar()) * Math.Exp(-t / 0.0008) * 0.4;
            x[i] = (float)(pip + click);
        }
        return Finish(x, -7, fadeIn: 0.0003, fadeOut: 0.02);
    }
}
