using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace Meridian.Presentation.Art;

/// <summary>A checked art file: where it is and how to cut it.</summary>
/// <param name="Path">The file on disk.</param>
/// <param name="Frames">Frames in the strip, left to right (1 for a still).</param>
/// <param name="Insets">A 9-slice's left, top, right and bottom insets, if it has them.</param>
public sealed record ArtEntry(string Path, int FrameW, int FrameH, int Frames, int Fps, bool Loop, IReadOnlyList<int>? Insets,
                              IReadOnlyList<int>? Anchor);

/// <summary>What one set covers: the ids it has art for, and those still drawn as placeholders.</summary>
public sealed record ArtCoverage(string Set, IReadOnlyList<string> Covered, IReadOnlyList<string> Missing);

/// <summary>A problem with the pack's art. An error leaves its entry out (it draws as a placeholder); a warning doesn't.</summary>
public sealed record ArtProblem(string Set, string Where, string Message, bool Error = true)
{
    public override string ToString() => $"{(Error ? "error" : "warning")}: {Set}/{Where}: {Message}";
}

/// <summary>
/// Checks a pack's art against the contract and the rules content (M5 plan, Task 1): every manifest, every id, every
/// file's bytes and frames. Used by a unit test on the shipped pack, by <c>tools/meridian-assets/check</c> on an art
/// branch, and by the TV, which draws only what passes.
/// </summary>
public sealed class ArtCheck
{
    private readonly Dictionary<string, ArtEntry> _entries = new(StringComparer.Ordinal);
    private readonly List<ArtProblem> _problems = [];
    private readonly List<ArtCoverage> _coverage = [];

    /// <summary>Every entry that passed, keyed <c>set/id[/name…]</c> (variants as <c>tiles/id/variants/deck</c>).</summary>
    public IReadOnlyDictionary<string, ArtEntry> Entries => _entries;

    public IReadOnlyList<ArtProblem> Problems => _problems;
    public IEnumerable<ArtProblem> Errors => _problems.Where(p => p.Error);
    public IReadOnlyList<ArtCoverage> Coverage => _coverage;

    public static ArtCheck Run(string packRoot, RulesPack pack)
    {
        var check = new ArtCheck();
        foreach (var set in ArtContract.Sets)
        {
            check.CheckSet(packRoot, pack, set);
        }
        return check;
    }

    private void CheckSet(string root, RulesPack pack, ArtSet set)
    {
        var ids = set.Ids(pack);
        var path = Path.Combine(root, set.Name, "manifest.json");
        var covered = new List<string>();
        if (File.Exists(path))
        {
            JsonElement manifest;
            try
            {
                manifest = JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();
            }
            catch (JsonException e)
            {
                _problems.Add(new(set.Name, "manifest.json", e.Message));
                _coverage.Add(new(set.Name, [], [.. ids]));
                return;
            }
            if (!manifest.TryGetProperty(set.MapKey, out var map) || map.ValueKind != JsonValueKind.Object)
            {
                _problems.Add(new(set.Name, "manifest.json", $"has no \"{set.MapKey}\" object"));
            }
            else
            {
                foreach (var item in map.EnumerateObject())
                {
                    if (!ids.Contains(item.Name))
                    {
                        _problems.Add(new(set.Name, item.Name, set.Strict ? "is not an id the game has" : "is not a piece the game draws yet",
                                          Error: set.Strict));
                        if (set.Strict)
                        {
                            continue;
                        }
                    }
                    var before = _entries.Count;
                    Walk(root, pack, set, item.Name, $"{set.Name}/{item.Name}", item.Value, depth: 0, inherited: null);
                    if (_entries.Count > before)
                    {
                        covered.Add(item.Name);
                    }
                }
            }
            if (set.Name == "hatches")
            {
                foreach (var state in ArtContract.HatchStates.Where(s => !covered.Contains(s)))
                {
                    _problems.Add(new(set.Name, state, "a hatch state is missing; all four are drawn", Error: covered.Count > 0));
                }
            }
        }
        _coverage.Add(new(set.Name, covered, [.. ids.Where(i => !covered.Contains(i))]));
    }

    /// <summary>An entry (an object with a file), or an object of named entries and groups.</summary>
    /// <param name="inherited">A group's <c>anchor</c> and <c>frameSize</c>, which its entries share unless they say otherwise.</param>
    private void Walk(string root, RulesPack pack, ArtSet set, string id, string key, JsonElement node, int depth, JsonElement? inherited)
    {
        if (node.ValueKind != JsonValueKind.Object || depth > 4)
        {
            return;
        }
        if (node.TryGetProperty("file", out _))
        {
            Entry(root, pack, set, id, key, node, inherited);
            return;
        }
        var shared = node.TryGetProperty("anchor", out _) || node.TryGetProperty("frameSize", out _) ? node : inherited;
        foreach (var child in node.EnumerateObject().Where(c => c.Value.ValueKind == JsonValueKind.Object))
        {
            if (child.Name == "variants" && set.Name == "tiles")
            {
                var decks = ArtContract.TileDecks(pack, id);
                foreach (var variant in child.Value.EnumerateObject())
                {
                    if (!decks.Contains(variant.Name))
                    {
                        _problems.Add(new(set.Name, $"{id}/variants/{variant.Name}", $"is a deck this tile can't be placed on ({string.Join(", ", decks)})"));
                        continue;
                    }
                    Entry(root, pack, set, id, $"{key}/variants/{variant.Name}", variant.Value, shared);
                }
                continue;
            }
            Walk(root, pack, set, id, $"{key}/{child.Name}", child.Value, depth + 1, shared);
        }
    }

