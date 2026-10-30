using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tactics.Logic;

/// <summary>
/// A rules pack's <c>manifest.json</c> (spec §4.9): its id, the rules version it is written for, the packs it builds on and
/// its content files, relative to the pack folder with forward slashes.
/// </summary>
public sealed record PackManifest(string Id, int RulesVersion, ImmutableArray<string> Requires, ImmutableArray<string> Files);

/// <summary>A definition read from a pack file, with its id, the id it replaces and where it was written.</summary>
internal sealed record Loaded<T>(T Def, string Id, string? Replaces, string File, string Pointer);

/// <summary>
/// Reads rules packs for <see cref="RulesPack.Load"/>: the manifests, the pack order, each listed file by its kind, the
/// merge of ids across packs (a later pack may add ids but changes one only with <c>"replaces"</c>) and the content hash.
/// The checks themselves are in <see cref="Validation"/>.
/// </summary>
internal static class PackLoader
{
    /// <summary>The newest rules version this engine reads.</summary>
    public const int RulesVersion = 1;

    /// <summary>What a load produces.</summary>
    internal sealed record Result(string Hash, ImmutableSortedDictionary<AbilityId, AbilityDef> Abilities,
        ImmutableSortedDictionary<string, PassiveDef> Passives, ImmutableSortedDictionary<ConditionId, ConditionDef> Conditions,
        ImmutableSortedDictionary<InjuryId, InjuryDef> Injuries, ImmutableSortedDictionary<string, BattleMap> Maps,
        RulesPack.Catalog Catalog);

    private sealed record Pack(string Dir, PackManifest Manifest, byte[] ManifestBytes)
    {
        public string Id => Manifest.Id;

        public string ManifestFile => $"{Id}/manifest.json";
    }

    /// <summary>The kinds of content file, by where they sit in a pack.</summary>
    private enum FileKind { Conditions, Races, Backgrounds, Injuries, Xp, AiProfiles, Items, Abilities, Class, Monsters, Map, Encounter }

    /// <summary>Everything read from the packs, in load order, before ids are merged.</summary>
    private sealed class Content
    {
        public List<Loaded<AbilityDef>> Abilities { get; } = [];
        public List<Loaded<PassiveDef>> Passives { get; } = [];
        public List<Loaded<ConditionDef>> Conditions { get; } = [];
        public List<Loaded<ClassDef>> Classes { get; } = [];
        public List<Loaded<RaceDef>> Races { get; } = [];
        public List<Loaded<BackgroundDef>> Backgrounds { get; } = [];
        public List<Loaded<MonsterDef>> Monsters { get; } = [];
        public List<Loaded<ItemDef>> Items { get; } = [];
        public List<Loaded<MapContent>> Maps { get; } = [];
        public List<Loaded<EncounterDef>> Encounters { get; } = [];
        public List<Loaded<AiProfileDef>> AiProfiles { get; } = [];
        public List<Loaded<InjuryDef>> Injuries { get; } = [];
        public List<Loaded<XpTable>> Xp { get; } = [];
    }

    /// <summary>Loads the packs in <paramref name="packDirectories"/>; see <see cref="RulesPack.Load"/>.</summary>
    public static Result Load(IEnumerable<string> packDirectories, ArtManifest? art)
    {
        var v = new Validation(art);
        var hashed = new List<(string, byte[])>();
        var content = new Content();

        foreach (var pack in Order(ReadManifests(packDirectories, v), v))
        {
            hashed.Add((pack.ManifestFile, pack.ManifestBytes));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < pack.Manifest.Files.Length; i++)
            {
                var file = pack.Manifest.Files[i];
                var at = Pointer.Append("/files", i);
                if (!IsRelativePath(file))
                    v.Error(pack.ManifestFile, at, $"'{file}' isn't a path inside the pack (use forward slashes, no '.' or '..').");
                else if (!seen.Add(file))
                    v.Error(pack.ManifestFile, at, $"'{file}' is listed twice.");
                else if (KindOf(file) is not { } kind)
                    v.Error(pack.ManifestFile, at, $"'{file}' isn't a known content file (see rules/schema for the file kinds).");
                else if (!File.Exists(Path.Combine(pack.Dir, file)))
                    v.Error(pack.ManifestFile, at, $"The file '{file}' doesn't exist.");
                else
                {
                    var label = $"{pack.Id}/{file}";
                    try
                    {
                        var bytes = File.ReadAllBytes(Path.Combine(pack.Dir, file));
                        hashed.Add((label, bytes));
                        if (Parse(bytes, label, v) is { } doc)
                            using (doc)
                                Read(kind, doc.RootElement, label, content, v);
                    }
                    catch (Exception ex) when (Validation.NonFatal(ex))
                    {
                        v.Error(label, "", $"This file couldn't be read: {ex.Message}");
                    }
                }
            }
        }

