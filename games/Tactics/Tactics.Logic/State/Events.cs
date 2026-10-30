using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Tactics.Logic;

/// <summary>Something that happened, in order. The TV animates events, the phone shows them and golden tests diff them.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(RoundStarted), nameof(RoundStarted))]
[JsonDerivedType(typeof(TurnStarted), nameof(TurnStarted))]
[JsonDerivedType(typeof(TurnEnded), nameof(TurnEnded))]
[JsonDerivedType(typeof(Delayed), nameof(Delayed))]
[JsonDerivedType(typeof(AbilityUsed), nameof(AbilityUsed))]
[JsonDerivedType(typeof(Rolled), nameof(Rolled))]
[JsonDerivedType(typeof(Damaged), nameof(Damaged))]
[JsonDerivedType(typeof(Healed), nameof(Healed))]
[JsonDerivedType(typeof(ConditionApplied), nameof(ConditionApplied))]
[JsonDerivedType(typeof(ConditionRemoved), nameof(ConditionRemoved))]
[JsonDerivedType(typeof(Moved), nameof(Moved))]
[JsonDerivedType(typeof(Pushed), nameof(Pushed))]
[JsonDerivedType(typeof(Fell), nameof(Fell))]
[JsonDerivedType(typeof(Collided), nameof(Collided))]
[JsonDerivedType(typeof(Teleported), nameof(Teleported))]
[JsonDerivedType(typeof(ReactionOffered), nameof(ReactionOffered))]
[JsonDerivedType(typeof(Reacted), nameof(Reacted))]
[JsonDerivedType(typeof(ConditionSaved), nameof(ConditionSaved))]
[JsonDerivedType(typeof(ConcentrationBroken), nameof(ConcentrationBroken))]
[JsonDerivedType(typeof(PassiveTriggered), nameof(PassiveTriggered))]
public abstract record BattleEvent;

/// <summary>A new round began with this turn order.</summary>
public sealed record RoundStarted(int Round, ImmutableArray<UnitId> Order) : BattleEvent;

/// <summary>A unit's turn began.</summary>
public sealed record TurnStarted(UnitId Unit, int Round) : BattleEvent;

/// <summary>A unit's turn ended.</summary>
public sealed record TurnEnded(UnitId Unit) : BattleEvent;

/// <summary>A unit delayed its turn to index <see cref="ToIndex"/> of this round's order.</summary>
public sealed record Delayed(UnitId Unit, int ToIndex) : BattleEvent;

/// <summary>A unit used an ability; <see cref="Presentation"/> carries the ability's animation, effect and projectile keys.</summary>
public sealed record AbilityUsed(UnitId Source, AbilityId Ability, Target Target, PresentationDef? Presentation) : BattleEvent;

/// <summary>A unit rolled a d20 (both dice when it had an edge).</summary>
public sealed record Rolled(UnitId Roller, RollKind Kind, RollResult Result) : BattleEvent;

/// <summary>A unit took <see cref="Amount"/> damage (after resistances, temporary HP included) and has <see cref="HpAfter"/> left.</summary>
public sealed record Damaged(UnitId Unit, int Amount, string? Type, int HpAfter) : BattleEvent;

/// <summary>A unit regained <see cref="Amount"/> HP and has <see cref="HpAfter"/>.</summary>
public sealed record Healed(UnitId Unit, int Amount, int HpAfter) : BattleEvent;

/// <summary>A unit gained a condition.</summary>
public sealed record ConditionApplied(UnitId Unit, ConditionId Condition) : BattleEvent;

/// <summary>A unit lost a condition.</summary>
public sealed record ConditionRemoved(UnitId Unit, ConditionId Condition) : BattleEvent;

/// <summary>A unit walked one step of a move, or dashed or jumped: <see cref="Path"/> starts at the tile it left.</summary>
public sealed record Moved(UnitId Unit, ImmutableArray<Tile> Path) : BattleEvent;

/// <summary>A unit was pushed or pulled one tile.</summary>
public sealed record Pushed(UnitId Unit, Tile From, Tile To) : BattleEvent;

/// <summary>
/// A pushed unit fell <see cref="Levels"/> levels and took <see cref="Damage"/> (rolled, before resistances), or fell into a
/// deep tile (<see cref="IntoDeep"/>). Emitted for falls of more than 3 levels and for every fall into deep water.
/// </summary>
public sealed record Fell(UnitId Unit, int Levels, int Damage, bool IntoDeep) : BattleEvent;

/// <summary>A pushed unit hit a wall, the map edge or another unit (<see cref="With"/>) and took <see cref="Damage"/> (rolled).</summary>
public sealed record Collided(UnitId Unit, UnitId? With, int Damage) : BattleEvent;

/// <summary>A unit was moved without walking (a teleport or a swap).</summary>
public sealed record Teleported(UnitId Unit, Tile From, Tile To) : BattleEvent;

/// <summary>A player-controlled unit was offered a reaction; the battle waits for its <see cref="React"/>.</summary>
public sealed record ReactionOffered(ReactionOffer Offer) : BattleEvent;

/// <summary>A unit answered a reaction offer (or the AI took one) with the choice of this id.</summary>
public sealed record Reacted(UnitId Reactor, string Choice) : BattleEvent;

/// <summary>A unit saved against an until-save condition at the end of its turn (<see cref="Roll"/>); the condition ends.</summary>
public sealed record ConditionSaved(UnitId Unit, ConditionId Condition, RollResult Roll) : BattleEvent;

/// <summary>A caster's concentration on <see cref="Ability"/> ended; what it sustained ends with it.</summary>
public sealed record ConcentrationBroken(UnitId Caster, AbilityId Ability) : BattleEvent;

/// <summary>A passive's hook fired and its effects are about to run.</summary>
public sealed record PassiveTriggered(UnitId Owner, string Passive) : BattleEvent;
