using System.Text.Json;
using ArcBox.Sdk.Speech;


namespace Tactics.Logic.Tests.Content;

/// <summary>
/// One person, one voice: the themes the server registers for live speech match the casting the pre-rendered clips
/// use (tools/tactics-lore/voice-casting.json), and every lore character names a theme that exists.
/// </summary>
public sealed class VoiceTests
{
    private static JsonElement Json(params string[] parts)
        => JsonDocument.Parse(File.ReadAllBytes(Path.Combine([ContentPaths.RepoRoot, .. parts]))).RootElement;

    private static readonly Dictionary<string, VoiceTheme> Themes = TacticsVoices.All.ToDictionary(t => t.Id);

    [Fact]
    public void Theme_ids_are_valid_and_unique()
    {
        Assert.All(TacticsVoices.All, t => Assert.True(VoiceThemes.IsValidId(t.Id), t.Id));
        Assert.Equal(TacticsVoices.All.Count, Themes.Count);
    }

    [Fact]
    public void Every_cast_speaker_has_a_theme_in_the_same_voice()
    {
        var casting = Json("tools", "tactics-lore", "voice-casting.json");
        foreach (var c in casting.GetProperty("characters").EnumerateObject())
        {
            var id = c.Name == "narrator" ? TacticsVoices.Narrator : TacticsVoices.Npc(c.Name);
            Assert.True(Themes.TryGetValue(id, out var theme), $"no theme {id}");
            Assert.Equal($"en-US-Chirp3-HD-{c.Value.GetProperty("preset").GetString()}", theme.Voice);
        }
        foreach (var set in casting.GetProperty("sets").EnumerateObject())
            foreach (var cast in set.Value.EnumerateObject())
            {
                var id = TacticsVoices.PlayerVoice(set.Name, cast.Name);
                Assert.True(Themes.TryGetValue(id, out var theme), $"no theme {id}");
                Assert.Equal($"en-US-Chirp3-HD-{cast.Value.GetString()}", theme.Voice);
            }
    }

    [Fact]
    public void Every_lore_character_names_a_registered_theme()
    {
        var characters = Json("content", "tactics", "base", "lore", "characters.json").GetProperty("characters");
        foreach (var c in characters.EnumerateArray())
        {
            var theme = c.GetProperty("voice").GetProperty("theme").GetString()!;
            Assert.True(Themes.ContainsKey(theme), $"{c.GetProperty("id").GetString()} names unregistered theme {theme}");
        }
    }
}
