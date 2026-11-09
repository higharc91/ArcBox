using System.Collections.Frozen;
using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>
/// The content a battle runs under: abilities, passives, conditions, injuries, maps and the rest of the rules content, with
/// the hash that pins them. <see cref="Load"/> reads it from rules packs; <see cref="InMemory"/> builds one for tests.
/// </summary>
public sealed partial class RulesPack
{
    private RulesPack(string hash, ImmutableSortedDictionary<AbilityId, AbilityDef> abilities,
        ImmutableSortedDictionary<string, PassiveDef> passives, ImmutableSortedDictionary<ConditionId, ConditionDef> conditions,
        ImmutableSortedDictionary<InjuryId, InjuryDef> injuries, ImmutableSortedDictionary<string, BattleMap> maps,
        Catalog? catalog = null, ImmutableSortedDictionary<string, UnitSheet>? monsterSheets = null, ArtManifest? art = null)
    {
        _monsterSheets = monsterSheets ?? Ordinal.Map<UnitSheet>();
        Art = art;
        Hash = hash;
        Abilities = abilities;
        Passives = passives;
        Conditions = conditions;
        Injuries = injuries;
        Maps = maps;
        catalog ??= Catalog.Empty;
        Trinkets = catalog.Trinkets ?? Ordinal.Map<TrinketDef>();
        _trinketPassives = Trinkets.Values.ToImmutableSortedDictionary(t => TrinketPassiveId(t.Id), t => t.Passive(), StringComparer.Ordinal);
        Environment = catalog.Environment ?? EnvironmentDef.None;
        Warnings = catalog.Warnings.IsDefault ? [] : catalog.Warnings;
        Classes = catalog.Classes;
        Races = catalog.Races;
        Backgrounds = catalog.Backgrounds;
        Monsters = catalog.Monsters;
        Items = catalog.Items;
        LootTables = catalog.LootTables ?? Ordinal.Map<LootTableDef>();
        Weapons = catalog.Weapons;
        Armors = catalog.Armors;
        Encounters = catalog.Encounters;
        AiProfiles = catalog.AiProfiles;
        Xp = catalog.Xp;
        Regions = catalog.Regions;
        Interactables = catalog.Interactables ?? ImmutableSortedDictionary.Create<string, ImmutableArray<InteractableDef>>(StringComparer.Ordinal);
        Mobs = catalog.Mobs ?? ImmutableSortedDictionary.Create<string, ImmutableArray<MobDef>>(StringComparer.Ordinal);
        Shops = catalog.Shops ?? ImmutableSortedDictionary.Create<string, ImmutableArray<ShopDef>>(StringComparer.Ordinal);
        _grounds = Regions.ToImmutableSortedDictionary(r => r.Key, r => new Lazy<WorldGround>(() => WorldGround.Of(r.Value, maps, art ?? ArtManifest.None, [.. MobsIn(r.Key).Select(m => m.Arena)])), StringComparer.Ordinal);
        _reach = Regions.ToImmutableSortedDictionary(r => r.Key,
            r => new Lazy<FrozenSet<Tile>>(() => WorldWalk.Reach(GroundFor(r.Key)!.Terrain, r.Value.Def.Spawns.Start, ClassDef.DefaultJump).ToFrozenSet()),
            StringComparer.Ordinal);
        _injuryPassives = injuries.Values.ToImmutableSortedDictionary(i => InjuryPassiveId(i.Id),
            i => new PassiveDef(InjuryPassiveId(i.Id), i.Hook, null, i.Effects.IsDefault ? [] : i.Effects, i.Static with { MaxHp = 0, MaxHpPercent = 0 },
                Target: PassiveTarget.Self), StringComparer.Ordinal);
    }

    private readonly ImmutableSortedDictionary<string, UnitSheet> _monsterSheets;
    private readonly ImmutableSortedDictionary<string, PassiveDef> _injuryPassives;
    private readonly ImmutableSortedDictionary<string, PassiveDef> _trinketPassives;
    private readonly ImmutableSortedDictionary<string, Lazy<WorldGround>> _grounds;
    private readonly ImmutableSortedDictionary<string, Lazy<FrozenSet<Tile>>> _reach;

