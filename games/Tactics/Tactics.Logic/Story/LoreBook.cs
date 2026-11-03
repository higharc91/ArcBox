using System.Collections.Concurrent;
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
/// The lore a game plays (lore spec §7), read from a pack's <c>lore/pack.json</c> and the files it lists: the arcs of <c>arcs.json</c>,
/// the scenes of <c>scenes/*.json</c>, the characters of <c>characters.json</c>, the places' names of <c>regions.json</c>, and how long
/// each scene line's clip plays, from <c>audio/manifest.json</c>. Lore isn't part of the rules pack, and <c>tools/tactics-lore</c> checks it, so a book checks only what it
/// needs to play: every field it reads is there and of the right type, and each scene's graph holds together (see
/// <see cref="ReadScene"/>). A pack with no lore, or whose lore lists no <c>arcs.json</c>, has no arcs and no scenes.
/// </summary>
public sealed class LoreBook
{
    private readonly ImmutableSortedDictionary<string, Arc> _byRegion;
    private readonly ImmutableDictionary<string, ImmutableArray<SceneDef>> _scenesByBeat;
    private readonly ImmutableDictionary<string, LoreCharacter> _characters;
    private readonly ImmutableDictionary<string, string> _clipFiles;
    private readonly ImmutableDictionary<string, string> _places;
    private readonly ConcurrentDictionary<string, double?> _clipSeconds = new(StringComparer.Ordinal);

    private LoreBook(ImmutableArray<Arc> arcs, ImmutableArray<SceneDef> scenes, ImmutableArray<LoreCharacter> characters,
        ImmutableDictionary<string, string> clipFiles, ImmutableDictionary<string, string> places, ImmutableArray<string> warnings)
    {
        _places = places;
        Warnings = warnings;
        Arcs = arcs;
        Scenes = scenes;
        _byRegion = arcs.ToImmutableSortedDictionary(a => a.Region, a => a, StringComparer.Ordinal);
        _scenesByBeat = scenes.GroupBy(s => s.Beat, StringComparer.Ordinal)
            .ToImmutableDictionary(g => g.Key, g => g.ToImmutableArray(), StringComparer.Ordinal);
        _characters = characters.ToImmutableDictionary(c => c.Id, c => c, StringComparer.Ordinal);
        _clipFiles = clipFiles;
    }

    /// <summary>A book with no arcs, for a game with no lore: no beat is ever available.</summary>
    public static LoreBook Empty { get; } = new([], [], [], ImmutableDictionary<string, string>.Empty, ImmutableDictionary<string, string>.Empty, []);

    /// <summary>Every arc, in the file's order.</summary>
    public ImmutableArray<Arc> Arcs { get; }

    /// <summary>Every scene, in the order the pack lists its files and each file lists them.</summary>
    public ImmutableArray<SceneDef> Scenes { get; }

    /// <summary>
    /// What the lore has that a game can play but would never show, each naming its file, in the pack's order (review): an option that
    /// leads back to its own node, which <see cref="SceneRun.Offered"/> never offers, since that node has played; and a node whose every
    /// option waits on flags, unless one choice beat's choices each set a flag one of them waits on, so a vote guarantees one shows (with
    /// none, the scene's stage ends there). <c>tools/tactics-lore/check.py</c> refuses both, by the same rules; the server logs these.
    /// </summary>
    public ImmutableArray<string> Warnings { get; }

    /// <summary>The arc set in <paramref name="region"/>, or null if none is.</summary>
    public Arc? ArcFor(string region) => _byRegion.GetValueOrDefault(region);

    /// <summary>The scenes of <paramref name="beat"/>, in the order they play (lore §7, Order); empty for a beat with none.</summary>
    public ImmutableArray<SceneDef> ScenesOf(string beat) => _scenesByBeat.GetValueOrDefault(beat, []);

    /// <summary>The character with this id, or null: the narrator isn't one, and neither is an id the pack doesn't have.</summary>
    public LoreCharacter? Character(string id) => _characters.GetValueOrDefault(id);

    /// <summary>
    /// The name of the place <paramref name="id"/> (<c>regions.json</c>, a region's <c>places</c>), as the walk screen's objective says it, or
    /// null for a place the pack doesn't name.
    /// </summary>
    public string? PlaceName(string id) => _places.GetValueOrDefault(id);

