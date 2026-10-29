namespace ArcBox.Host.Core.Audio;

/// <summary>
/// Voice ducking (audio spec §3.3): while the narrator speaks, Music dips <see cref="MusicDuckDb"/> and Sfx
/// <see cref="SfxDuckDb"/>, ramping down over <see cref="AttackSeconds"/> and back up over <see cref="ReleaseSeconds"/>
/// (linear in dB). Advance it once a frame with <see cref="Step"/> and add the offsets to the buses' own volumes.
/// </summary>
public sealed class DuckEnvelope
{
    public const double MusicDuckDb = -12;
    public const double SfxDuckDb = -6;
    public const double AttackSeconds = 0.2;
    public const double ReleaseSeconds = 0.6;

    /// <summary>0 = not ducked, 1 = fully ducked.</summary>
    public double Amount { get; private set; }

    public double MusicDb => MusicDuckDb * Amount;

    public double SfxDb => SfxDuckDb * Amount;

    public void Step(bool voiceActive, double seconds)
    {
        var delta = Math.Max(0, seconds) / (voiceActive ? AttackSeconds : -ReleaseSeconds);
        Amount = Math.Clamp(Amount + delta, 0, 1);
    }
}

/// <summary>Linear volume (0–1) to decibels.</summary>
public static class AudioLevels
{
    /// <summary>What silence is sent as (Godot treats −80 dB as off).</summary>
    public const double SilentDb = -80;

    public static double Db(double linear) =>
        double.IsNaN(linear) || linear <= 0.0001 ? SilentDb : 20 * Math.Log10(Math.Min(1, linear));
}

/// <summary>The TV's mixer settings from <see cref="HostSettings"/>, with defaults filled in and levels clamped to 0–1.</summary>
public sealed record AudioSettings(double Master, double Music, double Sfx, double Voice, bool Muted)
{
    public const double DefaultMusic = 0.55;
    public const double DefaultSfx = 0.85;

    public static AudioSettings From(HostSettings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new(Level(s.MasterVolume, 1), Level(s.MusicVolume, DefaultMusic), Level(s.SfxVolume, DefaultSfx),
            Level(s.VoiceVolume, 1), s.Muted ?? false);
    }

    private static double Level(double? value, double fallback) =>
        value is { } v && !double.IsNaN(v) ? Math.Clamp(v, 0, 1) : fallback;
}