        var abilities = Merge(content.Abilities, "ability", v);
        var passives = Merge(content.Passives, "passive", v);
        var conditions = Merge(content.Conditions, "condition", v);
        var classes = Merge(content.Classes, "class", v);
        var races = Merge(content.Races, "race", v);
        var backgrounds = Merge(content.Backgrounds, "background", v);
        var monsters = Merge(content.Monsters, "monster", v);
        var items = Merge(content.Items, "item", v);
        var maps = Merge(content.Maps, "map", v);
        var encounters = Merge(content.Encounters, "encounter", v);
        var aiProfiles = Merge(content.AiProfiles, "AI profile", v);
        var injuries = Merge(content.Injuries, "injury", v);
        var xp = Merge(content.Xp, "XP table", v);

        v.Resolve((kind, id) => kind switch
        {
            RefKind.Ability => abilities.ContainsKey(id),
            RefKind.Passive => passives.ContainsKey(id),
            RefKind.Condition => conditions.ContainsKey(id),
            RefKind.Monster => monsters.ContainsKey(id),
            RefKind.Item => items.ContainsKey(id),
            RefKind.Map => maps.ContainsKey(id),
            RefKind.AiProfile => aiProfiles.ContainsKey(id),
            _ => false,
        }, anyAiProfiles: aiProfiles.Count > 0);
        var battleMaps = Index(maps.Values, m => m.Id, m => m.Def.Map, StringComparer.Ordinal);
        v.CheckEncounters(encounters.Values, battleMaps);

        if (v.Errors.Count > 0)
            throw new ContentException([.. v.Errors]);

