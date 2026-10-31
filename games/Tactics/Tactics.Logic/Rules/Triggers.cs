using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>
/// Scripted encounter triggers (spec §4.8). Triggers are checked at every checkpoint: before the first step a command
/// resolves and after each step (so at round start, after each step and at turn end), in file order, while the battle goes
/// on and nothing is pending. Every condition is judged against the state as it is at the checkpoint; a trigger that fires
/// emits <see cref="TriggerFired"/> and queues its actions, in order, at the front of the queue
/// (<see cref="TriggerActionStep"/>), ahead of everything else. So a trigger's actions can't make another trigger fire at
/// the same checkpoint: what they change is seen at the next one. A <see cref="TriggerDef.Once"/> trigger stays in
/// <see cref="BattleState.FiredTriggers"/> for good; any other leaves it when its condition stops holding, and fires again
/// the next time the condition holds.
/// </summary>
public static class Triggers
{
    /// <summary>The checkpoint: fires every trigger whose condition holds now and hasn't fired (see <see cref="Triggers"/>).</summary>
    public static void Check(BattleWriter w, RulesPack rules)
    {
        var s = w.State;
        if (s.Triggers.IsDefaultOrEmpty || s.Outcome is not null || s.Pending is not null)
            return;
        var map = w.Terrain.Map;
        var fired = s.FiredTriggers;
        var steps = new List<Step>();
        foreach (var trigger in s.Triggers)
        {
            var holds = Holds(s, trigger.When, map);
            if (fired.Contains(trigger.Id))
            {
                if (!trigger.Once && !holds)
                    fired = fired.Remove(trigger.Id);
                continue;
            }
            if (!holds)
                continue;
            fired = fired.Add(trigger.Id);
            w.Emit(new TriggerFired(trigger.Id));
            steps.AddRange((trigger.Do.IsDefault ? [] : trigger.Do).Select(a => new TriggerActionStep(trigger.Id, a)));
        }
        if (!ReferenceEquals(fired, s.FiredTriggers))
            w.State = w.State with { FiredTriggers = fired };
        if (steps.Count > 0)
            w.PushFront([.. steps]);
    }

    /// <summary>
    /// Whether <paramref name="when"/> holds in <paramref name="state"/> on <paramref name="map"/> (the base map): every key
    /// present must hold (see <see cref="TriggerWhen"/>). A condition with no key never holds.
    /// </summary>
    public static bool Holds(BattleState state, TriggerWhen when, BattleMap map)
    {
        var any = false;
        bool Note(bool value)
        {
            any = true;
            return value;
        }

        if (!when.All.IsDefaultOrEmpty && !Note(when.All.All(w => Holds(state, w, map))))
            return false;
        if (!when.Any.IsDefaultOrEmpty && !Note(when.Any.Any(w => Holds(state, w, map))))
            return false;
        if (when.Round is { } round && !Note(state.Round == round))
            return false;
        if (when.UnitEntersZone is { } enters && !Note(InZone(state, enters, map)))
            return false;
        if (when.HpBelow is { } hp && !Note(Tagged(state, hp.Tag).Any(u => DownedRules.InBattle(u)
                                                                                && u.Hp * 100 < hp.Percent * u.Sheet.MaxHp)))
            return false;
        if (when.UnitDies is { } dies && !Note(Tagged(state, dies.Tag).Any(u => u.Life is LifeState.Dead or LifeState.Out)))
            return false;
        if (when.Flag is { } flag && !Note(state.Flags.Contains(flag)))
            return false;
        if (when.Counter is { } counter && !Note(state.Counters.GetValueOrDefault(counter.Name) >= counter.AtLeast))
            return false;
        if (when.Objective is { } objective
            && !Note(Objectives.Leaf(state, objective.Name, map) == (objective.State == ObjectiveStatus.Met)))
            return false;
        return any;
    }

    private static bool InZone(BattleState state, ZoneWatch watch, BattleMap map)
        => map.Zones.TryGetValue(watch.Zone, out var tiles)
           && Tagged(state, watch.Tag).Count(u => u.Life == LifeState.Up && tiles.Contains(u.Pos)) >= Math.Max(1, watch.Count);

    /// <summary>The units with <paramref name="tag"/> (<see cref="Objectives.HasTag"/>), in <see cref="UnitId"/> order.</summary>
    internal static IEnumerable<UnitState> Tagged(BattleState state, string tag)
        => state.Units.Values.Where(u => Objectives.HasTag(u, tag));

    // ---- Actions ----------------------------------------------------------------------------------------------------

