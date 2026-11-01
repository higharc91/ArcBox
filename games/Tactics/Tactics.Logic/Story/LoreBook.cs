using System.Collections.Immutable;
using System.Text.Json;

namespace Tactics.Logic;

/// <summary>One option of a choice beat, and the flags choosing it sets.</summary>
public sealed record ArcChoice(string Id, string Label, ImmutableArray<string> Sets);

/// <summary>
/// One beat of an arc, as <c>lore/arcs.json</c> writes it. <see cref="Kind"/> is <c>story</c>, <c>choice</c>, <c>battle</c>,
/// <c>discovery</c> or <c>camp</c>. A battle beat names its <see cref="Encounter"/>. <see cref="Requires"/> are the flags it waits
/// for, and <see cref="Sets"/> the flags it sets once done; a choice's options set their own. A beat can have no <see cref="Place"/>
/// (<c>ch4_pip_remembers</c> is a camp anywhere).
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
/// the rules pack, and <c>tools/tactics-lore</c> checks it, so a book checks only what it needs to play: every field it reads is
/// there and of the right type. A pack with no lore, or whose lore lists no <c>arcs.json</c>, has no arcs.
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
    /// Reads the lore of <paramref name="basePack"/>, the folder that holds <c>lore/</c> (<c>content/tactics/base</c>). A pack with no
    /// <c>lore/pack.json</c> has no arcs, as one whose pack lists no <c>arcs.json</c> has none: the server keeps the book for the life of
    /// the process, so a missing file mustn't fail every room. Lore that's there and can't be read throws
    /// <see cref="InvalidDataException"/>, naming the file: bad JSON, a field missing or of the wrong type, two arcs sharing a region,
    /// or an arc repeating a beat.
    /// </summary>
    public static LoreBook Load(string basePack)
    {
        var lore = Path.Combine(basePack, "lore");
        var pack = Path.Combine(lore, "pack.json");
        if (!File.Exists(pack))
            return Empty;
        var listed = Read(pack, root => List(root, "files", "the pack", required: false)
            .Any(f => f.ValueKind == JsonValueKind.String && f.GetString() == "arcs.json"));
        if (!listed)
            return Empty;

        var path = Path.Combine(lore, "arcs.json");
        var arcs = Read(path, root => List(root, "arcs", "the file", required: true).Select(ReadArc).ToImmutableArray());
        if (arcs.GroupBy(a => a.Region, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1) is { } shared)
            throw new InvalidDataException($"{path}: the region '{shared.Key}' has more than one arc.");
        foreach (var arc in arcs)
            if (arc.Beats.GroupBy(b => b.Id, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1) is { } repeated)
                throw new InvalidDataException($"{path}: the arc '{arc.Id}' lists the beat '{repeated.Key}' twice.");
        return new LoreBook(arcs);
    }

    /// <summary>Lore that can't be read; <see cref="Read{T}"/> names the file.</summary>
    private sealed class BadLore(string message) : Exception(message);

    private static T Read<T>(string path, Func<JsonElement, T> read)
    {
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllBytes(path));
            return read(json.RootElement);
        }
        catch (Exception ex) when (ex is JsonException or BadLore or IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException($"{path} can't be read as lore: {ex.Message}", ex);
        }
    }

    private static Arc ReadArc(JsonElement e)
    {
        var id = Required(e, "id", "an arc");
        var where = $"the arc '{id}'";
        return new(id, Whole(e, "chapter", where), Required(e, "region", where), Optional(e, "title", where) ?? "",
            [.. List(e, "beats", where, required: true).Select(ReadBeat)]);
    }

    private static ArcBeat ReadBeat(JsonElement e)
    {
        var id = Required(e, "id", "a beat");
        var where = $"the beat '{id}'";
        return new(id, Required(e, "kind", where), Optional(e, "place", where), Optional(e, "encounter", where),
            Strings(e, "requires", where), Strings(e, "sets", where), [.. List(e, "choices", where, required: false).Select(c => ReadChoice(c, where))]);
    }

    private static ArcChoice ReadChoice(JsonElement e, string beat)
    {
        var id = Required(e, "id", $"a choice of {beat}");
        var where = $"the choice '{id}'";
        return new(id, Optional(e, "label", where) ?? "", Strings(e, "sets", where));
    }

    private static JsonElement Field(JsonElement e, string name, string where)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new BadLore($"{where} isn't a JSON object.");
        return e.TryGetProperty(name, out var value) ? value : default;
    }

    private static string Required(JsonElement e, string name, string where)
        => Optional(e, name, where) ?? throw new BadLore($"{where} has no '{name}'.");

    /// <summary>A string, or null when it's missing or null.</summary>
    private static string? Optional(JsonElement e, string name, string where) => Field(e, name, where) switch
    {
        { ValueKind: JsonValueKind.Undefined or JsonValueKind.Null } => null,
        { ValueKind: JsonValueKind.String } value => value.GetString(),
        _ => throw new BadLore($"{where} has a '{name}' that isn't a string."),
    };

    private static int Whole(JsonElement e, string name, string where)
        => Field(e, name, where) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt32(out var n)
            ? n
            : throw new BadLore($"{where} needs a whole number for '{name}'.");

    private static IEnumerable<JsonElement> List(JsonElement e, string name, string where, bool required) => Field(e, name, where) switch
    {
        { ValueKind: JsonValueKind.Array } value => value.EnumerateArray(),
        { ValueKind: JsonValueKind.Undefined or JsonValueKind.Null } when !required => [],
        { ValueKind: JsonValueKind.Undefined or JsonValueKind.Null } => throw new BadLore($"{where} has no '{name}' list."),
        _ => throw new BadLore($"{where} has a '{name}' that isn't a list."),
    };

    private static ImmutableArray<string> Strings(JsonElement e, string name, string where)
        =>
        [
            .. List(e, name, where, required: false).Select(s => s.ValueKind == JsonValueKind.String
                ? s.GetString()!
                : throw new BadLore($"{where} has something in '{name}' that isn't a string.")),
        ];
}
