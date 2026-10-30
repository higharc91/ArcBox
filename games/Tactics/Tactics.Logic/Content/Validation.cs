using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArcBox.Sdk;

namespace Tactics.Logic;

/// <summary>What a content reference points at; see <see cref="Validation.Resolve"/>.</summary>
internal enum RefKind { Ability, Passive, AbilityOrPassive, Condition, Monster, Item, Map, AiProfile, Art }

/// <summary>A reference from one content file to an id, checked once every pack is loaded.</summary>
internal sealed record ContentRef(RefKind Kind, string Id, string File, string Pointer);

/// <summary>A map as written and the engine's map built from it.</summary>
internal sealed record MapContent(MapDef Def, BattleMap Map);

/// <summary>
/// The checks behind <see cref="RulesPack.Load"/> (spec §4.10), for one load. <see cref="PackLoader"/> hands each element of
/// each file to the method for its kind, which deserialises it with <see cref="ArcJson.Options"/> (effects through
/// <see cref="EffectJsonConverter"/>) and checks what can be checked within the element; references to other ids are kept
/// and checked by <see cref="Resolve"/> when everything is loaded, and encounters by <see cref="CheckEncounters"/>. Every
/// problem is added to <see cref="Errors"/> with its file and JSON pointer; nothing throws.
/// </summary>
internal sealed class Validation(ArtManifest? art)
{
    /// <summary>The largest ability or effect range, in tiles.</summary>
    public const int MaxRange = 12;

    /// <summary>The largest area size.</summary>
    public const int MaxAreaSize = 6;

