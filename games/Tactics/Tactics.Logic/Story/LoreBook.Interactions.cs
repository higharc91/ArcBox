using ArcBox.Sdk.Content;
using System.Collections.Immutable;
using System.Text.Json;

namespace Tactics.Logic;

public sealed partial class LoreBook
{
    public ImmutableArray<ConversationDef> Conversations { get; private init; } = [];
    public ImmutableArray<QuestDef> Quests { get; private init; } = [];
    public ConversationDef? Conversation(string character) => Conversations.FirstOrDefault(c => c.Character == character);

    private static ConversationDef ReadConversation(JsonElement root, ImmutableDictionary<string, string> beats)
    {
        var scene = ReadScene(root, beats);
        var where = $"conversation '{scene.Id}'";
        if (scene.Beat is not null || scene.Camp || scene.After) throw new BadLore($"{where} must be a standalone scene.");
        var entries = List(root, "entries", where, required: true).Select(e => new ConversationEntry(
            Required(e, "node", where), Strings(e, "requires", where), Strings(e, "unless", where))).ToImmutableArray();
        if (entries.IsEmpty) throw new BadLore($"{where} has no entry points.");
        foreach (var entry in entries)
            if (scene.Node(entry.Node) is null) throw new BadLore($"{where} entry '{entry.Node}' is not a node.");
        foreach (var node in List(root, "nodes", where, required: true))
            foreach (var line in List(node, "lines", where, required: true))
            {
                _ = Required(line, "mood", where);
                _ = Required(line, "direction", where);
            }
        return new ConversationDef(Required(root, "character", where), scene, entries);
    }

    private static QuestDef ReadQuest(JsonElement root)
    {
        var id = Required(root, "id", "a quest");
        var where = $"quest '{id}'";
        QuestEnding Ending(JsonElement e) => new(Strings(e, "when", where), Required(e, "log", where));
        var stages = List(root, "stages", where, required: true).Select(e =>
        {
            QuestTarget? target = null;
            if (Field(e, "target", where) is { ValueKind: JsonValueKind.Object } t)
            {
                Tile? tile = null;
                if (Field(t, "tile", where) is { ValueKind: JsonValueKind.Array } xy)
                {
                    var coords = xy.EnumerateArray().ToArray();
                    if (coords.Length != 2 || !coords[0].TryGetInt32(out var x) || !coords[1].TryGetInt32(out var y))
                        throw new BadLore($"{where} needs a two-integer tile target.");
                    tile = new Tile(x, y);
                }
                target = new QuestTarget(Optional(t, "person", where), Optional(t, "thing", where), tile, Optional(t, "region", where));
                if ((target.Person is null ? 0 : 1) + (target.Thing is null ? 0 : 1) + (tile is null ? 0 : 1) != 1)
                    throw new BadLore($"{where} target must name exactly one person, thing or tile.");
            }
            var stage = new QuestStage(Required(e, "id", where), Required(e, "objective", where), Required(e, "log", where), Strings(e, "when", where), target);
            if (stage.Objective.Length > 40 || stage.When.IsEmpty) throw new BadLore($"{where} stage '{stage.Id}' needs flags and an objective of at most 40 characters.");
            return stage;
        }).ToImmutableArray();
        if (stages.IsEmpty || stages.Select(s => s.Id).Distinct().Count() != stages.Length) throw new BadLore($"{where} needs distinct stages.");
        var rewards = Field(root, "rewards", where);
        var items = List(rewards, "items", where, required: false).Select(e => new QuestItem(Required(e, "item", where), Whole(e, "count", where))).ToImmutableArray();
        ImmutableSortedDictionary<string, string>? trinkets = null;
        if (Field(rewards, "trinkets", where) is { ValueKind: not JsonValueKind.Undefined } rewardMap)
        {
            if (rewardMap.ValueKind != JsonValueKind.Object) throw new BadLore($"{where} trinket rewards must map classes to trinket IDs.");
            var builder = ImmutableSortedDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
            foreach (var entry in rewardMap.EnumerateObject())
            {
                if (string.IsNullOrWhiteSpace(entry.Name) || entry.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(entry.Value.GetString()))
                    throw new BadLore($"{where} has an empty or invalid class trinket reward.");
                if (!builder.TryAdd(entry.Name, entry.Value.GetString()!)) throw new BadLore($"{where} repeats trinket class '{entry.Name}'.");
            }
            if (builder.Count == 0) throw new BadLore($"{where} has an empty trinket reward map.");
            trinkets = builder.ToImmutable();
        }
        int Amount(string key) => Field(rewards, key, where).ValueKind == JsonValueKind.Undefined ? 0 : Whole(rewards, key, where);
        var result = new QuestDef(id, Required(root, "name", where), Required(root, "giver", where), Required(root, "region", where), stages,
            Ending(Field(root, "done", where)), Field(root, "failed", where) is { ValueKind: JsonValueKind.Object } failed ? Ending(failed) : null,
            new QuestRewards(Amount("gold"), Amount("xp"), items, Strings(rewards, "sets", where), trinkets));
        if (result.Done.When.IsEmpty || result.Rewards.Gold < 0 || result.Rewards.Xp < 0 || items.Any(i => i.Count <= 0))
            throw new BadLore($"{where} has invalid rewards or no completing flags.");
        return result;
    }