    /// <summary>Runs one trigger action (see <see cref="TriggerAction"/>).</summary>
    internal static void Run(BattleWriter w, TriggerAction action, RulesPack rules)
    {
        if (action.Say is { } say)
            w.Emit(new Said(say.Speaker, say.Line));
        if (action.Spawn is { } spawn)
            Spawn(w, spawn, rules);
        if (action.SetFlag is { } flag && !w.State.Flags.Contains(flag))
            w.State = w.State with { Flags = w.State.Flags.Add(flag) };
        if (action.AddCounter is { } add)
            w.State = w.State with
            {
                Counters = w.State.Counters.SetItem(add.Name, w.State.Counters.GetValueOrDefault(add.Name) + add.Amount),
            };
        if (action.SetObjective is { } patch)
        {
            var current = w.State.Objectives.Current;
            var next = new ObjectivesDef(patch.Win ?? current.Win, patch.Lose ?? current.Lose);
            w.State = w.State with { Objectives = w.State.Objectives with { Current = next } };
            w.Emit(new ObjectiveChanged(next));
        }
        if (action.CameraHint is { } place && place.Tiles(w.Terrain.Map) is [var tile, ..])
            w.Emit(new CameraHint(tile));
        if (action.Phase is { } phase)
            EnterPhase(w, phase);
        if (!action.Effects.IsDefaultOrEmpty)
            QueueEffects(w, action);
        if (action.EndBattle is { } outcome)
            Objectives.End(w, outcome);
    }

    /// <summary>
    /// Brings <see cref="SpawnAction.Count"/> of the monster into the battle (fewer if the place has fewer free tiles): each
    /// on the next free tile of the place (<see cref="Motion.CanLand"/>), named <c>"&lt;monster&gt;.&lt;n&gt;"</c> with the
    /// lowest n not in use, on the enemy side (the party's for an ally, as a <see cref="UnitState.Helper"/>), AI-controlled,
    /// tagged, joining the turn order next round (<see cref="BattleState.Joining"/>). Emits <see cref="Spawned"/> for each. Throws
    /// <see cref="InvalidOperationException"/> when the rules pack has no such monster (the loader checks it).
    /// </summary>
    private static void Spawn(BattleWriter w, SpawnAction spawn, RulesPack rules)
    {
        var baseSheet = rules.MonsterSheet(spawn.Unit, spawn.Level)
                        ?? throw new InvalidOperationException($"There is no monster '{spawn.Unit}' to spawn.");
        var profile = spawn.AiProfile ?? baseSheet.AiProfile ?? "brute";
        var sheet = baseSheet with
        {
            Side = spawn.Ally ? Side.Party : Side.Enemy,
            Tags = baseSheet.Tags.WithComparer(StringComparer.Ordinal).Add(spawn.UnitTag),
            AiProfile = profile,
        };
        var placed = 0;
        foreach (var tile in spawn.At.Tiles(w.Terrain.Map))
        {
            if (placed >= spawn.Count)
                break;
            if (!w.Terrain.Map.InBounds(tile) || !Motion.CanLand(w, tile, default))
                continue;
            var id = FreeId(w.State, spawn.Unit);
            var unit = new UnitState(id, sheet, new AiControlled(profile), tile, sheet.MaxHp, TempHp: 0,
                sheet.Resources.WithComparers(StringComparer.Ordinal), Conditions: [], Economy.Fresh(sheet), LifeState.Up,
                Countdown: 0, UsesLeft: Ordinal.Map<int>(), Concentrating: null, Bag: [], ItemsUsedByAi: 0, SummonedBy: null,
                Protected: false, UsedThisTurn: Ordinal.Set(), UsedThisRound: Ordinal.Set()) { Helper = spawn.Ally };
            w.State = w.State with { Units = w.State.Units.Add(id, unit), Joining = w.State.Joining.Add(id) };
            w.Emit(new Spawned(id, tile, sheet.Sprite));
            placed++;
        }
    }

    private static UnitId FreeId(BattleState state, string monster)
    {
        for (var n = 1; ; n++)
        {
            var id = new UnitId($"{monster}.{n}");
            if (!state.Units.ContainsKey(id))
                return id;
        }
    }

