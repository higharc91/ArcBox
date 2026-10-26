using ArcBox.Sdk.Speech;

namespace Redline.Logic;

/// <summary>
/// REDLINE's voice themes. The server registers them (<c>AddRedline</c>); the TV asks <c>/api/tts</c> for these ids.
/// Any field can be overridden per id in the server's <c>Speech:Voices:&lt;id&gt;</c> configuration.
/// </summary>
/// <remarks>
/// Chirp 3: HD voices: https://cloud.google.com/text-to-speech/docs/chirp3-hd (voice list and the "Pace control"
/// section: <c>speakingRate</c> 0.25–2.0). The voices share their names and characters with Google's Gemini TTS
/// voices, whose list gives a one-word description of each: https://ai.google.dev/gemini-api/docs/speech-generation#voices
/// (Fenrir "Excitable", Charon "Informative", Puck "Upbeat"). Chirp 3: HD voices reject <c>pitch</c>, so the
/// themes leave it unset.
/// </remarks>
public static class RedlineVoices
{
    /// <summary>The race commentator.</summary>
    public const string Announcer = "redline.announcer";

    /// <summary>Reads each question during the Reading phase.</summary>
    public const string Questions = "redline.questions";

    public static IReadOnlyList<VoiceTheme> All { get; } =
    [
        new(Announcer, "Race commentator: excitable, a touch quick", "en-US-Chirp3-HD-Fenrir",
            SpeakingRate: 1.1, Fallback: new FallbackVoice(Rate: 1.1f, Pitch: 1.05f)),
        // Native pace: RedlineRules.ReadingTime assumes about 2.7 words a second.
        new(Questions, "Question reader: clear and informative", "en-US-Chirp3-HD-Charon",
            Fallback: new FallbackVoice()),
    ];
}
