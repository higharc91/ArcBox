using System.Text.RegularExpressions;

namespace ArcBox.Sdk.Speech;

/// <summary>
/// A named voice for the platform's text-to-speech service. Games declare their themes in their Logic project and
/// register them on the server; the TV asks <c>/api/tts</c> for a theme id, never for a provider voice.
/// </summary>
/// <param name="Id">Namespaced id: <c>arcbox.*</c> for the platform, <c>&lt;gameId&gt;.*</c> for a game (see <see cref="VoiceThemes.IsValidId"/>).</param>
/// <param name="Description">What the voice is for, shown to people (for example in the TV's settings).</param>
/// <param name="Voice">Provider voice name, for example <c>en-US-Chirp3-HD-Charon</c>.</param>
/// <param name="LanguageCode">BCP-47 language code sent with the voice.</param>
/// <param name="SpeakingRate">0.25–2.0; null leaves the voice's native pace. Sent only to voices that support it.</param>
/// <param name="Pitch">Semitones, −20–20; null leaves the native pitch. Sent only to voices that support it.</param>
/// <param name="VolumeGainDb">−96–16 dB; 0 sends nothing. Sent only to voices that support it.</param>
/// <param name="Fallback">Hints for the TV's built-in voice when the server's voice is unavailable.</param>
public sealed record VoiceTheme(
    string Id,
    string Description,
    string Voice,
    string LanguageCode = "en-US",
    double? SpeakingRate = null,
    double? Pitch = null,
    double VolumeGainDb = 0,
    FallbackVoice? Fallback = null);

/// <summary>Rate and pitch multipliers for the TV's built-in TTS (Godot <c>DisplayServer.TtsSpeak</c>; 1 is normal).</summary>
public sealed record FallbackVoice(float Rate = 1f, float Pitch = 1f);

public static partial class VoiceThemes
{
    /// <summary>The platform's default theme, used when a request names none.</summary>
    public const string Host = "arcbox.host";

    /// <summary>
    /// True for a lower-case, dot-namespaced id with at least two segments, such as <c>arcbox.host</c> or
    /// <c>redline.announcer</c>: <c>^[a-z0-9]+(\.[a-z0-9-]+)+$</c>.
    /// </summary>
    public static bool IsValidId(string? id) => id is not null && IdPattern().IsMatch(id);

    // \z rather than $: in .NET, $ also matches before a trailing newline.
    [GeneratedRegex(@"^[a-z0-9]+(\.[a-z0-9-]+)+\z", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();
}
