namespace Redline.Presentation.Audio.Synth;

/// <summary>
/// A streaming 1.6 L V6 turbo-hybrid engine for an <c>AudioStreamGenerator</c>. A harmonic stack on the half-order
/// (cam) frequency rpm/120, with the firing order (3 per crank turn, harmonic 6) and its multiples emphasised, gives
/// the pitch; half-order harmonics give the growl. Load opens a brightness filter and an exhaust "scream" formant,
/// adds combustion rasp (noise pulsed at the firing rate), intake hiss and a turbo whine, and drives a soft
/// saturator. Each harmonic has a fixed random phase (same spectrum, no saw-tooth edges). Lifting off throttle
/// at high rpm adds sparse overrun pops. Rpm and load are slewed per sample and every
/// oscillator, filter and noise source keeps its state between calls, so any split of <see cref="Fill"/> calls
/// produces the same samples as one big call.
/// </summary>
public sealed class EngineVoice
{
    private const int MaxHarmonics = 120;
    private const int Block = 32;
    private const double MinRpm = 1000;
    private const double MaxRpm = 16000;

    private readonly int _sampleRate;
    private readonly double _rpmSlew;
    private readonly double _loadSlew;
    private readonly double[] _sinWeights = new double[MaxHarmonics + 1];
    private readonly double[] _cosWeights = new double[MaxHarmonics + 1];
    private readonly double[] _harmonicPhase = new double[MaxHarmonics + 1];
    private int _harmonics;
    private double _stackGain;

    private double _phase;
    private double _whinePhase;
    private long _sample;
    private bool _started;

    private Rng _rng;
    private SmoothNoise _pitchJitter;
    private SmoothNoise _levelJitter;
    private Svf _rasp;
    private Svf _hiss;
    private Svf _pop;
    private double _popEnv;
    private double _dcX, _dcY;

