using System.Text.Json;
using Tactics.Presentation.Audio;
using Tactics.Presentation.Tests.Board;

namespace Tactics.Presentation.Tests.Audio;

/// <summary><see cref="VoiceClips"/>: an encounter's <c>say</c> lines find their recorded clips.</summary>
public sealed class VoiceClipsTests
{
    private static readonly Lazy<VoiceClips> Clips = new(() => VoiceClips.Load(Pack.Root));

    private static IEnumerable<(string Speaker, string Line)> Says(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            if (node.TryGetProperty("say", out var say) && say.ValueKind == JsonValueKind.Object)
                yield return (say.GetProperty("speaker").GetString()!, say.GetProperty("line").GetString()!);
            foreach (var found in node.EnumerateObject().SelectMany(p => Says(p.Value)))
                yield return found;
        }
        else if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var found in node.EnumerateArray().SelectMany(Says))
                yield return found;
        }
    }

    private static (string Id, (string Speaker, string Line)[] Lines)[] Encounters() =>
        [.. Directory.GetFiles(Path.Combine(Pack.Root, "rules", "encounters")).Select(file =>
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            return (doc.RootElement.GetProperty("id").GetString()!, Says(doc.RootElement).ToArray());
        })];

    [Fact]
    public void Every_say_line_in_the_pack_has_a_clip_on_disk()
    {
        var lines = 0;
        foreach (var (id, says) in Encounters())
            foreach (var (speaker, line) in says)
            {
                var clip = Clips.Value.For(id, speaker, line);
                Assert.True(clip is not null, $"{id}: {line}");
                Assert.True(File.Exists(Path.Combine(Pack.Root, clip.Path)), clip.Path);
                lines++;
            }

        Assert.Equal(20, lines);      // when this moves, check the new lines were rendered
    }

    [Fact]
    public void Lines_are_numbered_in_the_order_the_encounter_has_them()
    {
        var (id, says) = Encounters().Single(e => e.Id == "warlord_bridge");

        for (var i = 0; i < says.Length; i++)
            Assert.Equal($"say_warlord_bridge_{i + 1}", Clips.Value.For(id, says[i].Speaker, says[i].Line)!.Id);
    }

    [Fact]
    public void The_clip_is_the_main_casts_file_in_the_lore_audio_folder()
    {
        var (id, says) = Encounters().Single(e => e.Id == "warlord_bridge");

        var clip = Clips.Value.For(id, says[0].Speaker, says[0].Line)!;

        Assert.StartsWith("lore/audio/say_warlord_bridge_1.", clip.Path);
        Assert.EndsWith(".ogg", clip.Path);
    }

    [Theory]
    [InlineData("warlord_bridge", "warlord", "A line nobody recorded.")]
    [InlineData("river_crossing", "narrator", "An encounter with no lines at all.")]
    [InlineData("no_such_encounter", "narrator", "An encounter nobody wrote.")]
    public void A_line_with_no_clip_has_none(string encounter, string speaker, string line)
        => Assert.Null(Clips.Value.For(encounter, speaker, line));

    [Fact]
    public void A_recorded_line_said_by_someone_else_is_not_their_clip()
    {
        var (id, says) = Encounters().Single(e => e.Id == "warlord_bridge");

        Assert.Null(Clips.Value.For(id, "somebody_else", says[0].Line));
    }
}