    /// <summary>The pack's reactions; they run only in encounters that opt in.</summary>
    public EnvironmentDef Environment { get; }

    /// <summary>Non-fatal content diagnostics, such as large connected fuel patches.</summary>
    public ImmutableArray<ContentError> Warnings { get; }

    /// <summary>The art manifest the pack was loaded with, if any.</summary>
    public ArtManifest? Art { get; }

    /// <summary>The hash of everything a battle runs under; a battle records it in <see cref="BattleState.ContentHash"/>. Regions aren't in it.</summary>
    public string Hash { get; }

    /// <summary>Abilities by id.</summary>
    public ImmutableSortedDictionary<AbilityId, AbilityDef> Abilities { get; }

    /// <summary>Passives by id, ordinal.</summary>
    public ImmutableSortedDictionary<string, PassiveDef> Passives { get; }

    /// <summary>Conditions by id.</summary>
    public ImmutableSortedDictionary<ConditionId, ConditionDef> Conditions { get; }

    /// <summary>The injury table by id; a unit going Out gains one, picked by weight (<see cref="DownedRules.GoOut"/>).</summary>
    public ImmutableSortedDictionary<InjuryId, InjuryDef> Injuries { get; }

    /// <summary>Class-bound trinkets; a wrong-class trinket remains equipped but dormant.</summary>
    public ImmutableSortedDictionary<string, TrinketDef> Trinkets { get; }

    public static string TrinketPassiveId(string id) => "trinket:" + id;

    /// <summary>Base maps by id, ordinal.</summary>
    public ImmutableSortedDictionary<string, BattleMap> Maps { get; }

    /// <summary>Classes by id, ordinal.</summary>
    public ImmutableSortedDictionary<string, ClassDef> Classes { get; }

    /// <summary>Races by id, ordinal.</summary>
    public ImmutableSortedDictionary<string, RaceDef> Races { get; }

    /// <summary>Backgrounds by id, ordinal.</summary>
    public ImmutableSortedDictionary<string, BackgroundDef> Backgrounds { get; }

    /// <summary>Monster stat blocks by id, ordinal.</summary>
    public ImmutableSortedDictionary<string, MonsterDef> Monsters { get; }

    /// <summary>Consumable items by id.</summary>
    public ImmutableSortedDictionary<ItemId, ItemDef> Items { get; }

    /// <summary>Fixed battle reward bundles by id.</summary>
    public ImmutableSortedDictionary<string, LootTableDef> LootTables { get; }

    /// <summary>Weapons characters can carry (<c>weapons.json</c>) by id, ordinal.</summary>
    public ImmutableSortedDictionary<string, WeaponDef> Weapons { get; }

    /// <summary>Armour characters can wear (<c>armor.json</c>) by id, ordinal.</summary>
    public ImmutableSortedDictionary<string, ArmorDef> Armors { get; }

    /// <summary>Encounters by id, ordinal.</summary>
    public ImmutableSortedDictionary<string, EncounterDef> Encounters { get; }

    /// <summary>AI weight sets by id, ordinal.</summary>
    public ImmutableSortedDictionary<string, AiProfileDef> AiProfiles { get; }

    /// <summary>The XP thresholds per level (<see cref="XpTable.Default"/> unless a pack has <c>xp.json</c>).</summary>
    public XpTable Xp { get; }

    /// <summary>Overworld regions by id, ordinal.</summary>
    public ImmutableSortedDictionary<string, WorldRegion> Regions { get; }

    /// <summary>The things to interact with in each region (<c>interactables/*.json</c>), by region id, in the order they're written.</summary>
    public ImmutableSortedDictionary<string, ImmutableArray<InteractableDef>> Interactables { get; }

    /// <summary>The things to interact with in the region with this id: none for a region with no <c>interactables</c> file.</summary>
    public ImmutableArray<InteractableDef> InteractablesIn(string region) => Interactables.GetValueOrDefault(region, []);