    private static SceneQuest? QuestAction(JsonElement e, string where)
    {
        var action = Field(e, "quest", where);
        if (action.ValueKind == JsonValueKind.Undefined) return null;
        var result = new SceneQuest(Optional(action, "start", where), Optional(action, "advance", where), Optional(action, "complete", where), Optional(action, "stage", where));
        if ((result.Start is null ? 0 : 1) + (result.Advance is null ? 0 : 1) + (result.Complete is null ? 0 : 1) != 1)
            throw new BadLore($"{where} quest action must name exactly one of start, advance or complete.");
        return result;
    }
    private static void ValidateSideContent(string basePack, ImmutableArray<QuestDef> quests,
        IEnumerable<SceneDef> scenes, IEnumerable<ConversationDef> conversations, ImmutableArray<LoreCharacter> characters,
        IReadOnlySet<string> mainFlags, BarkBook barks, IEnumerable<string> beatFlags, IFileSource source)
    {
        if (quests.IsEmpty && !conversations.Any() && !scenes.Any(s => s.Beat is null)) return;
        var allScenes = scenes.ToArray();
        var setters = allScenes.SelectMany(s => s.Nodes).SelectMany(n => n.Sets.Concat(n.Options.SelectMany(o => o.Sets))).ToHashSet(StringComparer.Ordinal);
        setters.UnionWith(beatFlags);
        var regions = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var things = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var items = new HashSet<string>(StringComparer.Ordinal);
        var classes = new HashSet<string>(StringComparer.Ordinal);
        var trinkets = new Dictionary<string, string>(StringComparer.Ordinal);
        var shops = new HashSet<string>(StringComparer.Ordinal);
        var hooks = new List<(string Thing,string Scene)>();
        var rules = Path.Combine(basePack, "rules");
        var manifest = Path.Combine(rules, "manifest.json");
        if (source.Exists(manifest))
        {
            using var index = JsonDocument.Parse(source.ReadAllText(manifest));
            foreach (var file in index.RootElement.GetProperty("files").EnumerateArray().Select(f => f.GetString()!))
            {
                using var doc = JsonDocument.Parse(source.ReadAllText(Path.Combine(rules, file)));
                var root = doc.RootElement;
                Gather(root);
                if (file.StartsWith("interactables/", StringComparison.Ordinal))
                    foreach (var thing in root.GetProperty("interactables").EnumerateArray())
                        if (thing.TryGetProperty("scene", out var hook)) hooks.Add((thing.GetProperty("id").GetString()!,hook.GetString()!));
                if (file.StartsWith("world/", StringComparison.Ordinal)) regions.Add(root.GetProperty("id").GetString()!, root.Clone());
                if (file.StartsWith("interactables/", StringComparison.Ordinal)) things.Add(root.GetProperty("region").GetString()!,
                    root.GetProperty("interactables").EnumerateArray().Select(t => t.GetProperty("id").GetString()!).ToHashSet(StringComparer.Ordinal));
                if (file == "items.json") items.UnionWith(root.EnumerateArray().Select(t => t.GetProperty("id").GetString()!));
                if (file.StartsWith("classes/", StringComparison.Ordinal)) classes.Add(root.GetProperty("id").GetString()!);
                if (file == "trinkets.json")
                    foreach (var trinket in root.EnumerateArray()) trinkets.Add(trinket.GetProperty("id").GetString()!, trinket.GetProperty("class").GetString()!);
                if (file.StartsWith("shops/", StringComparison.Ordinal)) shops.UnionWith(root.GetProperty("shops").EnumerateArray().Select(t => t.GetProperty("id").GetString()!));
            }
        }
        foreach (var (thing, scene) in hooks)
            if (!allScenes.Any(s => s.Id == scene && s.Beat is null))
                throw new InvalidDataException($"thing '{thing}' names unknown standalone scene '{scene}'.");
        foreach (var region in regions.Values)
            if (region.TryGetProperty("people", out var people))
                foreach (var person in people.EnumerateArray())
                {
                    if (person.TryGetProperty("character", out var who))
                    {
                        var character = who.GetString()!;
                        if (!characters.Any(c => c.Id == character)) throw new InvalidDataException($"unknown placed character '{character}'.");
                        if (!conversations.Any(c => c.Character == character) && !barks.Has(character))
                            throw new InvalidDataException($"placed character '{character}' has neither conversation nor barks.");
                    }
                    if (person.TryGetProperty("bark", out var bark) && !barks.Has(bark.GetString()!))
                        throw new InvalidDataException($"unknown bark set '{bark.GetString()}'.");
                }
        void Gather(JsonElement node)
        {
            if (node.ValueKind == JsonValueKind.Object)
                foreach (var p in node.EnumerateObject())
                {
                    if (p.Name == "sets" && p.Value.ValueKind == JsonValueKind.Array) setters.UnionWith(p.Value.EnumerateArray().Select(v => v.GetString()!));
                    if (p.Name == "setFlag" && p.Value.ValueKind == JsonValueKind.String) setters.Add(p.Value.GetString()!);
                    Gather(p.Value);
                }
            else if (node.ValueKind == JsonValueKind.Array) foreach (var child in node.EnumerateArray()) Gather(child);
        }
        foreach (var option in allScenes.SelectMany(s => s.Nodes).SelectMany(n => n.Options))
        {
            if (source.Exists(manifest) && option.Give is { } give && !items.Contains(give)) throw new InvalidDataException($"unknown reward item '{give}'.");
            if (source.Exists(manifest) && option.Service is { } service && !shops.Contains(service)) throw new InvalidDataException($"unknown service '{service}'.");
            if (option.Quest is { } action)
            {
                var quest = quests.First(q => q.Id == (action.Start ?? action.Advance ?? action.Complete));
                var assigned = quest.Flags(action);
                if (assigned.Any(mainFlags.Contains)) throw new InvalidDataException($"a side quest action cannot set main-story flags of '{quest.Id}'.");
                setters.UnionWith(assigned);
            }
        }
        foreach (var quest in quests)
        {
            if (!characters.Any(c => c.Id == quest.Giver)) throw new InvalidDataException($"quest '{quest.Id}' has unknown giver '{quest.Giver}'.");
            if (source.Exists(manifest) && !regions.ContainsKey(quest.Region)) throw new InvalidDataException($"quest '{quest.Id}' has unknown region '{quest.Region}'.");
            foreach (var stage in quest.Stages)
            {
                var targetRegion = stage.Target?.Region ?? quest.Region;
                if (source.Exists(manifest) && !regions.ContainsKey(targetRegion))
                    throw new InvalidDataException($"quest '{quest.Id}' stage '{stage.Id}' has unknown target region '{targetRegion}'.");
                var region = regions.GetValueOrDefault(targetRegion);
                if (stage.When.IsDefaultOrEmpty || source.Exists(manifest) && !stage.When.All(setters.Contains)) throw new InvalidDataException($"quest '{quest.Id}' stage '{stage.Id}' has flags nothing in the pack can set.");
                if (source.Exists(manifest) && stage.Target?.Thing is { } thing && (!things.TryGetValue(targetRegion, out var regionThings) || !regionThings.Contains(thing)))
                    throw new InvalidDataException($"quest '{quest.Id}' stage '{stage.Id}' has unknown thing '{thing}'.");
                if (source.Exists(manifest) && stage.Target?.Tile is { } tile)
                {
                    var size = region.GetProperty("size").EnumerateArray().Select(v => v.GetInt32()).ToArray();
                    if (tile.X < 0 || tile.Y < 0 || tile.X >= size[0] || tile.Y >= size[1])
                        throw new InvalidDataException($"quest '{quest.Id}' stage '{stage.Id}' target is outside region '{targetRegion}'.");
                }
                if (source.Exists(manifest) && stage.Target?.Person is { } person && (!region.TryGetProperty("people", out var people)
                    || !people.EnumerateArray().Any(p => p.TryGetProperty("id", out var id) && id.GetString() == person)))
                    throw new InvalidDataException($"quest '{quest.Id}' stage '{stage.Id}' has unknown person '{person}'.");
            }
            if (!quest.Rewards.Sets.IsDefault && quest.Rewards.Sets.Any(mainFlags.Contains))
                throw new InvalidDataException($"quest '{quest.Id}' cannot set main-story flags.");
            foreach (var item in quest.Rewards.Items)
                if (source.Exists(manifest) && !items.Contains(item.Item)) throw new InvalidDataException($"quest '{quest.Id}' has unknown item '{item.Item}'.");
            if (source.Exists(manifest) && quest.Rewards.Trinkets is { } rewardMap)
            {
                foreach (var (cls, id) in rewardMap)
                    if (!classes.Contains(cls) || !trinkets.TryGetValue(id, out var owner) || owner != cls)
                        throw new InvalidDataException($"quest '{quest.Id}' has invalid trinket '{id}' for class '{cls}'.");
                foreach (var cls in classes)
                    if (!rewardMap.ContainsKey(cls)) throw new InvalidDataException($"quest '{quest.Id}' has no trinket reward for class '{cls}'.");
            }
        }
    }
}