        return new Result(ContentHash.Of(hashed),
            Index(abilities.Values, a => a.Def.Id, a => a.Def, Comparer<AbilityId>.Default),
            Index(passives.Values, p => p.Id, p => p.Def, StringComparer.Ordinal),
            Index(conditions.Values, c => c.Def.Id, c => c.Def, Comparer<ConditionId>.Default),
            Index(injuries.Values, i => i.Def.Id, i => i.Def, Comparer<InjuryId>.Default),
            battleMaps,
            new RulesPack.Catalog(
                Index(classes.Values, c => c.Id, c => c.Def, StringComparer.Ordinal),
                Index(races.Values, r => r.Id, r => r.Def, StringComparer.Ordinal),
                Index(backgrounds.Values, b => b.Id, b => b.Def, StringComparer.Ordinal),
                Index(monsters.Values, m => m.Id, m => m.Def, StringComparer.Ordinal),
                Index(items.Values, i => i.Def.Id, i => i.Def, Comparer<ItemId>.Default),
                Index(encounters.Values, e => e.Id, e => e.Def, StringComparer.Ordinal),
                Index(aiProfiles.Values, a => a.Id, a => a.Def, StringComparer.Ordinal),
                xp.Values.SingleOrDefault()?.Def ?? XpTable.Default));
    }

    // ---- Packs ------------------------------------------------------------------------------------------------------

    private static List<Pack> ReadManifests(IEnumerable<string> packDirectories, Validation v)
    {
        var packs = new List<Pack>();
        foreach (var dir in packDirectories)
        {
            var label = $"{Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir)))}/manifest.json";
            var path = Path.Combine(dir, "manifest.json");
            if (!File.Exists(path))
            {
                v.Error(label, "", "The pack has no manifest.json.");
                continue;
            }
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception ex) when (Validation.NonFatal(ex))
            {
                v.Error(label, "", $"This file couldn't be read: {ex.Message}");
                continue;
            }
            using var doc = Parse(bytes, label, v);
            if (doc is null || v.Manifest(doc.RootElement, label) is not { } manifest)
                continue;
            if (string.IsNullOrWhiteSpace(manifest.Id))
            {
                v.Error(label, "/id", "The id is empty.");
                continue;
            }
            var pack = new Pack(dir, manifest, bytes);
            if (manifest.RulesVersion is < 1 or > RulesVersion)
                v.Error(pack.ManifestFile, "/rulesVersion",
                    $"Rules version {manifest.RulesVersion} isn't supported: this engine reads rules versions 1 to {RulesVersion}.");
            if (packs.Any(p => p.Id == manifest.Id))
            {
                v.Error(pack.ManifestFile, "/id", $"Two packs have the id '{manifest.Id}'.");
                continue;
            }
            packs.Add(pack);
        }
        return packs;
    }

    /// <summary>
    /// The packs in load order: <c>"base"</c> first, then each pack once every pack it requires is placed, ties by ordinal
    /// id. A missing requirement or a cycle is an error; the packs involved still load, so their own errors are found too.
    /// </summary>
    private static List<Pack> Order(List<Pack> packs, Validation v)
    {
        var ids = packs.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var pack in packs)
            for (var i = 0; i < pack.Manifest.Requires.Length; i++)
                if (!ids.Contains(pack.Manifest.Requires[i]))
                    v.Error(pack.ManifestFile, Pointer.Append("/requires", i),
                        $"Pack '{pack.Id}' requires '{pack.Manifest.Requires[i]}', which isn't loaded.");

        var remaining = packs.OrderBy(p => p.Id != "base").ThenBy(p => p.Id, StringComparer.Ordinal).ToList();
        var placed = new List<Pack>();
        var done = new HashSet<string>(StringComparer.Ordinal);
        while (remaining.Count > 0)
        {
            var next = remaining.FirstOrDefault(p => p.Manifest.Requires.All(r => done.Contains(r) || !ids.Contains(r)));
            if (next is null)
            {
                foreach (var pack in remaining)
                    v.Error(pack.ManifestFile, "/requires", $"Pack '{pack.Id}' is part of a requires cycle.");
                placed.AddRange(remaining);
                break;
            }
            placed.Add(next);
            done.Add(next.Id);
            remaining.Remove(next);
        }
        return placed;
    }

    private static bool IsRelativePath(string file)
        => file.Length > 0 && !file.Contains('\\') && !Path.IsPathRooted(file)
           && file.Split('/').All(part => part.Length > 0 && part != "." && part != "..");

    private static FileKind? KindOf(string file) => file.Split('/') switch
    {
        ["conditions.json"] => FileKind.Conditions,
        ["races.json"] => FileKind.Races,
        ["backgrounds.json"] => FileKind.Backgrounds,
        ["injuries.json"] => FileKind.Injuries,
        ["xp.json"] => FileKind.Xp,
        ["ai-profiles.json"] => FileKind.AiProfiles,
        ["items.json"] => FileKind.Items,
        ["abilities", var name] when name.EndsWith(".json", StringComparison.Ordinal) => FileKind.Abilities,
        ["classes", var name] when name.EndsWith(".json", StringComparison.Ordinal) => FileKind.Class,
        ["monsters", var name] when name.EndsWith(".json", StringComparison.Ordinal) => FileKind.Monsters,
        ["maps", var name] when name.EndsWith(".json", StringComparison.Ordinal) => FileKind.Map,
        ["encounters", var name] when name.EndsWith(".json", StringComparison.Ordinal) => FileKind.Encounter,
        _ => null,
    };

    // ---- Files ------------------------------------------------------------------------------------------------------

    private static JsonDocument? Parse(byte[] bytes, string file, Validation v)
    {
        ReadOnlyMemory<byte> json = bytes;
        if (json.Span.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
            json = json[3..];
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            v.Error(file, "", ex.Message);
            return null;
        }
    }

    private static void Read(FileKind kind, JsonElement root, string file, Content c, Validation v)
    {
        switch (kind)
        {
            case FileKind.Conditions:
                List(root, file, "conditions", v, (e, p) => Add(c.Conditions, v.Condition(e, file, p), d => d.Id.Value,
                    d => d.Replaces?.Value, file, p, v, e, RefKind.Condition));
                break;
            case FileKind.Races:
                List(root, file, "races", v, (e, p) => Add(c.Races, v.Race(e, file, p), d => d.Id, d => d.Replaces, file, p, v));
                break;
            case FileKind.Backgrounds:
                List(root, file, "backgrounds", v,
                    (e, p) => Add(c.Backgrounds, v.Background(e, file, p), d => d.Id, d => d.Replaces, file, p, v));
                break;
            case FileKind.Injuries:
                List(root, file, "injuries", v,
                    (e, p) => Add(c.Injuries, v.Injury(e, file, p), d => d.Id.Value, d => d.Replaces?.Value, file, p, v));
                break;
            case FileKind.AiProfiles:
                List(root, file, "AI profiles", v, (e, p) => Add(c.AiProfiles, v.AiProfile(e, file, p), d => d.Id, d => d.Replaces,
                    file, p, v, e, RefKind.AiProfile));
                break;
            case FileKind.Items:
                List(root, file, "items", v, (e, p) => Add(c.Items, v.Item(e, file, p), d => d.Id.Value, d => d.Replaces?.Value,
                    file, p, v, e, RefKind.Item));
                break;
            case FileKind.Monsters:
                List(root, file, "monsters", v, (e, p) => Add(c.Monsters, v.Monster(e, file, p), d => d.Id, d => d.Replaces,
                    file, p, v, e, RefKind.Monster));
                break;
            case FileKind.Xp:
                Add(c.Xp, v.Xp(root, file, ""), _ => XpTable.Id, d => d.Replaces, file, "", v, idPointer: false);
                break;
            case FileKind.Class:
                Add(c.Classes, v.Class(root, file, ""), d => d.Id, d => d.Replaces, file, "", v);
                break;
            case FileKind.Map:
                Add(c.Maps, v.Map(root, file, ""), m => m.Def.Id, m => m.Def.Replaces, file, "", v, root, RefKind.Map);
                break;
            case FileKind.Encounter:
                Add(c.Encounters, v.Encounter(root, file, ""), d => d.Id, d => d.Replaces, file, "", v);
                break;
            case FileKind.Abilities:
                if (root.ValueKind != JsonValueKind.Object)
                {
                    v.Error(file, "", "An abilities file is an object with \"abilities\" and \"passives\" lists.");
                    break;
                }
                foreach (var part in root.EnumerateObject())
                {
                    var at = Pointer.Append("", part.Name);
                    if (part.Name == "abilities")
                        List(part.Value, file, "abilities", v, (e, p) => Add(c.Abilities, v.Ability(e, file, at + p),
                            d => d.Id.Value, d => d.Replaces?.Value, file, at + p, v, e, RefKind.Ability));
                    else if (part.Name == "passives")
                        List(part.Value, file, "passives", v, (e, p) => Add(c.Passives, v.Passive(e, file, at + p),
                            d => d.Id, d => d.Replaces, file, at + p, v, e, RefKind.Passive));
                    else
                        v.Error(file, at, $"Unknown key '{part.Name}' (expected \"abilities\" and \"passives\").");
                }
                break;
        }
    }

    /// <summary>Runs <paramref name="item"/> on each element of the list <paramref name="root"/>, with its pointer.</summary>
    private static void List(JsonElement root, string file, string what, Validation v, Action<JsonElement, string> item)
    {
        if (root.ValueKind != JsonValueKind.Array)
        {
            v.Error(file, "", $"Expected a list of {what}.");
            return;
        }
        var i = 0;
        foreach (var element in root.EnumerateArray())
            item(element, Pointer.Append("", i++));
    }

    /// <summary>
    /// Adds a loaded definition, or notes a failed one as broken. An empty id is an error, and so is a <c>"replaces"</c>
    /// that names any id but the definition's own.
    /// </summary>
    private static void Add<T>(List<Loaded<T>> list, T? def, Func<T, string?> id, Func<T, string?> replaces, string file,
        string ptr, Validation v, JsonElement? element = null, RefKind? kind = null, bool idPointer = true)
    {
        if (def is null)
        {
            if (element is { } e && kind is { } k)
                v.Broken(k, e);
            return;
        }
        var key = id(def);
        if (string.IsNullOrWhiteSpace(key))
        {
            v.Error(file, idPointer ? Pointer.Append(ptr, "id") : ptr, "The id is empty.");
            return;
        }
        if (replaces(def) is { } replaced && replaced != key)
        {
            v.Error(file, Pointer.Append(ptr, "replaces"), $"'replaces' must be this definition's own id '{key}'.");
            return;
        }
        list.Add(new Loaded<T>(def, key, replaces(def), file, ptr));
    }

    /// <summary>
    /// Merges one kind's definitions in load order. A repeated id needs <c>"replaces"</c>, and then the later definition
    /// wins; without it the error names both files (Review Focus 5). A <c>"replaces"</c> with nothing earlier to replace
    /// is an error too.
    /// </summary>
    private static SortedDictionary<string, Loaded<T>> Merge<T>(List<Loaded<T>> loaded, string kind, Validation v)
    {
        var merged = new SortedDictionary<string, Loaded<T>>(StringComparer.Ordinal);
        foreach (var entry in loaded)
        {
            if (merged.TryGetValue(entry.Id, out var first))
            {
                if (entry.Replaces is null)
                    v.Error(entry.File, Pointer.Append(entry.Pointer, "id"),
                        $"Duplicate {kind} '{entry.Id}': it is already defined at {first.File}#{first.Pointer}. "
                        + $"A later pack can change it only with \"replaces\": \"{entry.Id}\".");
                else
                    merged[entry.Id] = entry;
            }
            else
            {
                if (entry.Replaces is not null)
                    v.Error(entry.File, Pointer.Append(entry.Pointer, "replaces"),
                        $"'replaces' names '{entry.Replaces}', but no earlier pack defines it.");
                merged[entry.Id] = entry;
            }
        }
        return merged;
    }

    private static ImmutableSortedDictionary<TKey, TValue> Index<T, TKey, TValue>(IEnumerable<Loaded<T>> entries,
        Func<Loaded<T>, TKey> key, Func<Loaded<T>, TValue> value, IComparer<TKey> comparer) where TKey : notnull
    {
        var builder = ImmutableSortedDictionary.CreateBuilder<TKey, TValue>(comparer);
        foreach (var entry in entries)
            builder.Add(key(entry), value(entry));
        return builder.ToImmutable();
    }
}
