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
[JsonDerivedType(typeof(AttackResolved), nameof(AttackResolved))]
[JsonDerivedType(typeof(Downed), nameof(Downed))]
[JsonDerivedType(typeof(CountdownTicked), nameof(CountdownTicked))]
[JsonDerivedType(typeof(Revived), nameof(Revived))]
[JsonDerivedType(typeof(UnitOut), nameof(UnitOut))]
[JsonDerivedType(typeof(Died), nameof(Died))]
[JsonDerivedType(typeof(TurnSkipped), nameof(TurnSkipped))]
[JsonDerivedType(typeof(BattleEnded), nameof(BattleEnded))]
[JsonDerivedType(typeof(ZoneCreated), nameof(ZoneCreated))]
[JsonDerivedType(typeof(ZoneEnded), nameof(ZoneEnded))]
[JsonDerivedType(typeof(TerrainAltered), nameof(TerrainAltered))]
[JsonDerivedType(typeof(Spawned), nameof(Spawned))]
[JsonDerivedType(typeof(Despawned), nameof(Despawned))]
[JsonDerivedType(typeof(Transformed), nameof(Transformed))]
[JsonDerivedType(typeof(TurnOrderChanged), nameof(TurnOrderChanged))]
[JsonDerivedType(typeof(Marked), nameof(Marked))]
[JsonDerivedType(typeof(TempHpGained), nameof(TempHpGained))]
[JsonDerivedType(typeof(Countered), nameof(Countered))]
public abstract record BattleEvent
{
    /// <summary>
    /// True on the events of a turn the AI finished for a player whose time ran out (spec §5.5), so the TV can show the
    /// badge: <see cref="Battle.Apply"/> with <c>aiFinished</c> sets it. Left out of the JSON when false.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool AiFinished { get; init; }
}

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

/// <summary>
/// An attack's final outcome, after any reaction to its roll (a <see cref="Rolled"/> hit can become a miss, a hit on a
/// helpless adjacent target a critical hit). The TV labels the attack from this.
/// </summary>
public sealed record AttackResolved(UnitId Source, UnitId Target, bool Hit, bool Crit) : BattleEvent;

/// <summary>A unit dropped to 0 HP and is Downed: prone, unable to act, with a countdown of 3 (spec §3.8).</summary>
public sealed record Downed(UnitId Unit) : BattleEvent;

/// <summary>A Downed unit's countdown dropped (a new round, or a hit) to <see cref="Left"/>; at 0 the unit goes Out.</summary>
public sealed record CountdownTicked(UnitId Unit, int Left) : BattleEvent;

/// <summary>A Downed unit got back up with <see cref="Hp"/> HP (healing, or Help at 1 HP).</summary>
public sealed record Revived(UnitId Unit, int Hp) : BattleEvent;

/// <summary>A Downed unit's countdown ran out: it has left the battle with <see cref="Injury"/>.</summary>
public sealed record UnitOut(UnitId Unit, InjuryId Injury) : BattleEvent;

/// <summary>A unit that doesn't go Downed (an enemy, a summon) dropped to 0 HP and died.</summary>
public sealed record Died(UnitId Unit) : BattleEvent;

/// <summary>A unit's turn came up but it can't act (<see cref="Reason"/>, see <see cref="Battle.SkipReason"/>: usually
/// <c>"downed"</c> or <c>"incapacitated"</c>), so the turn passed straight to the next unit.</summary>
public sealed record TurnSkipped(UnitId Unit, string Reason) : BattleEvent;

/// <summary>The battle is over: emitted once, as the last event of the battle.</summary>
public sealed record BattleEnded(BattleOutcome Outcome) : BattleEvent;

/// <summary>A zone was laid on the map.</summary>
public sealed record ZoneCreated(Zone Zone) : BattleEvent;

/// <summary>The zone with this serial ended (its rounds ran out, its owner's concentration ended, or it was dispelled).</summary>
public sealed record ZoneEnded(int Serial) : BattleEvent;

/// <summary>These tiles were raised (or sunk, for a negative <see cref="Delta"/>); units on them moved with them.</summary>
public sealed record TerrainAltered(ImmutableArray<Tile> Tiles, int Delta) : BattleEvent;

/// <summary>A unit entered the battle (a summon) on <see cref="At"/>, shown with <see cref="Sprite"/>.</summary>
public sealed record Spawned(UnitId Unit, Tile At, string? Sprite) : BattleEvent;

/// <summary>A summon left the battle: its time ran out or its summoner's concentration ended.</summary>
public sealed record Despawned(UnitId Unit) : BattleEvent;

/// <summary>A unit took another form, or went back to its own: <see cref="Into"/> is the sprite it now shows (its sheet's
/// name when the sheet has none).</summary>
public sealed record Transformed(UnitId Unit, string Into) : BattleEvent;

/// <summary>This round's turn order changed (a <c>turnOrder</c> effect).</summary>
public sealed record TurnOrderChanged(ImmutableArray<UnitId> Order) : BattleEvent;

/// <summary><see cref="Unit"/> is now <see cref="By"/>'s marked target.</summary>
public sealed record Marked(UnitId Unit, UnitId By) : BattleEvent;

/// <summary>A unit's temporary HP rose to <see cref="TempHp"/>.</summary>
public sealed record TempHpGained(UnitId Unit, int TempHp) : BattleEvent;

/// <summary><see cref="By"/> countered <see cref="Caster"/>'s <see cref="Ability"/>: its effects don't happen.</summary>
public sealed record Countered(UnitId Caster, AbilityId Ability, UnitId By) : BattleEvent;
