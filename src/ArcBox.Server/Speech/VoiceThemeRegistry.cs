using System.Globalization;
using ArcBox.Sdk.Speech;
using Microsoft.Extensions.Options;

namespace ArcBox.Server.Speech;

/// <summary>The voice themes this server offers: everything registered with <c>AddVoiceThemes</c>, with configuration applied.</summary>
public interface IVoiceThemeRegistry
{
    VoiceTheme? Find(string id);

    IReadOnlyList<VoiceTheme> All { get; }

    /// <summary>What to send the provider for a theme (unsupported fields removed); null for an unknown id.</summary>
    ResolvedVoice? Resolved(string id);
}

/// <summary>One <c>AddVoiceThemes</c> call; a game's defaults or the platform's own.</summary>
public sealed record VoiceThemeRegistration(IReadOnlyList<VoiceTheme> Themes);

/// <summary>The audio parameters actually sent for a theme: its voice, minus the fields that voice cannot take.</summary>
public sealed record ResolvedVoice(string Voice, string LanguageCode, double? SpeakingRate, double? Pitch, double? VolumeGainDb)
{
    /// <summary>Stable text form of every field that changes the audio, for the cache key.</summary>
    public string Fingerprint =>
        string.Join('|', Voice, LanguageCode, Num(SpeakingRate), Num(Pitch), Num(VolumeGainDb));

    private static string Num(double? d) => d?.ToString("R", CultureInfo.InvariantCulture) ?? "-";
}

/// <summary>Which optional <c>audioConfig</c> fields a voice family accepts.</summary>
/// <remarks>
/// Sources (checked 2026-10-26):
/// <list type="bullet">
/// <item>Chirp 3: HD (https://cloud.google.com/text-to-speech/docs/chirp3-hd): "Pace control" documents
/// <c>speakingRate</c> 0.25–2.0. Pitch is not supported: the supported-voices page
/// (https://cloud.google.com/text-to-speech/docs/list-voices-and-types) says Chirp 3: HD voices do not support the pitch
/// audio parameter, and Google answers a <c>pitch</c> field (even 0.0) with 400 "This voice does not support pitch
/// parameters at this time." (https://github.com/home-assistant/core/issues/144824). <c>volumeGainDb</c> is not
/// documented for Chirp 3: HD, so it is not sent; the TV's Voice bus sets loudness instead.</item>
/// <item>Chirp HD (the earlier generation): no speaking rate or pitch (same supported-voices page).</item>
/// <item>Standard, WaveNet, Neural2, News and Polyglot: the whole <c>AudioConfig</c>
/// (https://cloud.google.com/text-to-speech/docs/reference/rest/v1/AudioConfig).</item>
/// <item>Anything else (Studio, Journey, Casual, unknown): nothing optional is sent, to be safe.</item>
/// </list>
/// </remarks>
public sealed record VoiceCapabilities(bool SpeakingRate, bool Pitch, bool VolumeGain)
{
    private static readonly VoiceCapabilities All = new(true, true, true);
    private static readonly VoiceCapabilities None = new(false, false, false);
    private static readonly VoiceCapabilities RateOnly = new(true, false, false);

    private static readonly string[] Classic = ["-Standard-", "-Wavenet-", "-Neural2-", "-News-", "-Polyglot-"];

    public static VoiceCapabilities For(string voice)
    {
        if (voice.Contains("-Chirp3-HD-", StringComparison.OrdinalIgnoreCase))
            return RateOnly;
        if (voice.Contains("-Chirp-HD-", StringComparison.OrdinalIgnoreCase))
            return None;
        return Classic.Any(f => voice.Contains(f, StringComparison.OrdinalIgnoreCase)) ? All : None;
    }
}

/// <summary>Merges registered themes with configuration; reports problems instead of throwing so the validator can use it.</summary>
public static class VoiceThemeMerger
{
    public static IReadOnlyList<VoiceTheme> Merge(
        IEnumerable<VoiceThemeRegistration> registrations,
        IReadOnlyDictionary<string, VoiceThemeOverride>? overrides,
        List<string> errors)
    {
        var themes = new List<VoiceTheme>();
        var byId = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var theme in registrations.SelectMany(r => r.Themes))
        {
            if (!VoiceThemes.IsValidId(theme.Id))
            {
                errors.Add($"Voice theme id '{theme.Id}' is invalid: use lower-case dot-namespaced ids such as 'arcbox.host' or '<gameId>.announcer'.");
                continue;
            }

            if (!byId.TryAdd(theme.Id, themes.Count))
            {
                errors.Add($"Voice theme '{theme.Id}' is registered more than once.");
                continue;
            }

            themes.Add(theme);
        }

