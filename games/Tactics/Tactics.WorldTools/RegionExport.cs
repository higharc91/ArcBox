using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ArcBox.Sdk;
using Tactics.Logic;

namespace Tactics.WorldTools;

/// <summary>What <see cref="RegionExport.Export"/> did: the file it wrote, or null and everything that stopped it.</summary>
public sealed record ExportResult(string? Written, ImmutableArray<string> Problems);

/// <summary>
/// Turns a painted region into its file in the rules pack. The Godot plugin's button and its headless command both end here, so
/// what's tested is what a painter gets.
/// </summary>
public static partial class RegionExport
{
    /// <summary>Written indented, so each chunk is one line and a small paint change is a small diff. LF on every platform: packs are hashed.</summary>
    private static readonly JsonSerializerOptions FileOptions = new(ArcJson.Options)
    {
        WriteIndented = true,
        NewLine = "\n",
    };

    [GeneratedRegex("^[a-z0-9_]+$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();

    /// <summary>
    /// Writes <c>world/&lt;id&gt;.json</c> under <paramref name="rulesDirectory"/>, only if the region passes everything the pack
    /// loader would check, which is more than <see cref="RegionWriter"/> sees: a stamped tile that was changed, a marker that can't
    /// be reached. Every problem found is returned, with the file left as it was. A problem that stops the region being built at
    /// all, such as an unknown material, comes before the checks that need it built, so those show on the next export.
    /// It doesn't touch the pack's manifest: a region is listed there once, by hand.
    /// </summary>
    public static ExportResult Export(PaintedRegion painted, string rulesDirectory, string terrainManifest)
    {
        if (!IdPattern().IsMatch(painted.Id))
            return new ExportResult(null, [$"The region id '{painted.Id}' must be lower-case letters, digits and underscores."]);

        var (source, sceneProblems) = painted.ToSource();
        var written = RegionWriter.Write(source, PaletteBuilder.Materials(terrainManifest).ToHashSet(StringComparer.Ordinal));
        if (written.Region is not { } region)
            return new ExportResult(null, [.. sceneProblems, .. written.Problems]);

        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(region, FileOptions) + "\n");
        ImmutableArray<string> problems = [.. sceneProblems, .. Check(rulesDirectory, region.Id, bytes)];
        if (problems.Length > 0)
            return new ExportResult(null, problems);

        var path = Path.Combine(rulesDirectory, "world", $"{region.Id}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return new ExportResult(path, []);
    }

    /// <summary>
    /// Loads a copy of the pack with this region in it, as the game will. It's a copy of the same pack, not a pack on top, because a
    /// region already listed would then have its id twice; the copy also means an old file there is replaced, not checked.
    /// </summary>
    private static List<string> Check(string rulesDirectory, string id, byte[] region)
    {
        var copy = Directory.CreateTempSubdirectory("tactics-region-check-").FullName;
        try
        {
            foreach (var file in Directory.EnumerateFiles(rulesDirectory, "*", SearchOption.AllDirectories))
            {
                var to = Path.Combine(copy, Path.GetRelativePath(rulesDirectory, file));
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Copy(file, to);
            }
            Directory.CreateDirectory(Path.Combine(copy, "world"));
            File.WriteAllBytes(Path.Combine(copy, "world", $"{id}.json"), region);

            var entry = $"world/{id}.json";
            var manifestPath = Path.Combine(copy, "manifest.json");
            var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!;
            var files = manifest["files"]!.AsArray();
            if (!files.Any(f => f?.GetValue<string>() == entry))
                files.Add(entry);
            File.WriteAllText(manifestPath, manifest.ToJsonString());

            RulesPack.Load([copy]);
            return [];
        }
        catch (ContentException ex)
        {
            var label = $"world/{id}.json";
            return [.. ex.Errors.Select(e => e.File.EndsWith(label, StringComparison.Ordinal) ? e.Message : $"{e.File}: {e.Message}")];
        }
        finally
        {
            Directory.Delete(copy, recursive: true);
        }
    }
}
