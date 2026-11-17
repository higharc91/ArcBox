using ArcBox.Sdk.Speech;

namespace Meridian.Logic;

/// <summary>
/// Meridian's voice: Tiller, the ship's computer (spec §5.5). It reads Incident cards, announces the Breach and the
/// scenario, and calls round warnings. Secret text is never spoken: every line comes from the TV's public host view
/// (M4 plan, Task 5). Owners can retune it under <c>Speech:Voices</c> without a build.
/// </summary>
public static class MeridianVoices
{
    public const string Tiller = "meridian.tiller";

    /// <remarks>
    /// Native pace: <see cref="MeridianRules.ReadingWordsPerSecond"/> (2.7) assumes the voice's own rate, and every text
    /// presentation's fallback is sized from it.
    /// </remarks>
    public static IReadOnlyList<VoiceTheme> All { get; } =
    [
        new(Tiller, "Ship's computer: level, unhurried, faintly polite", "en-US-Chirp3-HD-Iapetus",
            Fallback: new FallbackVoice(Rate: 0.95f, Pitch: 0.92f)),
    ];
}
