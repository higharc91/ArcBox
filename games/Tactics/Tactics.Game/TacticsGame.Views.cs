using System.Collections.Immutable;
using System.Text.Json;
using ArcBox.Sdk;
using Tactics.Logic;

namespace Tactics.Game;

/// <summary>A unit as the TV and phones draw it.</summary>
public sealed record UnitView(UnitId Id, string Name, string Side, string? Sprite, Tile At, int Hp, int MaxHp, int TempHp,
    string Life, int Countdown, ImmutableArray<string> Conditions, string? Player);

/// <summary>
/// An ability on the phone's turn screen. <see cref="Targets"/> are the targets that affect someone (with their
/// previews); <see cref="Tiles"/> are the other legal aim tiles, bare, since a tile-aimed area spell can have hundreds
/// and each would otherwise repeat an empty preview (adapter spec §8: the view stays under 64 KB).
/// </summary>
public sealed record PhoneAbility(AbilityId Ability, ItemId? Item, string Name, string Tooltip, CostDef Cost,
    ImmutableArray<TargetChoice> Targets, ImmutableArray<Tile> Tiles, string Aim = "unit");

/// <summary><see cref="TurnChoices"/> as the phone gets them, abilities compacted (<see cref="PhoneAbility"/>).</summary>
public sealed record PhoneChoices(UnitId Unit, TurnEconomy Economy, ImmutableArray<MoveChoice> Moves,
    ImmutableArray<PhoneAbility> Abilities, bool CanDelay)
{
    /// <summary>
    /// Compacts <paramref name="choices"/>: a tile target that affects no one becomes a bare tile. Each ability says how
    /// it's aimed (<see cref="AimOf"/>), so the phone can say "Pick a direction" for a cone.
    /// </summary>
    public static PhoneChoices Of(TurnChoices choices, RulesPack? rules = null) => new(choices.Unit, choices.Economy, choices.Moves,
        [.. choices.Abilities.Select(a =>
        {
            bool Bare(TargetChoice t) => t.Target.Unit is null && t.Target.Tile == t.Aim && t.Affected.IsEmpty;
            var def = rules is null ? null
                : a.Item is { } item && rules.Items.TryGetValue(item, out var it) ? rules.Abilities.GetValueOrDefault(it.Use)
                : rules.Abilities.GetValueOrDefault(a.Ability);
            return new PhoneAbility(a.Ability, a.Item, a.Name, a.Tooltip, a.Cost,
                [.. a.Targets.Where(t => !Bare(t))], [.. a.Targets.Where(Bare).Select(t => t.Aim)], AimOf(def));
        })],
        choices.CanDelay);

    /// <summary>"self", "direction" (a line or cone from the user), "tile" or "unit".</summary>
    public static string AimOf(AbilityDef? ability) => ability?.Target switch
    {
        null => "unit",
        { Kind: TargetKind.Self, Area.Shape: AreaShape.Line or AreaShape.Cone } => "direction",
        { Kind: TargetKind.Self } or { Area.Shape: AreaShape.AllAllies or AreaShape.AllEnemies } => "self",
        { Kind: TargetKind.Tile or TargetKind.EmptyTile } => "tile",
        _ => "unit",
    };
}

/// <summary>A lasting area effect on the map, as the TV draws it.</summary>
public sealed record ZoneView(int Serial, string Kind, ImmutableArray<Tile> Tiles);

/// <summary>A tile's height, changed from the map's by <paramref name="Delta"/> steps.</summary>
public sealed record HeightChange(Tile At, int Delta);

/// <summary>What the battle has done to the map so far. The TV's alone: phones don't get it.</summary>
/// <param name="Zones">The zones on the map (<see cref="BattleState.Zones"/>).</param>
/// <param name="Heights">The changed heights (<see cref="BattleState.HeightDelta"/>).</param>
/// <param name="Sunk">The tiles that became deep (<see cref="BattleState.Sunk"/>).</param>
public sealed record BoardChanges(ImmutableArray<ZoneView> Zones, ImmutableArray<HeightChange> Heights, ImmutableArray<Tile> Sunk)
{
    /// <summary>An untouched map.</summary>
    public static BoardChanges None { get; } = new([], [], []);