    private static readonly ImmutableSortedSet<string> HookNames = Ordinal.Set(
        typeof(HookKind).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => f.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()!.Name));

    private static readonly int[] SubclassLevels = [3, 6, 10];

    private readonly List<ContentRef> _refs = [];
    private readonly HashSet<(RefKind, string)> _broken = [];

    /// <summary>Every error found so far, in the order found.</summary>
    public List<ContentError> Errors { get; } = [];

    /// <summary>Records an error.</summary>
    public void Error(string file, string pointer, string message) => Errors.Add(new ContentError(file, pointer, message));

    /// <summary>
    /// Notes that the element of this kind in <paramref name="e"/> failed to load, so references to its id are not reported
    /// as unknown on top of its own errors.
    /// </summary>
    public void Broken(RefKind kind, JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
            _broken.Add((kind, id.GetString()!));
    }

    // ---- Elements -------------------------------------------------------------------------------------------------

    /// <summary>An ability: its effects, then the whole; range, area and presentation keys.</summary>
    public AbilityDef? Ability(JsonElement e, string file, string ptr)
        => Guarded(() =>
        {
            if (!EffectsIn(e, "effects", file, ptr, inSave: false))
                return null;
            var def = Deserialize<AbilityDef>(e, file, ptr);
            if (def is null)
                return null;
            var target = Pointer.Append(ptr, "target");
            if (def.Target.Range > MaxRange)
                Error(file, Pointer.Append(target, "range"), $"Range {def.Target.Range} is over the limit of {MaxRange}.");
            if (def.Target.Area.Size > MaxAreaSize)
                Error(file, Pointer.Append(Pointer.Append(target, "area"), "size"),
                    $"Area size {def.Target.Area.Size} is over the limit of {MaxAreaSize}.");
            if (def.Presentation is { } p)
            {
                var at = Pointer.Append(ptr, "presentation");
                Ref(RefKind.Art, p.Anim, file, Pointer.Append(at, "anim"));
                Ref(RefKind.Art, p.Fx, file, Pointer.Append(at, "fx"));
                Ref(RefKind.Art, p.Projectile, file, Pointer.Append(at, "projectile"));
            }
            return def;
        });

    /// <summary>A passive: its hook name, its effects, then the whole; the conditions its <c>when</c> names.</summary>
    public PassiveDef? Passive(JsonElement e, string file, string ptr)
        => Guarded(() =>
        {
            var ok = true;
            if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("hook", out var hook)
                && hook.ValueKind == JsonValueKind.String && !HookNames.Contains(hook.GetString()!))
            {
                Error(file, Pointer.Append(ptr, "hook"), $"Unknown hook '{hook.GetString()}'.");
                ok = false;
            }
            ok &= EffectsIn(e, "effects", file, ptr, inSave: false);
            if (!ok)
                return null;
            var def = Deserialize<PassiveDef>(e, file, ptr);
            if (def?.When is { } when)
                When(when, file, Pointer.Append(ptr, "when"));
            return def;
        });

    /// <summary>A condition: its passives, then the whole; the condition it implies.</summary>
    public ConditionDef? Condition(JsonElement e, string file, string ptr)
        => Guarded(() =>
        {
            var ok = true;
            if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("passives", out var passives)
                && passives.ValueKind == JsonValueKind.Array)
            {
                var i = 0;
                foreach (var passive in passives.EnumerateArray())
                    ok &= Passive(passive, file, Pointer.Append(Pointer.Append(ptr, "passives"), i++)) is not null;
            }
            if (!ok)
                return null;
            var def = Deserialize<ConditionDef>(e, file, ptr);
            if (def?.Implies is { } implies)
                Ref(RefKind.Condition, implies.Value, file, Pointer.Append(ptr, "implies"));
            return def;
        });

    /// <summary>
    /// A class: hit die, resource tables, a feature at every level 1–10, subclasses with features at exactly 3, 6 and 10
    /// (unless <see cref="ClassDef.SubclassesPending"/>), the stat priority, and every id its features and kit name.
    /// </summary>
    public ClassDef? Class(JsonElement e, string file, string ptr)
        => Guarded(() =>
        {
            var def = Deserialize<ClassDef>(e, file, ptr);
            if (def is null)
                return null;
            if (def.HitDie is not (6 or 8 or 10 or 12))
                Error(file, Pointer.Append(ptr, "hitDie"), $"Hit die {def.HitDie} isn't one of 6, 8, 10 or 12.");
            foreach (var (resource, table) in def.Resources)
                Levels(table, file, Pointer.Append(Pointer.Append(ptr, "resources"), resource));

            var features = Pointer.Append(ptr, "features");
            for (var level = 1; level <= 10; level++)
                if (!def.Features.TryGetValue(level, out var list) || list.IsEmpty)
                    Error(file, features, $"Class '{def.Id}' has no features at level {level}.");
            FeatureLists(def.Features, file, features, level => level is >= 1 and <= 10, "Level {0} is outside levels 1–10.");

            var subclasses = Pointer.Append(ptr, "subclasses");
            if (def.Subclasses.IsEmpty && !def.SubclassesPending)
                Error(file, subclasses, $"Class '{def.Id}' has no subclasses.");
            for (var i = 0; i < def.Subclasses.Length; i++)
            {
                var sub = def.Subclasses[i];
                var at = Pointer.Append(subclasses, i);
                if (string.IsNullOrWhiteSpace(sub.Id))
                    Error(file, Pointer.Append(at, "id"), "The id is empty.");
                var subFeatures = Pointer.Append(at, "features");
                if (!def.SubclassesPending)
                {
                    foreach (var level in SubclassLevels)
                        if (!sub.Features.TryGetValue(level, out var list) || list.IsEmpty)
                            Error(file, subFeatures, $"Subclass '{sub.Id}' has no features at level {level}.");
                }
                FeatureLists(sub.Features, file, subFeatures, level => SubclassLevels.Contains(level),
                    "Subclass features come only at levels 3, 6 and 10, not {0}.");
                if (sub.Companion is { } companion)
                    Ref(RefKind.Monster, companion, file, Pointer.Append(at, "companion"));
            }

            foreach (var (level, picks) in def.DefaultPicks)
                for (var i = 0; i < picks.Length; i++)
                    Ref(RefKind.Ability, picks[i].Value, file,
                        Pointer.Append(Pointer.Append(Pointer.Append(ptr, "defaultPicks"), level), i));
            for (var i = 0; i < def.StartingKit.Bag.Length; i++)
                Ref(RefKind.Item, def.StartingKit.Bag[i].Item.Value, file,
                    Pointer.Append(Pointer.Append(Pointer.Append(Pointer.Append(ptr, "startingKit"), "bag"), i), "item"));
            if (def.AiTags.Priority.Length != 6 || def.AiTags.Priority.Distinct().Count() != 6)
                Error(file, Pointer.Append(Pointer.Append(ptr, "aiTags"), "priority"), "The priority must list each of the six stats once.");
            return def;
        });

    /// <summary>A race: its racial ability and trait passives.</summary>
    public RaceDef? Race(JsonElement e, string file, string ptr)
        => Guarded(() =>
        {
            var def = Deserialize<RaceDef>(e, file, ptr);
            if (def is null)
                return null;
            if (def.Ability is { } ability)
                Ref(RefKind.Ability, ability.Value, file, Pointer.Append(ptr, "ability"));
            Refs(RefKind.Passive, def.Passives, file, Pointer.Append(ptr, "passives"));
            return def;
        });

    /// <summary>A background.</summary>
    public BackgroundDef? Background(JsonElement e, string file, string ptr) => Guarded(() => Deserialize<BackgroundDef>(e, file, ptr));

    /// <summary>A monster: its abilities, passives, AI profile, sprite and phases.</summary>
    public MonsterDef? Monster(JsonElement e, string file, string ptr)
        => Guarded(() =>
        {
            var def = Deserialize<MonsterDef>(e, file, ptr);
            if (def is null)
                return null;
            Refs(RefKind.Ability, def.Abilities.Select(a => a.Value), file, Pointer.Append(ptr, "abilities"));
            Refs(RefKind.Passive, def.Passives, file, Pointer.Append(ptr, "passives"));
            Ref(RefKind.AiProfile, def.AiProfile, file, Pointer.Append(ptr, "aiProfile"));
            Ref(RefKind.Art, def.Sprite, file, Pointer.Append(ptr, "sprite"));
            for (var i = 0; i < def.Phases.Length; i++)
            {
                var at = Pointer.Append(Pointer.Append(ptr, "phases"), i);
                Refs(RefKind.Ability, def.Phases[i].Abilities.Select(a => a.Value), file, Pointer.Append(at, "abilities"));
                Refs(RefKind.Passive, def.Phases[i].Passives, file, Pointer.Append(at, "passives"));
                Ref(RefKind.AiProfile, def.Phases[i].AiProfile, file, Pointer.Append(at, "aiProfile"));
            }
            return def;
        });

    /// <summary>An item: the ability it uses.</summary>
    public ItemDef? Item(JsonElement e, string file, string ptr)
        => Guarded(() =>
        {
            var def = Deserialize<ItemDef>(e, file, ptr);
            if (def is not null)
                Ref(RefKind.Ability, def.Use.Value, file, Pointer.Append(ptr, "use"));
            return def;
        });

    /// <summary>An injury: a weight of at least 1.</summary>
    public InjuryDef? Injury(JsonElement e, string file, string ptr)
        => Guarded(() =>
        {
            var def = Deserialize<InjuryDef>(e, file, ptr);
            if (def is { Weight: < 1 })
                Error(file, Pointer.Append(ptr, "weight"), "The weight must be at least 1.");
            return def;
        });

    /// <summary>An AI profile.</summary>
    public AiProfileDef? AiProfile(JsonElement e, string file, string ptr) => Guarded(() => Deserialize<AiProfileDef>(e, file, ptr));

    /// <summary>The XP table: thresholds that start at 0 and rise.</summary>
    public XpTable? Xp(JsonElement e, string file, string ptr)
        => Guarded(() =>
        {
            var def = Deserialize<XpTable>(e, file, ptr);
            var t = def?.Thresholds ?? [];
            if (def is not null && (t.IsEmpty || t[0] != 0 || t.Zip(t.Skip(1)).Any(p => p.Second <= p.First)))
                Error(file, Pointer.Append(ptr, "thresholds"), "The thresholds must start at 0 and rise.");
            return def;
        });

    /// <summary>
    /// A map: its size, tile bytes (<c>w × h × 3</c> of them, material indexes in range, only defined flag bits), props and
    /// zones inside the map, and prop cover none, half or three-quarters. Returns the engine's map, or null on any error.
    /// </summary>
    public MapContent? Map(JsonElement e, string file, string ptr)
        => Guarded(() =>
        {
            var def = Deserialize<MapDef>(e, file, ptr);
            if (def is null)
                return null;
            if (def.Size is not [var w and > 0, var h and > 0])
            {
                Error(file, Pointer.Append(ptr, "size"), "The size must be [w, h], both at least 1.");
                return null;
            }
            var tilesAt = Pointer.Append(ptr, "tiles");
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(def.Tiles);
            }
            catch (FormatException)
            {
                Error(file, tilesAt, "The tiles aren't valid base64.");
                return null;
            }
            if (bytes.Length != w * h * 3)
            {
                Error(file, tilesAt, $"Expected {w * h * 3} tile bytes for a {w}x{h} map (3 per tile), got {bytes.Length}.");
                return null;
            }

            var errors = Errors.Count;
            const int knownFlags = (int)(TileFlags.Water | TileFlags.Difficult | TileFlags.Impassable | TileFlags.Hazard | TileFlags.Deep);
            // The first bad tile of each kind is enough to find the problem; one per tile could be thousands of errors.
            for (var i = 0; i < w * h; i++)
                if (bytes[i * 3 + 1] >= def.Materials.Length)
                {
                    Error(file, tilesAt,
                        $"Tile {new Tile(i % w, i / w)} uses material {bytes[i * 3 + 1]}, but the map has {def.Materials.Length} materials.");
                    break;
                }
            for (var i = 0; i < w * h; i++)
                if ((bytes[i * 3 + 2] & ~knownFlags) != 0)
                {
                    Error(file, tilesAt, $"Tile {new Tile(i % w, i / w)} has undefined flag bits 0x{bytes[i * 3 + 2] & ~knownFlags:x2}.");
                    break;
                }

            bool InBounds(Tile t) => t.X >= 0 && t.Y >= 0 && t.X < w && t.Y < h;
            for (var i = 0; i < def.Props.Length; i++)
            {
                var prop = def.Props[i];
                var at = Pointer.Append(Pointer.Append(ptr, "props"), i);
                if (!InBounds(prop.At))
                    Error(file, Pointer.Append(at, "at"), $"Prop '{prop.Id}' at {prop.At} is outside the {w}x{h} map.");
                if (!MapDef.CoverNames.ContainsKey(prop.Cover))
                    Error(file, Pointer.Append(at, "cover"),
                        $"Cover '{prop.Cover}' isn't allowed in content (use \"none\", \"half\" or \"three_quarters\").");
            }
            foreach (var (zone, zoneTiles) in def.Zones)
                for (var i = 0; i < zoneTiles.Length; i++)
                    if (!InBounds(zoneTiles[i]))
                        Error(file, Pointer.Append(Pointer.Append(Pointer.Append(ptr, "zones"), zone), i),
                            $"Tile {zoneTiles[i]} is outside the {w}x{h} map.");

            return Errors.Count == errors ? new MapContent(def, def.ToBattleMap()) : null;
        });

    /// <summary>
    /// An encounter: party size, how each unit is placed and the monsters, AI profiles and map it names. Its zones and unit
    /// tags are checked against its map by <see cref="CheckEncounters"/>. Triggers without <c>"once"</c> get
    /// <c>once: true</c>, and objectives without <c>"lose"</c> get <c>party_out</c>.
    /// </summary>
    public EncounterDef? Encounter(JsonElement e, string file, string ptr)
        => Guarded(() =>
        {
            var def = Deserialize<EncounterDef>(e, file, ptr, raw =>
            {
                var objectives = raw.Objectives is null ? ObjectivesDef.Default
                    : raw.Objectives.Lose is null ? raw.Objectives with { Lose = ObjectivesDef.Default.Lose }
                    : raw.Objectives;
                var triggers = raw.Triggers.IsDefault ? raw.Triggers
                    : [.. raw.Triggers.Select((t, i) => t is null || HasOnce(e, i) ? t! : t with { Once = true })];
                return raw with { Objectives = objectives, Triggers = triggers };
            });
            if (def is null)
                return null;
            Ref(RefKind.Map, def.Map, file, Pointer.Append(ptr, "map"));
            if (def.Party.Min < 1 || def.Party.Max < def.Party.Min)
                Error(file, Pointer.Append(ptr, "party"), "The party size needs 1 ≤ min ≤ max.");
            foreach (var (units, name) in new[] { (def.Enemies, "enemies"), (def.Npcs, "npcs") })
                for (var i = 0; i < units.Length; i++)
                {
                    var at = Pointer.Append(Pointer.Append(ptr, name), i);
                    Ref(RefKind.Monster, units[i].Unit, file, Pointer.Append(at, "unit"));
                    Ref(RefKind.AiProfile, units[i].AiProfile, file, Pointer.Append(at, "aiProfile"));
                    if ((units[i].At is null) == (units[i].Zone is null))
                        Error(file, at, "A unit is placed with either \"at\" or \"zone\".");
                    if (units[i].Count < 1)
                        Error(file, Pointer.Append(at, "count"), "The count must be at least 1.");
                }
            for (var i = 0; i < def.Triggers.Length; i++)
                if (string.IsNullOrWhiteSpace(def.Triggers[i].Id))
                    Error(file, Pointer.Append(Pointer.Append(Pointer.Append(ptr, "triggers"), i), "id"), "The id is empty.");
            return def;
        });

    private static bool HasOnce(JsonElement encounter, int trigger)
        => encounter.TryGetProperty("triggers", out var triggers) && triggers.ValueKind == JsonValueKind.Array
           && trigger < triggers.GetArrayLength() && triggers[trigger].ValueKind == JsonValueKind.Object
           && triggers[trigger].TryGetProperty("once", out _);

    /// <summary>A pack manifest.</summary>
    public PackManifest? Manifest(JsonElement e, string file) => Guarded(() => Deserialize<PackManifest>(e, file, ""));

    // ---- After loading ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Checks every reference kept while loading. <paramref name="exists"/> answers for the merged content. AI profiles are
    /// checked only once some pack defines one, and art keys only when an art manifest was given; an art key must be
    /// <c>"&lt;set&gt;.&lt;asset&gt;"</c>.
    /// </summary>
    public void Resolve(Func<RefKind, string, bool> exists, bool anyAiProfiles)
    {
        foreach (var r in _refs)
        {
            if (_broken.Contains((r.Kind, r.Id))
                || (r.Kind == RefKind.AbilityOrPassive && (_broken.Contains((RefKind.Ability, r.Id)) || _broken.Contains((RefKind.Passive, r.Id)))))
                continue;
            var message = r.Kind switch
            {
                RefKind.Art when art is null => null,
                RefKind.Art when !r.Id.Contains('.', StringComparison.Ordinal) => $"Art key '{r.Id}' names no set (expected \"<set>.<asset>\").",
                RefKind.Art => art!.Keys.Contains(r.Id) ? null : $"Unknown art key '{r.Id}'.",
                RefKind.AiProfile when !anyAiProfiles => null,
                RefKind.AbilityOrPassive => exists(RefKind.Ability, r.Id) || exists(RefKind.Passive, r.Id)
                    ? null : $"Unknown ability or passive '{r.Id}'.",
                _ => exists(r.Kind, r.Id) ? null : $"Unknown {KindName(r.Kind)} '{r.Id}'.",
            };
            if (message is not null)
                Error(r.File, r.Pointer, message);
        }
    }

    /// <summary>
    /// Checks each encounter against its map: the zones and tiles its units, objectives and triggers use, its
    /// <c>party_spawn</c> zone, and that every unit tag its objectives and triggers name belongs to one of its units
    /// (placed or spawned). An encounter whose map is missing or broken skips the map checks.
    /// </summary>
    public void CheckEncounters(IEnumerable<Loaded<EncounterDef>> encounters, IReadOnlyDictionary<string, BattleMap> maps)
    {
        foreach (var (def, _, _, file, ptr) in encounters)
            new EncounterCheck(this, def, maps.GetValueOrDefault(def.Map), file, ptr).Run();
    }

    private static string KindName(RefKind kind) => kind switch
    {
        RefKind.AiProfile => "AI profile",
        _ => kind.ToString().ToLowerInvariant(),
    };

    // ---- Effects ----------------------------------------------------------------------------------------------------

    /// <summary>Checks the effect list at <c>e[name]</c>, if there is one. False when an effect couldn't be read.</summary>
    private bool EffectsIn(JsonElement e, string name, string file, string ptr, bool inSave)
        => e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var list)
           || EffectList(list, file, Pointer.Append(ptr, name), inSave);

    private bool EffectList(JsonElement list, string file, string ptr, bool inSave)
    {
        if (list.ValueKind != JsonValueKind.Array)
            return true;   // the element's own deserialisation reports it
        var ok = true;
        var i = 0;
        foreach (var effect in list.EnumerateArray())
            ok &= Effect(effect, file, Pointer.Append(ptr, i++), inSave);
        return ok;
    }

    /// <summary>
    /// One effect: exactly one known block key, its branches (checked first, with their own pointers), then the block's
    /// values: attack kinds, named addends, by-level tables, condition ids, until-save under a save, and ranges.
    /// </summary>
    private bool Effect(JsonElement e, string file, string ptr, bool inSave)
    {
        if (e.ValueKind != JsonValueKind.Object)
        {
            Error(file, ptr, "An effect must be an object with one block key, e.g. {\"damage\": {...}}.");
            return false;
        }
        var names = e.EnumerateObject().Select(p => p.Name).ToArray();
        var keys = names.Where(n => BlockRegistry.TryFor(n) is not null).ToArray();
        if (keys.Length != 1)
        {
            Error(file, ptr, keys.Length == 0
                ? names.Length == 0 ? "An effect needs a block key, e.g. {\"damage\": {...}}."
                    : $"Unknown effect block {string.Join(", ", names.Select(n => $"'{n}'"))}."
                : $"An effect has one block key, but this one has {string.Join(" and ", keys.Select(k => $"'{k}'"))}.");
            return false;
        }

        var key = keys[0];
        var ok = true;
        foreach (var branch in Branches(BlockRegistry.For(key).DefType))
            if (Locate(e, key, branch, ptr) is { } found)
                ok &= EffectList(found.Value, file, found.Pointer, inSave || key == "save");
        if (!ok)
            return false;

        var def = Deserialize<EffectDef>(e, file, ptr);
        if (def is null)
            return false;
        string At(string param) => Locate(e, key, param, ptr)?.Pointer ?? Pointer.Append(ptr, key);
        switch (def)
        {
            case AttackEffect { Kind: not ("weapon" or "spell") } attack:
                Error(file, At("kind"), $"Unknown attack kind '{attack.Kind}' (expected \"weapon\" or \"spell\").");
                break;
            case DamageEffect damage:
                if (damage.Dice is not null)
                    Levels(damage.Dice, file, At("dice"));
                Addends(damage.Bonus, file, At("bonus"));
                break;
            case HealEffect heal:
                Levels(heal.Dice, file, At("dice"));
                Addends(heal.Bonus, file, At("bonus"));
                break;
            case ApplyEffect apply:
                Ref(RefKind.Condition, apply.Condition.Value, file, At("condition"));
                if (apply.UntilSave && !inSave)
                    Error(file, At("untilSave"), "'untilSave' needs an enclosing \"save\" block.");
                break;
            case RemoveEffect remove:
                Ref(RefKind.Condition, remove.Condition.Value, file, At("condition"));
                break;
            case TeleportEffect { Range: > MaxRange } teleport:
                Error(file, At("range"), $"Range {teleport.Range} is over the limit of {MaxRange}.");
                break;
            case JumpToEffect { Range: > MaxRange } jump:
                Error(file, At("range"), $"Range {jump.Range} is over the limit of {MaxRange}.");
                break;
        }
        return true;
    }

    private static readonly ConcurrentDictionary<Type, ImmutableArray<string>> BranchCache = new();

    /// <summary>The JSON names of an effect record's branch parameters (its lists of effects).</summary>
    private static ImmutableArray<string> Branches(Type defType)
        => BranchCache.GetOrAdd(defType, t => [.. t.GetConstructors().MaxBy(c => c.GetParameters().Length)!.GetParameters()
            .Where(p => p.ParameterType == typeof(ImmutableArray<EffectDef>))
            .Select(p => JsonName(p.Name!))]);

    /// <summary>
    /// Where an effect's parameter is written: beside the block key, or inside the key's object. Null when it is absent.
    /// </summary>
    private static (JsonElement Value, string Pointer)? Locate(JsonElement e, string key, string param, string ptr)
    {
        if (param != key && e.TryGetProperty(param, out var beside))
            return (beside, Pointer.Append(ptr, param));
        if (e.GetProperty(key) is { ValueKind: JsonValueKind.Object } head && head.TryGetProperty(param, out var inner))
            return (inner, Pointer.Append(Pointer.Append(ptr, key), param));
        return null;
    }

    private void Addends(ImmutableArray<string> addends, string file, string ptr)
    {
        for (var i = 0; i < addends.Length; i++)
        {
            var name = addends[i];
            var known = name is "prof" or "level" or "mod.cast"
                        || (name.StartsWith("mod.", StringComparison.Ordinal) && EffectContext.ParseStat(name[4..]) is not null);
            if (!known)
                Error(file, Pointer.Append(ptr, i),
                    $"Unknown addend '{name}' (expected \"mod.<stat>\", \"mod.cast\", \"prof\" or \"level\").");
        }
    }

    /// <summary>A by-level table must start at level 1 and stay within levels 1–10.</summary>
    private void Levels<T>(ByLevel<T> table, string file, string ptr)
    {
        if (table.Steps.IsEmpty)
        {
            Error(file, ptr, "The by-level table is empty.");
            return;
        }
        foreach (var level in table.Steps.Keys.Where(l => l is < 1 or > 10))
            Error(file, ptr, $"Level {level} is outside levels 1–10.");
        var first = table.Steps.Keys.First();
        if (first > 1)
            Error(file, ptr, $"The by-level table doesn't cover level 1 (it starts at level {first}).");
    }

    // ---- Classes ----------------------------------------------------------------------------------------------------

    private void FeatureLists(ImmutableSortedDictionary<int, ImmutableArray<FeatureRef>> lists, string file, string ptr,
        Func<int, bool> levelAllowed, string badLevel)
    {
        foreach (var (level, features) in lists)
        {
            var at = Pointer.Append(ptr, level);
            if (!levelAllowed(level))
                Error(file, at, string.Format(CultureInfo.InvariantCulture, badLevel, level));
            for (var i = 0; i < features.Length; i++)
                Feature(features[i], file, Pointer.Append(at, i));
        }
    }

    private void Feature(FeatureRef f, string file, string ptr)
    {
        var kinds = new object?[] { f.Ability, f.Passive, f.Choose, f.ResourceRegen, f.Attacks }.Count(k => k is not null);
        if (kinds != 1 || (!f.From.IsEmpty && f.Choose is null))
        {
            Error(file, ptr, "A feature has exactly one of \"ability\", \"passive\", \"choose\" (with \"from\"), \"resourceRegen\" or \"attacks\".");
            return;
        }
        if (f.Ability is { } ability)
            Ref(RefKind.Ability, ability.Value, file, Pointer.Append(ptr, "ability"));
        Ref(RefKind.Passive, f.Passive, file, Pointer.Append(ptr, "passive"));
        if (f.Choose is { } n)
        {
            if (n < 1 || f.From.Length < n)
                Error(file, Pointer.Append(ptr, "choose"), $"'choose' {n} needs at least 1, and at least {n} entries in 'from'.");
            Refs(RefKind.AbilityOrPassive, f.From, file, Pointer.Append(ptr, "from"));
        }
        if (f.Attacks is < 1)
            Error(file, Pointer.Append(ptr, "attacks"), "'attacks' must be at least 1.");
        if (f.ResourceRegen is { } regen && (regen.Amount < 1 || regen.EveryRounds < 1))
            Error(file, Pointer.Append(ptr, "resourceRegen"), "'amount' and 'everyRounds' must be at least 1.");
    }

    // ---- When -------------------------------------------------------------------------------------------------------

    private void When(WhenDef when, string file, string ptr)
    {
        for (var i = 0; i < when.All.Length; i++)
            When(when.All[i], file, Pointer.Append(Pointer.Append(ptr, "all"), i));
        for (var i = 0; i < when.Any.Length; i++)
            When(when.Any[i], file, Pointer.Append(Pointer.Append(ptr, "any"), i));
        if (when.Not is { } not)
            When(not, file, Pointer.Append(ptr, "not"));
        if (when.TargetHasCondition is { } condition)
            Ref(RefKind.Condition, condition.Value, file, Pointer.Append(ptr, "targetHasCondition"));
    }

    // ---- Encounters -------------------------------------------------------------------------------------------------

    /// <summary>The map-dependent checks of one encounter; see <see cref="CheckEncounters"/>.</summary>
    private sealed class EncounterCheck(Validation v, EncounterDef def, BattleMap? map, string file, string ptr)
    {
        private readonly HashSet<string> _tags = new(StringComparer.Ordinal);

        public void Run()
        {
            foreach (var unit in def.Enemies.Concat(def.Npcs))
                _tags.Add(unit.UnitTag);
            foreach (var trigger in def.Triggers)
                foreach (var spawn in Actions(trigger.Do, "spawn"))
                    if (spawn.ValueKind == JsonValueKind.Object && Text(spawn, "unit") is { } unit)
                        _tags.Add(Text(spawn, "tag") ?? unit);

            foreach (var (units, name) in new[] { (def.Enemies, "enemies"), (def.Npcs, "npcs") })
                for (var i = 0; i < units.Length; i++)
                {
                    var at = Pointer.Append(Pointer.Append(ptr, name), i);
                    if (units[i].Zone is { } zone)
                        Zone(zone, Pointer.Append(at, "zone"));
                    if (units[i].At is { } tile)
                        Tile(tile, Pointer.Append(at, "at"));
                }
            if (map is not null)
            {
                if (!map.Zones.TryGetValue("party_spawn", out var spawn))
                    v.Error(file, Pointer.Append(ptr, "map"), $"The map '{map.Id}' has no \"party_spawn\" zone.");
                else if (spawn.Length < def.Party.Max)
                    v.Error(file, Pointer.Append(Pointer.Append(ptr, "party"), "max"),
                        $"The map '{map.Id}' has {spawn.Length} party_spawn tiles, fewer than the party's {def.Party.Max}.");
            }

            var objectives = Pointer.Append(ptr, "objectives");
            Objective(def.Objectives.Win, Pointer.Append(objectives, "win"));
            Objective(def.Objectives.Lose, Pointer.Append(objectives, "lose"));

            for (var i = 0; i < def.Triggers.Length; i++)
            {
                var at = Pointer.Append(Pointer.Append(ptr, "triggers"), i);
                Condition(def.Triggers[i].When, Pointer.Append(at, "when"));
                Do(def.Triggers[i].Do, Pointer.Append(at, "do"));
            }
        }

        private void Objective(ObjectiveExpr expr, string at)
        {
            for (var i = 0; i < expr.Any.Length; i++)
                Objective(expr.Any[i], Pointer.Append(Pointer.Append(at, "any"), i));
            for (var i = 0; i < expr.All.Length; i++)
                Objective(expr.All[i], Pointer.Append(Pointer.Append(at, "all"), i));
            if (expr.Leaf is not { } leaf)
                return;
            var parts = leaf.Split(':');
            switch (parts)
            {
                case [Objectives.DefeatAll] or [Objectives.PartyOut]:
                case ["survive", var rounds] when int.TryParse(rounds, NumberStyles.None, CultureInfo.InvariantCulture, out var r) && r >= 1:
                case ["flag", { Length: > 0 }]:
                    break;
                case ["defeat" or "protect", { Length: > 0 } tag]:
                    UnitTag(tag, at);
                    break;
                case ["reach", { Length: > 0 } zone, var count] when int.TryParse(count, NumberStyles.None, CultureInfo.InvariantCulture, out var c) && c >= 1:
                    Zone(zone, at);
                    break;
                default:
                    v.Error(file, at, $"'{leaf}' isn't an objective (expected defeat_all, party_out, defeat:<tag>, survive:<rounds>, "
                                      + "reach:<zone>:<count>, protect:<tag> or flag:<name>).");
                    break;
            }
        }

        /// <summary>A trigger's <c>when</c> (spec §4.8 conditions, combined with <c>all</c>/<c>any</c>).</summary>
        private void Condition(JsonElement when, string at)
        {
            if (when.ValueKind != JsonValueKind.Object)
            {
                v.Error(file, at, "A trigger condition must be an object.");
                return;
            }
            foreach (var p in when.EnumerateObject())
            {
                var here = Pointer.Append(at, p.Name);
                switch (p.Name)
                {
                    case "all" or "any" when p.Value.ValueKind == JsonValueKind.Array:
                        var i = 0;
                        foreach (var item in p.Value.EnumerateArray())
                            Condition(item, Pointer.Append(here, i++));
                        break;
                    case "unitEntersZone":
                        TagIn(p.Value, here);
                        if (Text(p.Value, "zone") is { } zone)
                            Zone(zone, Pointer.Append(here, "zone"));
                        break;
                    case "hpBelow" or "unitDies":
                        TagIn(p.Value, here);
                        break;
                    case "round" or "flag" or "counter" or "objective":
                        break;
                    default:
                        v.Error(file, here, $"Unknown trigger condition '{p.Name}'.");
                        break;
                }
            }
        }

        /// <summary>A trigger's <c>do</c> list (spec §4.8 actions, or any effect block).</summary>
        private void Do(JsonElement list, string at)
        {
            if (list.ValueKind != JsonValueKind.Array)
            {
                v.Error(file, at, "A trigger's \"do\" must be a list of actions.");
                return;
            }
            var i = 0;
            foreach (var action in list.EnumerateArray())
            {
                var here = Pointer.Append(at, i++);
                if (action.ValueKind != JsonValueKind.Object)
                {
                    v.Error(file, here, "A trigger action must be an object.");
                    continue;
                }
                foreach (var p in action.EnumerateObject())
                {
                    var inner = Pointer.Append(here, p.Name);
                    switch (p.Name)
                    {
                        case "spawn":
                            if (Text(p.Value, "unit") is { } unit)
                                v.Ref(RefKind.Monster, unit, file, Pointer.Append(inner, "unit"));
                            if (p.Value.ValueKind == JsonValueKind.Object && p.Value.TryGetProperty("at", out var spawnAt))
                                Place(spawnAt, Pointer.Append(inner, "at"));
                            break;
                        case "cameraHint":
                            if (p.Value.ValueKind == JsonValueKind.Object && p.Value.TryGetProperty("at", out var hintAt))
                                Place(hintAt, Pointer.Append(inner, "at"));
                            break;
                        case "setObjective":
                            foreach (var side in new[] { "win", "lose" })
                                if (p.Value.ValueKind == JsonValueKind.Object && p.Value.TryGetProperty(side, out var expr))
                                    RawObjective(expr, Pointer.Append(inner, side));
                            break;
                        case "say" or "setFlag" or "addCounter" or "endBattle":
                            break;
                        default:
                            if (BlockRegistry.TryFor(p.Name) is null)
                                v.Error(file, inner, $"Unknown trigger action '{p.Name}'.");
                            break;
                    }
                }
            }
        }

        private void RawObjective(JsonElement expr, string at)
        {
            try
            {
                Objective(expr.Deserialize<ObjectiveExpr>(ArcJson.Options)!, at);
            }
            catch (JsonException ex)
            {
                v.Error(file, at, Pointer.CleanMessage(ex.Message));
            }
        }

        /// <summary>A place given as a zone name or an <c>[x, y]</c> tile.</summary>
        private void Place(JsonElement place, string at)
        {
            if (place.ValueKind == JsonValueKind.String)
                Zone(place.GetString()!, at);
            else if (place is { ValueKind: JsonValueKind.Array } && place.GetArrayLength() == 2
                     && place[0].TryGetInt32(out var x) && place[1].TryGetInt32(out var y))
                Tile(new Tile(x, y), at);
            else
                v.Error(file, at, "A place is a zone name or an [x, y] tile.");
        }

        private void TagIn(JsonElement e, string at)
        {
            if (Text(e, "tag") is { } tag)
                UnitTag(tag, Pointer.Append(at, "tag"));
            else
                v.Error(file, at, "This needs a unit \"tag\".");
        }

        private void UnitTag(string tag, string at)
        {
            if (!_tags.Contains(tag))
                v.Error(file, at, $"No unit in this encounter has the tag '{tag}'.");
        }

        private void Zone(string zone, string at)
        {
            if (map is not null && !map.Zones.ContainsKey(zone))
                v.Error(file, at, $"The map '{map.Id}' has no zone '{zone}'.");
        }

        private void Tile(Tile tile, string at)
        {
            if (map is not null && !map.InBounds(tile))
                v.Error(file, at, $"Tile {tile} is outside the {map.Width}x{map.Height} map.");
        }

        private static IEnumerable<JsonElement> Actions(JsonElement list, string name)
            => list.ValueKind != JsonValueKind.Array ? []
                : list.EnumerateArray().Where(a => a.ValueKind == JsonValueKind.Object && a.TryGetProperty(name, out _))
                    .Select(a => a.GetProperty(name));

        private static string? Text(JsonElement e, string name)
            => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    // ---- Plumbing ---------------------------------------------------------------------------------------------------

    private void Ref(RefKind kind, string? id, string file, string ptr)
    {
        if (id is not null)
            _refs.Add(new ContentRef(kind, id, file, ptr));
    }

    private void Refs(RefKind kind, IEnumerable<string> ids, string file, string ptr)
    {
        var i = 0;
        foreach (var id in ids)
            Ref(kind, id, file, Pointer.Append(ptr, i++));
    }

    /// <summary>
    /// Runs one element's checks, keeping the references it found only when it loads; an element that fails keeps none,
    /// so its own errors aren't followed by unknown-id errors from a half-read element.
    /// </summary>
    private T? Guarded<T>(Func<T?> check)
    {
        var refs = _refs.Count;
        var result = check();
        if (result is null)
            _refs.RemoveRange(refs, _refs.Count - refs);
        return result;
    }

    /// <summary>
    /// Deserialises one element with <see cref="ArcJson.Options"/>, applies <paramref name="patch"/>, then fills absent
    /// lists, sets and maps with empty ones and reports absent required values (see <see cref="ContentShape"/>). Any
    /// deserialisation exception becomes an error at the element's pointer (plus the path inside it, when known).
    /// </summary>
    private T? Deserialize<T>(JsonElement e, string file, string ptr, Func<T, T>? patch = null) where T : class
    {
        T? def;
        try
        {
            def = e.Deserialize<T>(ArcJson.Options);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or ArgumentException
                                       or NotSupportedException or OverflowException)
        {
            Error(file, ex is JsonException json ? Pointer.Combine(ptr, json.Path) : ptr, Pointer.CleanMessage(ex.Message));
            return null;
        }
        if (def is null)
        {
            Error(file, ptr, "Expected an object, not null.");
            return null;
        }
        if (patch is not null)
            def = patch(def);
        var errors = Errors.Count;
        ContentShape.Fix(def, file, ptr, this);
        return Errors.Count == errors ? def : null;
    }

    private static string JsonName(string name) => JsonNamingPolicy.CamelCase.ConvertName(name);

    /// <summary>
    /// Makes a freshly deserialised definition safe to use: System.Text.Json leaves an absent list as a default
    /// <see cref="ImmutableArray{T}"/> and an absent map, set or object as null. Lists, sets and maps become empty (string
    /// keys ordinal); a missing non-nullable value of any other kind, and a null inside a list or map, is an error.
    /// Walks every content record reachable from the definition.
    /// </summary>
    private static class ContentShape
    {
        private sealed record Prop(PropertyInfo Info, string JsonName, bool NotNull);

        private static readonly ConcurrentDictionary<Type, ImmutableArray<Prop>> Props = new();

        public static void Fix(object def, string file, string ptr, Validation v)
        {
            foreach (var prop in Props.GetOrAdd(def.GetType(), PropsOf))
            {
                var value = prop.Info.GetValue(def);
                var at = Pointer.Append(ptr, prop.JsonName);
                var type = prop.Info.PropertyType;
                if (IsImmutableArray(type) && IsDefaultArray(value!))
                    prop.Info.SetValue(def, type.GetField(nameof(ImmutableArray<int>.Empty))!.GetValue(null));
                else if (value is null)
                {
                    if (!prop.NotNull)
                        continue;
                    if (EmptyCollection(type) is { } empty)
                        prop.Info.SetValue(def, empty);
                    else
                        v.Error(file, at, $"'{prop.JsonName}' is missing.");
                }
                else
                    Item(value, file, at, v);
            }
        }

        private static void Item(object? item, string file, string ptr, Validation v)
        {
            switch (item)
            {
                case null:
                    v.Error(file, ptr, "A value is missing.");
                    break;
                case not null when IsImmutableArray(item.GetType()):
                    if (IsDefaultArray(item))
                        v.Error(file, ptr, "A value is missing.");
                    else
                    {
                        var i = 0;
                        foreach (var element in (IEnumerable)item)
                            Item(element, file, Pointer.Append(ptr, i++), v);
                    }
                    break;
                case ImmutableSortedSet<string> set:
                    if (set.Contains(null!))
                        v.Error(file, ptr, "A value is missing.");
                    break;
                case IDictionary map:
                    foreach (DictionaryEntry entry in map)
                        Item(entry.Value, file, Pointer.Append(ptr, KeyText(entry.Key)), v);
                    break;
                case not null when IsContentRecord(item.GetType()):
                    Fix(item, file, ptr, v);
                    break;
            }
        }

        private static ImmutableArray<Prop> PropsOf(Type type)
        {
            if (!IsContentRecord(type))
                return [];
            var nullability = new NullabilityInfoContext();
            return
            [
                .. type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.SetMethod is { IsPublic: true } && p.GetIndexParameters().Length == 0)
                    .Select(p => new Prop(p, JsonName(p.Name),
                        !p.PropertyType.IsValueType && nullability.Create(p).ReadState == NullabilityState.NotNull)),
            ];
        }

        /// <summary>A record of this assembly worth walking (not a <see cref="ByLevel{T}"/> table, whose steps are values).</summary>
        private static bool IsContentRecord(Type type)
            => type.IsClass && type.Assembly == typeof(ContentShape).Assembly
               && !(type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ByLevel<>));

        private static bool IsImmutableArray(Type type)
            => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ImmutableArray<>);

        private static bool IsDefaultArray(object array)
            => (bool)array.GetType().GetProperty(nameof(ImmutableArray<int>.IsDefault))!.GetValue(array)!;

        private static object? EmptyCollection(Type type)
        {
            if (!type.IsGenericType)
                return null;
            var generic = type.GetGenericTypeDefinition();
            if (generic != typeof(ImmutableSortedSet<>) && generic != typeof(ImmutableSortedDictionary<,>))
                return null;
            if (type == typeof(ImmutableSortedSet<string>))
                return Ordinal.Set();
            var empty = type.GetField("Empty")!.GetValue(null)!;
            return generic == typeof(ImmutableSortedDictionary<,>) && type.GetGenericArguments()[0] == typeof(string)
                ? type.GetMethod("WithComparers", [typeof(IComparer<string>)])!.Invoke(empty, [StringComparer.Ordinal])
                : empty;
        }

        private static string KeyText(object key)
            => key is Enum
                ? key.GetType().GetField(key.ToString()!)?.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name ?? key.ToString()!
                : Convert.ToString(key, CultureInfo.InvariantCulture) ?? "";
    }
}

