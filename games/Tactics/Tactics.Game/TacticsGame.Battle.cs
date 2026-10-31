using System.Collections.Immutable;
using System.Text.Json;
using ArcBox.Sdk;
using Tactics.Logic;

namespace Tactics.Game;

public sealed partial class TacticsGame
{
    /// <summary>The companion profile absent members and timed-out turns play (rules-engine spec §5.5).</summary>
    private const string Companion = "companion";

    /// <summary>The beat the TV has to show before the next AI step, or null when nothing is waiting on it.</summary>
    private int? _awaitShown;

    /// <summary>The rest of the AI's plan for the unit whose turn it is.</summary>
    private (UnitId Unit, int Round, int TurnIndex, Queue<Command> Commands)? _plan;

    /// <summary>A played unit whose turn the AI is finishing (it timed out, or its seat dropped mid-turn).</summary>
    private UnitId? _aiFinishing;

    private (UnitId Unit, int Round, int TurnIndex)? _turnTimerFor;
    private (UnitId Reactor, int Seq)? _reactTimerFor;
    private int _seq;

    private BattleSave Save_ => _doc.Battle!;

    private BattleState State => Save_.State;

    /// <summary>Party members are <c>hero1</c>, <c>hero2</c>, … in the order of <see cref="BattleSave.Members"/>.</summary>
    internal static UnitId UnitFor(int index) => new($"hero{index + 1}");

    private UnitId? UnitOf(ProfileId profile)
        => _doc.Battle is { } save && save.Members.IndexOf(profile) is var i and >= 0 ? UnitFor(i) : null;

    private bool Online(ProfileId profile) => _players.Any(p => p.Profile == profile && _online.Contains(p.Id));

    /// <summary>True when a present player decides for <paramref name="unit"/>.</summary>
    private bool IsPlayed(UnitId unit)
        => State.Units.TryGetValue(unit, out var u) && u.Controller is PlayerControlled { Owner: var owner } && Online(owner);

    private Controller ControllerFor(ProfileId profile) => Online(profile) ? new PlayerControlled(profile) : new AiControlled(Companion);

    // ---- Starting and continuing --------------------------------------------------------------------------------------

    private void StartBattle(string encounter, ulong seed, ImmutableArray<ProfileId> members)
    {
        var resolution = Begin(encounter, seed, members);
        _doc = _doc with { Battle = new BattleSave(encounter, seed, members, resolution.State, []) };
        _seq = 0;
        AddBeat(null, resolution.Events);
        _phase = TacticsPhase.Battle;
        Drive();
    }

    private Resolution Begin(string encounter, ulong seed, ImmutableArray<ProfileId> members)
    {
        ImmutableArray<PartyMember> party = [.. members.Select((p, i) =>
            new PartyMember(UnitFor(i), _doc.For(p)!.Character, ControllerFor(p)))];
        return Battle.Start(Encounters.Setup(encounter, party, Difficulty.Normal, Rules), Rules, seed);
    }

    /// <summary>Starts a saved battle over from its encounter and seed (the rules pack changed under it, spec §7).</summary>
    private void RestartBattle(BattleSave save)
    {
        var resolution = Begin(save.Encounter, save.Seed, save.Members);
        _doc = _doc with { Battle = save with { State = resolution.State, Log = [] } };
        _seq = 0;
        AddBeat(null, resolution.Events);
    }

    /// <summary>Back to a saved battle: each member goes to its player if they're here, else to the companion AI.</summary>
    private void ContinueBattle()
    {
        _phase = TacticsPhase.Battle;
        _seq = Save_.Log.IsDefaultOrEmpty ? 0 : Save_.Log[^1].Seq;
        var state = State;
        for (var i = 0; i < Save_.Members.Length; i++)
            if (state.Units.ContainsKey(UnitFor(i)))
                state = Battle.Hand(state, UnitFor(i), ControllerFor(Save_.Members[i]));
        SetState(state);
        Save();
        Drive();
    }

    private void SeatChanged(ProfileId profile, bool connected)
    {
        if (UnitOf(profile) is not { } unit || !State.Units.ContainsKey(unit))
            return;
        SetState(Battle.Hand(State, unit, connected ? new PlayerControlled(profile) : new AiControlled(Companion)));
        if (!connected && State.Active == unit && State.Pending is null)
            _aiFinishing = unit;     // the AI finishes what's left of the turn
        Save();
        Drive();
    }

    // ---- The loop -----------------------------------------------------------------------------------------------------

