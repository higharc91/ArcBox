using Tactics.Game;
using Tactics.Presentation.Audio;

namespace Tactics.Presentation.Story;

/// <summary>What the TV does with a scene's line that has no recording.</summary>
public enum SpeechFallback
{
    /// <summary>It shows the caption and says nothing.</summary>
    Captions,

    /// <summary>The voice chain reads the line out (a TTS key's work).</summary>
    Speech,
}

/// <summary>A line for the voice: say it now, at Question priority, so it cuts whatever was speaking.</summary>
/// <param name="Line">The line's id.</param>
/// <param name="Text">What's said: the voice chain's text when there's no clip.</param>
/// <param name="Clip">The recording, relative to the pack's root; null to have the voice chain read <paramref name="Text"/>.</param>
/// <param name="Theme">The voice theme for the voice chain.</param>
public sealed record StorySay(string Line, string Text, string? Clip, string Theme);

/// <summary>Which voice a scene's line gets (overworld 5d §10): its own clip, or for a line with none, <see cref="Fallback"/>.</summary>
public static class StoryVoice
{
    /// <summary>
    /// The one switch for a line with no clip (the user's decision 1: a TTS key, or captions only). Captions until the user decides, as the
    /// local session leans: the server reads such a line on a reading timer, which a voice reading it out might overrun.
    /// </summary>
    public const SpeechFallback Fallback = SpeechFallback.Captions;

    /// <summary>The voice theme the voice chain reads a line with no clip in.</summary>
    public const string Theme = "tactics.story";

    /// <summary>What to say for <paramref name="line"/>: its clip, or by <paramref name="fallback"/>, the voice chain's reading, or nothing.</summary>
    public static StorySay? For(StoryLineView line, VoiceClips? clips, SpeechFallback fallback = Fallback)
        => clips?.ForLine(line.Id) is { } clip
            ? new StorySay(line.Id, line.Text, clip.Path, Theme)
            : fallback == SpeechFallback.Speech ? new StorySay(line.Id, line.Text, null, Theme) : null;
}
