using System.Collections.Immutable;
using System.Text.Json;
using ArcBox.Sdk;
using Tactics.Logic;

namespace Tactics.Game;

/// <summary>A unit as the TV and phones draw it.</summary>
public sealed record UnitView(UnitId Id, string Name, string Side, string? Sprite, Tile At, int Hp, int MaxHp, int TempHp,
    string Life, int Countdown, ImmutableArray<string> Conditions, string? Player);

/// <summary>The battle as the TV draws it (adapter spec §8).</summary>
public sealed record BattleView(string Encounter, string Map, int Round, ImmutableArray<UnitId> Order, UnitId? Active,
    ImmutableArray<UnitView> Units, ImmutableArray<Beat> Beats, string? Outcome);

/// <summary>An encounter the VIP can pick.</summary>
public sealed record EncounterOption(string Id, string Name, int? Level, int Min, int Max, bool Fits);

/// <summary>A class a member can pick at Muster.</summary>
public sealed record ClassOption(string Id, string Name, string Role);

/// <summary>A member's line on the results screen.</summary>
public sealed record ResultLine(string Name, int Xp, ImmutableArray<string> Injuries);

/// <summary>The TV's view of a tactics session (adapter spec §8).</summary>
public sealed record TacticsHostView(string Phase, string Banner, string? Notice, int PartyLevel,
    ImmutableArray<string> Mustered, ImmutableArray<EncounterOption>? Encounters, BattleView? Battle,
    string? Outcome, ImmutableArray<ResultLine>? Results);

public sealed partial class TacticsGame
{
    /// <inheritdoc />
    public object HostView() => new TacticsHostView(_phase.ToString(), Banner(), _notice, _doc.PartyLevel,
        [.. _players.Where(p => _doc.For(p.Profile!.Value) is not null).Select(p => p.Name)],
        _phase == TacticsPhase.EncounterPick ? EncounterOptions() : null,
        _doc.Battle is not null && _phase is TacticsPhase.Battle or TacticsPhase.Resuming ? BattleViewOf() : null,
        _result?.Outcome.ToString(), _phase == TacticsPhase.Results ? ResultLines() : null);

    /// <inheritdoc />
    public ControllerView PlayerView(PlayerId id)
    {
        var player = _players.FirstOrDefault(p => p.Id == id);
        if (player?.Profile is not { } profile)
            return new WaitView("Waiting…");
        var view = _phase switch
        {
            TacticsPhase.Muster when _doc.For(profile) is null => Custom(Screens.PickClass, new
            {
                classes = Rules.Classes.Values.Select(c => new ClassOption(c.Id, c.Name, c.Role)),
            }),
            TacticsPhase.Muster => new WaitView("Waiting for everyone to choose a class"),
            TacticsPhase.EncounterPick when player.IsVip => Custom(Screens.PickEncounter, new { encounters = EncounterOptions() }),
            TacticsPhase.EncounterPick => new WaitView($"Waiting for {_players.FirstOrDefault(p => p.IsVip)?.Name ?? "the host"} to choose a battle"),
            TacticsPhase.Battle => BattleScreen(profile),
            TacticsPhase.Results => Custom(Screens.Results, new
            {
                outcome = _result?.Outcome.ToString(), encounter = _resultEncounter, lines = ResultLines(), vip = player.IsVip,
                level = _doc.PartyLevel,
            }),
            TacticsPhase.Resuming when player.IsVip => new ButtonsView(
                $"Resuming… waiting for {string.Join(", ", Waiting())}", [new Option("continue", "Continue without them")]),
            TacticsPhase.Resuming => new WaitView($"Resuming… waiting for {string.Join(", ", Waiting())}"),
            _ => (ControllerView)new WaitView("Waiting…"),
        };
        return view is CustomView custom && _toasts.TryGetValue(id, out var toast)
            ? custom with { Header = toast.Text }
            : view;
    }

