using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Tactics.Logic;

/// <summary>A decision the battle is waiting for. While set, only the answering command is accepted.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(ReactionOffer), nameof(ReactionOffer))]
public abstract record Pending;

/// <summary>
/// A unit may react to <see cref="Trigger"/> (the trigger's name, for the phone) with one of <see cref="Options"/>.
/// <see cref="Cause"/> is the trigger itself (who did what to whom, the known roll), which <see cref="Ai.React"/> decides
/// on; it travels with the offer, so a saved, paused battle can still be answered by the AI. Offers built by
/// <see cref="Reactions.OffersFor"/> always have it.
/// </summary>
public sealed record ReactionOffer(UnitId Reactor, ImmutableArray<ReactionChoice> Options, string Trigger,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ReactionTrigger? Cause = null) : Pending;

/// <summary>One answer to a reaction offer. The id <c>"pass"</c> declines.</summary>
public sealed record ReactionChoice(string Id, AbilityId? Ability, UnitId? Target);