    /// <summary>The changes in <paramref name="state"/>.</summary>
    public static BoardChanges Of(BattleState state) => new(
        [.. state.Zones.Select(z => new ZoneView(z.Serial, z.Kind, z.Tiles))],
        [.. state.HeightDelta.Select(h => new HeightChange(h.Key, h.Value))],
        state.Sunk is { } sunk ? [.. sunk] : []);
}

/// <summary>
/// The sprite key the TV gets for a unit. A character's sheet has no sprite, since a record says nothing about looks yet, so
/// it's drawn as its class's preset; a monster or a wild shape has its own. Phones don't get this: their views are unchanged.
/// </summary>
public static class HostSprite
{
    /// <summary>The key for a sheet with this sprite and these tags, or null if it has neither a sprite nor a class.</summary>
    public static string? Of(string? sprite, IEnumerable<string> tags)
        => sprite ?? tags.FirstOrDefault(t => t.StartsWith(SheetBuilder.ClassTag, StringComparison.Ordinal)) switch
        {
            { } tag => $"characters.class_{tag[SheetBuilder.ClassTag.Length..]}",
            null => null,
        };
}

/// <summary>The battle as the TV draws it (adapter spec §8).</summary>
public sealed record BattleView(string Encounter, string Map, int Round, ImmutableArray<UnitId> Order, UnitId? Active,
    ImmutableArray<UnitView> Units, ImmutableArray<Beat> Beats, string? Outcome, BoardChanges? Board = null);

/// <summary>An encounter the VIP can pick, with how many AI companions would fill the party up to its minimum.</summary>
public sealed record EncounterOption(string Id, string Name, int? Level, int Min, int Max, bool Fits, int Companions = 0);

/// <summary>A class a member can pick at Muster.</summary>
public sealed record ClassOption(string Id, string Name, string Role);

/// <summary>A member's line on the results screen.</summary>
public sealed record ResultLine(string Name, int Xp, ImmutableArray<string> Injuries);

/// <summary>The TV's view of a tactics session (adapter spec §8).</summary>
/// <param name="Phase">The phase.</param>
/// <param name="Banner">The prompt banner.</param>
/// <param name="Notice">A note for everyone, or null.</param>
/// <param name="PartyLevel">The party's level.</param>
/// <param name="Mustered">Who has a character.</param>
/// <param name="Encounters">The list at Encounter pick.</param>
/// <param name="Battle">The battle in progress.</param>
/// <param name="Outcome">How the last battle ended.</param>
/// <param name="Results">The results' lines.</param>
/// <param name="LastBattle">
/// At Results, the battle as it ended. The blow that ends a battle goes straight to Results, so no <paramref name="Battle"/>
/// ever carries it: without this the TV could never play it.
/// </param>
public sealed record TacticsHostView(string Phase, string Banner, string? Notice, int PartyLevel,
    ImmutableArray<string> Mustered, ImmutableArray<EncounterOption>? Encounters, BattleView? Battle,
    string? Outcome, ImmutableArray<ResultLine>? Results, BattleView? LastBattle = null);

