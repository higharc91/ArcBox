using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>Why a command was refused.</summary>
public enum RejectCode
{
    /// <summary>The actor isn't the active unit.</summary>
    NotYourTurn,
    /// <summary>The action is spent.</summary>
    NoAction,
    /// <summary>The bonus action is spent.</summary>
    NoBonus,
    /// <summary>The reaction is spent.</summary>
    NoReaction,
    /// <summary>Not enough Move left.</summary>
    NoMove,
    /// <summary>The target is too far.</summary>
    OutOfRange,
    /// <summary>The target can't be seen.</summary>
    NoLineOfSight,
    /// <summary>A resource is short.</summary>
    CantAfford,
    /// <summary>No per-battle uses left.</summary>
    NoUses,
    /// <summary>The path is illegal.</summary>
    PathBlocked,
    /// <summary>The target doesn't fit the ability.</summary>
    InvalidTarget,
    /// <summary>The unit can't act.</summary>
    Incapacitated,
    /// <summary>A reaction offer must be answered first.</summary>
    ReactionPending,
    /// <summary>A reaction was sent with no offer pending.</summary>
    NothingToReact,
    /// <summary>The unit doesn't know the ability.</summary>
    UnknownAbility,
    /// <summary>The battle has ended.</summary>
    BattleOver,
    /// <summary>Delay is only allowed before spending anything, once per round, with a later slot to move to.</summary>
    CantDelay,
}

/// <summary>A refused command: the code and the exact text shown on the phone.</summary>
public sealed record Rejection(RejectCode Code, string Reason)
{
    /// <summary>The rejection for <paramref name="code"/> with its standard text; <paramref name="resource"/> names what
    /// <see cref="RejectCode.CantAfford"/> is short of.</summary>
    public static Rejection Of(RejectCode code, string? resource = null) => new(code, code switch
    {
        RejectCode.NotYourTurn => "It's not your turn.",
        RejectCode.NoAction => "You've already used your action.",
        RejectCode.NoBonus => "You've already used your bonus action.",
        RejectCode.NoReaction => "You've already used your reaction.",
        RejectCode.NoMove => "You don't have enough movement.",
        RejectCode.OutOfRange => "Out of range.",
        RejectCode.NoLineOfSight => "No line of sight.",
        RejectCode.CantAfford => $"Not enough {resource}.",
        RejectCode.NoUses => "No uses left this battle.",
        RejectCode.PathBlocked => "You can't move there.",
        RejectCode.InvalidTarget => "That's not a valid target.",
        RejectCode.Incapacitated => "You can't act right now.",
        RejectCode.ReactionPending => "Answer the reaction first.",
        RejectCode.NothingToReact => "There's nothing to react to.",
        RejectCode.UnknownAbility => "You don't have that ability.",
        RejectCode.BattleOver => "The battle is over.",
        RejectCode.CantDelay => "You can only delay at the start of your turn, once per round.",
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, null),
    });
}

/// <summary>The outcome of <see cref="Battle.Apply"/>: the new state and its events, or the same state and a rejection.</summary>
public sealed record Resolution(BattleState State, ImmutableArray<BattleEvent> Events, Rejection? Rejected);