/// <summary>JSON pointers (RFC 6901) for content errors.</summary>
internal static class Pointer
{
    /// <summary>The pointer to property <paramref name="name"/> of the value at <paramref name="ptr"/>.</summary>
    public static string Append(string ptr, string name) => ptr + "/" + name.Replace("~", "~0").Replace("/", "~1");

    /// <summary>The pointer to item <paramref name="index"/> of the value at <paramref name="ptr"/>.</summary>
    public static string Append(string ptr, int index) => ptr + "/" + index.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// <paramref name="ptr"/> followed by a System.Text.Json path inside it (<c>$.target.range</c>, <c>$.effects[0]</c>,
    /// <c>$['odd.name']</c>).
    /// </summary>
    public static string Combine(string ptr, string? path)
    {
        if (string.IsNullOrEmpty(path) || path[0] != '$')
            return ptr;
        var i = 1;
        while (i < path.Length)
        {
            if (path[i] == '.')
            {
                var end = path.IndexOfAny(['.', '['], i + 1);
                end = end < 0 ? path.Length : end;
                ptr = Append(ptr, path[(i + 1)..end]);
                i = end;
            }
            else if (path[i] == '[' && i + 1 < path.Length && path[i + 1] == '\'')
            {
                var end = path.IndexOf("']", i + 2, StringComparison.Ordinal);
                if (end < 0)
                    break;
                ptr = Append(ptr, path[(i + 2)..end]);
                i = end + 2;
            }
            else if (path[i] == '[')
            {
                var end = path.IndexOf(']', i + 1);
                if (end < 0)
                    break;
                ptr += "/" + path[(i + 1)..end];
                i = end + 1;
            }
            else
                break;
        }
        return ptr;
    }

    /// <summary>
    /// A System.Text.Json message without its trailing <c>Path: … | LineNumber: …</c>, which the pointer replaces. Its
    /// generic "could not be converted to &lt;type&gt;" names the enclosing record rather than the value, so it is replaced.
    /// </summary>
    public static string CleanMessage(string message)
    {
        if (message.StartsWith("The JSON value could not be converted to ", StringComparison.Ordinal))
            return "The value has the wrong type, or isn't one of the allowed values.";
        var cut = message.IndexOf(" Path: ", StringComparison.Ordinal);
        return cut > 0 ? message[..cut] : message;
    }
}
