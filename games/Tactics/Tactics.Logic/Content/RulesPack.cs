using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>
/// The content a battle runs under: abilities, passives, conditions, injuries and maps, with the hash that pins them.
/// Built in memory for now; Task 9 adds loading from packs.
/// </summary>
public sealed class RulesPack
{
    private readonly ImmutableSortedDictionary<string, BattleMap> _maps;

    private RulesPack(string hash, ImmutableSortedDictionary<AbilityId, AbilityDef> abilities,
        ImmutableSortedDictionary<string, PassiveDef> passives, ImmutableSortedDictionary<ConditionId, ConditionDef> conditions,
        ImmutableSortedDictionary<InjuryId, InjuryDef> injuries, ImmutableSortedDictionary<string, BattleMap> maps)
    {
        Hash = hash;
        Abilities = abilities;
        Passives = passives;
        Conditions = conditions;
        Injuries = injuries;
        _maps = maps;
    }

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

    /// <summary>The base map with this id, or null.</summary>
    public BattleMap? MapFor(string id) => _maps.GetValueOrDefault(id);

    /// <summary>
    /// The basic Attack action, known to every unit: one weapon attack whose hit deals weapon damage, repeated
    /// <see cref="UnitSheet.Attacks"/> times in one Action. (Task 13 moves it to <c>abilities/common.json</c>.)
    /// </summary>
    public static AbilityDef CommonAttack { get; } = new(
        new AbilityId("common.attack"), "Attack", Tier: 0, new CostDef(Action: 1),
        new TargetDef(TargetKind.Enemy, Range: 1, Los: false, new AreaDef(AreaShape.Single)),
        [new AttackEffect("weapon", null, OnHit: [new DamageEffect(null, null, [], Weapon: true)], OnMiss: [], OnCrit: [])],
        new PresentationDef("attack", null, null), new AiHints(["damage", "single"]), IsWeaponAttack: true);

    /// <summary>
    /// The Help action, known to every unit: an adjacent Downed ally gets up at 1 HP; a standing one gets
    /// <see cref="Helped"/> (see <see cref="HelpBlock"/>). (Task 13 moves it to <c>abilities/common.json</c>.)
    /// </summary>
    public static AbilityDef CommonHelp { get; } = new(
        new AbilityId("common.help"), "Help", Tier: 0, new CostDef(Action: 1),
        new TargetDef(TargetKind.Ally, Range: 1, Los: false, new AreaDef(AreaShape.Single)),
        [new HelpEffect()], new PresentationDef("help", null, null), new AiHints(["revive", "buff", "single"]));

    /// <summary>
    /// The condition <see cref="CommonHelp"/> gives a standing ally: advantage on its next d20 (an attack roll or a save),
    /// after which it ends. It also ends at the end of the helper's next turn. (Task 13 moves it to
    /// <c>conditions.json</c>.)
    /// </summary>
    public static ConditionDef Helped { get; } = new(new ConditionId("helped"), "Helped", null, [], Ordinal.Set("help"));

    /// <summary>The one injury an in-memory pack carries unless given a table (Task 13 adds <c>injuries.json</c>).</summary>
    public static InjuryDef TestInjury { get; } = new(new InjuryId("test.injury"), "Test injury", Weight: 1, StaticMods.None);

    /// <summary>
    /// A pack built from the given definitions, plus the built-ins (<see cref="CommonAttack"/>, <see cref="CommonHelp"/>
    /// and <see cref="Helped"/>) unless it defines their ids itself, and <see cref="TestInjury"/> when no injuries are
    /// given. Throws <see cref="ArgumentException"/> on a duplicate id.
    /// </summary>
    public static RulesPack InMemory(string hash, IEnumerable<AbilityDef>? abilities = null, IEnumerable<PassiveDef>? passives = null,
        IEnumerable<ConditionDef>? conditions = null, IEnumerable<BattleMap>? maps = null, IEnumerable<InjuryDef>? injuries = null)
        => new(hash,
            WithBuiltIn(Index(abilities, a => a.Id, Comparer<AbilityId>.Default, "ability"), a => a.Id, CommonAttack, CommonHelp),
            Index(passives, p => p.Id, StringComparer.Ordinal, "passive"),
            WithBuiltIn(Index(conditions, c => c.Id, Comparer<ConditionId>.Default, "condition"), c => c.Id, Helped),
            Index(injuries ?? [TestInjury], i => i.Id, Comparer<InjuryId>.Default, "injury"),
            Index(maps, m => m.Id, StringComparer.Ordinal, "map"));

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
