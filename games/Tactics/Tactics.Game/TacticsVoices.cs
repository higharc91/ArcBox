using ArcBox.Sdk.Speech;

namespace Tactics.Game;

/// <summary>
/// Tactics' voice themes. The server registers them (<c>AddTactics</c>); the TV asks <c>/api/tts</c> for these ids when a
/// trigger's <c>say</c> line comes up. Any field can be overridden per id in the server's <c>Speech:Voices:&lt;id&gt;</c>
/// configuration. Chirp 3: HD voices reject <c>pitch</c>, so the themes leave it unset (see <c>RedlineVoices</c>).
/// </summary>
public static class TacticsVoices
{
    /// <summary>The storyteller: lines whose speaker is <c>narrator</c>.</summary>
    public const string Narrator = "tactics.narrator";

    /// <summary>Whoever else speaks in a battle.</summary>
    public const string Character = "tactics.character";

    /// <summary>The speaker of a storyteller's line.</summary>
    public const string NarratorSpeaker = "narrator";

    /// <summary>All of the game's themes.</summary>
    public static IReadOnlyList<VoiceTheme> All { get; } =
    [
        new(Narrator, "Battle narrator: a clear storyteller", "en-US-Chirp3-HD-Charon", Fallback: new FallbackVoice()),
        new(Character, "Battle characters: a taunting warlord", "en-US-Chirp3-HD-Fenrir", Fallback: new FallbackVoice(Pitch: 0.85f)),
    ];
}
