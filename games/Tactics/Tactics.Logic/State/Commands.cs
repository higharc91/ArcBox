using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Tactics.Logic;

/// <summary>What a phone or the AI asks a unit to do. <see cref="Battle.Apply"/> validates and resolves it.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(Move), nameof(Move))]
[JsonDerivedType(typeof(Use), nameof(Use))]
[JsonDerivedType(typeof(Delay), nameof(Delay))]
[JsonDerivedType(typeof(React), nameof(React))]
[JsonDerivedType(typeof(EndTurn), nameof(EndTurn))]
public abstract record Command([property: JsonPropertyOrder(-1)] UnitId Actor);

/// <summary>Walk along <see cref="Path"/>, which starts at the actor's tile.</summary>
public sealed record Move(UnitId Actor, ImmutableArray<Tile> Path) : Command(Actor);

/// <summary>Use an ability (attacks, spells, items, Dash, Shove, Help, …) on a target.</summary>
public sealed record Use(UnitId Actor, AbilityId Ability, Target Target) : Command(Actor);

/// <summary>Move the actor's turn <see cref="Slots"/> places later in the current round.</summary>
public sealed record Delay(UnitId Actor, int Slots) : Command(Actor);

/// <summary>Answer the pending reaction offer.</summary>
public sealed record React(UnitId Actor, ReactionChoice Choice) : Command(Actor);

/// <summary>End the actor's turn.</summary>
public sealed record EndTurn(UnitId Actor) : Command(Actor);

/// <summary>What an ability is aimed at: a unit, a tile, or neither (self).</summary>
public sealed record Target(UnitId? Unit = null, Tile? Tile = null);
