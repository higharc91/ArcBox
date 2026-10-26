namespace Redline.Presentation.Audio.Synth;

/// <summary>A mono 16-bit PCM clip. The TV wraps <see cref="ToBytes"/> in an <c>AudioStreamWav</c> (Format 16-bit, Stereo off).</summary>
public sealed record Pcm(short[] Samples, int SampleRate)
{
    /// <summary>The loudest any clip may peak: −1 dBFS.</summary>
    public const double CeilingDbfs = -1.0;

    public double Seconds => Samples.Length / (double)SampleRate;

    /// <summary>Peak level in dBFS (negative infinity for silence).</summary>
    public double PeakDbfs
    {
        get
        {
            var peak = 0;
            foreach (var s in Samples) peak = Math.Max(peak, Math.Abs((int)s));
            return 20 * Math.Log10(peak / (double)short.MaxValue);
        }
    }

    /// <summary>Little-endian signed 16-bit bytes, the layout <c>AudioStreamWav.Data</c> expects.</summary>
    public byte[] ToBytes()
    {
        var bytes = new byte[Samples.Length * 2];
        for (var i = 0; i < Samples.Length; i++)
        {
            bytes[2 * i] = (byte)(Samples[i] & 0xFF);
            bytes[2 * i + 1] = (byte)((Samples[i] >> 8) & 0xFF);
        }
        return bytes;
    }

    /// <summary>
    /// Scales <paramref name="samples"/> so the loudest one lands on <paramref name="peakDbfs"/> (never above
    /// <see cref="CeilingDbfs"/>) and quantises to 16 bits. NaN and infinities become silence.
    /// </summary>
    public static Pcm FromFloats(ReadOnlySpan<float> samples, int sampleRate, double peakDbfs)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(sampleRate, 0);
        double peak = 0;
        foreach (var s in samples)
            if (float.IsFinite(s)) peak = Math.Max(peak, Math.Abs(s));

        var target = Math.Floor(short.MaxValue * Math.Pow(10, Math.Min(peakDbfs, CeilingDbfs) / 20));
        var scale = peak > 0 ? target / peak : 0;
        var output = new short[samples.Length];
        for (var i = 0; i < samples.Length; i++)
            output[i] = float.IsFinite(samples[i]) ? (short)Math.Round(samples[i] * scale) : (short)0;
        return new Pcm(output, sampleRate);
    }
}