    /// <summary>Traders by region.</summary>
    public ImmutableSortedDictionary<string, ImmutableArray<MobDef>> Mobs { get; }

    public ImmutableArray<MobDef> MobsIn(string region) => Mobs.GetValueOrDefault(region, []);

    public ImmutableSortedDictionary<string, ImmutableArray<ShopDef>> Shops { get; }

    /// <summary>Traders in a region, or none.</summary>
    public ImmutableArray<ShopDef> ShopsIn(string region) => Shops.GetValueOrDefault(region, []);

    /// <summary>The base map with this id, or null.</summary>
    public BattleMap? MapFor(string id) => Maps.GetValueOrDefault(id);

    /// <summary>The overworld region with this id, or null.</summary>
    public WorldRegion? RegionFor(string id) => Regions.GetValueOrDefault(id);

    /// <summary>The ground of the region with this id, or null: one shared instance, built the first time it's asked for, since every room walks the same ground.</summary>
    public WorldGround? GroundFor(string id) => _grounds.GetValueOrDefault(id)?.Value;

    /// <summary>
    /// Every tile of the region with this id that can be walked to from its start spawn (<see cref="WorldWalk.Reach"/> with
    /// <see cref="ClassDef.DefaultJump"/>), or null: one shared set, flooded the first time it's asked for, as <see cref="GroundFor"/> is.
    /// A place the party stands, lands or travels to has to be in it.
    /// </summary>
    public IReadOnlySet<Tile>? ReachFor(string id) => _reach.GetValueOrDefault(id)?.Value;

    /// <summary>
    /// The id of the passive that carries an injury's static modifiers (<c>"injury:&lt;id&gt;"</c>). <see cref="SheetBuilder"/>
    /// puts it on the sheets of injured characters, and <see cref="PassiveFor"/> finds it, so <see cref="Modifiers"/> honours
    /// an injury like any other passive.
    /// </summary>
    public static string InjuryPassiveId(InjuryId injury) => "injury:" + injury.Value;

    /// <summary>
    /// The passive a sheet's passive id names: one the pack defines, or the one an injury makes
    /// (<see cref="InjuryPassiveId"/>: its static modifiers without <see cref="StaticMods.MaxHp"/>, which the sheet builder
    /// applies, and its hook effects on the owner). Null when there is none.
    /// </summary>
    public PassiveDef? PassiveFor(string id)
        => Passives.TryGetValue(id, out var passive) ? passive : _injuryPassives.GetValueOrDefault(id) ?? _trinketPassives.GetValueOrDefault(id);

    /// <summary>
    /// The unit sheet of the monster <paramref name="id"/> (for summons and transformations), or null when there is none:
    /// a sheet given to <see cref="InMemory"/>, else <see cref="SheetBuilder.ForMonster"/> at the stat block's own level.
    /// The sheet is on the enemy side; a summoner puts it on its own. This is the one place summons and named hooks get
    /// their monsters from.
    /// </summary>
    public UnitSheet? MonsterSheet(string id, int? level = null)
    {
        if (_monsterSheets.TryGetValue(id, out var sheet))
            return sheet;
        return Monsters.ContainsKey(id) ? SheetBuilder.ForMonster(id, this, level) : null;
    }

    /// <summary>
    /// Loads rules packs (spec §4.9) in dependency order, <c>"base"</c> first and then by <c>requires</c>, whatever order the
    /// folders are given in; each folder holds a pack's <c>manifest.json</c> and the files it lists. Every file is validated
    /// (spec §4.10), and the art keys too when <paramref name="art"/> is given. <see cref="Hash"/> is the
    /// <see cref="ContentHash"/> of every loaded file but the regions in <c>world/</c>, which no battle reads, so repainting one
    /// doesn't restart a saved battle; a stamped map's tiles stay pinned by its map file. Throws <see cref="ContentException"/> with
    /// every error found.
    /// </summary>
    public static RulesPack Load(IEnumerable<string> packDirectories, ArtManifest? art = null)
    {
        var loaded = PackLoader.Load(packDirectories, art);
        return new RulesPack(loaded.Hash, loaded.Abilities, loaded.Passives, loaded.Conditions, loaded.Injuries, loaded.Maps,
            loaded.Catalog, art: art);
    }

