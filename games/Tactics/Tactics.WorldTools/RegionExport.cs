using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ArcBox.Sdk;
using Tactics.Logic;

namespace Tactics.WorldTools;

/// <summary>What <see cref="RegionExport.Export"/> did: the file it wrote, or null and everything that stopped it.</summary>
public sealed record ExportResult(string? Written, ImmutableArray<string> Problems)
{
    /// <summary>The pack's manifest entry for the region, which an export never adds.</summary>
    public string? Entry { get; init; }

    /// <summary>Whether the pack's manifest lists the region, so the game will load it.</summary>
    public bool Listed { get; init; }

    /// <summary>What's worth fixing but doesn't stop the file being written: old stamp areas still to repaint.</summary>
    public ImmutableArray<string> Warnings { get; init; } = [];

    /// <summary>Where the region's story can't be played in order. The file is still written: the pack loads it, and a painter may be part way through.</summary>
    public ImmutableArray<string> StoryWarnings { get; init; } = [];

    /// <summary>One line for the painter on how it went.</summary>
    public string Status => this switch
    {
        { Written: null } when Problems.Any(p => RegionExport.TileIn(p) is not null) => $"Not exported: {Problems.Length} {(Problems.Length == 1 ? "problem. Click it" : "problems. Click one")} to go there.",
        { Written: null } => $"Not exported: {Problems.Length} {(Problems.Length == 1 ? "problem" : "problems")}.",
        { Listed: true } => $"Wrote {Written}.{Pending}{Story}",
        _ => $"Wrote {Written}. Add \"{Entry}\" to rules/manifest.json, or the game won't load it.{Pending}{Story}",
    };

    private string Pending => Warnings.Length switch
    {
        0 => "",
        1 => " 1 old stamp area still needs repainting.",
        var count => $" {count} old stamp areas still need repainting.",
    };

    private string Story => StoryWarnings.Length switch
    {
        0 => "",
        1 => " 1 story warning.",
        var count => $" {count} story warnings.",
    };
}

/// <summary>
/// Turns a painted region into its file in the rules pack. The Godot plugin's button and its headless command both end here, so
/// what's tested is what a painter gets.
/// </summary>
public static partial class RegionExport
{
    /// <summary>What a region is called while it's checked, if its own id can't name a file.</summary>
    private const string StandInId = "region_being_checked";

    /// <summary>How close to now a file's time must be for an unchanged time to prove nothing about a save.</summary>
    private const int SaveClockSeconds = 5;

    /// <summary>Written indented, so each chunk is one line and a small paint change is a small diff. LF on every platform: packs are hashed.</summary>
    private static readonly JsonSerializerOptions FileOptions = new(ArcJson.Options)
    {
        WriteIndented = true,
        NewLine = "\n",
    };