public sealed partial class TacticsGame
{
    /// <inheritdoc />
    public object HostView() => new TacticsHostView(_phase.ToString(), Banner(), _notice, _doc.PartyLevel,
        [.. _players.Where(p => _doc.For(p.Profile!.Value) is not null).Select(p => p.Name)],
        _phase == TacticsPhase.EncounterPick ? EncounterOptions() : null,
        _doc.Battle is not null && _phase is TacticsPhase.Battle or TacticsPhase.Resuming ? BattleViewOf() : null,
        _result?.Outcome.ToString(), _phase == TacticsPhase.Results ? ResultLines() : null,
        _phase == TacticsPhase.Results ? _lastBattle : null);

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
                portal = PortalPath(),
            }),
            TacticsPhase.Muster => new WaitView("Waiting for everyone to choose a class"),
            TacticsPhase.EncounterPick when IsVip(player) => Custom(Screens.PickEncounter, new { encounters = EncounterOptions() }),
            TacticsPhase.EncounterPick => new WaitView($"Waiting for {Vip?.Name ?? "the host"} to choose a battle"),
            TacticsPhase.Battle => BattleScreen(profile),
            TacticsPhase.Results => Custom(Screens.Results, new
            {
                outcome = _result?.Outcome.ToString(), encounter = _resultEncounter, lines = ResultLines(), vip = IsVip(player),
                level = _doc.PartyLevel, portal = PortalPath(),
            }),
            TacticsPhase.Resuming when IsVip(player) => new ButtonsView(
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
                // Who is doing what to whom, in words, so a 10 s decision doesn't start with working it out.
                actor = offer.Cause is { } c && state.Units.TryGetValue(c.Actor, out var actor) ? actor.Sheet.Name : null,
                targetName = offer.Cause?.Target is { } t && state.Units.TryGetValue(t, out var tu) ? (t == unit ? "you" : tu.Sheet.Name) : null,
                ability = offer.Cause?.Ability is { } ca ? Rules.Abilities.GetValueOrDefault(ca)?.Name : null,
                options = offer.Options.Select(o => new
                {
                    id = o.Id,
                    label = o.Ability is { } a ? Rules.Abilities.GetValueOrDefault(a)?.Name ?? a.Value : "Don't react",
                    target = o.Target,
                    tooltip = o.Ability is { } ta && Rules.Abilities.TryGetValue(ta, out var def) ? Tooltips.For(def, me.Sheet.Level, Rules) : null,
                }),
            }) with { DeadlineUtc = _ctx.TimerDeadline(Timers.React) };
        if (state.Active == unit && state.Pending is null && IsPlayed(unit) && _aiFinishing != unit)
            return Custom(Screens.Turn, new
            {
                me = UnitViewOf(me),
                map = state.MapId,
                units = state.Units.Values.Where(DownedRules.InBattle).Select(UnitViewOf),
                choices = PhoneChoices.Of(Choices.For(state, unit, Rules), Rules),
            })
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

    /// <summary>The campaign portal's path (<c>/c/&lt;code&gt;</c>), or null outside a real campaign.</summary>
    private string? PortalPath() => _lease.Info.Id == "local" ? null : $"/c/{_lease.Info.Code}";

    private static CustomView Custom(string screen, object data)
        => new(Id, screen, JsonSerializer.SerializeToElement(data, ArcJson.Options));

    private IEnumerable<string> Waiting()
        => _players.Where(p => p.Profile is { } pr && _awaited.Contains(pr)).Select(p => p.Name);

    private ImmutableArray<EncounterOption> EncounterOptions()
        => [.. Rules.Encounters.Values.OrderBy(e => e.Level ?? 0).ThenBy(e => e.Id, StringComparer.Ordinal)
            .Select(e => new EncounterOption(e.Id, e.Name, e.Level, e.Party.Min, e.Party.Max,
                _players.Count <= e.Party.Max, Math.Max(0, e.Party.Min - _players.Count)))];

    private ImmutableArray<ResultLine> ResultLines()
    {
        if (_result is null || _doc.Battle is not null && _phase != TacticsPhase.Results)
            return [];
        return [.. _resultMembers.Select((profile, i) =>
        {
            var unit = UnitFor(i);
            var name = _players.FirstOrDefault(p => p.Profile == profile)?.Name ?? _doc.For(profile)?.Character.Name ?? unit.Value;
            return new ResultLine(name, _result.Xp.GetValueOrDefault(unit),
                [.. _result.Injuries.GetValueOrDefault(unit, []).Select(inj => Rules.Injuries.GetValueOrDefault(inj)?.Name ?? inj.Value)]);
        })];
    }

    private BattleView BattleViewOf()
    {
        var save = Save_;
        var state = save.State;
        return new BattleView(save.Encounter, state.MapId, state.Round, state.Order, state.Active,
            [.. state.Units.Values.Select(u => UnitViewOf(u) with { Sprite = HostSprite.Of(u.Sheet.Sprite, u.Sheet.Tags) })],
            save.Log.IsDefault ? [] : save.Log, state.Outcome?.ToString(), BoardChanges.Of(state));
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
                return $"{Vip?.Name ?? "The host"} is choosing a battle";
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