    public EngineVoice(int sampleRate, int seed = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 8000);
        _sampleRate = sampleRate;
        _rpmSlew = 1 - Math.Exp(-1 / (0.025 * sampleRate));
        _loadSlew = 1 - Math.Exp(-1 / (0.04 * sampleRate));
        _rng = new Rng(seed * 7919 + 17);
        _pitchJitter = new SmoothNoise(seed * 31 + 1, 22, sampleRate);
        _levelJitter = new SmoothNoise(seed * 31 + 2, 45, sampleRate);
        _hiss = new Svf(6500, 0.7, sampleRate);
        _pop = new Svf(900, 0.9, sampleRate);
        var phases = new Rng(seed);
        _phase = phases.NextDouble();
        // Fixed random phase per harmonic: same spectrum, but no saw-tooth edge (exhaust pulses smear in the pipes).
        for (var k = 1; k <= MaxHarmonics; k++) _harmonicPhase[k] = 2 * Math.PI * phases.NextDouble();
    }

    /// <summary>Smoothed rpm the voice is currently producing.</summary>
    public double Rpm { get; private set; }

    /// <summary>Smoothed load (0 = off throttle, 1 = flat out) the voice is currently producing.</summary>
    public double Load { get; private set; }

    /// <summary>
    /// Writes mono samples in [-1, 1], gliding from the previous rpm/load toward these targets.
    /// Rpm is clamped to 1 000–16 000 and load to 0–1; NaN keeps the previous target.
    /// </summary>
    public void Fill(Span<float> buffer, double rpm, double load)
    {
        rpm = double.IsNaN(rpm) ? Rpm : Math.Clamp(rpm, MinRpm, MaxRpm);
        load = double.IsNaN(load) ? Load : Math.Clamp(load, 0, 1);
        if (!_started)
        {
            Rpm = rpm;
            Load = load;
            _started = true;
        }

        for (var i = 0; i < buffer.Length; i++)
        {
            if (_sample++ % Block == 0) Recompute();
            Rpm += (rpm - Rpm) * _rpmSlew;
            Load += (load - Load) * _loadSlew;
            buffer[i] = (float)Next();
        }
    }

    private double Next()
    {
        var sr = (double)_sampleRate;
        var f0 = Rpm / 120 * (1 + 0.0035 * _pitchJitter.Next());
        _phase += f0 / sr;
        _phase -= Math.Floor(_phase);

        // Harmonic stack via the Chebyshev recurrences x(kθ) = 2cosθ·x((k−1)θ) − x((k−2)θ) for sin and cos.
        var theta = 2 * Math.PI * _phase;
        var s1 = Math.Sin(theta);
        var c1 = Math.Cos(theta);
        var c2 = 2 * c1;
        double sPrev = 0, sCur = s1, cPrev = 1, cCur = c1;
        var stack = _sinWeights[1] * s1 + _cosWeights[1] * c1;
        for (var k = 2; k <= _harmonics; k++)
        {
            var sNext = c2 * sCur - sPrev;
            var cNext = c2 * cCur - cPrev;
            sPrev = sCur;
            sCur = sNext;
            cPrev = cCur;
            cCur = cNext;
            stack += _sinWeights[k] * sNext + _cosWeights[k] * cNext;
        }
        var level = (0.55 + 0.45 * Load) * (1 + 0.04 * _levelJitter.Next());
        stack *= _stackGain * level;

        // Combustion rasp: band noise pulsed at the firing frequency (harmonic 6).
        var pulse = 0.5 + 0.5 * Math.Cos(6 * theta);
        pulse *= pulse;
        var white = _rng.Bipolar();
        var rasp = _rasp.BandPass(white) * (0.25 + 1.6 * pulse * pulse) * (0.03 + 0.11 * Load);
        var hiss = _hiss.BandPass(_rng.Bipolar()) * (0.008 + 0.03 * Load) * Math.Min(1, Rpm / 12000);

        // Turbo whine, rising faster than rpm.
        var whineHz = 2300 * Math.Pow(Rpm / 12000, 1.4);
        _whinePhase += whineHz / sr;
        _whinePhase -= Math.Floor(_whinePhase);
        var whine = Math.Sin(2 * Math.PI * _whinePhase) * 0.012 * (0.3 + 0.7 * Load) * Math.Min(1, Rpm / 9000);

        // Overrun pops when lifting at high rpm.
        var offThrottle = Dsp.SmoothStep(0.3, 0.05, Load) * Dsp.SmoothStep(6000, 9000, Rpm);
        if (offThrottle > 0 && _rng.NextDouble() < 9.0 / sr * offThrottle) _popEnv = 0.6 + 0.4 * _rng.NextDouble();
        _popEnv *= 0.9985;
        var pop = _popEnv > 1e-3 ? _pop.BandPass(white) * _popEnv * 0.8 : _pop.BandPass(0);

        var x = stack + rasp + hiss + whine + pop;
        var drive = 1.1 + 1.4 * Load;
        var y = Math.Tanh(x * drive) / Math.Tanh(drive);
        return 0.42 * Dsp.DcBlock(y, ref _dcX, ref _dcY);
    }

    private void Recompute()
    {
        var sr = (double)_sampleRate;
        var f0 = Rpm / 120;
        var nyquistCap = Math.Min(16000, 0.45 * sr);
        _harmonics = Math.Clamp((int)(nyquistCap / f0), 1, MaxHarmonics);

        var revs = Rpm / 12000;
        var cutoff = (1300 + 5000 * Math.Pow(Load, 1.2)) * Math.Pow(revs, 0.7);
        var screamHz = 2700 * Math.Sqrt(revs);
        double energy = 0;
        for (var k = 1; k <= _harmonics; k++)
        {
            var emphasis = k % 6 == 0 ? 1.0 : k % 3 == 0 ? 0.5 : k % 2 == 0 ? 0.3 : 0.17;
            var f = k * f0;
            var r = f / cutoff;
            var tone = 1 / Math.Sqrt(1 + r * r * r * r);
            var d = (f - screamHz) / 900;
            var scream = 1 + 0.8 * Load * Math.Exp(-d * d);
            var lowCut = f < 60 ? f / 60 : 1;
            var w = emphasis * tone * scream * lowCut / Math.Pow(k, 0.85);
            _sinWeights[k] = w * Math.Cos(_harmonicPhase[k]);
            _cosWeights[k] = w * Math.Sin(_harmonicPhase[k]);
            energy += w * w;
        }
        _stackGain = energy > 0 ? 0.3 / Math.Sqrt(energy / 2) : 0;

        _rasp.Set((1400 + 2600 * Load) * Math.Pow(revs, 0.8), 1.1, _sampleRate);
    }
}
