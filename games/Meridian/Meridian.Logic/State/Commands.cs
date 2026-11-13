using System.Text.Json.Serialization;

namespace Meridian.Logic;

/// <summary>
/// A seat's intent, or the adapter's clock. Every target is absolute ("move to c12", "open c4's east hatch"), never
/// relative, so a command that arrives late or twice is safe: it is legal or it is rejected.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(MoveTo), "moveTo")]
[JsonDerivedType(typeof(Open), "open")]
[JsonDerivedType(typeof(Orient), "orient")]
[JsonDerivedType(typeof(Roll), "roll")]
[JsonDerivedType(typeof(Choose), "choose")]
[JsonDerivedType(typeof(UseItem), "useItem")]
[JsonDerivedType(typeof(UseCompartment), "useCompartment")]
[JsonDerivedType(typeof(OfferTrade), "offerTrade")]
[JsonDerivedType(typeof(AnswerTrade), "answerTrade")]
[JsonDerivedType(typeof(EndTurn), "endTurn")]
[JsonDerivedType(typeof(Timeout), "timeout")]
public abstract record Command(SeatId From);

public sealed record MoveTo(SeatId From, CompartmentId To) : Command(From);
public sealed record Open(SeatId From, CompartmentId At, Side Side) : Command(From);
public sealed record Orient(SeatId From, int Rotation) : Command(From);
public sealed record Roll(SeatId From) : Command(From);
public sealed record Choose(SeatId From, string Option) : Command(From);
public sealed record UseItem(SeatId From, CardId Item) : Command(From);
public sealed record UseCompartment(SeatId From) : Command(From);
public sealed record OfferTrade(SeatId From, SeatId To, TradeOffer Offer) : Command(From);
public sealed record AnswerTrade(SeatId From, bool Accept) : Command(From);
public sealed record EndTurn(SeatId From) : Command(From);

/// <summary>The adapter's deadline for <see cref="From"/> passed: resolves whatever that seat owes (see Rules).</summary>
public sealed record Timeout(SeatId From) : Command(From);
