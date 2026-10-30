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
        Catalog? catalog = null, ImmutableSortedDictionary<string, UnitSheet>? monsterSheets = null)
    {
        _monsterSheets = monsterSheets ?? Ordinal.Map<UnitSheet>();
        Hash = hash;
        Abilities = abilities;
        Passives = passives;
        Conditions = conditions;
        Injuries = injuries;
        Maps = maps;
        catalog ??= Catalog.Empty;
        Classes = catalog.Classes;
        Races = catalog.Races;
        Backgrounds = catalog.Backgrounds;
        Monsters = catalog.Monsters;
        Items = catalog.Items;
        Weapons = catalog.Weapons;
        Armors = catalog.Armors;
        Encounters = catalog.Encounters;
        AiProfiles = catalog.AiProfiles;
        Xp = catalog.Xp;
        _injuryPassives = injuries.Values.ToImmutableSortedDictionary(i => InjuryPassiveId(i.Id),
            i => new PassiveDef(InjuryPassiveId(i.Id), i.Hook, null, i.Effects.IsDefault ? [] : i.Effects, i.Static with { MaxHp = 0 },
                Target: PassiveTarget.Self), StringComparer.Ordinal);
    }

    private readonly ImmutableSortedDictionary<string, UnitSheet> _monsterSheets;
    private readonly ImmutableSortedDictionary<string, PassiveDef> _injuryPassives;

    /// <summary>The content hash; a battle records it in <see cref="BattleState.ContentHash"/>.</summary>
    public string Hash { get; }

    /// <summary>Abilities by id.</summary>
    public ImmutableSortedDictionary<AbilityId, AbilityDef> Abilities { get; }

    /// <summary>Passives by id, ordinal.</summary>
    public ImmutableSortedDictionary<string, PassiveDef> Passives { get; }

    /// <summary>Conditions by id.</summary>
    public ImmutableSortedDictionary<ConditionId, ConditionDef> Conditions { get; }

    /// <summary>The injury table by id; a unit going Out gains one, picked by weight (<see cref="DownedRules.GoOut"/>).</summary>
    public ImmutableSortedDictionary<InjuryId, InjuryDef> Injuries { get; }

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

    /// <summary>The base map with this id, or null.</summary>
    public BattleMap? MapFor(string id) => Maps.GetValueOrDefault(id);

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
        => Passives.TryGetValue(id, out var passive) ? passive : _injuryPassives.GetValueOrDefault(id);

    /// <summary>
    /// The unit sheet of the monster <paramref name="id"/> (for summons and transformations), or null when there is none:
    /// a sheet given to <see cref="InMemory"/>, else <see cref="SheetBuilder.ForMonster"/> at the stat block's own level.
    /// The sheet is on the enemy side; a summoner puts it on its own. This is the one place summons and named hooks get
    /// their monsters from.
    /// </summary>
    public UnitSheet? MonsterSheet(string id)
    {
        if (_monsterSheets.TryGetValue(id, out var sheet))
            return sheet;
        return Monsters.ContainsKey(id) ? SheetBuilder.ForMonster(id, this) : null;
    }

    /// <summary>
    /// Loads rules packs (spec §4.9) in dependency order, <c>"base"</c> first and then by <c>requires</c>, whatever order the
    /// folders are given in; each folder holds a pack's <c>manifest.json</c> and the files it lists. Every file is validated
    /// (spec §4.10), and the art keys too when <paramref name="art"/> is given. <see cref="Hash"/> is the
    /// <see cref="ContentHash"/> of every loaded file. Throws <see cref="ContentException"/> with every error found.
    /// </summary>
    public static RulesPack Load(IEnumerable<string> packDirectories, ArtManifest? art = null)
    {
        var loaded = PackLoader.Load(packDirectories, art);
        return new RulesPack(loaded.Hash, loaded.Abilities, loaded.Passives, loaded.Conditions, loaded.Injuries, loaded.Maps,
            loaded.Catalog);
    }

    /// <summary>The rest of a loaded pack's content, which an in-memory pack leaves empty.</summary>
    internal sealed record Catalog(ImmutableSortedDictionary<string, ClassDef> Classes,
        ImmutableSortedDictionary<string, RaceDef> Races, ImmutableSortedDictionary<string, BackgroundDef> Backgrounds,
        ImmutableSortedDictionary<string, MonsterDef> Monsters, ImmutableSortedDictionary<ItemId, ItemDef> Items,
        ImmutableSortedDictionary<string, WeaponDef> Weapons, ImmutableSortedDictionary<string, ArmorDef> Armors,
        ImmutableSortedDictionary<string, EncounterDef> Encounters, ImmutableSortedDictionary<string, AiProfileDef> AiProfiles,
        XpTable Xp)
    {
        public static Catalog Empty { get; } = new(Ordinal.Map<ClassDef>(), Ordinal.Map<RaceDef>(), Ordinal.Map<BackgroundDef>(),
            Ordinal.Map<MonsterDef>(), ImmutableSortedDictionary<ItemId, ItemDef>.Empty, Ordinal.Map<WeaponDef>(),
            Ordinal.Map<ArmorDef>(), Ordinal.Map<EncounterDef>(), Ordinal.Map<AiProfileDef>(), XpTable.Default);
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
    /// given. It has no classes, races, monster stat blocks or other catalogue content, and <see cref="XpTable.Default"/>;
    /// <paramref name="monsters"/> are ready-made sheets for <see cref="MonsterSheet"/>. Nothing is validated. Throws
    /// <see cref="ArgumentException"/> on a duplicate id.
    /// </summary>
    public static RulesPack InMemory(string hash, IEnumerable<AbilityDef>? abilities = null, IEnumerable<PassiveDef>? passives = null,
        IEnumerable<ConditionDef>? conditions = null, IEnumerable<BattleMap>? maps = null, IEnumerable<InjuryDef>? injuries = null,
        IEnumerable<(string Id, UnitSheet Sheet)>? monsters = null)
        => new(hash,
            WithBuiltIn(Index(abilities, a => a.Id, Comparer<AbilityId>.Default, "ability"), a => a.Id, CommonAttack, CommonHelp),
            Index(passives, p => p.Id, StringComparer.Ordinal, "passive"),
            WithBuiltIn(Index(conditions, c => c.Id, Comparer<ConditionId>.Default, "condition"), c => c.Id, Helped),
            Index(injuries ?? [TestInjury], i => i.Id, Comparer<InjuryId>.Default, "injury"),
            Index(maps, m => m.Id, StringComparer.Ordinal, "map"),
            monsterSheets: Index(monsters, m => m.Id, StringComparer.Ordinal, "monster").ToImmutableSortedDictionary(
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
