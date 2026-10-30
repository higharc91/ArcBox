using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Tactics.Logic;

/// <summary>A decision the battle is waiting for. While set, only the answering command is accepted.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(ReactionOffer), nameof(ReactionOffer))]
public abstract record Pending;

/// <summary>A unit may react to <see cref="Trigger"/> with one of <see cref="Options"/>.</summary>
public sealed record ReactionOffer(UnitId Reactor, ImmutableArray<ReactionChoice> Options, string Trigger) : Pending;

/// <summary>One answer to a reaction offer. The id <c>"pass"</c> declines.</summary>
public sealed record ReactionChoice(string Id, AbilityId? Ability, UnitId? Target);