    /// <summary>The rest of a loaded pack's content, which an in-memory pack leaves empty.</summary>
    internal sealed record Catalog(ImmutableSortedDictionary<string, ClassDef> Classes,
        ImmutableSortedDictionary<string, RaceDef> Races, ImmutableSortedDictionary<string, BackgroundDef> Backgrounds,
        ImmutableSortedDictionary<string, MonsterDef> Monsters, ImmutableSortedDictionary<ItemId, ItemDef> Items,
        ImmutableSortedDictionary<string, WeaponDef> Weapons, ImmutableSortedDictionary<string, ArmorDef> Armors,
        ImmutableSortedDictionary<string, EncounterDef> Encounters, ImmutableSortedDictionary<string, AiProfileDef> AiProfiles,
        XpTable Xp, ImmutableSortedDictionary<string, WorldRegion> Regions,
        ImmutableSortedDictionary<string, ImmutableArray<InteractableDef>>? Interactables = null,
        ImmutableSortedDictionary<string, ImmutableArray<ShopDef>>? Shops = null,
        EnvironmentDef? Environment = null, ImmutableArray<ContentError> Warnings = default,
        ImmutableSortedDictionary<string, TrinketDef>? Trinkets = null,
        ImmutableSortedDictionary<string, ImmutableArray<MobDef>>? Mobs = null,
        ImmutableSortedDictionary<string, LootTableDef>? LootTables = null)
    {
        public static Catalog Empty { get; } = new(Ordinal.Map<ClassDef>(), Ordinal.Map<RaceDef>(), Ordinal.Map<BackgroundDef>(),
            Ordinal.Map<MonsterDef>(), ImmutableSortedDictionary<ItemId, ItemDef>.Empty, Ordinal.Map<WeaponDef>(),
            Ordinal.Map<ArmorDef>(), Ordinal.Map<EncounterDef>(), Ordinal.Map<AiProfileDef>(), XpTable.Default,
            Ordinal.Map<WorldRegion>());
    }

    /// <summary>
    /// The basic Attack action, known to every unit: one weapon attack whose hit deals weapon damage, repeated
    /// <see cref="UnitSheet.Attacks"/> times in one Action. A loaded pack takes it from <c>abilities/common.json</c>; this copy is
    /// for <see cref="InMemory"/> packs, and the base pack's tests check that the two agree.
    /// </summary>
    public static AbilityDef CommonAttack { get; } = new(
        new AbilityId("common.attack"), "Attack", Tier: 0, new CostDef(Action: 1),
        new TargetDef(TargetKind.Enemy, Range: 1, Los: false, new AreaDef(AreaShape.Single)),
        [new AttackEffect("weapon", null, OnHit: [new DamageEffect(null, null, [], Weapon: true)], OnMiss: [], OnCrit: [])],
        new PresentationDef("characters.attack", null, null), new AiHints(["damage", "single"]), IsWeaponAttack: true);

    /// <summary>
    /// The Help action, known to every unit: an adjacent Downed ally gets up at 1 HP; a standing one gets
    /// <see cref="Helped"/> (see <see cref="HelpBlock"/>). A loaded pack takes it from <c>abilities/common.json</c>; this copy
    /// is for <see cref="InMemory"/> packs.
    /// </summary>
    public static AbilityDef CommonHelp { get; } = new(
        new AbilityId("common.help"), "Help", Tier: 0, new CostDef(Action: 1),
        new TargetDef(TargetKind.Ally, Range: 1, Los: false, new AreaDef(AreaShape.Single)),
        [new HelpEffect()], new PresentationDef("characters.item", null, null), new AiHints(["revive", "buff", "single"]));

