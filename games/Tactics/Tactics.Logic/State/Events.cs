using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Tactics.Logic;

/// <summary>Something that happened, in order. The TV animates events, the phone shows them and golden tests diff them.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(RoundStarted), nameof(RoundStarted))]
[JsonDerivedType(typeof(TurnStarted), nameof(TurnStarted))]
[JsonDerivedType(typeof(TurnEnded), nameof(TurnEnded))]
[JsonDerivedType(typeof(Delayed), nameof(Delayed))]
public abstract record BattleEvent;

/// <summary>A new round began with this turn order.</summary>
public sealed record RoundStarted(int Round, ImmutableArray<UnitId> Order) : BattleEvent;

/// <summary>A unit's turn began.</summary>
public sealed record TurnStarted(UnitId Unit, int Round) : BattleEvent;

/// <summary>A unit's turn ended.</summary>
public sealed record TurnEnded(UnitId Unit) : BattleEvent;

/// <summary>A unit delayed its turn to index <see cref="ToIndex"/> of this round's order.</summary>
public sealed record Delayed(UnitId Unit, int ToIndex) : BattleEvent;
