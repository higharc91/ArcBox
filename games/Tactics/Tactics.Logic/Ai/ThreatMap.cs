using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>
/// The threat map (spec §5.2): for each tile, the damage the units hostile to one side are expected to deal to a unit of
/// that side standing there next round. Each hostile unit that can act walks its full Move (<see cref="Paths.From"/>, its
/// allies passable, everyone else blocking) and from any tile it reaches strikes with its best option that reaches the
/// tile: the Attack action (melee reach, or its weapon's range plus high ground) or a damaging ability it can pay for
/// (its range). Expected damage is rated against a typical unit of the side (the middle AC). Sight lines are ignored.
/// </summary>
public sealed class ThreatMap
{
    private readonly int _height;
    private readonly int _width;
    private readonly double[] _total;
    private readonly ImmutableArray<Attacker> _attackers;

    private ThreatMap(int width, int height, double[] total, ImmutableArray<Attacker> attackers)
    {
        _width = width;
        _height = height;
        _total = total;
        _attackers = attackers;
    }

    /// <summary>The threat against <paramref name="against"/> in <paramref name="s"/>.</summary>
    public static ThreatMap For(BattleState s, Side against, RulesPack rules)
    {
        var scorer = new Scorer(s, rules);
        return Build(scorer, against, scorer.Victim(against));
    }

    /// <summary>The expected damage onto <paramref name="t"/> next round; 0 outside the map.</summary>
    public double At(Tile t) => Index(t) is var i && i >= 0 ? _total[i] : 0;

    /// <summary>
    /// What each hostile unit threatens on <paramref name="t"/>: the unit, where it stands and the option it would use.
    /// </summary>
    internal IEnumerable<(UnitState Unit, ThreatOption Option)> Sources(Tile t)
    {
        var i = Index(t);
        if (i < 0)
            yield break;
        foreach (var attacker in _attackers)
            if (attacker.Best[i] >= 0)
                yield return (attacker.Unit, attacker.Options[attacker.Best[i]]);
    }

    private int Index(Tile t) => t.X >= 0 && t.Y >= 0 && t.X < _width && t.Y < _height ? t.X * _height + t.Y : -1;

    /// <summary>
    /// The threat against <paramref name="against"/>, rated on <paramref name="victim"/> (none: no threat at all).
    /// <paramref name="mover"/>, the unit planning its turn, doesn't stand in the foes' way: it is about to move.
    /// </summary>
    internal static ThreatMap Build(Scorer scorer, Side against, UnitState? victim, UnitId? mover = null)
    {
        var terrain = scorer.Terrain;
        var map = terrain.Map;
        var size = map.Width * map.Height;
        var total = new double[size];
        var attackers = ImmutableArray.CreateBuilder<Attacker>();
        var units = scorer.State.Units.Values;
        foreach (var unit in units)
        {
            if (victim is null || unit.Sheet.Side == against || !scorer.CanAct(unit))
                continue;
            var options = scorer.Options(unit, victim).Where(o => o.Ev > 0).ToImmutableArray();
            if (options.IsEmpty)
                continue;

            var walker = new Mover(Modifiers.Move(unit.Sheet, scorer.Mods(unit)), unit.Sheet.Jump,
                [.. units.Where(u => u.Id != unit.Id && DownedRules.InBattle(u) && u.Sheet.Side == unit.Sheet.Side).Select(u => u.Pos)],
                [.. units.Where(u => u.Id != mover && DownedRules.InBattle(u) && u.Sheet.Side != unit.Sheet.Side).Select(u => u.Pos)]);
            var from = Paths.From(terrain, unit.Pos, walker).Reachable;

            var best = new int[size];
            Array.Fill(best, -1);
            for (var o = 0; o < options.Length; o++)
            {
                var option = options[o];
                foreach (var r in from)
                {
                    var reach = option.Melee ? (option.Reach ? 2 : 1) : option.Range + RangeSlack;
                    for (var dx = -reach; dx <= reach; dx++)
                    for (var dy = -reach + Math.Abs(dx); dy <= reach - Math.Abs(dx); dy++)
                    {
                        var t = new Tile(r.X + dx, r.Y + dy);
                        if (!map.InBounds(t) || (dx == 0 && dy == 0 && !option.Melee))
                            continue;
                        var i = t.X * map.Height + t.Y;
                        if (best[i] >= 0 && options[best[i]].Ev >= option.Ev)
                            continue;
                        if (Strikes(terrain, option, r, t))
                            best[i] = o;
                    }
                }
            }

            for (var i = 0; i < size; i++)
                if (best[i] >= 0)
                    total[i] += options[best[i]].Ev;
            attackers.Add(new Attacker(unit, options, best));
        }
        return new ThreatMap(map.Width, map.Height, total, attackers.ToImmutable());
    }

    // The most high ground can add to a ranged option's range (⌊9 levels / 2⌋).
    private const int RangeSlack = 4;

    private static bool Strikes(Terrain terrain, ThreatOption option, Tile from, Tile to)
    {
        if (option.Melee)
            return from != to && Reach.Melee(terrain, from, to, option.Reach);
        var bonus = option.Range > 1 ? Reach.RangeBonus(terrain, from, to) : 0;
        return Reach.Distance(from, to) <= option.Range + bonus;
    }

    private sealed record Attacker(UnitState Unit, ImmutableArray<ThreatOption> Options, int[] Best);
}
