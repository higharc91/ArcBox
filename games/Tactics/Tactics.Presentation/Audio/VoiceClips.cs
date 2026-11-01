using System.Text.Json;

namespace Tactics.Presentation.Audio;

/// <summary>A recorded line.</summary>
/// <param name="Id">Its id in <c>lore/audio/manifest.json</c>.</param>
/// <param name="Path">Its file, relative to the pack's root.</param>
public sealed record VoiceClip(string Id, string Path);

/// <summary>
/// The recordings of the encounters' <c>say</c> lines. Those lines have no id of their own: they're <c>say_&lt;encounter&gt;_&lt;n&gt;</c>,
/// numbered in the order the encounter's JSON has them (<c>tools/tactics-lore/voices.py</c>), so a line is found by who says what.
/// </summary>
public sealed class VoiceClips
{
    private const string Cast = "main";

    private readonly Dictionary<(string Encounter, string Speaker, string Line), VoiceClip> _clips = [];

    private VoiceClips()
    {
    }

    /// <summary>Reads the manifest and the encounters under the pack's root. Throws if one is missing or malformed.</summary>
    public static VoiceClips Load(string packRoot)
    {
        var clips = new VoiceClips();
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(packRoot, "lore", "audio", "manifest.json")));
        var lines = manifest.RootElement.GetProperty("lines");
        foreach (var file in Directory.GetFiles(Path.Combine(packRoot, "rules", "encounters"), "*.json"))
        {
            using var encounter = JsonDocument.Parse(File.ReadAllText(file));
            var id = encounter.RootElement.GetProperty("id").GetString() ?? "";
            var n = 0;
            foreach (var say in Says(encounter.RootElement))
            {
                n++;
                var line = $"say_{id}_{n}";
                if (lines.TryGetProperty(line, out var casts) && casts.TryGetProperty(Cast, out var clip))
                    clips._clips.TryAdd((id, say.GetProperty("speaker").GetString() ?? "", say.GetProperty("line").GetString() ?? ""), new VoiceClip(line, $"lore/audio/{clip.GetString()}"));
            }
        }
        return clips;
    }

    /// <summary>The recording of <paramref name="speaker"/> saying <paramref name="line"/> in <paramref name="encounter"/>, or null: then the voice chain reads it.</summary>
    public VoiceClip? For(string encounter, string speaker, string line) => _clips.GetValueOrDefault((encounter, speaker, line));

    private static IEnumerable<JsonElement> Says(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            if (node.TryGetProperty("say", out var say) && say.ValueKind == JsonValueKind.Object)
                yield return say;
            foreach (var found in node.EnumerateObject().SelectMany(p => Says(p.Value)))
                yield return found;
        }
        else if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var found in node.EnumerateArray().SelectMany(Says))
                yield return found;
        }
    }
}