    /// <summary>
    /// How long the clip of the scene line <paramref name="line"/> plays, in seconds: its speaker's own voice (<c>main</c>) in
    /// <c>audio/manifest.json</c>. Null when the line has no clip, its file is missing, or it can't be read (<see cref="ClipLength"/>):
    /// the game then gives the line a reading timer. The file is read the first time its line asks, not at <see cref="Load"/>, since
    /// lines are spoken one at a time and reading all 829 would hold up the first room; the answer is kept after that.
    /// </summary>
    public double? ClipSeconds(string line)
        => _clipFiles.TryGetValue(line, out var path) ? _clipSeconds.GetOrAdd(line, static (_, file) => Measure(file), path) : null;

    /// <summary>A clip's length, or null when it isn't there, can't be opened, or isn't a whole Ogg Vorbis stream.</summary>
    private static double? Measure(string path)
    {
        try
        {
            using var ogg = File.OpenRead(path);
            return ClipLength.Of(ogg);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the lore of <paramref name="basePack"/>, the folder that holds <c>lore/</c> (<c>content/tactics/base</c>). A pack with no
    /// <c>lore/pack.json</c> has no arcs, as one whose pack lists no <c>arcs.json</c> has none: the server keeps the book for the life of
    /// the process, so a missing file mustn't fail every room. A pack that lists no scenes has its arcs and no scenes, and with no audio
    /// manifest no line has a clip. Lore that's there and can't be read throws <see cref="InvalidDataException"/>, naming the file: bad
    /// JSON, a field missing or of the wrong type, two arcs sharing a region, an arc repeating a beat, a character listed twice, or a scene
    /// that can't be played (<see cref="ReadScene"/>).
    /// </summary>
    public static LoreBook Load(string basePack)
    {
        var lore = Path.Combine(basePack, "lore");
        var pack = Path.Combine(lore, "pack.json");
        if (!File.Exists(pack))
            return Empty;
        var files = Read(pack, root => List(root, "files", "the pack", required: false)
            .Where(f => f.ValueKind == JsonValueKind.String).Select(f => f.GetString()!).ToImmutableArray());
        if (!files.Contains("arcs.json"))
            return Empty;

        var path = Path.Combine(lore, "arcs.json");
        var arcs = Read(path, root => List(root, "arcs", "the file", required: true).Select(ReadArc).ToImmutableArray());
        if (arcs.GroupBy(a => a.Region, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1) is { } shared)
            throw new InvalidDataException($"{path}: the region '{shared.Key}' has more than one arc.");
        foreach (var arc in arcs)
            if (arc.Beats.GroupBy(b => b.Id, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1) is { } repeated)
                throw new InvalidDataException($"{path}: the arc '{arc.Id}' lists the beat '{repeated.Key}' twice.");

        var characterPath = Path.Combine(lore, "characters.json");
        ImmutableArray<LoreCharacter> characters = files.Contains("characters.json")
            ? Read(characterPath, root => List(root, "characters", "the file", required: true).Select(ReadCharacter).ToImmutableArray())
            : [];
        if (characters.GroupBy(c => c.Id, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1) is { } twice)
            throw new InvalidDataException($"{characterPath}: the character '{twice.Key}' is listed twice.");

        var beats = arcs.SelectMany(a => a.Beats).ToImmutableDictionary(b => b.Id, b => b.Kind, StringComparer.Ordinal);
        var scenes = ImmutableArray.CreateBuilder<SceneDef>();
        var warnings = ImmutableArray.CreateBuilder<string>();
        var choiceBeats = arcs.SelectMany(a => a.Beats).Where(b => !b.Choices.IsEmpty).ToImmutableArray();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var aftermath = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files.Where(f => f.StartsWith("scenes/", StringComparison.Ordinal)))
        {
            var scenePath = Path.Combine(lore, file);
            foreach (var scene in Read(scenePath, root => List(root, "scenes", "the file", required: true).Select(s => ReadScene(s, beats)).ToImmutableArray()))
            {
                if (!seen.Add(scene.Id))
                    throw new InvalidDataException($"{scenePath}: the scene '{scene.Id}' is already in the pack.");
                // A battle beat plays its lead-in, the fight, then its aftermath, so in the pack's order no lead-in can follow an aftermath.
                if (scene.After)
                    aftermath.Add(scene.Beat);
                else if (aftermath.Contains(scene.Beat))
                    throw new InvalidDataException($"{scenePath}: the scene '{scene.Id}' is a lead-in of '{scene.Beat}', and comes after one of its aftermath scenes.");
                scenes.Add(scene);
                warnings.AddRange(Unshown(scene, choiceBeats).Select(w => $"{scenePath}: {w}"));
            }
        }

        var regionsPath = Path.Combine(lore, "regions.json");
        var places = files.Contains("regions.json")
            ? Read(regionsPath, root => List(root, "regions", "the file", required: true)
                .SelectMany(r => List(r, "places", $"the region '{Optional(r, "id", "a region")}'", required: false))
                .Select(p => (Id: Required(p, "id", "a place"), Name: Required(p, "name", $"the place '{Optional(p, "id", "a place")}'")))
                .ToImmutableArray())
            : [];
        if (places.GroupBy(p => p.Id, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1) is { } named)
            throw new InvalidDataException($"{regionsPath}: the place '{named.Key}' is listed twice.");

        var lines = scenes.SelectMany(s => s.Lines).Select(l => l.Id).ToImmutableHashSet(StringComparer.Ordinal);
        return new LoreBook(arcs, scenes.ToImmutable(), characters, Clips(Path.Combine(lore, "audio"), lines),
            places.ToImmutableDictionary(p => p.Id, p => p.Name, StringComparer.Ordinal), warnings.ToImmutable());
    }

    /// <summary>What of <paramref name="scene"/> can never show (<see cref="Warnings"/>), node by node.</summary>
    private static IEnumerable<string> Unshown(SceneDef scene, ImmutableArray<ArcBeat> choiceBeats)
    {
        foreach (var node in scene.Nodes)
        {
            var where = $"the node '{node.Id}' of the scene '{scene.Id}'";
            foreach (var option in node.Options.Where(o => o.Targets.Contains(node.Id)))
                yield return $"the option '{option.Id}' of {where} leads back to its own node, so it's never offered.";
            if (node.Options.IsEmpty || node.Options.Any(o => o.Requires.IsEmpty))
                continue;
            var waits = node.Options.SelectMany(o => o.Requires).ToHashSet(StringComparer.Ordinal);
            if (!choiceBeats.Any(b => b.Choices.All(c => c.Sets.Any(waits.Contains))))
                yield return $"{where} has every option gated on flags, and no vote guarantees one of them is set: the party could be left with nothing to pick.";
        }
    }

    /// <summary>
    /// The clip file of each of <paramref name="lines"/> that has a <c>main</c> clip in the manifest of <paramref name="audio"/>: only
    /// the manifest is read here. No manifest means no clips; a manifest that's there and can't be read is lore that can't be read, as
    /// is one naming a file outside the audio folder (an absolute path, or one with <c>..</c>).
    /// </summary>
    private static ImmutableDictionary<string, string> Clips(string audio, ImmutableHashSet<string> lines)
    {
        var manifest = Path.Combine(audio, "manifest.json");
        if (lines.IsEmpty || !File.Exists(manifest))
            return ImmutableDictionary<string, string>.Empty;
        var listed = Read(manifest, root => Field(root, "lines", "the manifest") switch
        {
            { ValueKind: JsonValueKind.Object } all => all.EnumerateObject().Where(l => lines.Contains(l.Name))
                .Select(l => (Line: l.Name, File: Optional(l.Value, "main", $"the line '{l.Name}'"))).ToImmutableArray(),
            _ => throw new BadLore("the manifest has no 'lines' object."),
        });

        var clips = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        foreach (var (line, file) in listed)
        {
            if (file is null)
                continue;
            if (Path.IsPathRooted(file) || file.Split('/', '\\').Contains(".."))
                throw new InvalidDataException($"{manifest} can't be read as lore: the line '{line}' names the clip '{file}', which isn't in the audio folder.");
            clips[line] = Path.Combine(audio, file);
        }
        return clips.ToImmutable();
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

    private static LoreCharacter ReadCharacter(JsonElement e)
    {
        var id = Required(e, "id", "a character");
        var where = $"the character '{id}'";
        return new(id, Required(e, "name", where), Required(e, "preset", where), Required(e, "role", where));
    }

    /// <summary>
    /// A scene, refused unless it can be played to the end without a lookup failing: its <c>beat</c> is one of <paramref name="beats"/>,
    /// its nodes have distinct ids and <c>start</c> is one of them, each node has at least one line and exactly one of <c>options</c>
    /// (at least one), <c>next</c> or <c>end: true</c>, each option has <c>to</c> or a <c>check</c>, and everything they lead to is a
    /// node of the scene; only a battle beat's scene is an aftermath (<c>after</c>); and every check is of a skill in
    /// <see cref="SkillCheck.Skills"/>. These are <c>tools/tactics-lore/check.py</c>'s rules too, so the server and the tool agree. What a game can play but a writer wouldn't want (a node nobody reaches, a flag nothing sets) is
    /// <c>tools/tactics-lore</c>'s to find.
    /// </summary>
    private static SceneDef ReadScene(JsonElement e, ImmutableDictionary<string, string> beats)
    {
        var id = Required(e, "id", "a scene");
        var where = $"the scene '{id}'";
        var beat = Required(e, "beat", where);
        if (!beats.TryGetValue(beat, out var kind))
            throw new BadLore($"{where} plays the beat '{beat}', which no arc has.");
        if (Flag(e, "after", where) && kind != ArcBeat.Battle)
            throw new BadLore($"{where} is an aftermath, and only a battle beat has one: '{beat}' is a {kind} beat.");
        var nodes = List(e, "nodes", where, required: true).Select(n => ReadNode(n, where)).ToImmutableArray();
        var scene = new SceneDef(id, beat, Optional(e, "place", where), Optional(e, "title", where) ?? "", Strings(e, "requires", where),
            Strings(e, "unless", where), Strings(e, "cast", where), Required(e, "start", where), nodes, Flag(e, "after", where));

        if (nodes.GroupBy(n => n.Id, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1) is { } repeated)
            throw new BadLore($"{where} has the node '{repeated.Key}' twice.");
        if (scene.Node(scene.Start) is null)
            throw new BadLore($"{where} starts at '{scene.Start}', which isn't one of its nodes.");
        foreach (var node in nodes)
            if (node.Targets.FirstOrDefault(t => scene.Node(t) is null) is { } missing)
                throw new BadLore($"{where}: the node '{node.Id}' leads to '{missing}', which isn't one of its nodes.");
        return scene;
    }

    private static SceneNode ReadNode(JsonElement e, string scene)
    {
        var id = Required(e, "id", $"a node of {scene}");
        var where = $"the node '{id}' of {scene}";
        var lines = List(e, "lines", where, required: true).Select(l => ReadLine(l, where)).ToImmutableArray();
        if (lines.IsEmpty)
            throw new BadLore($"{where} has no lines.");
        var options = List(e, "options", where, required: false).Select(o => ReadOption(o, where)).ToImmutableArray();
        var hasOptions = Field(e, "options", where).ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null);
        if (hasOptions && options.IsEmpty)
            throw new BadLore($"{where} has an empty 'options' list.");
        var next = Optional(e, "next", where);
        var end = Flag(e, "end", where);
        if ((hasOptions ? 1 : 0) + (next is null ? 0 : 1) + (end ? 1 : 0) != 1)
            throw new BadLore($"{where} needs exactly one of 'options', 'next' or 'end'.");
        return new(id, lines, Strings(e, "sets", where), options, Flag(e, "vote", where), next, end, Gold(e, where));
    }

    private static SceneLine ReadLine(JsonElement e, string node)
    {
        var id = Required(e, "id", $"a line of {node}");
        var where = $"the line '{id}'";
        return new(id, Required(e, "speaker", where), Required(e, "text", where), Optional(e, "mood", where) ?? SceneLine.DefaultMood);
    }

    private static SceneOption ReadOption(JsonElement e, string node)
    {
        var id = Required(e, "id", $"an option of {node}");
        var where = $"the option '{id}' of {node}";
        var tags = Field(e, "tags", where) is { ValueKind: JsonValueKind.Object } t
            ? new OptionTags(Strings(t, "background", where), Strings(t, "race", where), Strings(t, "class", where))
            : OptionTags.None;
        var check = Field(e, "check", where) is { ValueKind: not (JsonValueKind.Undefined or JsonValueKind.Null) } c
            ? new SceneCheck(Required(c, "skill", $"the check of {where}"), Whole(c, "dc", $"the check of {where}"),
                Required(c, "pass", $"the check of {where}"), Required(c, "fail", $"the check of {where}"))
            : null;
        if (check is not null && SkillCheck.StatOf(check.Skill) is null)
            throw new BadLore($"the check of {where} is of '{check.Skill}', which isn't a skill.");
        var to = Optional(e, "to", where);
        if (to is null && check is null)
            throw new BadLore($"{where} needs 'to' or a 'check'.");
        return new(id, Required(e, "label", where), to, Optional(e, "choice", where), Strings(e, "sets", where), Strings(e, "requires", where),
            tags, check, Gold(e, where));
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

    /// <summary>A boolean, false when it's missing.</summary>
    private static bool Flag(JsonElement e, string name, string where) => Field(e, name, where) switch
    {
        { ValueKind: JsonValueKind.Undefined or JsonValueKind.Null } => false,
        { ValueKind: JsonValueKind.True } => true,
        { ValueKind: JsonValueKind.False } => false,
        _ => throw new BadLore($"{where} has a '{name}' that isn't true or false."),
    };

    private static int Gold(JsonElement e, string where)
    {
        if (!e.TryGetProperty("gold", out _)) return 0;
        var value = Whole(e, "gold", where);
        if (value == int.MinValue) throw new BadLore($"{where} gold is outside the supported range.");
        return value;
    }

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
