using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Tactics.Logic;

/// <summary>Whether a unit is standing, Downed (counting down), Out (left the battle with an injury) or Dead.</summary>
public enum LifeState { Up, Downed, Out, Dead }

/// <summary>What a unit has left to spend this turn. Reset at the start of its turn (see <see cref="Economy.Fresh"/>).</summary>
public sealed record TurnEconomy(int MoveLeft, bool Action, bool Bonus, bool Reaction, int ExtraActions);

/// <summary>
/// A condition on a unit. <see cref="RoundsLeft"/> null means it lasts until removed; <see cref="UntilSave"/> rolls
/// <see cref="SaveStat"/> against <see cref="SaveDc"/> at the end of the unit's turn; <see cref="Concentration"/> ties it to
/// the source's concentration.
/// </summary>
public sealed record ActiveCondition(ConditionId Id, UnitId? Source, int? RoundsLeft, bool UntilSave, int SaveDc,
    Stat SaveStat, bool Concentration);

/// <summary>A stack of one item in a unit's bag.</summary>
public sealed record ItemStack(ItemId Item, int Count);

/// <summary>
/// A unit as it is now. <see cref="Resources"/> holds what is left this battle; <see cref="UsesLeft"/> counts per-battle
/// uses by feature id; <see cref="Countdown"/> matters while Downed.
/// </summary>
public sealed record UnitState(UnitId Id, UnitSheet Sheet, Controller Controller, Tile Pos, int Hp, int TempHp,
    [property: JsonConverter(typeof(OrdinalJsonConverter))] ImmutableSortedDictionary<string, int> Resources,
    ImmutableArray<ActiveCondition> Conditions, TurnEconomy Economy, LifeState Life, int Countdown,
    [property: JsonConverter(typeof(OrdinalJsonConverter))] ImmutableSortedDictionary<string, int> UsesLeft,
    AbilityId? Concentrating, ImmutableArray<ItemStack> Bag, int ItemsUsedByAi, UnitId? SummonedBy, bool Protected);
