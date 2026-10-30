using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace Tactics.Logic;

/// <summary>
/// The one-ply turn planner behind <see cref="Ai.Plan"/> (spec §5.2):
/// <list type="number">
/// <item>The tiles the unit can reach with its Move left (<see cref="Paths.From"/>).</item>
/// <item>Each tile's score on position alone (<see cref="Position"/>); the best 12 plus the current tile are kept.</item>
/// <item>From each kept tile, each ability it can pay for on each legal target or aim (an area aim only on tiles in range
/// that cover a unit it is meant for): "move, then act", scored as what the action is worth plus the tile, less the
/// resources it spends (hoard).</item>
/// <item>"Act from here, then move to the best tile the Move left reaches", for actions that leave the map as it is;
/// "just move"; and Dash then move.</item>
/// <item>The best bonus action, added greedily where the turn ends.</item>
/// </list>
/// Ties break on the ordinal (ability id, tile, target). Every command of the chosen plan is checked with
/// <see cref="Battle.Check"/> against the state the plan expects at that point; a plan that fails is passed over.
/// </summary>
internal sealed partial class Planner
{
    private const int KeptTiles = 12;
    private const double Epsilon = 1e-6;

    private readonly BattleState _s;
    private readonly RulesPack _rules;
    private readonly UnitState _me;
    private readonly AiProfile _p;
    private readonly AiOptions _options;
    private readonly Scorer _scorer;
    private readonly bool _flee;
    private readonly ThreatMap _threat;
    private readonly PathMap _paths;
    private readonly Dictionary<Tile, Spot> _spots = [];
    private readonly ImmutableArray<UnitState> _foes;
    private readonly ImmutableArray<UnitState> _reactors;
    private readonly int[] _walk;
    private readonly int _reach;
    private readonly int _move;

    /// <summary>
    /// How much the threat map still weighs: fully for the first 5 rounds, then 10% less each round down to 30%, so two
    /// careful sides that keep out of each other's reach don't stall the battle for good.
    /// </summary>
    private readonly double _wariness;
    private readonly ImmutableArray<Usable> _usables;
    private readonly Dictionary<(AbilityId, ItemId?, Tile), double> _aimValues = [];
    private readonly Dictionary<Tile, (BattleState State, BattleWriter W)> _hypo = [];

    private Planner(BattleState s, UnitState me, RulesPack rules, AiProfile profile, AiOptions options)
    {
        _s = s;
        _rules = rules;
        _me = me;
        _options = options;
        _scorer = new Scorer(s, rules, me.Id);
        _flee = profile.FleeBelowHp > 0 && me.Hp < profile.FleeBelowHp * me.Sheet.MaxHp;
        _p = _flee ? profile with { Damage = 0, Kill = 0, Control = 0, Caution = Math.Max(1, profile.Caution) * 2 } : profile;
        _threat = ThreatMap.Build(_scorer, me.Sheet.Side, me, me.Id);

        var units = s.Units.Values;
        _foes = [.. units.Where(u => u.Life == LifeState.Up && Scorer.Hostile(me, u) && u.Sheet.Side != Side.Neutral)];
        _reactors = [.. _foes.Where(f => f.Economy.Reaction && !f.Sheet.Weapon.Ranged && _scorer.CanAct(f))];
        var mover = Mover(me.Economy.MoveLeft);
        _paths = Paths.From(_scorer.Terrain, me.Pos, mover);
        _walk = WalkDistances();
        _usables = Usables();
        _move = Modifiers.Move(me.Sheet, _scorer.Mods(me));
        _wariness = _flee ? 1 : Math.Max(0.3, 1 - 0.1 * Math.Max(0, s.Round - 5));
        _reach = Math.Max(me.Sheet.Weapon.Ranged ? me.Sheet.Weapon.Range : 1,
            _usables.Where(u => Scorer.Deals(u.Ability.Effects) && u.Ability.Target.Kind != TargetKind.Self)
                .Select(u => u.Ability.Target.Range).DefaultIfEmpty(1).Max());
    }

    /// <summary>See <see cref="Ai.Plan"/>.</summary>
    public static TurnPlan Plan(BattleState s, UnitId unit, RulesPack rules, AiOptions? options)
    {
        if (s.Outcome is not null || s.Pending is not null || s.Active != unit || !s.Units.TryGetValue(unit, out var me))
            return new TurnPlan([], 0, $"{unit}: not its turn.");
        if (!Modifiers.CanAct(s, unit, rules))
            return new TurnPlan([new EndTurn(unit)], 0, $"{unit} can't act: end turn.");
        var profile = AiProfile.For(s, unit, rules, options?.ProfileOverride);
        return new Planner(s, me, rules, profile, options ?? new AiOptions()).Run();
    }

