namespace Tactics.Presentation.Audio;

/// <summary>
/// Which of a sound's files is next. Each sound goes round its own files in order, so it never plays the same one twice
/// running, and the same calls give the same files. Picking at random could do neither.
/// </summary>
public sealed class SoundPicker(SoundBank bank)
{
    private readonly Dictionary<string, int> _next = new(StringComparer.Ordinal);

    /// <summary>The next file of the sound <paramref name="id"/>, relative to the pack's root, or null if the pack lacks it.</summary>
    public string? Next(string id)
    {
        if (bank.Sound(id) is not { } sound)
            return null;
        var at = _next.GetValueOrDefault(id);
        _next[id] = (at + 1) % sound.Files.Length;
        return sound.Files[at];
    }
}
