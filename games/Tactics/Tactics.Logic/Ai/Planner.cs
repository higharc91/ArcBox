using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace Tactics.Logic;

/// <summary>
/// The one-ply turn planner behind <see cref="Ai.Plan"/> (spec §5.2):
/// <list type="number">
/// <item>The tiles the unit can reach with its Move left (<see cref="Paths.From"/>).</item>
/// <item>Each tile's score on position alone (<see cref="Position"/>); the best 12 plus the current tile are kept, and for
/// the ranged and skirmisher profiles the best few it can strike a foe from (<see cref="StrikeTiles"/>).</item>
/// <item>From each kept tile, each ability it can pay for on each legal target or aim (an area aim only on tiles in range
/// that cover a unit it is meant for; a teleport or leap on the best landing tiles in reach, a summon beside its foes):
/// "move, then act", scored as what the action is worth plus the tile, less the resources it spends (hoard). For the
/// ranged and skirmisher profiles (<see cref="HitAndRun"/>), also "move, act, then move again" to the best tile the Move
/// left reaches, for actions that leave the map as it is.</item>
/// <item>"Act from here, then move to the best tile the Move left reaches", for actions that leave the map as it is;
/// "just move"; and Dash then move.</item>
/// <item>With a bonus action that teleports or leaps, "walk, teleport, act" to landing tiles near the best position tiles
/// and beside foes (<see cref="HopThenAct"/>); with a bonus-action Dash, "dash, walk further, act" (<see cref="DashThenAct"/>).</item>
/// <item>The best second action, after an extra action (Action Surge) when it is worth what the surge costs, then the best
/// bonus action, added greedily where the turn ends.</item>
/// </list>
/// With the Action already spent (a timed-out turn finishing what the player left), the bonus actions take the action's
/// place in steps 3 and 4, so the move goes where the bonus action is worth most, and a bonus-action Dash can lengthen the
/// move.
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
    private readonly bool _companion;
    private readonly bool _actionSpent;
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
        _companion = Companion.Is(profile);
        _actionSpent = !me.Economy.Action && me.Economy.ExtraActions <= 0;
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

        // Move, then act (a teleport or leap as the action ends where it lands); for the ranged and skirmisher profiles, then
        // move again with what is left.
        var kept = _spots.Values.OrderByDescending(s => Round(s.Score)).ThenBy(s => s.Tile).Take(KeptTiles)
            .Select(s => s.Tile).Append(here).Concat(HitAndRun ? StrikeTiles() : []).Distinct().Order().ToArray();
        foreach (var t in kept)
        {
            var spot = _spots[t];
            var me = Moved(t);
            foreach (var usable in _usables.Where(u => Leads(u) && Affordable(me, u)))
            {
                var hoard = Hoard(usable);
                var escapeFirst = usable.Ability.Cost.Bonus > 0 ? null : bonusEscape;
                var (arrive, escape) = Arrive(spot, escapeFirst);
                var away = HitAndRun && usable.Stable && t != here
                    ? Retreat(t, _me.Economy.MoveLeft - spot.Cost - usable.Ability.Cost.Move, escapeFirst)
                    : null;
                foreach (var action in Actions(usable, t))
                {
                    if (action.Value <= Epsilon || !ItemOk(usable, t, action))
                        continue;
                    var end = usable.Hop > 0 ? action.Aim : t;
                    candidates.Add(new Candidate(Kind.MoveAct, arrive + action.Value - hoard, usable, action, t, end, escape,
                        action.Value - hoard, usable.Hop > 0 ? Land(end) : spot.Position));
                    if (away is { } back && Round(back.Score) > Round(arrive))
                        candidates.Add(new Candidate(Kind.MoveActMove, back.Score + action.Value - hoard, usable, action, t,
                            back.Spot.Tile, back.Escape, action.Value - hoard, back.Spot.Position, back.Path));
                }
            }
        }

        // A bonus-action teleport or leap, then act where it lands; a bonus-action Dash, then walk further and act.
        if (!_actionSpent)
        {
            candidates.AddRange(HopThenAct());
            candidates.AddRange(DashThenAct());
        }

        // Act here, then move with what is left (only when the action leaves the map as it is).
        var byScore = _spots.Values.OrderByDescending(s => Round(s.Score)).ThenBy(s => s.Tile).ToArray();
        foreach (var usable in _usables.Where(u => Leads(u) && u.Stable && Affordable(_me, u)))
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
                if (action.Value > Epsilon && ItemOk(usable, here, action))
                    candidates.Add(new Candidate(Kind.ActMove, bestArrive + action.Value - hoard, usable, action, here, best.Tile,
                        bestEscape, action.Value - hoard, best.Position));
        }

        // Dash, then move further: with the action, or with the bonus action when the action is spent.
        foreach (var dash in _usables.Where(u => u.Dash && (u.Phase == Phase.Main || _actionSpent) && Affordable(_me, u)))
        {
            var far = Far(dash);
            foreach (var t in far.Reachable.Where(t => !_spots.ContainsKey(t)))
            {
                var spot = SpotAt(t, far.PathTo(t), escaped: dash.Escape);
                var (arrive, escape) = Arrive(spot, dash.Phase == Phase.Main ? bonusEscape : null);
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

    /// <summary>
    /// A plan's shape, in the order its commands run: <see cref="MoveAct"/> walk, act; <see cref="ActMove"/> act, walk;
    /// <see cref="Move"/> walk; <see cref="DashMove"/> dash, walk; <see cref="MoveActMove"/> walk, act, walk again;
    /// <see cref="HopAct"/> walk, teleport or leap (<see cref="Candidate.Lead"/>), act; <see cref="DashAct"/> bonus-action
    /// dash (<see cref="Candidate.Lead"/>), walk, act. Each may begin with a disengage. New shapes go last, so ties between
    /// the older ones still break as they did.
    /// </summary>
    private enum Kind { MoveAct, ActMove, Move, DashMove, MoveActMove, HopAct, DashAct }

    /// <summary>
    /// True for the abilities a turn is planned around: the action's (but not Dash, planned on its own), or with the Action
    /// spent, the bonus action's (but not an escape, which only helps a move). Never an extra action (Action Surge), which is
    /// worth the best second action and is planned after the first (<see cref="Commands"/>).
    /// </summary>
    private bool Leads(Usable u) => !u.Dash && !u.Surge && (u.Phase == Phase.Main || _actionSpent && !u.Escape);

    /// <summary>
    /// A whole-turn plan before it is checked: its shape, total score, the ability and target, the tile it acts from, the
    /// tile it ends on, a disengage to use before walking, what the action is worth less its cost, and the end tile's
    /// position score (for the explanation); the path of the walk the ordinary path map doesn't have (a dash's, or the
    /// walk after acting in move–act–move or before it in <see cref="Kind.DashAct"/>); the bonus action used before acting
    /// (<see cref="Lead"/>: a teleport or leap to the tile it acts from, or a dash) and the tile a teleport or leap is used
    /// from (<see cref="Via"/>, the unit's own if none).
    /// </summary>
    private sealed record Candidate(Kind Kind, double Score, Usable? Usable, Action? Action, Tile ActTile, Tile End,
        Usable? Escape, double ActionValue, double EndPosition, ImmutableArray<Tile> Path = default, Usable? Lead = null,
        Tile? Via = null)
    {
        /// <summary>
        /// Best score first; ties by ability id, then the tile it acts from, then the target, then the shape, then the end
        /// tile, then the bonus action used before acting and the tile it is used from.
        /// </summary>
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
            if (c == 0)
                c = string.CompareOrdinal(Key(a.Lead), Key(b.Lead));
            if (c == 0)
                c = (a.Via ?? a.ActTile).CompareTo(b.Via ?? b.ActTile);
            return c;
        }

        private string AbilityKey => Key(Usable);

        private static string Key(Usable? usable) => usable is null ? "" : usable.Command.Value + "/" + usable.Item?.Value;

        public string TargetKey => (Action?.Target switch
        {
            { Unit: { } unit } => "u:" + unit.Value,
            { Tile: { } tile } => $"t:{tile.X:D3},{tile.Y:D3}",
            _ => "",
        }) + (Action is { Targets.IsDefaultOrEmpty: false } a ? ">" + string.Join(",", a.Targets.Select(t => t.Value)) : "");
    }

    /// <summary>Scores compared to a millionth, so sums that differ only by rounding still tie.</summary>
    private static long Round(double score) => (long)Math.Round(score * 1e6);
}
