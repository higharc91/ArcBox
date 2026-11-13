using System.Text.Json.Serialization;

namespace Meridian.Logic;

/// <summary>
/// What happened, in order. The adapter turns these into presentation events and narration. No event may have a
/// property named <c>Kind</c>: <c>kind</c> is the JSON discriminator.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Rejected), "rejected")]
[JsonDerivedType(typeof(TurnStarted), "turnStarted")]
[JsonDerivedType(typeof(TurnEnded), "turnEnded")]
[JsonDerivedType(typeof(Moved), "moved")]
[JsonDerivedType(typeof(HatchSealed), "hatchSealed")]
[JsonDerivedType(typeof(OrientRequested), "orientRequested")]
[JsonDerivedType(typeof(CompartmentRevealed), "compartmentRevealed")]
[JsonDerivedType(typeof(CardDrawn), "cardDrawn")]
[JsonDerivedType(typeof(DeckReshuffled), "deckReshuffled")]
[JsonDerivedType(typeof(DeckEmpty), "deckEmpty")]
[JsonDerivedType(typeof(Rolled), "rolled")]
[JsonDerivedType(typeof(BandResolved), "bandResolved")]
[JsonDerivedType(typeof(ChoiceMade), "choiceMade")]
[JsonDerivedType(typeof(TraitChanged), "traitChanged")]
[JsonDerivedType(typeof(MovesChanged), "movesChanged")]
[JsonDerivedType(typeof(ItemGained), "itemGained")]
[JsonDerivedType(typeof(ItemUsed), "itemUsed")]
[JsonDerivedType(typeof(CompartmentUsed), "compartmentUsed")]
[JsonDerivedType(typeof(TradeOffered), "tradeOffered")]
[JsonDerivedType(typeof(TradeAnswered), "tradeAnswered")]
[JsonDerivedType(typeof(BreachHeld), "breachHeld")]
[JsonDerivedType(typeof(BreachFired), "breachFired")]
public abstract record GameEvent;

/// <summary>A command that wasn't legal. The state is unchanged; player input never throws.</summary>
public sealed record Rejected(Command Command, string Reason) : GameEvent;
public sealed record TurnStarted(SeatId Seat, int Round, int Moves) : GameEvent;
public sealed record TurnEnded(SeatId Seat) : GameEvent;
public sealed record Moved(SeatId Seat, CompartmentId From, CompartmentId To, int Cost) : GameEvent;
public sealed record HatchSealed(CompartmentId Compartment, Side Side) : GameEvent;
public sealed record OrientRequested(SeatId Seat, TileId Tile, EqArray<int> Rotations) : GameEvent;
public sealed record CompartmentRevealed(SeatId Seat, Compartment Compartment) : GameEvent;
public sealed record CardDrawn(SeatId Seat, CardId Card, CardKind Deck) : GameEvent;
public sealed record DeckReshuffled(CardKind Deck) : GameEvent;
public sealed record DeckEmpty(CardKind Deck) : GameEvent;
public sealed record Rolled(SeatId Seat, RollReason Reason, Trait? Trait, DiceRoll Dice) : GameEvent;
public sealed record BandResolved(SeatId Seat, CardId Card, int Band, string Text) : GameEvent;
public sealed record ChoiceMade(SeatId Seat, CardId Card, string Option, string Text) : GameEvent;
public sealed record TraitChanged(SeatId Seat, Trait Trait, int FromStep, int ToStep) : GameEvent;
public sealed record MovesChanged(SeatId Seat, int Moves) : GameEvent;
public sealed record ItemGained(SeatId Seat, CardId Card) : GameEvent;
public sealed record ItemUsed(SeatId Seat, CardId Card, bool Discarded) : GameEvent;
public sealed record CompartmentUsed(SeatId Seat, CompartmentId Compartment) : GameEvent;
public sealed record TradeOffered(SeatId From, SeatId To, TradeOffer Offer) : GameEvent;
public sealed record TradeAnswered(SeatId From, SeatId To, bool Accepted) : GameEvent;
public sealed record BreachHeld(SeatId Seat, int Total, int Needed) : GameEvent;
public sealed record BreachFired(BreachInfo Breach) : GameEvent;
