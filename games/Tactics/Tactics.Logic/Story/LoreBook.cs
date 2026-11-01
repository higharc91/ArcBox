using System.Collections.Immutable;
using System.Text.Json;

namespace Tactics.Logic;

/// <summary>One option of a choice beat, and the flags choosing it sets.</summary>
public sealed record ArcChoice(string Id, string Label, ImmutableArray<string> Sets);

/// <summary>
/// One beat of an arc, as <c>lore/arcs.json</c> writes it. <see cref="Kind"/> is <c>story</c>, <c>choice</c>, <c>battle</c>,
/// <c>discovery</c> or <c>camp</c>. A battle beat names its <see cref="Encounter"/>. <see cref="Requires"/> are the flags it waits
/// for, and <see cref="Sets"/> the flags it sets once done; a choice's options set their own.
/// </summary>
public sealed record ArcBeat(string Id, string Kind, string? Place, string? Encounter, ImmutableArray<string> Requires,
    ImmutableArray<string> Sets, ImmutableArray<ArcChoice> Choices)
{
    /// <summary>The beat kind that's a fight.</summary>
    public const string Battle = "battle";
}

/// <summary>A chapter's story in one region: its beats, in the order they're offered.</summary>
public sealed record Arc(string Id, int Chapter, string Region, string Title, ImmutableArray<ArcBeat> Beats);

/// <summary>
/// The arcs of the lore (lore spec), read from a pack's <c>lore/pack.json</c> and the <c>arcs.json</c> it lists. Lore isn't part of
/// the rules pack, and <c>tools/tactics-lore</c> checks it, so a book trusts its shape and refuses only what it can't read. A pack
/// that lists no <c>arcs.json</c> has no arcs.
/// </summary>
public sealed class LoreBook
{
    private readonly ImmutableSortedDictionary<string, Arc> _byRegion;

    private LoreBook(ImmutableArray<Arc> arcs)
    {
        Arcs = arcs;
        _byRegion = arcs.ToImmutableSortedDictionary(a => a.Region, a => a, StringComparer.Ordinal);
    }

    /// <summary>A book with no arcs, for a game with no lore: no beat is ever available.</summary>
    public static LoreBook Empty { get; } = new([]);

    /// <summary>Every arc, in the file's order.</summary>
    public ImmutableArray<Arc> Arcs { get; }

    /// <summary>The arc set in <paramref name="region"/>, or null if none is.</summary>
    public Arc? ArcFor(string region) => _byRegion.GetValueOrDefault(region);

    /// <summary>
    /// Reads the lore of <paramref name="basePack"/>, the folder that holds <c>lore/</c> (<c>content/tactics/base</c>). Throws
    /// <see cref="InvalidDataException"/>, naming the file, when the lore can't be read or two arcs share a region or an arc repeats a beat,
    /// and an <see cref="IOException"/> when a file is missing: a server without its lore is deployed wrong, as one without its rules is.
    /// </summary>
    public static LoreBook Load(string basePack)
    {
        var lore = Path.Combine(basePack, "lore");
        var pack = Path.Combine(lore, "pack.json");
        var listed = Read(pack, root => root.TryGetProperty("files", out var files) && files.EnumerateArray().Any(f => f.GetString() == "arcs.json"));
        if (!listed)
            return Empty;

        var path = Path.Combine(lore, "arcs.json");
        var arcs = Read(path, root => root.GetProperty("arcs").EnumerateArray().Select(ReadArc).ToImmutableArray());
        if (arcs.GroupBy(a => a.Region, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1) is { } shared)
            throw new InvalidDataException($"{path}: the region '{shared.Key}' has more than one arc.");
        foreach (var arc in arcs)
            if (arc.Beats.GroupBy(b => b.Id, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1) is { } repeated)
                throw new InvalidDataException($"{path}: the arc '{arc.Id}' lists the beat '{repeated.Key}' twice.");
        return new LoreBook(arcs);
    }

    private static T Read<T>(string path, Func<JsonElement, T> read)
    {
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllBytes(path));
            return read(json.RootElement);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidDataException($"{path} can't be read as lore: {ex.Message}", ex);
        }
    }

    private static Arc ReadArc(JsonElement e)
        => new(e.GetProperty("id").GetString()!, e.GetProperty("chapter").GetInt32(), e.GetProperty("region").GetString()!,
            e.TryGetProperty("title", out var title) ? title.GetString() ?? "" : "",
            [.. e.GetProperty("beats").EnumerateArray().Select(ReadBeat)]);

    private static ArcBeat ReadBeat(JsonElement e)
        => new(e.GetProperty("id").GetString()!, e.GetProperty("kind").GetString()!, Optional(e, "place"), Optional(e, "encounter"),
            Strings(e, "requires"), Strings(e, "sets"),
            e.TryGetProperty("choices", out var choices)
                ? [.. choices.EnumerateArray().Select(c => new ArcChoice(c.GetProperty("id").GetString()!, Optional(c, "label") ?? "", Strings(c, "sets")))]
                : []);

    private static string? Optional(JsonElement e, string name) => e.TryGetProperty(name, out var value) ? value.GetString() : null;

    private static ImmutableArray<string> Strings(JsonElement e, string name)
        => e.TryGetProperty(name, out var list) ? [.. list.EnumerateArray().Select(s => s.GetString()!)] : [];
}