    /// <summary>
    /// Moves the battle on until it waits for someone: a present player's turn or reaction, or the TV showing the last
    /// AI beat (adapter spec §4). Each AI step is one command, paced by <c>shown</c> or the fallback timer.
    /// </summary>
    private void Drive()
    {
        for (var guard = 0; guard < 10_000 && _phase == TacticsPhase.Battle; guard++)
        {
            var state = State;
            if (state.Outcome is not null)
            {
                EndBattle();
                return;
            }
            if (_aiFinishing is { } finishing && state.Active != finishing)
                _aiFinishing = null;
            if (_turnTimerFor is { } timed && state.Active != timed.Unit)
            {
                _ctx.CancelTimer(Timers.Turn);
                _turnTimerFor = null;
            }
            if (_awaitShown is not null)
                return;

            if (state.Pending is ReactionOffer offer)
            {
                if (IsPlayed(offer.Reactor))
                {
                    if (_reactTimerFor?.Reactor != offer.Reactor || _reactTimerFor?.Seq != _seq)
                    {
                        _ctx.SetTimer(Timers.React, _timing.Reaction);
                        _reactTimerFor = (offer.Reactor, _seq);
                    }
                    return;
                }
                ApplyAi(new React(offer.Reactor, Ai.React(state, offer, Rules)), aiFinished: false);
                continue;
            }

            if (state.Active is not { } active)
                return;
            if (IsPlayed(active) && _aiFinishing != active)
            {
                var key = (active, state.Round, state.TurnIndex);
                if (_turnTimerFor != key)
                {
                    _ctx.SetTimer(Timers.Turn, _timing.Turn);
                    _turnTimerFor = key;
                }
                return;
            }
            ApplyAi(NextAiCommand(state, active), aiFinished: _aiFinishing == active);
        }
    }

    private Command NextAiCommand(BattleState state, UnitId active)
    {
        if (_plan is not { } plan || plan.Unit != active || plan.Round != state.Round || plan.TurnIndex != state.TurnIndex
            || plan.Commands.Count == 0)
        {
            var options = _aiFinishing == active
                ? new AiOptions(AllowItems: false, AllowDelay: false, RemainingOnly: true, ProfileOverride: Companion)
                : state.Units[active].Controller is AiControlled { Profile: Companion }
                    ? new AiOptions(AllowItems: true)     // an absent member: the companion's one-potion rule applies
                    : new AiOptions();
            var commands = Ai.Plan(state, active, Rules, options).Commands;
            plan = (active, state.Round, state.TurnIndex, new Queue<Command>(commands.IsDefaultOrEmpty ? [new EndTurn(active)] : commands));
            _plan = plan;
        }
        return plan.Commands.Dequeue();
    }

    private void ApplyAi(Command command, bool aiFinished)
    {
        var before = State;
        var resolution = Battle.Apply(before, command, Rules, aiFinished);
        if (resolution.Rejected is not null && command is React react && before.Pending is ReactionOffer offer)
        {
            // An answer the engine won't take: pass rather than ask again forever.
            var pass = offer.Options.FirstOrDefault(o => o.Id == Reactions.Pass) ?? offer.Options[0];
            if (react.Choice.Id != pass.Id)
                ApplyAi(new React(react.Actor, pass), aiFinished);
            return;
        }
        if (resolution.Rejected is not null)
        {
            // The plan went stale (a reaction moved things): end the turn rather than loop. A refused EndTurn can't happen
            // for the active unit, so this always moves the battle on.
            _plan = null;
            if (command is not EndTurn && before.Active == command.Actor && before.Pending is null)
                ApplyAi(new EndTurn(command.Actor), aiFinished);
            return;
        }
        SetState(resolution.State);
        var after = resolution.State;
        if (after.Active != command.Actor || after.Pending is not null
            || command is Move move && after.Units.TryGetValue(move.Actor, out var mover) && mover.Pos != move.Path[^1])
            _plan = null;
        Pace(AddBeat(command.Actor, resolution.Events), resolution.Events.Length);
    }

    // ---- Players ------------------------------------------------------------------------------------------------------

