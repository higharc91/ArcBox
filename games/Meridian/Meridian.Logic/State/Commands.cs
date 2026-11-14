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
[JsonDerivedType(typeof(Attack), "attack")]
[JsonDerivedType(typeof(Allocate), "allocate")]
[JsonDerivedType(typeof(MonsterMove), "monsterMove")]
[JsonDerivedType(typeof(MonsterAttack), "monsterAttack")]
[JsonDerivedType(typeof(Lend), "lend")]
[JsonDerivedType(typeof(Begin), "begin")]
[JsonDerivedType(typeof(ScenarioAct), "scenarioAct")]
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

/// <summary>Attack a crew member (by seat id) or a monster (by token id) in the same compartment (spec §2.7).</summary>
public sealed record Attack(SeatId From, string Target) : Command(From);

/// <summary>Answer an <see cref="AllocatePending"/>: <see cref="First"/> steps off the group's first trait, the rest off the second.</summary>
public sealed record Allocate(SeatId From, int First) : Command(From);

public sealed record MonsterMove(SeatId From, string Monster, CompartmentId To) : Command(From);
public sealed record MonsterAttack(SeatId From, string Monster, SeatId Target) : Command(From);

/// <summary>An echo lends a living crew member an extra die on their next check (spec §2.8).</summary>
public sealed record Lend(SeatId From, SeatId To) : Command(From);

/// <summary>The adapter's command once every briefing is read: the endgame begins (spec §3.1).</summary>
public sealed record Begin(SeatId From) : Command(From);

/// <summary>A scenario's own action (spec §4.3), by its id, with an optional target.</summary>
public sealed record ScenarioAct(SeatId From, string Action, string? Data = null) : Command(From);