    private ControllerView BattleScreen(ProfileId profile)
    {
        var state = State;
        if (UnitOf(profile) is not { } unit || !state.Units.TryGetValue(unit, out var me))
            return new WaitView("You're not in this battle.");
        if (state.Pending is ReactionOffer offer && offer.Reactor == unit && IsPlayed(unit))
            return Custom(Screens.React, new
            {
                unit,
                trigger = offer.Trigger,
                cause = offer.Cause,
                options = offer.Options.Select(o => new
                {
                    id = o.Id,
                    label = o.Ability is { } a ? Rules.Abilities.GetValueOrDefault(a)?.Name ?? a.Value : "Don't react",
                    target = o.Target,
                }),
            }) with { DeadlineUtc = _ctx.TimerDeadline(Timers.React) };
        if (state.Active == unit && state.Pending is null && IsPlayed(unit) && _aiFinishing != unit)
            return Custom(Screens.Turn, new { me = UnitViewOf(me), choices = Choices.For(state, unit, Rules) })
                with { DeadlineUtc = _ctx.TimerDeadline(Timers.Turn) };
        return Custom(Screens.Status, new
        {
            me = UnitViewOf(me),
            active = state.Active is { } a2 && state.Units.TryGetValue(a2, out var au) ? au.Sheet.Name : null,
            upIn = UpIn(state, unit),
            aiPlaying = !IsPlayed(unit),
        });
    }

    /// <summary>How many turns until <paramref name="unit"/>'s, this round (exact: no initiative rolls), or null.</summary>
    private static int? UpIn(BattleState state, UnitId unit)
    {
        var at = state.Order.IndexOf(unit);
        return at > state.TurnIndex ? at - state.TurnIndex : null;
    }

    private static CustomView Custom(string screen, object data)
        => new(Id, screen, JsonSerializer.SerializeToElement(data, ArcJson.Options));

    private IEnumerable<string> Waiting()
        => _players.Where(p => p.Profile is { } pr && _awaited.Contains(pr)).Select(p => p.Name);

    private ImmutableArray<EncounterOption> EncounterOptions()
        => [.. Rules.Encounters.Values.OrderBy(e => e.Level ?? 0).ThenBy(e => e.Id, StringComparer.Ordinal)
            .Select(e => new EncounterOption(e.Id, e.Name, e.Level, e.Party.Min, e.Party.Max,
                _players.Count >= e.Party.Min && _players.Count <= e.Party.Max))];

    private ImmutableArray<ResultLine> ResultLines()
    {
        if (_result is null || _doc.Battle is not null && _phase != TacticsPhase.Results)
            return [];
        return [.. _players.Select((p, i) =>
        {
            var unit = UnitFor(i);
            return new ResultLine(p.Name, _result.Xp.GetValueOrDefault(unit),
                [.. _result.Injuries.GetValueOrDefault(unit, []).Select(inj => Rules.Injuries.GetValueOrDefault(inj)?.Name ?? inj.Value)]);
        })];
    }

    private BattleView BattleViewOf()
    {
        var save = Save_;
        var state = save.State;
        return new BattleView(save.Encounter, state.MapId, state.Round, state.Order, state.Active,
            [.. state.Units.Values.Select(UnitViewOf)], save.Log.IsDefault ? [] : save.Log, state.Outcome?.ToString());
    }

    private UnitView UnitViewOf(UnitState u)
    {
        var player = u.Controller is PlayerControlled { Owner: var owner }
            ? _players.FirstOrDefault(p => p.Profile == owner)?.Name
            : null;
        return new UnitView(u.Id, u.Sheet.Name, u.Sheet.Side.ToString(), u.Sheet.Sprite, u.Pos, u.Hp, u.Sheet.MaxHp, u.TempHp,
            u.Life.ToString(), u.Countdown, [.. u.Conditions.Select(c => c.Id.Value)], player);
    }

    private string Banner()
    {
        switch (_phase)
        {
            case TacticsPhase.Muster:
                return "Choose your classes on your phones";
            case TacticsPhase.EncounterPick:
                return $"{_players.FirstOrDefault(p => p.IsVip)?.Name ?? "The host"} is choosing a battle";
            case TacticsPhase.Results:
                return _result?.Outcome == BattleOutcome.Won ? $"Victory at {_resultEncounter}!" : $"Defeat at {_resultEncounter}";
            case TacticsPhase.Resuming:
                return "Resuming the battle…";
        }
        var state = State;
        if (state.Pending is ReactionOffer offer)
            return $"{state.Units[offer.Reactor].Sheet.Name} can react";
        if (state.Active is not { } active)
            return "";
        var unit = state.Units[active];
        return IsPlayed(active) && _aiFinishing != active
            ? $"{_players.FirstOrDefault(p => p.Profile == ((PlayerControlled)unit.Controller).Owner)?.Name ?? unit.Sheet.Name}'s turn"
            : $"{unit.Sheet.Name} is acting";
    }
}
