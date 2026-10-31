using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Tactics.Logic;

/// <summary>Whether a unit is standing, Downed (counting down), Out (left the battle with an injury) or Dead.</summary>
public enum LifeState { Up, Downed, Out, Dead }

/// <summary>What a unit has left to spend this turn. Reset at the start of its turn (see <see cref="Economy.Fresh"/>).</summary>
public sealed record TurnEconomy(int MoveLeft, bool Action, bool Bonus, bool Reaction, int ExtraActions);

/// <summary>
/// A condition on a unit. <see cref="RoundsLeft"/> null means it lasts until removed; otherwise it counts the ends of its
/// source's turns (see <see cref="Conditions.EndTurn"/>). <see cref="UntilSave"/> rolls <see cref="SaveStat"/> against
/// <see cref="SaveDc"/> at the end of the unit's turn; <see cref="Concentration"/> ties it to the source's concentration.
/// <see cref="AppliedThisTurn"/> marks a condition applied during the turn of the unit whose turn ends tick it: that
/// turn's end doesn't count, so 1 round lasts until the end of the source's next turn. <see cref="Mods"/> are static
/// modifiers it carries itself (a <c>statMod</c>), on top of any its definition has.
/// </summary>
public sealed record ActiveCondition(ConditionId Id, UnitId? Source, int? RoundsLeft, bool UntilSave, int SaveDc,
    Stat SaveStat, bool Concentration,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool AppliedThisTurn = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] StaticMods? Mods = null);

/// <summary>A stack of one item in a unit's bag.</summary>
public sealed record ItemStack(ItemId Item, int Count);

/// <summary>
/// A unit as it is now. <see cref="Resources"/> holds what is left this battle; <see cref="UsesLeft"/> counts per-battle
/// uses by feature id; <see cref="Countdown"/> matters while Downed. <see cref="Conditions"/> are in the order they were
/// applied. <see cref="UsedThisTurn"/> and <see cref="UsedThisRound"/> hold the ids of once-per-turn and once-per-round
/// passives that have fired (cleared at every turn start and every round start). <see cref="TrueSheet"/> holds the unit's
/// own sheet while it is transformed (wild shape, polymorph; see <see cref="Transforms"/>).
/// </summary>
public sealed record UnitState(UnitId Id, UnitSheet Sheet, Controller Controller, Tile Pos, int Hp, int TempHp,
    [property: JsonConverter(typeof(OrdinalJsonConverter))] ImmutableSortedDictionary<string, int> Resources,
    ImmutableArray<ActiveCondition> Conditions, TurnEconomy Economy, LifeState Life, int Countdown,
    [property: JsonConverter(typeof(OrdinalJsonConverter))] ImmutableSortedDictionary<string, int> UsesLeft,
    AbilityId? Concentrating, ImmutableArray<ItemStack> Bag, int ItemsUsedByAi, UnitId? SummonedBy, bool Protected,
    [property: JsonConverter(typeof(OrdinalJsonConverter))] ImmutableSortedSet<string> UsedThisTurn,
    [property: JsonConverter(typeof(OrdinalJsonConverter))] ImmutableSortedSet<string> UsedThisRound)
{
    /// <summary>The unit's own sheet while <see cref="Sheet"/> is a form it took; null when it isn't transformed.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public UnitSheet? TrueSheet { get; init; }

    /// <summary>
    /// True for an ally a trigger brought in (<see cref="SpawnAction.Ally"/>): it fights on the party's side but isn't a party
    /// member, so it dies at 0 HP and counts for no objective, party tag or XP. Left out of the JSON when false.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Helper { get; init; }
}
