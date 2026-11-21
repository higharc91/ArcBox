using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>
/// A player character's voice (lore spec §7): one of the six sets in <c>lore/voices</c>, in a low or high register, written
/// <c>"&lt;set&gt;.&lt;register&gt;"</c> (<c>"wry.high"</c>). The register picks the cast whose recordings play.
/// </summary>
public static class PlayerVoices
{
    /// <summary>The voice sets, as <c>lore/voices/&lt;set&gt;.json</c> names them.</summary>
    public static ImmutableArray<string> Sets { get; } = ["steady", "fierce", "wry", "earnest", "scholarly", "grim"];

    /// <summary>The two casts every set is recorded in.</summary>
    public static ImmutableArray<string> Registers { get; } = ["low", "high"];

    /// <summary>Whether <paramref name="voice"/> is <c>"&lt;set&gt;.&lt;register&gt;"</c> for a known set and register.</summary>
    public static bool TryParse(string? voice, out string set, out string register)
    {
        (set, register) = ("", "");
        if (voice?.Split('.') is not [var s, var r] || !Sets.Contains(s) || !Registers.Contains(r)) return false;
        (set, register) = (s, r);
        return true;
    }

    /// <summary>
    /// The voice of a character who never picked one: the same for the same name on every machine and run (FNV-1a, not the
    /// process-randomised string hash), so a replay or an old save keeps its sound.
    /// </summary>
    public static string Default(string name)
    {
        var hash = 2166136261u;
        foreach (var c in name)
            hash = (hash ^ c) * 16777619u;
        return $"{Sets[(int)(hash % (uint)Sets.Length)]}.{Registers[(int)(hash / (uint)Sets.Length % (uint)Registers.Length)]}";
    }

    /// <summary>The voice <paramref name="character"/> speaks in: their pick, or the default for their name.</summary>
    public static string Of(CharacterRecord character)
        => TryParse(character.Voice, out _, out _) ? character.Voice! : Default(character.Name);
}
