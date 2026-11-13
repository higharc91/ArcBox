using System.Text.Json.Serialization;

namespace Meridian.Logic;

/// <summary>An answer the game needs from one seat before anything else can happen. The adapter times each one.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(OrientPending), "orient")]
[JsonDerivedType(typeof(RollPending), "roll")]
[JsonDerivedType(typeof(ChoicePending), "choice")]
[JsonDerivedType(typeof(TradePending), "trade")]
public abstract record Pending(SeatId From);

/// <summary>Choose which way round a new compartment goes. <see cref="Toward"/> is its side facing the mover.</summary>
public sealed record OrientPending(SeatId From, TileId Tile, Cell At, Side Toward, EqArray<int> Rotations) : Pending(From);

[JsonConverter(typeof(JsonStringEnumConverter<RollReason>))]
public enum RollReason
{
    /// <summary>A card's check; <see cref="RollPending.Card"/> (and its option, for a choice) says which.</summary>
    [JsonStringEnumMemberName("check")] Check,

    /// <summary>The Breach roll after an Anomaly (spec §2.6).</summary>
    [JsonStringEnumMemberName("breach")] Breach,
}

/// <summary>Roll dice. The server rolls; the phone's gesture is ceremony (spec §2.2).</summary>
public sealed record RollPending(SeatId From, RollReason Reason, Trait? Trait, int Dice, CardId? Card = null,
                                 string? Option = null) : Pending(From);

public sealed record ChoicePending(SeatId From, CardId Card) : Pending(From);

/// <summary>Accept or decline a trade. <see cref="Offer"/> is from the offerer's side.</summary>
public sealed record TradePending(SeatId From, SeatId Offerer, TradeOffer Offer) : Pending(From);

/// <summary>Items the offerer gives, and items they ask for in return.</summary>
public sealed record TradeOffer(EqArray<CardId> Give, EqArray<CardId> Take);
