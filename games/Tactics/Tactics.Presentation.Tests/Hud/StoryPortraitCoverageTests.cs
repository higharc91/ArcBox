using System.Text.Json;
using Tactics.Presentation.Board;
using Tactics.Presentation.Hud;
using Tactics.Presentation.Tests.Board;

namespace Tactics.Presentation.Tests.Hud;

public sealed class StoryPortraitCoverageTests
{
    [Fact]
    public void Every_story_speakers_six_moods_resolve_to_the_declared_portrait_or_explicit_single_face()
    {
        using var characters = JsonDocument.Parse(File.ReadAllText(Path.Combine(Pack.Root, "lore", "characters.json")));
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Pack.Root, "portraits", "manifest.json")));
        var single = manifest.RootElement.GetProperty("singleMood").EnumerateArray().Select(p => p.GetString()).ToHashSet();
        var art = HudArt.Load(Pack.Root, UnitArt.Load(Pack.Root));
        foreach (var character in characters.RootElement.GetProperty("characters").EnumerateArray())
        {
            var id = character.GetProperty("id").GetString()!;
            var preset = character.TryGetProperty("portrait", out var authored) ? authored.GetString()! : character.GetProperty("preset").GetString()!;
            var files = manifest.RootElement.GetProperty("presets").GetProperty(preset);
            foreach (var mood in new[] { "idle", "happy", "sad", "angry", "surprised", "hurt" })
            {
                var face = art.Speaker(preset, mood, character.GetProperty("role").GetString());
                Assert.True(face is not null, $"{id}/{mood}: missing rendered portrait");
                Assert.Equal("portraits/" + files.GetProperty(single.Contains(preset) ? "idle" : mood).GetString(), face.Path);
                Assert.True(File.Exists(Path.Combine(Pack.Root, face.Path)), $"{id}/{mood}: missing file");
                Assert.Equal(new ArtRect(0, 0, 40, 40), face.Source);
            }
        }
    }
}
