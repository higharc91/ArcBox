using System.Collections.Immutable;
using System.Text.Json;
using ArcBox.Sdk;
using Tactics.Logic;

namespace Tactics.Game;

/// <summary>
/// An engine failure the adapter caught (rules-engine spec §8): what it was doing, and enough to do it again. The state is
/// the one from before the failure, which is the one kept, so <see cref="Battle.Apply"/> on <see cref="State"/> with
/// <see cref="Command"/> fails the same way.
/// </summary>
/// <param name="Where">What failed: <c>command</c> (a player's), <c>ai</c> (an AI or timed-out step), <c>plan</c> (the AI
/// planning a turn), <c>reaction</c> (the AI choosing a reaction) or <c>start</c> (starting or restarting a battle).</param>
/// <param name="Encounter">The encounter id.</param>
/// <param name="Seed">The seed the battle was started from.</param>
/// <param name="Beat">The last beat logged before the failure (0 before the first).</param>
/// <param name="Round">The battle's round, or 0 when it hadn't started.</param>
/// <param name="TurnIndex">The battle's turn index, or 0 when it hadn't started.</param>
/// <param name="Command">The command as JSON, or null when none was being applied (planning, starting).</param>
/// <param name="State">The battle state from before the failure, as JSON, or null when it hadn't started.</param>
/// <param name="Error">What the engine threw.</param>
public sealed record EngineFault(string Where, string Encounter, ulong Seed, int Beat, int Round, int TurnIndex, string? Command,
    string? State, Exception Error)
{
    /// <summary>One line for a log: everything but the state and the stack.</summary>
    public override string ToString()
        => $"Tactics engine failure ({Where}) in {Encounter}, seed {Seed}, after beat {Beat}, round {Round}, turn {TurnIndex}: "
           + $"{Error.GetType().Name}: {Error.Message}" + (Command is null ? "" : $" Command: {Command}");
}

public sealed partial class TacticsGame
{
    /// <summary>What the TV says, and the phone of whoever made the move, when the engine fails on a command (spec §8).</summary>
    public const string MoveFailed = "Something went wrong with that move.";

    /// <summary>What a player whose own command failed is told: the turn is still theirs.</summary>
    public const string MoveFailedTryAgain = MoveFailed + " Try something else.";

    /// <summary>What the VIP is told when a battle won't start.</summary>
    public const string BattleWontStart = "Something went wrong starting that battle. Pick another.";

    /// <summary>What a returning player is told when their hero couldn't be handed back to them.</summary>
    public const string HandBackFailed = "Something went wrong handing your hero back, so the AI plays it for now.";

    /// <summary>What everyone is told when a battle can't go on at all: it's dropped with nothing won or lost.</summary>
    public const string BattleCalledOff = "Something went wrong with that battle, so it was called off. Nothing was won or lost.";

    /// <summary>
    /// Told of every engine failure the adapter catches, with what's needed to reproduce it. The server logs these; the
    /// game itself does no I/O.
    /// </summary>
    public Action<EngineFault>? FaultLogged { get; init; }

    /// <summary>The engine's <see cref="Battle.Apply"/>. A seam for tests that need it to fail.</summary>
    internal Func<BattleState, Command, RulesPack, bool, Resolution> ApplyEngine { get; set; } = Battle.Apply;

    /// <summary>The engine's <see cref="Battle.Start"/>. A seam for tests that need it to fail.</summary>
    internal Func<BattleSetup, RulesPack, ulong, Resolution> StartEngine { get; set; } = Battle.Start;

    /// <summary>The AI's <see cref="Ai.Plan"/>. A seam for tests that need it to fail.</summary>
    internal Func<BattleState, UnitId, RulesPack, AiOptions?, TurnPlan> PlanEngine { get; set; } = Ai.Plan;

    /// <summary>The AI's <see cref="Ai.React"/>. A seam for tests that need it to fail.</summary>
    internal Func<BattleState, ReactionOffer, RulesPack, ReactionChoice> ReactEngine { get; set; } = Ai.React;

