using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>
/// The content a battle runs under: abilities, passives, conditions and maps, with the hash that pins them.
/// Built in memory for now; Task 9 adds loading from packs.
/// </summary>
public sealed class RulesPack
{
    private readonly ImmutableSortedDictionary<string, BattleMap> _maps;

    private RulesPack(string hash, ImmutableSortedDictionary<AbilityId, AbilityDef> abilities,
        ImmutableSortedDictionary<string, PassiveDef> passives, ImmutableSortedDictionary<ConditionId, ConditionDef> conditions,
        ImmutableSortedDictionary<string, BattleMap> maps)
    {
        Hash = hash;
        Abilities = abilities;
        Passives = passives;
        Conditions = conditions;
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
    /// A pack built from the given definitions, plus <see cref="CommonAttack"/> unless <paramref name="abilities"/> defines
    /// <c>common.attack</c> itself. Throws <see cref="ArgumentException"/> on a duplicate id.
    /// </summary>
    public static RulesPack InMemory(string hash, IEnumerable<AbilityDef>? abilities = null, IEnumerable<PassiveDef>? passives = null,
        IEnumerable<ConditionDef>? conditions = null, IEnumerable<BattleMap>? maps = null)
        => new(hash,
            WithCommonAttack(Index(abilities, a => a.Id, Comparer<AbilityId>.Default, "ability")),
            Index(passives, p => p.Id, StringComparer.Ordinal, "passive"),
            Index(conditions, c => c.Id, Comparer<ConditionId>.Default, "condition"),
            Index(maps, m => m.Id, StringComparer.Ordinal, "map"));

    private static ImmutableSortedDictionary<AbilityId, AbilityDef> WithCommonAttack(ImmutableSortedDictionary<AbilityId, AbilityDef> abilities)
        => abilities.ContainsKey(CommonAttack.Id) ? abilities : abilities.Add(CommonAttack.Id, CommonAttack);

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
