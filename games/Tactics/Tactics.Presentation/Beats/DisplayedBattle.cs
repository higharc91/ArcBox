using System.Collections.Immutable;
using Tactics.Game;
using Tactics.Logic;

namespace Tactics.Presentation.Beats;

/// <summary>
/// The battle as the player has actually seen it. The view's own units and board are already past the beat being played,
/// so what's drawn starts from the last whole view and takes each beat's events as they play. It's a
/// <see cref="BattleView"/>, so <c>UnitPlan</c> and <c>BoardTerrain</c> draw it as they would the real thing.
/// </summary>
public static class DisplayedBattle
{
    private const string Up = "Up";

    /// <summary>
    /// <paramref name="shown"/> after <paramref name="e"/>. An event this doesn't know changes nothing.
    /// <paramref name="live"/> is the view as it stands now, for the few things an event doesn't carry: what a summon is
    /// like, the height and depth of an altered tile. It's only asked about what the event names, so nothing else gets ahead.
    /// </summary>
    public static BattleView Apply(BattleView shown, BattleEvent e, BattleView live) => e switch
    {
        Moved moved => Change(shown, moved.Unit, u => u with { At = moved.Path[^1] }),
        Pushed pushed => Change(shown, pushed.Unit, u => u with { At = pushed.To }),
        Teleported port => Change(shown, port.Unit, u => u with { At = port.To }),
        Damaged hit => Change(shown, hit.Unit, u => u with
        {
            Hp = hit.HpAfter,
            TempHp = Math.Max(0, u.TempHp - (hit.Amount - (u.Hp - hit.HpAfter))),
        }),
        Healed healed => Change(shown, healed.Unit, u => u with { Hp = healed.HpAfter }),
        TempHpGained temp => Change(shown, temp.Unit, u => u with { TempHp = temp.TempHp }),
        ConditionApplied gained => Change(shown, gained.Unit, u => u.Conditions.Contains(gained.Condition.Value) ? u
            : u with { Conditions = u.Conditions.Add(gained.Condition.Value) }),
        ConditionRemoved lost => Change(shown, lost.Unit, u => u with { Conditions = u.Conditions.Remove(lost.Condition.Value) }),
        Downed down => Change(shown, down.Unit, u => u with { Life = nameof(LifeState.Downed), Hp = 0, TempHp = 0, Countdown = DownedRules.StartingCountdown }),
        CountdownTicked tick => Change(shown, tick.Unit, u => u with { Countdown = tick.Left }),
        Revived back => Change(shown, back.Unit, u => u with { Life = Up, Hp = back.Hp, Countdown = 0 }),
        UnitOut gone => Change(shown, gone.Unit, u => u with { Life = nameof(LifeState.Out), Hp = 0, TempHp = 0, Countdown = 0 }),
        Died dead => Change(shown, dead.Unit, u => u with { Life = nameof(LifeState.Dead), Hp = 0, TempHp = 0, Countdown = 0 }),
        Spawned spawned => Arrive(shown, spawned, live),
        Despawned left => shown with { Units = shown.Units.RemoveAll(u => u.Id == left.Unit) },
        Transformed form => Change(shown, form.Unit, u => u with { Sprite = SpriteOf(form, u, live) }),
        RoundStarted round => shown with { Round = round.Round, Order = round.Order },
        TurnOrderChanged order => shown with { Order = order.Order },
        TurnStarted turn => shown with { Active = turn.Unit, Round = turn.Round },
        TurnEnded or BattleEnded => shown with { Active = null },
        ZoneCreated zone => WithBoard(shown, b => b with { Zones = b.Zones.Add(new ZoneView(zone.Zone.Serial, zone.Zone.Kind, zone.Zone.Tiles)) }),
        ZoneEnded zone => WithBoard(shown, b => b with { Zones = b.Zones.RemoveAll(z => z.Serial == zone.Serial) }),
        TerrainAltered altered => WithBoard(shown, b => Altered(b, altered.Tiles, live.Board ?? BoardChanges.None)),
        ObjectiveChanged changed => shown with { Objectives = changed.Objectives },
        _ => shown,
    };

    /// <summary>True when two boards show the same thing: a new view is a new object every time, and rebuilding the board's plan isn't free.</summary>
    public static bool SameBoard(BoardChanges? a, BoardChanges? b)
    {
        var (x, y) = (a ?? BoardChanges.None, b ?? BoardChanges.None);
        return x.Heights.OrderBy(h => h.At).SequenceEqual(y.Heights.OrderBy(h => h.At)) && x.Sunk.Order().SequenceEqual(y.Sunk.Order())
            && x.Zones.Length == y.Zones.Length
            && x.Zones.OrderBy(z => z.Serial).Zip(y.Zones.OrderBy(z => z.Serial))
                .All(z => z.First.Serial == z.Second.Serial && z.First.Kind == z.Second.Kind && z.First.Tiles.SequenceEqual(z.Second.Tiles));
    }

    private static BattleView Change(BattleView view, UnitId id, Func<UnitView, UnitView> change)
    {
        var i = view.Units.Select(u => u.Id).ToList().IndexOf(id);
        return i < 0 ? view : view with { Units = view.Units.SetItem(i, change(view.Units[i])) };
    }

    private static BattleView WithBoard(BattleView view, Func<BoardChanges, BoardChanges> change)
        => view with { Board = change(view.Board ?? BoardChanges.None) };

    private static BattleView Arrive(BattleView shown, Spawned spawned, BattleView live)
    {
        if (live.Units.FirstOrDefault(u => u.Id == spawned.Unit) is not { } unit)
            return shown;
        return shown with { Units = [.. shown.Units.Where(u => u.Id != unit.Id), unit with { At = spawned.At, Life = Up }] };
    }

    /// <summary>A form with no sprite of its own is named instead, and then the view's key is the way to know what to draw.</summary>
    private static string? SpriteOf(Transformed form, UnitView unit, BattleView live)
        => form.Into.Contains('.', StringComparison.Ordinal) ? form.Into
            : live.Units.FirstOrDefault(u => u.Id == unit.Id)?.Sprite ?? unit.Sprite;

    /// <summary>The event only says the delta asked for. What a tile came to after the 0–9 limit, and whether it sank, is in the view.</summary>
    private static BoardChanges Altered(BoardChanges board, ImmutableArray<Tile> tiles, BoardChanges live) => board with
    {
        Heights = [.. board.Heights.Where(h => !tiles.Contains(h.At)), .. live.Heights.Where(h => tiles.Contains(h.At))],
        Sunk = [.. board.Sunk.Where(t => !tiles.Contains(t)), .. live.Sunk.Where(tiles.Contains)],
    };
}
