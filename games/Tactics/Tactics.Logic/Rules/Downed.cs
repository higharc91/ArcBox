namespace Tactics.Logic;

/// <summary>
/// Dropping to 0 HP, the Downed countdown, revival, going Out with an injury, and death (spec §3.8). Every way a unit can
/// reach 0 HP (damage, a deep tile) goes through <see cref="Drop"/>, and every unit that leaves the battle leaves the turn
/// order through it.
/// </summary>
public static class DownedRules
{
    /// <summary>A Downed unit's countdown when it drops.</summary>
    public const int StartingCountdown = 3;

    /// <summary>
    /// True when <paramref name="unit"/> follows the player rules at 0 HP and goes Downed instead of dying: a party member
    /// that isn't a summon, or an NPC marked <see cref="UnitState.Protected"/>.
    /// </summary>
    public static bool GoesDown(UnitState unit) => unit.Protected || (unit.Sheet.Side == Side.Party && unit.SummonedBy is null);

    /// <summary>True while <paramref name="unit"/> is in the battle (Up or Downed): it holds its tile and its place in the order.</summary>
    public static bool InBattle(UnitState unit) => unit.Life is LifeState.Up or LifeState.Downed;

    /// <summary>
    /// <paramref name="unit"/> has reached 0 HP, <paramref name="by"/> whoever dropped it, if anyone. Nothing happens unless
    /// it is Up. Its HP and temporary HP go to 0 and its concentration ends.
    /// <list type="bullet">
    /// <item>If it <see cref="GoesDown"/>: it is Downed with a countdown of <see cref="StartingCountdown"/> and knocked prone
    /// (<see cref="Downed"/>); its <see cref="HookKind.OnDowned"/> hooks fire (with <paramref name="by"/> as the other unit),
    /// then the <see cref="HookKind.OnAllyDowned"/> hooks of its standing allies in <see cref="UnitId"/> order.</item>
    /// <item>Otherwise it is Dead (<see cref="Died"/>), leaves the turn order and <paramref name="by"/>'s
    /// <see cref="HookKind.OnKill"/> hooks fire with it as the other unit.</item>
    /// </list>
    /// Either way it stops acting: the rest of its own abilities' effects and attacks still queued are dropped
    /// (<see cref="StopActing"/>), and a unit dropped during its own turn has its turn ended once what is still queued has
    /// resolved.
    /// </summary>
    public static void Drop(BattleWriter w, UnitId unit, UnitId? by)
    {
        var u = w.Unit(unit);
        if (u.Life != LifeState.Up)
            return;

        if (!GoesDown(u))
        {
            w.Set(u with { Hp = 0, TempHp = 0, Life = LifeState.Dead });
            w.Emit(new Died(unit));
            StopActing(w, unit);
            Conditions.BreakConcentration(w, unit);
            LeaveOrder(w, unit);
            if (by is { } killer)
                Hooks.Fire(w, HookKind.OnKill, new HookArgs(killer, unit), w.Rules);
            return;
        }

        w.Set(u with { Hp = 0, TempHp = 0, Life = LifeState.Downed, Countdown = StartingCountdown });
        w.Emit(new Downed(unit));
        StopActing(w, unit);
        Motion.KnockProne(w, unit);
        Conditions.BreakConcentration(w, unit);

        var hooks = new List<Step>();
        Hooks.Collect(w, HookKind.OnDowned, new HookArgs(unit, by), w.Rules, hooks);
        foreach (var ally in w.State.Units.Values.Where(a => a.Id != unit && a.Life == LifeState.Up && a.Sheet.Side == u.Sheet.Side))
            Hooks.Collect(w, HookKind.OnAllyDowned, new HookArgs(ally.Id, unit), w.Rules, hooks);
        if (hooks.Count > 0)
            w.PushFront([.. hooks]);
        EndTurnIfActive(w, unit);
    }

    /// <summary>
    /// A Downed <paramref name="unit"/> took a hit: its countdown drops by 1, or 2 for a <paramref name="crit"/>
    /// (<see cref="CountdownTicked"/>), and at 0 it goes Out. It takes no damage. A hit counts once however many damage
    /// effects it has: further damage in the same <paramref name="resolution"/> (see <see cref="BattleState.DownedHits"/>)
    /// costs nothing more. Damage outside any resolution (a fall, a collision) is a hit of its own.
    /// </summary>
    public static void Hit(BattleWriter w, UnitId unit, bool crit, int? resolution = null)
    {
        if (w.Unit(unit).Life != LifeState.Downed)
            return;
        if (resolution is { } id)
        {
            var mark = new HitMark(id, unit);
            if (w.State.DownedHits.Contains(mark))
                return;
            w.State = w.State with { DownedHits = w.State.DownedHits.Add(mark) };
        }
        Tick(w, unit, crit ? 2 : 1);
    }

    /// <summary>The start of a new round: every Downed unit's countdown drops by 1, in <see cref="UnitId"/> order.</summary>
    public static void TickRound(BattleWriter w)
    {
        foreach (var id in w.State.Units.Values.Where(u => u.Life == LifeState.Downed).Select(u => u.Id).ToArray())
            Tick(w, id, 1);
    }

