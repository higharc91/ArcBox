using System.Text.Json.Serialization;
using Tactics.Logic;

namespace Tactics.Game;

/// <summary>What a player wants done when their hero's opportunity attack is on offer (issue #113). JSON and phone: <c>ask</c>, <c>always</c>, <c>never</c>.</summary>
public enum OpportunityPref { Ask, Always, Never }

/// <summary>
/// What a player wants done when a defensive reaction (Shield and its like: an ability aimed at the reactor itself) is on offer. JSON
/// and phone: <c>ask</c>, <c>miss</c> (only when it changes the outcome: a miss or Uncanny Dodge preventing Downing), <c>never</c>.
/// </summary>
public enum DefencePref { Ask, Miss, Never }

/// <summary>
/// A member's reaction preferences, kept on their <see cref="MemberRecord"/> (issue #113). A reaction a preference answers resolves at once
/// with no prompt and no pause, exactly as if the player had tapped that answer. A summon's reactions follow its owner's.
/// </summary>
public sealed record ReactionPrefs(OpportunityPref Opportunity = OpportunityPref.Ask, DefencePref Defence = DefencePref.Ask)
{
    /// <summary>Ask about everything: today's behaviour, and what a member who never set anything has.</summary>
    public static ReactionPrefs Ask { get; } = new();

    /// <summary>The phone's word for <see cref="Opportunity"/>.</summary>
    public string OpportunityWord => Opportunity.ToString().ToLowerInvariant();

    /// <summary>The phone's word for <see cref="Defence"/>.</summary>
    public string DefenceWord => Defence.ToString().ToLowerInvariant();

    /// <summary>These settings with the ones named in <paramref name="opportunity"/> and <paramref name="defence"/>; null when a word isn't one of the settings.</summary>
    public ReactionPrefs? With(string? opportunity, string? defence)
    {
        var next = this;
        if (opportunity is not null)
        {
            if (!Enum.TryParse<OpportunityPref>(opportunity, ignoreCase: true, out var o) || !Enum.IsDefined(o))
                return null;
            next = next with { Opportunity = o };
        }
        if (defence is not null)
        {
            if (!Enum.TryParse<DefencePref>(defence, ignoreCase: true, out var d) || !Enum.IsDefined(d))
                return null;
            next = next with { Defence = d };
        }
        return next;
    }

    /// <summary>
    /// The answer these settings give to <paramref name="offer"/> without asking, or null to ask the player. An offer with any option
    /// they don't cover (a rebuke, a counterspell) is asked. Otherwise each kind of option on offer must have a setting that answers
    /// it: the opportunity attack by <see cref="Opportunity"/>, a defensive reaction by <see cref="Defence"/> (<see cref="DefencePref.Miss"/>
    /// takes the first one for which <see cref="Ai.ChangesOutcome"/> holds, else passes).
    /// </summary>
    public ReactionChoice? Answer(BattleState state, ReactionOffer offer, RulesPack rules)
    {
        var pass = offer.Options.FirstOrDefault(o => o.Id == Reactions.Pass) ?? new ReactionChoice(Reactions.Pass, null, null);
        ReactionChoice? attack = null;
        var defences = new List<ReactionChoice>();
        foreach (var option in offer.Options)
        {
            if (option.Id == Reactions.Pass)
                continue;
            if (option.Id == Reactions.Opportunity)
                attack = option;
            else if (option.Ability is { } id && rules.Abilities.TryGetValue(id, out var ability) && ability.Target.Kind == TargetKind.Self
                     && offer.Cause is { Kind: ReactionTriggerKind.BeforeHit })
                defences.Add(option);
            else
                return null;     // a reaction no setting covers
        }

        ReactionChoice? chosen = null;
        if (attack is not null)
            switch (Opportunity)
            {
                case OpportunityPref.Ask: return null;
                case OpportunityPref.Always: chosen = attack; break;
            }
        if (defences.Count > 0)
            switch (Defence)
            {
                case DefencePref.Ask: return null;
                case DefencePref.Miss: chosen ??= defences.FirstOrDefault(d => Ai.ChangesOutcome(state, offer, d, rules)); break;
            }
        return chosen ?? pass;
    }
}