    private void Entry(string root, RulesPack pack, ArtSet set, string id, string key, JsonElement entry, JsonElement? inherited)
    {
        var where = key[(set.Name.Length + 1)..];
        void Problem(string message) => _problems.Add(new(set.Name, where, message));

        if (entry.TryGetProperty("text", out _))
        {
            Problem("claims text; art never carries text, which is rendered live");
            return;
        }
        if (entry.GetProperty("file").GetString() is not { Length: > 0 } file || file.Contains("..") || Path.IsPathRooted(file))
        {
            Problem("names no file in its set's folder");
            return;
        }
        var path = Path.Combine(root, set.Name, file);
        if (!File.Exists(path))
        {
            Problem($"{file} is missing");
            return;
        }
        var bytes = File.ReadAllBytes(path);
        if (entry.TryGetProperty("sha256", out var sha) && sha.GetString() is { } want
            && !string.Equals(Convert.ToHexStringLower(SHA256.HashData(bytes)), want, StringComparison.OrdinalIgnoreCase))
        {
            Problem($"{file}'s bytes don't match its sha256");
            return;
        }

        // A tile variant must be for a deck the tile is on (a single tile entry may say which deck it was drawn for).
        if (set.Name == "tiles" && entry.TryGetProperty("deck", out var deck) && deck.GetString() is { } d
            && !ArtContract.TileDecks(pack, id).Contains(d))
        {
            Problem($"is drawn for the {d} deck, which this tile isn't on");
            return;
        }

        var frames = entry.TryGetProperty("frames", out var f) && f.TryGetInt32(out var n) ? n : 1;
        var fps = entry.TryGetProperty("fps", out var r) && r.TryGetInt32(out var q) ? q : 0;
        var loop = !entry.TryGetProperty("loop", out var l) || l.ValueKind != JsonValueKind.False;
        int[]? Ints(string name)
        {
            var from = entry.TryGetProperty(name, out _) ? entry : inherited is { } group && group.TryGetProperty(name, out _) ? group : (JsonElement?)null;
            return from is { } source && source.GetProperty(name) is { ValueKind: JsonValueKind.Array } a ? [.. a.EnumerateArray().Select(x => x.GetInt32())] : null;
        }
        var insets = Ints("insets");
        var anchor = Ints("anchor");
        var size = Ints("frameSize");
        if (frames < 1)
        {
            Problem("has fewer than one frame");
            return;
        }

        int fw = 0, fh = 0;
        if (Path.GetExtension(file).Equals(".png", StringComparison.OrdinalIgnoreCase))
        {
            if (Png(bytes) is not var (w, h))
            {
                Problem($"{file} isn't a PNG");
                return;
            }
            if (w % frames != 0)
            {
                Problem($"{file} is {w} px wide, which doesn't divide into {frames} frames");
                return;
            }
            (fw, fh) = (w / frames, h);
            if (size is [var sw, var sh] && (sw, sh) != (fw, fh))
            {
                Problem($"says its frames are {sw}x{sh}, but {file}'s are {fw}x{fh}");
                return;
            }
            if (set.Frames.Count > 0 && !set.Frames.Contains(new FrameSize(fw, fh)))
            {
                Problem($"frames are {fw}x{fh}; the contract allows {string.Join(" or ", set.Frames.Select(s => $"{s.W}x{s.H}"))}");
                return;
            }
            if (insets is { } i && (i.Length != 4 || i[0] + i[2] >= fw || i[1] + i[3] >= fh))
            {
                Problem("has 9-slice insets that don't fit its frame");
                return;
            }
        }
        if (set.NeedsInsets && insets is null)
        {
            Problem("needs 9-slice insets, so it can be drawn at any size");
            return;
        }
        _entries[key] = new ArtEntry(path, fw, fh, frames, fps, loop, insets, anchor);
    }

    /// <summary>A PNG's width and height from its header, or null.</summary>
    private static (int W, int H)? Png(byte[] bytes)
        => bytes.Length >= 24 && bytes[1] == 'P' && bytes[2] == 'N' && bytes[3] == 'G'
            ? (BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16)), BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20)))
            : null;

    /// <summary>A readable report: errors and warnings, then each set's coverage.</summary>
    public string Report()
    {
        var lines = _problems.Select(p => p.ToString()).ToList();
        lines.AddRange(_coverage.Select(c => $"{c.Set}: {c.Covered.Count} of {c.Covered.Count + c.Missing.Count} with art"
                                             + (c.Missing.Count is > 0 and <= 12 ? $" (placeholders: {string.Join(", ", c.Missing)})" : "")));
        return string.Join('\n', lines);
    }
}
