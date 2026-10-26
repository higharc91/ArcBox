namespace Redline.Presentation.Audio.Synth;

/// <summary>Deterministic xorshift64* noise, so every synthesised clip is identical run to run.</summary>
internal struct Rng
{
    private ulong _state;

    public Rng(int seed) => _state = 0x9E3779B97F4A7C15UL ^ ((ulong)(uint)seed * 0xBF58476D1CE4E5B9UL) | 1UL;

    /// <summary>Uniform in [0, 1).</summary>
    public double NextDouble()
    {
        _state ^= _state >> 12;
        _state ^= _state << 25;
        _state ^= _state >> 27;
        return ((_state * 0x2545F4914F6CDD1DUL) >> 11) * (1.0 / (1UL << 53));
    }

    /// <summary>Uniform in [-1, 1).</summary>
    public double Bipolar() => 2 * NextDouble() - 1;

    public double Range(double min, double max) => min + (max - min) * NextDouble();
}

/// <summary>Paul Kellett's economy pink-noise filter over white noise; output roughly in [-1, 1].</summary>
internal struct PinkNoise(int seed)
{
    private Rng _rng = new(seed);
    private double _b0, _b1, _b2;

    public double Next()
    {
        var w = _rng.Bipolar();
        _b0 = 0.99765 * _b0 + w * 0.0990460;
        _b1 = 0.96300 * _b1 + w * 0.2965164;
        _b2 = 0.57000 * _b2 + w * 1.0526913;
        return (_b0 + _b1 + _b2 + w * 0.1848) * 0.25;
    }
}

/// <summary>Smooth random wobble in [-1, 1]: a new random target every <c>1/rate</c> s, cosine-interpolated.</summary>
internal struct SmoothNoise
{
    private Rng _rng;
    private readonly double _step;
    private double _t, _a, _b;

    public SmoothNoise(int seed, double rateHz, int sampleRate)
    {
        _rng = new Rng(seed);
        _step = rateHz / sampleRate;
        _a = _rng.Bipolar();
        _b = _rng.Bipolar();
        _t = 0;
    }

    public double Next()
    {
        var mu = (1 - Math.Cos(Math.PI * _t)) * 0.5;
        var v = _a + (_b - _a) * mu;
        _t += _step;
        if (_t >= 1)
        {
            _t -= 1;
            _a = _b;
            _b = _rng.Bipolar();
        }
        return v;
    }
}

/// <summary>
/// Andrew Simper's trapezoidal state-variable filter: stable under per-sample cutoff sweeps.
/// <see cref="Band"/> is normalised to unity gain at the centre frequency.
/// </summary>
internal struct Svf
{
    private double _ic1, _ic2, _a1, _a2, _a3, _k;
    public double Low, Band, High;

    public Svf(double cutoff, double q, int sampleRate) : this() => Set(cutoff, q, sampleRate);

    public void Set(double cutoff, double q, int sampleRate)
    {
        var g = Math.Tan(Math.PI * Math.Clamp(cutoff, 10, sampleRate * 0.49) / sampleRate);
        _k = 1 / Math.Max(q, 0.05);
        _a1 = 1 / (1 + g * (g + _k));
        _a2 = g * _a1;
        _a3 = g * _a2;
    }

    public void Process(double x)
    {
        var v3 = x - _ic2;
        var v1 = _a1 * _ic1 + _a2 * v3;
        var v2 = _ic2 + _a2 * _ic1 + _a3 * v3;
        _ic1 = 2 * v1 - _ic1;
        _ic2 = 2 * v2 - _ic2;
        Low = v2;
        Band = _k * v1;
        High = x - _k * v1 - v2;
    }

    public double LowPass(double x) { Process(x); return Low; }
    public double BandPass(double x) { Process(x); return Band; }
    public double HighPass(double x) { Process(x); return High; }
}

/// <summary>Band-limited (polyBLEP) sawtooth and square oscillator.</summary>
internal struct BlepOsc
{
    public double Phase;

    private static double Blep(double t, double dt)
    {
        if (t < dt) { t /= dt; return t + t - t * t - 1; }
        if (t > 1 - dt) { t = (t - 1) / dt; return t * t + t + t + 1; }
        return 0;
    }

    private void Advance(double dt)
    {
        Phase += dt;
        if (Phase >= 1) Phase -= Math.Floor(Phase);
    }

    public double Saw(double freq, int sampleRate)
    {
        var dt = freq / sampleRate;
        var v = 2 * Phase - 1 - Blep(Phase, dt);
        Advance(dt);
        return v;
    }

    public double Square(double freq, int sampleRate)
    {
        var dt = freq / sampleRate;
        var v = (Phase < 0.5 ? 1 : -1) + Blep(Phase, dt) - Blep((Phase + 0.5) % 1, dt);
        Advance(dt);
        return v;
    }
}

internal static class Dsp
{
    public static double SmoothStep(double edge0, double edge1, double x)
    {
        var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>Raised-cosine fade in and out, in seconds, so one-shots start and end on silence.</summary>
    public static void Fade(Span<float> x, int sampleRate, double inSeconds, double outSeconds)
    {
        var n = Math.Min(x.Length, (int)(inSeconds * sampleRate));
        for (var i = 0; i < n; i++) x[i] *= (float)(0.5 - 0.5 * Math.Cos(Math.PI * i / n));
        var m = Math.Min(x.Length, (int)(outSeconds * sampleRate));
        for (var i = 0; i < m; i++) x[x.Length - 1 - i] *= (float)(0.5 - 0.5 * Math.Cos(Math.PI * i / m));
    }

    /// <summary>Attack-then-exponential-decay envelope: a raised-cosine rise over <paramref name="attack"/> s, then e^(−t/tau).</summary>
    public static double AttackDecay(double t, double attack, double tau)
    {
        if (t < 0) return 0;
        if (t < attack) return 0.5 - 0.5 * Math.Cos(Math.PI * t / attack);
        return Math.Exp(-(t - attack) / tau);
    }

    public static double Midi(double note) => 440 * Math.Pow(2, (note - 69) / 12);

    /// <summary>One-pole DC blocker.</summary>
    public static double DcBlock(double x, ref double x1, ref double y1)
    {
        var y = x - x1 + 0.995 * y1;
        x1 = x;
        y1 = y;
        return y;
    }
}