    /// <summary>
    /// The condition <see cref="CommonHelp"/> gives a standing ally: advantage on its next d20 (an attack roll or a save),
    /// after which it ends. It also ends at the end of the helper's next turn. A loaded pack takes it from
    /// <c>conditions.json</c>; this copy is for <see cref="InMemory"/> packs.
    /// </summary>
    public static ConditionDef Helped { get; } = new(new ConditionId("helped"), "Helped", null, [], Ordinal.Set("help"));

    /// <summary>The one injury an in-memory pack carries unless given a table (a loaded pack has <c>injuries.json</c>).</summary>
    public static InjuryDef TestInjury { get; } = new(new InjuryId("test.injury"), "Test injury", Weight: 1, StaticMods.None);

    /// <summary>
    /// A pack built from the given definitions, plus the built-ins (<see cref="CommonAttack"/>, <see cref="CommonHelp"/>
    /// and <see cref="Helped"/>) unless it defines their ids itself, and <see cref="TestInjury"/> when no injuries are
    /// given. It has the given <paramref name="items"/> and <paramref name="aiProfiles"/> but no classes, races, monster stat
    /// blocks or other catalogue content, and <see cref="XpTable.Default"/>;
    /// <paramref name="monsters"/> are ready-made sheets for <see cref="MonsterSheet"/>. Nothing is validated. Throws
    /// <see cref="ArgumentException"/> on a duplicate id.
    /// </summary>
    public static RulesPack InMemory(string hash, IEnumerable<AbilityDef>? abilities = null, IEnumerable<PassiveDef>? passives = null,
        IEnumerable<ConditionDef>? conditions = null, IEnumerable<BattleMap>? maps = null, IEnumerable<InjuryDef>? injuries = null,
        IEnumerable<(string Id, UnitSheet Sheet)>? monsters = null, IEnumerable<ItemDef>? items = null,
        IEnumerable<AiProfileDef>? aiProfiles = null, EnvironmentDef? environment = null, IEnumerable<TrinketDef>? trinkets = null)
        => new(hash,
            WithBuiltIn(Index(abilities, a => a.Id, Comparer<AbilityId>.Default, "ability"), a => a.Id, CommonAttack, CommonHelp),
            Index(passives, p => p.Id, StringComparer.Ordinal, "passive"),
            WithBuiltIn(Index(conditions, c => c.Id, Comparer<ConditionId>.Default, "condition"), c => c.Id, Helped),
            Index(injuries ?? [TestInjury], i => i.Id, Comparer<InjuryId>.Default, "injury"),
            Index(maps, m => m.Id, StringComparer.Ordinal, "map"),
            Catalog.Empty with
            {
                Environment = environment,
                Trinkets = Index(trinkets, t => t.Id, StringComparer.Ordinal, "trinket"),
                Items = Index(items, i => i.Id, Comparer<ItemId>.Default, "item"),
                AiProfiles = Index(aiProfiles, p => p.Id, StringComparer.Ordinal, "AI profile"),
            },
            Index(monsters, m => m.Id, StringComparer.Ordinal, "monster").ToImmutableSortedDictionary(
                kv => kv.Key, kv => kv.Value.Sheet, StringComparer.Ordinal));

    private static ImmutableSortedDictionary<TKey, TValue> WithBuiltIn<TKey, TValue>(ImmutableSortedDictionary<TKey, TValue> defs,
        Func<TValue, TKey> key, params TValue[] builtIns) where TKey : notnull
    {
        foreach (var builtIn in builtIns)
            if (!defs.ContainsKey(key(builtIn)))
                defs = defs.Add(key(builtIn), builtIn);
        return defs;
    }

    private static ImmutableSortedDictionary<TKey, TValue> Index<TKey, TValue>(IEnumerable<TValue>? items, Func<TValue, TKey> key,
        IComparer<TKey> comparer, string kind) where TKey : notnull
    {
        var builder = ImmutableSortedDictionary.CreateBuilder<TKey, TValue>(comparer);
        foreach (var item in items ?? [])
        {
            if (!builder.TryAdd(key(item), item))
                throw new ArgumentException($"Duplicate {kind} id '{key(item)}'.");
        }
        return builder.ToImmutable();
    }
}