    private void BattleInput(Player player, ProfileId profile, CustomInput input)
    {
        if (UnitOf(profile) is not { } unit)
            return;
        var state = State;
        Command? command;
        if (input.Action == "react")
        {
            if (state.Pending is not ReactionOffer offer || offer.Reactor != unit || !IsPlayed(unit))
                return;
            var id = Str(input.Data, "choice");
            if (offer.Options.FirstOrDefault(o => o.Id == id) is not { } choice)
            {
                Toast(player.Id, "That isn't one of the options.");
                return;
            }
            command = new React(unit, choice);
        }
        else
        {
            if (state.Active != unit || state.Pending is not null || !IsPlayed(unit) || _aiFinishing == unit)
                return;
            command = ParseCommand(unit, input);
            if (command is null)
            {
                Toast(player.Id, "That didn't make sense to the game. Try again.");
                return;
            }
        }

        var resolution = Battle.Apply(state, command, Rules);
        if (resolution.Rejected is { } rejected)
        {
            Toast(player.Id, rejected.Reason);
            return;
        }
        _toasts.Remove(player.Id);
        _plan = null;   // the player decided: nothing the AI planned for this turn still applies
        SetState(resolution.State);
        if (command is React)
        {
            _ctx.CancelTimer(Timers.React);
            _reactTimerFor = null;
        }
        Pace(AddBeat(unit, resolution.Events), resolution.Events.Length);
        Drive();
    }

    /// <summary>Holds the AI until the TV has shown beat <paramref name="seq"/>, or the fallback wait for its events is up.</summary>
    private void Pace(int? seq, int events)
    {
        if (seq is null)
            return;
        _awaitShown = seq;
        _ctx.SetTimer(Timers.Pace, _timing.Pace(events));
    }

    private static Command? ParseCommand(UnitId unit, CustomInput input)
    {
        try
        {
            return input.Action switch
            {
                "move" when input.Data?.TryGetProperty("path", out var path) == true
                    => new Move(unit, [.. path.EnumerateArray().Select(ReadTile)]),
                "use" => new Use(unit, new AbilityId(Str(input.Data, "ability") ?? ""), new Target(
                        Str(input.Data, "unit") is { } u ? new UnitId(u) : null,
                        input.Data?.TryGetProperty("tile", out var tile) == true && tile.ValueKind == JsonValueKind.Array ? ReadTile(tile) : null),
                    Str(input.Data, "item") is { } item ? new ItemId(item) : null),
                "delay" => new Delay(unit, input.Data?.TryGetProperty("slots", out var slots) == true ? slots.GetInt32() : 1),
                "endTurn" => new EndTurn(unit),
                _ => null,
            };
        }
        catch (Exception e) when (e is InvalidOperationException or FormatException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    private static Tile ReadTile(JsonElement e) => new(e[0].GetInt32(), e[1].GetInt32());

    private static string? Str(JsonElement? data, string name)
        => data is { ValueKind: JsonValueKind.Object } d && d.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private void TurnTimedOut()
    {
        _turnTimerFor = null;
        if (State.Active is { } active && IsPlayed(active))
            _aiFinishing = active;     // finished by the AI once any pending reaction is answered
        Drive();
    }

    private void ReactionTimedOut()
    {
        var timedOut = _reactTimerFor;
        _reactTimerFor = null;
        if (State.Pending is ReactionOffer offer && IsPlayed(offer.Reactor) && timedOut?.Reactor == offer.Reactor)
        {
            // No answer: the same rule the AI uses (rules-engine spec §5.5).
            var resolution = Battle.Apply(State, new React(offer.Reactor, Ai.React(State, offer, Rules)), Rules);
            if (resolution.Rejected is null)
            {
                SetState(resolution.State);
                Pace(AddBeat(offer.Reactor, resolution.Events), resolution.Events.Length);
            }
        }
        Drive();
    }

    // ---- State and the log --------------------------------------------------------------------------------------------

    private void SetState(BattleState state) => _doc = _doc with { Battle = Save_ with { State = state } };

    /// <summary>Logs a beat and saves the document (rules-engine spec §2.5). Returns its number, or null for no events.</summary>
    private int? AddBeat(UnitId? actor, ImmutableArray<BattleEvent> events)
    {
        if (events.IsDefaultOrEmpty)
        {
            Save();
            return null;
        }
        var beat = new Beat(++_seq, actor, events);
        var log = Save_.Log.IsDefault ? [] : Save_.Log;
        log = log.Add(beat);
        if (log.Length > TacticsDocument.LogBeats)
            log = log.RemoveRange(0, log.Length - TacticsDocument.LogBeats);
        _doc = _doc with { Battle = Save_ with { Log = log } };
        Save();
        return beat.Seq;
    }

    private void CancelBattleTimers()
    {
        _ctx.CancelTimer(Timers.Turn);
        _ctx.CancelTimer(Timers.React);
        _ctx.CancelTimer(Timers.Pace);
        (_turnTimerFor, _reactTimerFor, _awaitShown, _plan, _aiFinishing) = (null, null, null, null, null);
    }
}