    /// <summary>The engine's <see cref="Battle.Result"/>. A seam for tests that need it to fail.</summary>
    internal Func<BattleState, RulesPack, BattleResult?> ResultEngine { get; set; } = Battle.Result;

    /// <summary>The engine's <see cref="Battle.Hand"/>. A seam for tests that need it to fail.</summary>
    internal Func<BattleState, UnitId, Controller, BattleState> HandEngine { get; set; } = Battle.Hand;

    /// <summary>The AI steps skipped in a row (<see cref="_skips"/>), for tests.</summary>
    internal int Skips => _skips;

    /// <summary>The failure notice on the TV, and when it went up; null while there's none.</summary>
    private DateTimeOffset? _faultAt;

    /// <summary>
    /// AI steps in a row that failed and were skipped (a reaction passed or a turn ended in their place), with no command
    /// applying normally in between. More than a round's worth means every unit is failing, and the battle is called off
    /// rather than skip turns forever. Kept in memory only: a reopened save starts it from 0, which is still bounded, and
    /// the same for every open of the same save.
    /// </summary>
    private int _skips;

    /// <summary>
    /// <see cref="Battle.Apply"/>, or null if the engine failed, which is logged and put on the TV. Nothing changes on a
    /// failure: the state from before the command is the one kept, and it's the one in the save.
    /// </summary>
    private Resolution? TryApply(BattleState before, Command command, bool aiFinished, string where)
    {
        try
        {
            return ApplyEngine(before, command, Rules, aiFinished);
        }
        catch (Exception e) when (IsEngineFailure(e))
        {
            Fault(where, before, command, e);
            return null;
        }
    }

    /// <summary>
    /// Anything the engine throws but running out of memory. A broken invariant (<see cref="RulesInvariantException"/>) is the
    /// case spec §8 names, but the engine is pure and its states are immutable, so any exception from it leaves the state from
    /// before as good as it was.
    /// </summary>
    private static bool IsEngineFailure(Exception e) => e is not OutOfMemoryException;

    /// <summary>Logs a failure and, for a failed move (<paramref name="tv"/>), puts the notice on the TV.</summary>
    private void Fault(string where, BattleState? before, Command? command, Exception e, bool tv = true)
    {
        if (tv)
            _faultAt = _ctx.Now;
        var save = _doc.Battle;
        Report(() => new EngineFault(where, save?.Encounter ?? "", save?.Seed ?? 0, _seq, before?.Round ?? 0, before?.TurnIndex ?? 0,
            command is null ? null : JsonSerializer.Serialize(command, ArcJson.Options),
            before is null ? null : JsonSerializer.Serialize(before, ArcJson.Options), e));
    }

    /// <summary>
    /// Builds and hands over a fault, if anyone listens. Nothing in it may end the room: a state that won't serialise or a
    /// handler that throws loses the log line, never the session.
    /// </summary>
    private void Report(Func<EngineFault> fault)
    {
        if (FaultLogged is not { } logged)
            return;
        try
        {
            logged(fault());
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // Nowhere left to say it: the game does no I/O of its own.
        }
    }

    /// <summary>
    /// Counts one more skipped AI step, and calls the battle off (returning true) if that's more than a round's worth.
    /// </summary>
    private bool SkippedTooMany(BattleState before)
    {
        if (++_skips <= Math.Max(before.Order.Length, 1))
            return false;
        CallOffBattle();
        return true;
    }

    /// <summary>The TV's notice: <see cref="_notice"/>, then the failure notice while it stands.</summary>
    private string? Notice()
        => _faultAt is null ? _notice : _notice is null ? MoveFailed : $"{_notice} {MoveFailed}";

