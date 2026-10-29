namespace ArcBox.Server.Speech;

/// <summary>Settings for the platform speech service; binds from <c>Speech</c>.</summary>
public sealed class SpeechOptions
{
    public const string Section = "Speech";

    /// <summary>False turns <c>/api/tts</c> into a 503 and stops prewarming; the TV then uses its built-in voice.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Only <c>google</c> (Cloud Text-to-Speech) is implemented.</summary>
    public string Provider { get; set; } = "google";

    public GoogleSpeechOptions Google { get; set; } = new();

    /// <summary>Where synthesised MP3s are kept. Relative paths resolve against the content root.</summary>
    public string CacheDir { get; set; } = ".tts-cache";

    /// <summary>Longest text accepted, after trimming.</summary>
    public int MaxChars { get; set; } = 300;

    public int RequestsPerMinutePerRoom { get; set; } = 60;

    /// <summary>Per-theme overrides and extra themes, keyed by theme id (<c>Speech:Voices:&lt;id&gt;</c>).</summary>
    public Dictionary<string, VoiceThemeOverride> Voices { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class GoogleSpeechOptions
{
    /// <summary>
    /// Secret. Set it with user-secrets (<c>Speech:Google:ApiKey</c>) or the environment (<c>Speech__Google__ApiKey</c>);
    /// never in a committed file. Empty means speech is unconfigured.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>The API root. Only tests or a proxy would change it.</summary>
    public string Endpoint { get; set; } = "https://texttospeech.googleapis.com/";
}

/// <summary>Any subset of a <see cref="ArcBox.Sdk.Speech.VoiceTheme"/>'s fields; a new id must supply <see cref="Voice"/>.</summary>
public sealed class VoiceThemeOverride
{
    public string? Description { get; set; }
    public string? Voice { get; set; }
    public string? LanguageCode { get; set; }
    public double? SpeakingRate { get; set; }
    public double? Pitch { get; set; }
    public double? VolumeGainDb { get; set; }
    public FallbackVoiceOverride? Fallback { get; set; }
}

public sealed class FallbackVoiceOverride
{
    public float? Rate { get; set; }
    public float? Pitch { get; set; }
}
