using System.Text.Json;

namespace Tactics.Logic;

/// <summary>The per-element checks: abilities, passives, conditions, races, backgrounds, monsters, items, weapons, armour, injuries, AI profiles, the XP table, maps and pack manifests.</summary>
internal sealed partial class Validation
{
    // ---- Elements -------------------------------------------------------------------------------------------------

    /// <summary>An ability: its effects, then the whole; range, area and presentation keys.</summary>
    public AbilityDef? Ability(JsonElement e, string file, string ptr)
        => Guarded(file, ptr, () =>
        {
            var concentrates = e.ValueKind == JsonValueKind.Object && e.TryGetProperty("cost", out var cost)
                               && cost.ValueKind == JsonValueKind.Object && cost.TryGetProperty("concentration", out var flag)
                               && flag.ValueKind == JsonValueKind.True;
            if (!EffectsIn(e, "effects", file, ptr, inSave: false, concentrates))
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
        => Guarded(file, ptr, () =>
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

    /// <summary>
    /// A condition: its passives, then the whole; the condition it implies. It can't carry <see cref="StaticMods.MaxHp"/>
    /// (only the sheet builder applies it, so a condition's would do nothing).
    /// </summary>
    public ConditionDef? Condition(JsonElement e, string file, string ptr)
        => Guarded(file, ptr, () =>
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
            if (def?.Static is { MaxHp: not 0 })
                Error(file, Pointer.Append(Pointer.Append(ptr, "static"), "maxHp"), NoMaxHp);
            for (var i = 0; def is not null && i < def.Passives.Length; i++)
                if (def.Passives[i].Static is { MaxHp: not 0 })
                    Error(file, Pointer.Append(Pointer.Append(Pointer.Append(Pointer.Append(ptr, "passives"), i), "static"), "maxHp"), NoMaxHp);
            return def;
        });

    private const string NoMaxHp = "A condition can't change max HP (only passives and injuries do, when a character's sheet is built).";

    /// <summary>A race: its racial ability and trait passives.</summary>
    public RaceDef? Race(JsonElement e, string file, string ptr)
        => Guarded(file, ptr, () =>
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
    public BackgroundDef? Background(JsonElement e, string file, string ptr) => Guarded(file, ptr, () => Deserialize<BackgroundDef>(e, file, ptr));

    /// <summary>A monster: its abilities, passives, AI profile, sprite and phases.</summary>
    public MonsterDef? Monster(JsonElement e, string file, string ptr)
        => Guarded(file, ptr, () =>
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
        => Guarded(file, ptr, () =>
        {
            var def = Deserialize<ItemDef>(e, file, ptr);
            if (def is not null)
                Ref(RefKind.Ability, def.Use.Value, file, Pointer.Append(ptr, "use"));
            return def;
        });

    /// <summary>A weapon characters can carry: a range of at least 1.</summary>
    public WeaponDef? Weapon(JsonElement e, string file, string ptr)
        => Guarded(file, ptr, () =>
        {
            var def = Deserialize<WeaponDef>(e, file, ptr);
            if (def is { Range: < 1 })
                Error(file, Pointer.Append(ptr, "range"), "The range must be at least 1.");
            return def;
        });

    /// <summary>A suit of armour.</summary>
    public ArmorDef? Armor(JsonElement e, string file, string ptr) => Guarded(file, ptr, () => Deserialize<ArmorDef>(e, file, ptr));

    /// <summary>An injury: a weight of at least 1.</summary>
    public InjuryDef? Injury(JsonElement e, string file, string ptr)
        => Guarded(file, ptr, () =>
        {
            var def = Deserialize<InjuryDef>(e, file, ptr);
            if (def is { Weight: < 1 })
                Error(file, Pointer.Append(ptr, "weight"), "The weight must be at least 1.");
            return def;
        });

    /// <summary>An AI profile.</summary>
    public AiProfileDef? AiProfile(JsonElement e, string file, string ptr) => Guarded(file, ptr, () => Deserialize<AiProfileDef>(e, file, ptr));

    /// <summary>The XP table: thresholds that start at 0 and rise.</summary>
    public XpTable? Xp(JsonElement e, string file, string ptr)
        => Guarded(file, ptr, () =>
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
        => Guarded(file, ptr, () =>
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

    /// <summary>A pack manifest.</summary>
    public PackManifest? Manifest(JsonElement e, string file) => Guarded(file, "", () => Deserialize<PackManifest>(e, file, ""));

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
}