    /// <summary>
    /// A Downed <paramref name="unit"/> gets up with <paramref name="hp"/> HP (at least 1, at most its max): its countdown
    /// clears and it stays prone. Emits <see cref="Revived"/>. Nothing happens unless it is Downed.
    /// </summary>
    public static void Revive(BattleWriter w, UnitId unit, int hp)
    {
        var u = w.Unit(unit);
        if (u.Life != LifeState.Downed)
            return;
        hp = Math.Clamp(hp, 1, u.Sheet.MaxHp);
        w.Set(u with { Life = LifeState.Up, Hp = hp, Countdown = 0 });
        w.Emit(new Revived(unit, hp));
    }

    /// <summary>
    /// A Downed <paramref name="unit"/>'s countdown has run out: it is Out. It gains an injury from
    /// <see cref="RulesPack.Injuries"/>, picked by weight with one roll of the battle's RNG (the table in
    /// <see cref="InjuryId"/> order), recorded in <see cref="BattleState.Injuries"/>; emits <see cref="UnitOut"/> and leaves
    /// the turn order.
    /// </summary>
    public static void GoOut(BattleWriter w, UnitId unit)
    {
        var injury = PickInjury(w);
        w.Set(w.Unit(unit) with { Life = LifeState.Out, Hp = 0, TempHp = 0, Countdown = 0 });
        w.State = w.State with { Injuries = w.State.Injuries.Add(new RecordedInjury(unit, injury)) };
        w.Emit(new UnitOut(unit, injury));
        Conditions.BreakConcentration(w, unit);
        LeaveOrder(w, unit);
    }

    /// <summary>
    /// Drops what <paramref name="unit"/>, no longer Up, was still doing: its queued ability effects and attacks (steps at
    /// hook depth 0 with it as the source). Effects of hooks (its <see cref="HookKind.OnDowned"/> passives and the like)
    /// still run. Its walk halts by itself (<see cref="MoveStep"/>), which also takes it off any ally's tile it was passing.
    /// </summary>
    private static void StopActing(BattleWriter w, UnitId unit)
        => w.State = w.State with
        {
            Queue = w.State.Queue.RemoveAll(step => (step is RunEffectsStep { HookDepth: 0 } run && run.Source == unit)
                                                    || (step is AttackStep { HookDepth: 0 } attack && attack.Source == unit)),
        };

    private static void Tick(BattleWriter w, UnitId unit, int by)
    {
        var u = w.Unit(unit);
        if (u.Life != LifeState.Downed)
            return;
        var left = Math.Max(0, u.Countdown - by);
        w.Set(u with { Countdown = left });
        w.Emit(new CountdownTicked(unit, left));
        if (left == 0)
            GoOut(w, unit);
    }

    private static InjuryId PickInjury(BattleWriter w)
    {
        var injuries = w.Rules.Injuries.Values;
        var total = injuries.Sum(i => Math.Max(0, i.Weight));
        if (total <= 0)
            throw new RulesInvariantException("The rules pack has no injury to give a unit going Out.");
        var roll = w.Roll(total);
        foreach (var injury in injuries)
        {
            roll -= Math.Max(0, injury.Weight);
            if (roll <= 0)
                return injury.Id;
        }
        throw new RulesInvariantException("The injury roll ran past the table.");   // unreachable: roll ≤ total
    }

    /// <summary>
    /// Takes <paramref name="unit"/>, which has left the battle, out of the turn order. A unit before the current turn
    /// shifts <see cref="BattleState.TurnIndex"/> down by one, so nobody's turn is repeated or skipped. A unit whose turn
    /// is under way keeps its place until its turn has ended (<see cref="StartNextTurnStep"/> removes it then).
    /// </summary>
    private static void LeaveOrder(BattleWriter w, UnitId unit)
    {
        var s = w.State;
        var index = s.Order.IndexOf(unit);
        if (index < 0)
            return;
        if (index == s.TurnIndex && s.StartedThisRound.Contains(unit))
        {
            EndTurnIfActive(w, unit);
            return;
        }
        w.State = s with { Order = s.Order.RemoveAt(index), TurnIndex = index < s.TurnIndex ? s.TurnIndex - 1 : s.TurnIndex };
    }

    /// <summary>
    /// If it is <paramref name="unit"/>'s turn and that turn isn't already ending, queues its end behind everything already
    /// queued, so the rest of what is resolving finishes first and no command is awaited from a unit that can't act.
    /// </summary>
    private static void EndTurnIfActive(BattleWriter w, UnitId unit)
    {
        var s = w.State;
        if (s.TurnIndex >= s.Order.Length || s.Order[s.TurnIndex] != unit || !s.StartedThisRound.Contains(unit))
            return;
        if (s.Queue.Any(step => step is StartNextTurnStep or SkipTurnStep || (step is EndTurnStep end && end.Unit == unit)))
            return;
        w.PushBack(new EndTurnStep(unit));
    }
}