        foreach (var (rawId, o) in overrides ?? new Dictionary<string, VoiceThemeOverride>())
        {
            var id = rawId.ToLowerInvariant();
            if (byId.TryGetValue(id, out var index))
            {
                themes[index] = Apply(themes[index], o);
            }
            else if (!VoiceThemes.IsValidId(id))
            {
                errors.Add($"Speech:Voices:{rawId}: '{rawId}' is not a valid voice theme id.");
            }
            else if (string.IsNullOrWhiteSpace(o.Voice))
            {
                errors.Add($"Speech:Voices:{rawId}: no game registers '{rawId}', so the override must set Voice to add it.");
            }
            else
            {
                byId[id] = themes.Count;
                themes.Add(Apply(new VoiceTheme(id, id, o.Voice), o));
            }
        }

        foreach (var theme in themes)
            Check(theme, errors);

        return themes;
    }

    private static VoiceTheme Apply(VoiceTheme t, VoiceThemeOverride o) => t with
    {
        Description = o.Description ?? t.Description,
        Voice = string.IsNullOrWhiteSpace(o.Voice) ? t.Voice : o.Voice.Trim(),
        LanguageCode = string.IsNullOrWhiteSpace(o.LanguageCode) ? t.LanguageCode : o.LanguageCode.Trim(),
        SpeakingRate = o.SpeakingRate ?? t.SpeakingRate,
        Pitch = o.Pitch ?? t.Pitch,
        VolumeGainDb = o.VolumeGainDb ?? t.VolumeGainDb,
        Fallback = o.Fallback is null
            ? t.Fallback
            : new FallbackVoice(o.Fallback.Rate ?? t.Fallback?.Rate ?? 1f, o.Fallback.Pitch ?? t.Fallback?.Pitch ?? 1f),
    };

    private static void Check(VoiceTheme t, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(t.Voice))
            errors.Add($"Voice theme '{t.Id}' has no Voice.");
        if (string.IsNullOrWhiteSpace(t.LanguageCode))
            errors.Add($"Voice theme '{t.Id}' has no LanguageCode.");
        if (t.SpeakingRate is { } rate && (rate < 0.25 || rate > 2.0))
            errors.Add($"Voice theme '{t.Id}': SpeakingRate must be 0.25–2.0.");
        if (t.Pitch is { } pitch && (pitch < -20 || pitch > 20))
            errors.Add($"Voice theme '{t.Id}': Pitch must be -20–20.");
        if (t.VolumeGainDb < -96 || t.VolumeGainDb > 16)
            errors.Add($"Voice theme '{t.Id}': VolumeGainDb must be -96–16.");
    }
}

public sealed class VoiceThemeRegistry : IVoiceThemeRegistry
{
    private readonly Dictionary<string, VoiceTheme> _byId;
    private readonly Dictionary<string, ResolvedVoice> _resolved;

    public VoiceThemeRegistry(
        IEnumerable<VoiceThemeRegistration> registrations,
        IOptions<SpeechOptions> options,
        ILogger<VoiceThemeRegistry> log)
    {
        var errors = new List<string>();
        All = VoiceThemeMerger.Merge(registrations, options.Value.Voices, errors);
        if (errors.Count > 0)
            throw new OptionsValidationException(SpeechOptions.Section, typeof(SpeechOptions), errors);

        _byId = All.ToDictionary(t => t.Id, StringComparer.Ordinal);
        _resolved = All.ToDictionary(t => t.Id, t => Resolve(t, log), StringComparer.Ordinal);
    }

    public IReadOnlyList<VoiceTheme> All { get; }

    public VoiceTheme? Find(string id) => _byId.GetValueOrDefault(id);

    public ResolvedVoice? Resolved(string id) => _resolved.GetValueOrDefault(id);

    // Runs once per theme (the registry is a singleton), so each warning is logged once.
    private static ResolvedVoice Resolve(VoiceTheme t, ILogger log)
    {
        var can = VoiceCapabilities.For(t.Voice);
        var dropped = new List<string>();
        double? rate = t.SpeakingRate, pitch = t.Pitch, gain = t.VolumeGainDb == 0 ? null : t.VolumeGainDb;
        if (rate is not null && !can.SpeakingRate) { dropped.Add("SpeakingRate"); rate = null; }
        if (pitch is not null && !can.Pitch) { dropped.Add("Pitch"); pitch = null; }
        if (gain is not null && !can.VolumeGain) { dropped.Add("VolumeGainDb"); gain = null; }

        if (dropped.Count > 0)
            log.LogWarning("Voice theme {Theme}: {Voice} does not support {Fields}; ignoring them", t.Id, t.Voice, string.Join(", ", dropped));

        return new ResolvedVoice(t.Voice, t.LanguageCode, rate, pitch, gain);
    }
}