    // ---- The plan ---------------------------------------------------------------------------------------------------
    private TurnPlan Run()
    {
        foreach (var t in _paths.Reachable)
            _spots[t] = SpotAt(t, _paths.PathTo(t));

        var candidates = new List<Candidate>();
        var here = _me.Pos;
        var bonusEscape = _usables.Where(u => u.Escape && u.Phase == Phase.Bonus).FirstOrDefault(u => Affordable(_me, u));
        var actionEscape = _usables.Where(u => u.Escape && u.Phase == Phase.Main).FirstOrDefault(u => Affordable(_me, u));

        // Just move (or stay), escaping first if that beats the opportunity attacks.
        foreach (var (t, spot) in _spots)
        {
            var (score, escape) = Arrive(spot, bonusEscape ?? actionEscape);
            candidates.Add(new Candidate(Kind.Move, score, null, null, here, t, escape, 0, spot.Position));
        }

        // Move, then act.
        var kept = _spots.Values.OrderByDescending(s => Round(s.Score)).ThenBy(s => s.Tile).Take(KeptTiles)
            .Select(s => s.Tile).Append(here).Distinct().Order().ToArray();
        foreach (var t in kept)
        {
            var spot = _spots[t];
            var me = Moved(t);
            foreach (var usable in _usables.Where(u => u.Phase == Phase.Main && !u.Dash && Affordable(me, u)))
            {
                var hoard = Hoard(usable);
                var (arrive, escape) = Arrive(spot, usable.Ability.Cost.Bonus > 0 ? null : bonusEscape);
                foreach (var action in Actions(usable, t))
                    if (action.Value > Epsilon)
                        candidates.Add(new Candidate(Kind.MoveAct, arrive + action.Value - hoard, usable, action, t, t, escape,
                            action.Value - hoard, spot.Position));
            }
        }

        // Act here, then move with what is left (only when the action leaves the map as it is).
        var byScore = _spots.Values.OrderByDescending(s => Round(s.Score)).ThenBy(s => s.Tile).ToArray();
        foreach (var usable in _usables.Where(u => u.Phase == Phase.Main && !u.Dash && u.Stable && Affordable(_me, u)))
        {
            var hoard = Hoard(usable);
            var budget = _me.Economy.MoveLeft - usable.Ability.Cost.Move;
            var escapeAfter = usable.Ability.Cost.Bonus > 0 ? null : bonusEscape;
            Spot? best = null;
            var (bestArrive, bestEscape) = (double.NegativeInfinity, (Usable?)null);
            foreach (var spot in byScore.Where(s => s.Cost <= budget))
            {
                var (arrive, escape) = Arrive(spot, escapeAfter);
                if (Round(arrive) > Round(bestArrive))
                    (best, bestArrive, bestEscape) = (spot, arrive, escape);
            }
            if (best is null)
                continue;
            foreach (var action in Actions(usable, here))
                if (action.Value > Epsilon)
                    candidates.Add(new Candidate(Kind.ActMove, bestArrive + action.Value - hoard, usable, action, here, best.Tile,
                        bestEscape, action.Value - hoard, best.Position));
        }

        // Dash, then move further.
        foreach (var dash in _usables.Where(u => u.Phase == Phase.Main && u.Dash && Affordable(_me, u)))
        {
            var extra = Modifiers.Move(_me.Sheet, _scorer.Mods(_me));
            var far = Paths.From(_scorer.Terrain, _me.Pos, Mover(_me.Economy.MoveLeft - dash.Ability.Cost.Move + extra));
            foreach (var t in far.Reachable.Where(t => !_spots.ContainsKey(t)))
            {
                var spot = SpotAt(t, far.PathTo(t));
                var (arrive, escape) = Arrive(spot, bonusEscape);
                candidates.Add(new Candidate(Kind.DashMove, arrive - Hoard(dash), dash, new Action(new Target(), here, [], 0), here, t,
                    escape, -Hoard(dash), spot.Position, spot.Path));
            }
        }

        candidates.Sort(Candidate.Order);
        foreach (var candidate in candidates)
            if (Commands(candidate) is { } commands)
                return Finish(candidate, commands, candidates);
        return new TurnPlan([new EndTurn(_me.Id)], 0, $"{_me.Id} ({_p.Id}): no legal plan; end turn.");
    }

    private enum Kind { MoveAct, ActMove, Move, DashMove }

    /// <summary>
    /// A whole-turn plan before it is checked: its shape, total score, the ability and target, the tile it acts from, the
    /// tile it ends on, a disengage to use before walking, what the action is worth less its cost, and the end tile's
    /// position score (for the explanation); a dash's path, which the ordinary path map doesn't have.
    /// </summary>
    private sealed record Candidate(Kind Kind, double Score, Usable? Usable, Action? Action, Tile ActTile, Tile End,
        Usable? Escape, double ActionValue, double EndPosition, ImmutableArray<Tile> Path = default)
    {
        /// <summary>Best score first; ties by ability id, then the tile it acts from, then the target, then the shape.</summary>
        public static int Order(Candidate a, Candidate b)
        {
            var c = Round(b.Score).CompareTo(Round(a.Score));
            if (c == 0)
                c = string.CompareOrdinal(a.AbilityKey, b.AbilityKey);
            if (c == 0)
                c = a.ActTile.CompareTo(b.ActTile);
            if (c == 0)
                c = string.CompareOrdinal(a.TargetKey, b.TargetKey);
            if (c == 0)
                c = a.Kind.CompareTo(b.Kind);
            if (c == 0)
                c = a.End.CompareTo(b.End);
            return c;
        }

        private string AbilityKey => Usable is null ? "" : Usable.Command.Value + "/" + Usable.Item?.Value;

        public string TargetKey => Action?.Target switch
        {
            { Unit: { } unit } => "u:" + unit.Value,
            { Tile: { } tile } => $"t:{tile.X:D3},{tile.Y:D3}",
            _ => "",
        };
    }

    /// <summary>Scores compared to a millionth, so sums that differ only by rounding still tie.</summary>
    private static long Round(double score) => (long)Math.Round(score * 1e6);
}