    [GeneratedRegex("^[a-z0-9_]+$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"\((-?\d+), (-?\d+)\)", RegexOptions.CultureInvariant)]
    private static partial Regex TilePattern();

    /// <summary>Whether <paramref name="id"/> can name a region and its file.</summary>
    public static bool ValidId(string id) => IdPattern().IsMatch(id);

    /// <summary>The tile a problem is about, which is the first one it names, or null if it names none.</summary>
    public static Tile? TileIn(string problem)
    {
        var first = problem.IndexOf("first (", StringComparison.Ordinal);
        var match = TilePattern().Match(problem, first < 0 ? 0 : first);
        return match.Success
            ? new Tile(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture))
            : null;
    }

    /// <summary>
    /// Whether a save failed, given the scene file's modified time before and after it. A changed time means it was written, whichever
    /// way the time moved, since a file can be dated ahead of the clock. An unchanged time means it wasn't, unless the file was written
    /// moments ago, where the file system's clock may be too coarse to show a second write.
    /// </summary>
    public static bool SaveFailed(DateTime before, DateTime after, DateTime now)
        => after == before && (now - before).Duration() > TimeSpan.FromSeconds(SaveClockSeconds);

    /// <summary>
    /// Writes <c>world/&lt;id&gt;.json</c> under <paramref name="rulesDirectory"/>, only if the region passes everything the pack
    /// loader would check, which is more than <see cref="RegionWriter"/> sees: a stamped tile that was changed, a marker that can't
    /// be reached. One export finds every problem: a region that can't be written is repaired as far as the checks need, on the side,
    /// so they still run, and the file is left as it was. It doesn't touch the pack's manifest, and art and lore come from
    /// <paramref name="contentBase"/>. An old stamp area still to repaint is a warning, and doesn't stop the file; nor does a region that
    /// loads but can't be played in story order, which <see cref="StoryOrder"/> checks here since the loader can't read lore. A file that
    /// can't be read or replaced is a problem, not an exception. The file changes in one move, so a failure can't leave half of it.
    /// </summary>
    public static ExportResult Export(PaintedRegion painted, string rulesDirectory, string contentBase)
    {
        try
        {
            return Run(painted, rulesDirectory, contentBase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new ExportResult(null, [FilesFailed(ex)]);
        }
    }

    private static string FilesFailed(Exception ex) => $"The pack's files couldn't be read: {ex.Message} Close anything holding them, or fix them, then export again.";

    private static List<string> PendingWarnings(PaintedRegion painted, string rulesDirectory)
    {
        var covered = StampBrush.Covered(painted.Stamps, id => StampMaps.Read(rulesDirectory, id));
        return [.. painted.OldStamps.Select(old => StampBrush.PendingWarning(old, StampMaps.Read(rulesDirectory, old.Map), covered)).OfType<string>()];
    }

    private static ExportResult Run(PaintedRegion painted, string rulesDirectory, string contentBase)
    {
        if (!File.Exists(Path.Combine(rulesDirectory, "manifest.json")))
            return new ExportResult(null, [$"{rulesDirectory} isn't a rules pack: it has no manifest.json. Copy content/tactics/base/rules there, or fix --rules."]);

        var problems = new List<string>();
        ImmutableArray<string> warnings = [.. PendingWarnings(painted, rulesDirectory)];
        if (!ValidId(painted.Id))
            problems.Add($"The region id '{painted.Id}' must be lower-case letters, digits and underscores. Change Id on the Region node.");

        var checkable = ValidId(painted.Id) ? painted : painted with { Id = StandInId, SceneFile = null };
        var (source, sceneProblems) = checkable.ToSource();
        problems.AddRange(sceneProblems);
        problems.AddRange(BeatsNotInLore(painted, contentBase));

        // With no recovery marker, standing in the start avoids a line about a spawn nobody placed.
        if (painted.Recoveries.Count == 0)
            source.Recovery = source.Start;

        var maps = source.Stamps.Select(s => s.Map).Distinct(StringComparer.Ordinal)
            .Select(id => StampMaps.Read(rulesDirectory, id)).OfType<BattleMap>().ToList();
        var known = PaletteBuilder.AllSwatches(contentBase).Select(s => s.Material)
            .Concat(maps.SelectMany(m => m.Materials)).ToHashSet(StringComparer.Ordinal);

        var written = RegionWriter.Write(source, known);
        problems.AddRange(written.Problems);
        var region = written.Region ?? RegionWriter.Write(RegionWriter.Repair(source, known, StampedTiles(source, maps)), known).Region;
        if (region is null)
            return new ExportResult(null, [.. problems]) { Warnings = warnings };

        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(region, FileOptions) + "\n");
        var checks = Check(rulesDirectory, region.Id, bytes);
        problems.AddRange(painted.Starts.Count == 0 ? checks.Problems.Where(p => !NeedsTheStart(p)) : checks.Problems);
        if (problems.Count > 0)
            return new ExportResult(null, [.. problems]) { Warnings = warnings };

        // Before the file moves, so nothing thrown here can report "not written" for a file that was.
        ImmutableArray<string> story = checks.Pack is { } pack ? StoryOrder.Check(pack.RegionFor(region.Id)!, pack, contentBase).Problems : [];
        var path = Path.Combine(rulesDirectory, "world", $"{region.Id}.json");
        if (Replace(path, bytes) is { } failure)
            return new ExportResult(null, [failure]) { Warnings = warnings };
        var entry = $"world/{region.Id}.json";
        return new ExportResult(path, [])
        {
            Entry = entry,
            Listed = Listed(rulesDirectory, entry),
            Warnings = warnings,
            StoryWarnings = story,
        };
    }

    /// <summary>What each stamped map says its tiles should be, in world tiles. A later stamp wins where two overlap, which is its own problem.</summary>
    private static Dictionary<Tile, TileInfoNamed> StampedTiles(RegionSource source, List<BattleMap> maps)
    {
        var tiles = new Dictionary<Tile, TileInfoNamed>();
        foreach (var stamp in source.Stamps)
            if (maps.FirstOrDefault(m => m.Id == stamp.Map) is { } map)
                foreach (var cell in StampBrush.Cells(map, stamp.At, stamp.Height))
                    tiles[cell.At] = new TileInfoNamed(cell.Height, cell.Material, cell.Flags);
        return tiles;
    }

    private static List<string> BeatsNotInLore(PaintedRegion painted, string contentBase)
    {
        var path = Path.Combine(contentBase, "lore", "arcs.json");
        if (!File.Exists(path))
            return [];
        using var lore = JsonDocument.Parse(File.ReadAllBytes(path));
        var beats = lore.RootElement.GetProperty("arcs").EnumerateArray()
            .Where(arc => arc.GetProperty("chapter").GetInt32() == painted.Chapter)
            .SelectMany(arc => arc.GetProperty("beats").EnumerateArray())
            .Select(beat => beat.GetProperty("id").GetString()!).ToHashSet(StringComparer.Ordinal);
        return
        [
            .. painted.Beats.Where(b => b.Beat.Length > 0 && !beats.Contains(b.Beat)).DistinctBy(b => b.Beat)
                .Select(b => $"The beat '{b.Beat}' at {b.At} isn't in lore/arcs.json for chapter {painted.Chapter}. Check its spelling."),
        ];
    }

    private static bool NeedsTheStart(string problem)
        => problem.Contains("can't be reached from the start", StringComparison.Ordinal) || problem.StartsWith("The start spawn", StringComparison.Ordinal);

    /// <summary>
    /// Loads a copy of the pack with this region in it, as the game will. It's a copy of the same pack, not a pack on top, because a
    /// region already listed would then have its id twice; the copy also means an old file there is replaced, not checked. Gives what's
    /// wrong, or the loaded pack if nothing is.
    /// </summary>
    private static (List<string> Problems, RulesPack? Pack) Check(string rulesDirectory, string id, byte[] region)
    {
        var copy = Directory.CreateTempSubdirectory("tactics-region-check-").FullName;
        var entry = $"world/{id}.json";
        try
        {
            // The old file is about to be replaced, so a lock on it matters to Replace, not to the check.
            var own = Path.GetFullPath(Path.Combine(rulesDirectory, "world", $"{id}.json"));
            foreach (var file in Directory.EnumerateFiles(rulesDirectory, "*", SearchOption.AllDirectories))
            {
                var to = Path.Combine(copy, Path.GetRelativePath(rulesDirectory, file));
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                if (Path.GetFullPath(file) == own)
                    continue;
                File.Copy(file, to);

                // A read-only original makes a read-only copy, which could not be overwritten or deleted.
                File.SetAttributes(to, FileAttributes.Normal);
            }

            // A folder in the way is Replace's problem to report; here it would only stop the check.
            var regionFile = Path.Combine(copy, "world", $"{id}.json");
            if (Directory.Exists(regionFile))
                Directory.Delete(regionFile, recursive: true);
            Directory.CreateDirectory(Path.Combine(copy, "world"));
            File.WriteAllBytes(regionFile, region);

            var manifestPath = Path.Combine(copy, "manifest.json");
            var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!;
            var files = manifest["files"]!.AsArray();
            if (!files.Any(f => f?.GetValue<string>() == entry))
                files.Add(entry);
            File.WriteAllText(manifestPath, manifest.ToJsonString());

            return ([], RulesPack.Load([copy]));
        }
        catch (ContentException ex)
        {
            return ([.. ex.Errors.Select(e => e.File.EndsWith(entry, StringComparison.Ordinal) ? $"{e.Message} {Advice(e.Message)}" : $"{e.File}: {e.Message} Fix that file, then export again.")], null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return ([FilesFailed(ex)], null);
        }
        finally
        {
            Discard(copy);
        }
    }

    /// <summary>What a painter does about a message from the pack loader, which speaks for the game and doesn't know about the editor.</summary>
    private static string Advice(string message)
    {
        bool Has(string text) => message.Contains(text, StringComparison.Ordinal);

        if (Has("can't be reached from the start"))
            return $"Paint a walkable way to it, climbing no more than {ClassDef.DefaultJump} levels a step, or move the marker.";
        if (Has("can't be stood on"))
            return "Move the Start marker onto walkable ground, or clear the tile's Impassable or Deep flag.";
        if (Has("doesn't match its map"))
            return "Select its marker and press Apply stamp to put the map back.";
        if (Has("doesn't fit"))
            return "Move the marker, or change Size on the Region node.";
        if (Has("overlaps"))
            return "Move one of the markers, then press Apply stamp.";
        if (Has("is stamped twice"))
            return "Delete one of the markers.";
        if (Has("Unknown map"))
            return "Set the marker's Map to a file in rules/maps.";
        if (Has("The chapter"))
            return "Change Chapter on the Region node.";
        return Has("size") ? "Change Size on the Region node." : "Fix it in the scene, then export again.";
    }

    /// <summary>Puts <paramref name="bytes"/> at <paramref name="path"/> in one move, or says why it couldn't.</summary>
    private static string? Replace(string path, byte[] bytes)
    {
        var temporary = $"{path}.{Environment.ProcessId}.tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, overwrite: true);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Discard(temporary);
            return $"The file {path} couldn't be written: {ex.Message} Close anything holding it, then export again.";
        }
    }

    /// <summary>Removes a temporary file or folder. Failing to tidy up never fails an export.</summary>
    private static void Discard(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
            else
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // It's in a temporary place, or beside the file, and nothing reads it.
        }
    }

    private static bool Listed(string rulesDirectory, string entry)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(rulesDirectory, "manifest.json")));
        return manifest.RootElement.GetProperty("files").EnumerateArray().Any(f => f.GetString() == entry);
    }
}
