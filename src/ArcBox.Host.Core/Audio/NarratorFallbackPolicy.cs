namespace ArcBox.Host.Core.Audio;

/// <summary>Where narration comes from right now (shown in the F3 overlay).</summary>
public enum NarratorState
{
    /// <summary>The server's neural voice (<c>/api/tts</c>, Google Chirp 3: HD).</summary>
    Google,

    /// <summary>The TV's built-in voice (<c>DisplayServer.TtsSpeak</c>).</summary>
    Fallback,

    /// <summary>No voice at all: the server's is unavailable and the TV has no built-in voices. Lines are only logged.</summary>
    Off,
}

/// <summary>Why a <c>/api/tts</c> request gave no audio.</summary>
public enum TtsFailure
{
    /// <summary>No answer within <see cref="NarratorFallbackPolicy.Timeout"/>.</summary>
    Timeout,

    /// <summary>The server could not be reached.</summary>
    Network,

    /// <summary>401: wrong room or host token (the room closed).</summary>
    Unauthorized,

    /// <summary>503 (speech off, no key, Google failed) or any other unexpected answer.</summary>
    Unavailable,

    /// <summary>429: over the room's per-minute limit.</summary>
    RateLimited,

    /// <summary>400, or text the server would refuse: a problem with this line only.</summary>
    BadRequest,
}

/// <summary>
/// When to ask the server for a voice and when to use the TV's own (audio spec §3.3, Review Focus 1): any server
/// problem (a timeout after <see cref="Timeout"/>, no connection, 401, 503) falls back at once and keeps using the
/// built-in voice for <see cref="CoolDown"/> before trying the server again; a 429 waits for its <c>Retry-After</c>;
/// a 400 is about one line, so the next line tries the server as usual. Times are seconds on the narrator's clock.
/// </summary>
public sealed class NarratorFallbackPolicy
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2.5);
    public static readonly TimeSpan CoolDown = TimeSpan.FromSeconds(60);

    private double _retryAt = double.NegativeInfinity;

    /// <summary>A session (server, room code, host token) to ask.</summary>
    public bool HasServer { get; set; }

    /// <summary>The TV has at least one built-in TTS voice.</summary>
    public bool HasBuiltInVoice { get; set; }

    /// <summary>The last failure, for the overlay and the log; null after a success.</summary>
    public TtsFailure? LastFailure { get; private set; }

    /// <summary>True when the next line should be fetched from the server.</summary>
    public bool UseServer(double now) => HasServer && now >= _retryAt;

    /// <summary>Seconds until the server is tried again (0 when it would be tried now).</summary>
    public double CoolDownLeft(double now) => Math.Max(0, _retryAt - now);

    public void Failed(TtsFailure failure, double now, TimeSpan? retryAfter = null)
    {
        LastFailure = failure;
        if (failure == TtsFailure.BadRequest)
        {
            return;
        }
        var wait = failure == TtsFailure.RateLimited && retryAfter is { } r && r > TimeSpan.Zero ? r : CoolDown;
        _retryAt = now + wait.TotalSeconds;
    }

    public void Succeeded()
    {
        LastFailure = null;
        _retryAt = double.NegativeInfinity;
    }

    /// <summary>Google while the server is in use, else Fallback when the TV has a voice, else Off.</summary>
    public NarratorState State(double now) =>
        UseServer(now) ? NarratorState.Google : HasBuiltInVoice ? NarratorState.Fallback : NarratorState.Off;
}