    /// <summary>
    /// Every unit with the phase's tag still in the battle, in <see cref="UnitId"/> order, gains the phase's abilities and
    /// passives it hasn't got and, if the phase names one, its AI profile (an AI-controlled unit is then played by it);
    /// emits <see cref="PhaseEntered"/>. A transformed unit's own sheet changes too. Nothing happens without a resolved
    /// <see cref="PhaseAction.Def"/>.
    /// </summary>
    private static void EnterPhase(BattleWriter w, PhaseAction phase)
    {
        if (phase.Def is not { } def)
            return;
        foreach (var unit in Tagged(w.State, phase.Tag).Where(DownedRules.InBattle).ToArray())
        {
            var controller = def.AiProfile is { } profile && unit.Controller is AiControlled ? new AiControlled(profile) : unit.Controller;
            w.Set(unit with
            {
                Sheet = WithPhase(unit.Sheet, def),
                TrueSheet = unit.TrueSheet is { } own ? WithPhase(own, def) : null,
                Controller = controller,
            });
            w.Emit(new PhaseEntered(unit.Id, phase.Number));
        }
    }

    private static UnitSheet WithPhase(UnitSheet sheet, PhaseDef def)
    {
        var abilities = def.Abilities.IsDefault ? [] : def.Abilities;
        var passives = def.Passives.IsDefault ? [] : def.Passives;
        return sheet with
        {
            Abilities = sheet.Abilities.AddRange(abilities.Where(a => !sheet.Abilities.Contains(a))),
            Passives = sheet.Passives.AddRange(passives.Where(p => !sheet.Passives.Contains(p))),
            AiProfile = def.AiProfile ?? sheet.AiProfile,
        };
    }

    /// <summary>
    /// Queues the action's effects (one <see cref="RunEffectsStep"/> per run of consecutive blocks, as an ability does: a
    /// run of tile blocks once at an aim tile, a run of other blocks once on a unit), at hook depth 1 so they run whatever
    /// state their source is in:
    /// <list type="bullet">
    /// <item>with a <see cref="TriggerAction.Tag"/>: on each unit with the tag still in the battle (tile runs at its
    /// tile);</item>
    /// <item>with a <see cref="TriggerAction.Zone"/> or <see cref="TriggerAction.At"/>: tile runs at each of the place's
    /// tiles, unit runs on each unit in the battle standing on one.</item>
    /// </list>
    /// The source is the first unit in the battle with the tag <see cref="TriggerAction.Source"/>; without one, the unit the
    /// run lands on or, for a tile run, the first unit in the battle (the effects need a source for levels and DCs).
    /// </summary>
    private static void QueueEffects(BattleWriter w, TriggerAction action)
    {
        var s = w.State;
        var inBattle = s.Units.Values.Where(DownedRules.InBattle).ToArray();
        var source = action.Source is { } tag ? inBattle.FirstOrDefault(u => Objectives.HasTag(u, tag))?.Id : null;
        var fallback = source ?? inBattle.FirstOrDefault()?.Id;

        var runs = new List<(bool OnTile, ImmutableArray<EffectDef> Effects)>();
        var effects = action.Effects;
        for (var start = 0; start < effects.Length;)
        {
            var onTile = effects[start].OnTile;
            var end = start;
            while (end < effects.Length && effects[end].OnTile == onTile)
                end++;
            runs.Add((onTile, effects[start..end]));
            start = end;
        }

        var steps = new List<Step>();
        if (action.Tag is { } unitTag)
        {
            foreach (var unit in inBattle.Where(u => Objectives.HasTag(u, unitTag)))
                foreach (var (onTile, run) in runs)
                    steps.Add(new RunEffectsStep(source ?? unit.Id, onTile ? null : unit.Id, unit.Pos, null, run, HookDepth: 1));
        }
        else
        {
            var map = w.Terrain.Map;
            var tiles = action.Zone is { } zone ? map.Zones.TryGetValue(zone, out var zoneTiles) ? zoneTiles : []
                : action.At is { } at ? [at] : ImmutableArray<Tile>.Empty;
            foreach (var (onTile, run) in runs)
            {
                if (onTile)
                {
                    if (fallback is { } from)
                        steps.AddRange(tiles.Where(map.InBounds).Select(t => new RunEffectsStep(from, null, t, null, run, HookDepth: 1)));
                }
                else
                    foreach (var unit in inBattle.Where(u => tiles.Contains(u.Pos)))
                        steps.Add(new RunEffectsStep(source ?? unit.Id, unit.Id, unit.Pos, null, run, HookDepth: 1));
            }
        }
        if (steps.Count > 0)
            w.PushFront([.. steps]);
    }
}

/// <summary>Runs one action of the trigger <see cref="Trigger"/> (see <see cref="Triggers"/>).</summary>
public sealed record TriggerActionStep(string Trigger, TriggerAction Action) : Step
{
    /// <inheritdoc />
    public override void Run(BattleWriter w, RulesPack rules) => Triggers.Run(w, Action, rules);
}