    /// <summary>
    /// A command applied normally: the AI isn't skipping any more, and the failure notice comes down once it has been up
    /// long enough to read.
    /// </summary>
    private void Recovered()
    {
        _skips = 0;
        if (_faultAt is { } at && _ctx.Now - at >= _timing.FaultNotice)
            _faultAt = null;
    }

    /// <summary>
    /// What the AI does instead of a step that failed: a reaction it can't make is passed, any other step ends the turn.
    /// A turn the AI couldn't plan (<paramref name="failed"/> null) is ended too. Null when there's nothing left to fall back
    /// to (the pass or the end of the turn itself failed).
    /// </summary>
    private static Command? Fallback(BattleState before, Command? failed) => failed switch
    {
        React react when before.Pending is ReactionOffer offer
            => Pass(offer) is var pass && pass.Id != react.Choice.Id ? new React(react.Actor, pass) : null,
        EndTurn => null,
        _ when before.Pending is null && before.Active is { } active => new EndTurn(active),
        _ => null,
    };

    private static ReactionChoice Pass(ReactionOffer offer) => offer.Options.FirstOrDefault(o => o.Id == Reactions.Pass) ?? offer.Options[0];

    /// <summary>
    /// An AI step failed (rules-engine spec §8): it's skipped, deterministically, so a save that fails on its next AI step
    /// fails the same way each time it's opened and still moves on. If even the skip fails, or every unit has been
    /// skipping for a round, the battle is called off.
    /// </summary>
    private void SkipFailedStep(BattleState before, Command? failed, bool aiFinished)
    {
        _plan = null;
        if (Fallback(before, failed) is not { } fallback)
        {
            CallOffBattle();
            return;
        }
        if (!SkippedTooMany(before))
            ApplyAi(fallback, aiFinished, skipping: true);
    }

    /// <summary>
    /// The battle can't go on: it's dropped as if it had never been fought (no XP, no injuries, no win or loss), the saved
    /// document keeps no battle, and the party goes back to where a battle's results would have sent it, with a notice.
    /// The notice also covers <see cref="MoveFailed"/>, so the failure notice comes down.
    /// </summary>
    private void CallOffBattle()
    {
        CancelBattleTimers();
        (_faultAt, _skips, _lastBattle, _result) = (null, 0, null, null);
        _doc = _doc with { Battle = null };
        Save();
        Note(BattleCalledOff);
        ToMusterOrPick();
    }

    /// <summary>
    /// <see cref="Battle.Hand"/>, or null if the engine failed (logged; the controller stays as it was). A unit that already
    /// has <paramref name="controller"/> isn't handed at all. Nobody moved, so the TV says nothing: the returning player's
    /// toast is enough.
    /// </summary>
    private BattleState? TryHand(BattleState state, UnitId unit, Controller controller)
    {
        if (state.Units.TryGetValue(unit, out var u) && u.Controller == controller)
            return state;
        try
        {
            return HandEngine(state, unit, controller);
        }
        catch (Exception e) when (IsEngineFailure(e))
        {
            Fault("hand", state, null, e, tv: false);
            return null;
        }
    }

    /// <summary><see cref="Battle.Result"/>, or null if the engine failed (logged).</summary>
    private BattleResult? TryResult(BattleState state)
    {
        try
        {
            return ResultEngine(state, Rules) ?? throw new InvalidOperationException("The battle is over but has no result.");
        }
        catch (Exception e) when (IsEngineFailure(e))
        {
            Fault("result", state, null, e);
            return null;
        }
    }

    /// <summary><see cref="Begin"/>, or null if the engine failed, which is logged.</summary>
    private Resolution? TryBegin(string encounter, ulong seed, ImmutableArray<ProfileId> members, ImmutableArray<CharacterRecord> companions)
    {
        try
        {
            return Begin(encounter, seed, members, companions);
        }
        catch (Exception e) when (IsEngineFailure(e))
        {
            Report(() => new EngineFault("start", encounter, seed, 0, 0, 0, null, null, e));
            return null;
        }
    }
}
