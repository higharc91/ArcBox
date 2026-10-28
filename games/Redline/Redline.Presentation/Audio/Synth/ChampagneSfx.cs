namespace Redline.Presentation.Audio.Synth;

/// <summary>
/// The podium's champagne sounds (podium spec §5), synthesised like <see cref="Sfx"/>: 44.1 kHz mono, deterministic
/// (seeded noise), peaking at or below −1 dBFS, faded in and out so they never click.
/// </summary>
public static class ChampagneSfx
{
    private const int Sr = Sfx.SampleRate;
    private const double TwoPi = 2 * Math.PI;

    /// <summary>
    /// A cork pop: a sharp band-limited burst of air (the "p"), a hollow Helmholtz "op" from the empty neck falling
    /// 720 → 420 Hz, and a short gush of fizz behind it. 0.35 s.
    /// </summary>
    public static Pcm CorkPop()
    {
        var x = new float[(int)(0.35 * Sr)];
        var rng = new Rng(4101);
        var burst = new Svf(2600, 0.9, Sr);
        var fizzHp = new Svf(5200, 0.7, Sr);
        double phase = 0;
        for (var i = 0; i < x.Length; i++)
        {
            var t = i / (double)Sr;
            var crack = burst.BandPass(rng.Bipolar()) * Math.Exp(-t / 0.0022) * 2.4;
            var f = 420 + (300 * Math.Exp(-t / 0.018));
            phase += TwoPi * f / Sr;
            var neck = Math.Sin(phase) * Dsp.AttackDecay(t, 0.0008, 0.028) * 0.9;
            var thump = Math.Sin(TwoPi * 120 * t) * Math.Exp(-t / 0.02) * 0.35;
            var fizz = fizzHp.HighPass(rng.Bipolar()) * Dsp.SmoothStep(0.005, 0.03, t) * Math.Exp(-t / 0.09) * 0.32;
            x[i] = (float)(crack + neck + thump + fizz);
        }
        Dsp.Fade(x, Sr, 0.0002, 0.04);
        return Pcm.FromFloats(x, Sr, -2);
    }

    /// <summary>
    /// The spray: a roaring hiss of foam (pink and white noise through a wide band-pass that opens from 1.8 to
    /// 4.5 kHz), crackling with bursting bubbles, swelling in fast and tailing off as the bottle empties. 2.6 s.
    /// The TV scales its level and pitch with the spray's strength.
    /// </summary>
    public static Pcm SprayHiss()
    {
        const double length = 2.6;
        var x = new float[(int)(length * Sr)];
        var pink = new PinkNoise(4202);
        var white = new Rng(4203);
        var crackle = new Rng(4204);
        var band = new Svf(1800, 0.6, Sr);
        var air = new Svf(7000, 0.5, Sr);
        var wobble = new SmoothNoise(4205, 9, Sr);
        for (var i = 0; i < x.Length; i++)
        {
            var t = i / (double)Sr;
            var u = t / length;
            if (i % 32 == 0) band.Set(1800 + (2700 * Dsp.SmoothStep(0, 0.15, u) * (1 - (0.5 * u))), 0.6, Sr);
            var env = Dsp.SmoothStep(0, 0.04, t) * Math.Pow(1 - u, 1.6) * (0.85 + (0.15 * wobble.Next()));
            var roar = band.BandPass(pink.Next()) * 1.1;
            var hiss = air.HighPass(white.Bipolar()) * 0.45;
            // Bubbles bursting: sparse clicks, denser while the jet is strongest.
            var pop = crackle.NextDouble() < 0.0016 * (1.2 - u) ? crackle.Bipolar() * 1.8 : 0;
            x[i] = (float)((roar + hiss + pop) * env);
        }
        Dsp.Fade(x, Sr, 0.004, 0.08);
        return Pcm.FromFloats(x, Sr, -3);
    }
}
