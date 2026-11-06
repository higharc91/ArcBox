using System.Collections.Immutable;
using Tactics.Logic;

namespace Tactics.Sim;

/// <summary>
/// The scripted rogue of <c>--policy hidden-rogue</c> (#108): a human who sets up Sneak Attack every turn it can, and
/// otherwise plays as the AI does. It plays only legal commands (the engine refuses anything else, and the turn is planned
/// again) and never looks at the dice. From where it stands it weighs every way of:
/// <list type="bullet">
/// <item>walking, striking a foe (the weapon attack) and walking on to a tile where it may Hide, then hiding;</item>
/// <item>walking to one of the <see cref="HideTiles"/> nearest tiles where it may Hide, hiding, then walking and striking
/// from hiding;</item>
/// <item>with nothing to strike, walking to the tile nearest a foe where it may Hide, if that is nearer a foe than where it
/// stands and close enough to strike from next turn, and hiding.</item>
/// </list>
/// A plan scores <see cref="StrikeWorth"/> for striking (so it always strikes when it can), <see cref="AdvantageWorth"/> when
/// the strike has advantage (from hiding, a flank, its mark or the target's condition: Sneak Attack) and
/// <see cref="HiddenWorth"/> for ending the turn hiding, which buys the next turn's advantage and the foes' disadvantage
/// meanwhile (so, seen at the start of its turn, it strikes and then hides rather than hides and then strikes, when it can
/// do both). Ties go to the weaker target, then the fewest foes left behind to make an opportunity attack, then the shortest
/// walk, then tile and unit order. When the best plan sets nothing up (a strike without advantage that can't end hidden),
/// or there is none (nothing to strike, and no hiding place that close), the turn is the AI's: it chooses targets, riders
/// and Cunning Dash at least as well.
/// </summary>
internal static class HiddenRogue
{
    /// <summary>The <c>--policy</c> name.</summary>
    public const string Name = "hidden-rogue";

    /// <summary>The rogue's Hide.</summary>
    public static readonly AbilityId Hide = new("rogue.cunning_hide");

    private const int StrikeWorth = 10;
    private const int AdvantageWorth = 4;
    private const int HiddenWorth = 5;

    /// <summary>How many tiles it considers hiding on before striking (the cheapest to reach).</summary>
    private const int HideTiles = 5;

    private sealed record Option(ImmutableArray<Command> Commands, int Score, int Tiebreak, int Opportunities, int Walk)
    {
        /// <summary>True when it strikes with advantage or ends the turn hiding.</summary>
        public bool SetsUp => Score != StrikeWorth;
    }

    /// <summary>The scripted turn of <paramref name="unit"/>, ending with <see cref="EndTurn"/>; null when the turn is the AI's.</summary>
    public static ImmutableArray<Command>? Plan(BattleState s, UnitId unit, RulesPack rules)
    {
        var me = s.Units[unit];
        if (!me.Sheet.Abilities.Contains(Hide) || !rules.Abilities.TryGetValue(Hide, out var hide)
            || hide.Effects.OfType<ApplyEffect>().FirstOrDefault()?.Condition is not { } hiding)
            return null;
        var terrain = new BattleWriter(s, rules).Terrain;
        var attack = new Use(unit, RulesPack.CommonAttack.Id, new Target());
        var options = new List<Option>();
        var here = Walks(s, unit, terrain, rules);

        // Walk, strike, walk on and hide.
        foreach (var a in here.Reachable)
        {
            var atA = Moved(s, unit, a, here.CostTo(a));
            var foes = Struck(atA, unit, attack, rules);
            if (foes.Length == 0)
                continue;
            var after = Revealed(atA, unit, hiding);
            var on = Walks(after, unit, terrain, rules);
            var cover = on.Reachable.Where(b => CanHide(Moved(after, unit, b, on.CostTo(b)), unit, rules))
                .OrderBy(b => on.CostTo(b)).ThenBy(b => b).Cast<Tile?>().FirstOrDefault();
            foreach (var foe in foes)
            {
                ImmutableArray<Command> commands = [.. WalkTo(unit, here, a), attack with { Target = new Target(Unit: foe.Id) },
                    .. cover is { } b ? [.. WalkTo(unit, on, b), new Use(unit, Hide, new Target())] : Array.Empty<Command>(), new EndTurn(unit)];
                options.Add(new Option(commands, StrikeWorth + (Advantage(atA, unit, foe.Id, rules) ? AdvantageWorth : 0) + (cover is null ? 0 : HiddenWorth),
                    foe.Hp, cover is { } c ? Opportunities(after, unit, [a, .. on.PathTo(c)]) : 0,
                    here.CostTo(a) + (cover is { } d ? on.CostTo(d) : 0)));
            }
        }

        // Hide, then walk and strike from hiding.
        if (me.Economy.Bonus)
            foreach (var h in here.Reachable.Where(h => CanHide(Moved(s, unit, h, here.CostTo(h)), unit, rules))
                         .OrderBy(h => here.CostTo(h)).ThenBy(h => h).Take(HideTiles))
            {
                var hidden = Hiding(Moved(s, unit, h, here.CostTo(h)), unit, hiding);
                var on = Walks(hidden, unit, terrain, rules);
                foreach (var a in on.Reachable)
                {
                    var atA = Moved(hidden, unit, a, on.CostTo(a));
                    foreach (var foe in Struck(atA, unit, attack, rules))
                        options.Add(new Option(
                            [.. WalkTo(unit, here, h), new Use(unit, Hide, new Target()), .. WalkTo(unit, on, a),
                                attack with { Target = new Target(Unit: foe.Id) }, new EndTurn(unit)],
                            StrikeWorth + (Advantage(atA, unit, foe.Id, rules) ? AdvantageWorth : 0), foe.Hp, 0, here.CostTo(h) + on.CostTo(a)));
                }
            }

        // Nothing to strike: hide as close to a foe as it can, if that brings it nearer one and within a turn's strike of it.
        int Nearest(Tile t) => s.Units.Values.Where(u => Movement.Hostile(me, u) && u.Life == LifeState.Up)
            .Select(u => Reach.Distance(u.Pos, t)).DefaultIfEmpty(0).Min();
        var strikeReach = Modifiers.Move(me.Sheet, Modifiers.For(s, unit, rules)) + (me.Sheet.Weapon.Ranged ? me.Sheet.Weapon.Range : 1);
        if (options.Count == 0)
            foreach (var b in here.Reachable.Where(b => Nearest(b) < Nearest(me.Pos) && Nearest(b) <= strikeReach
                                                        && CanHide(Moved(s, unit, b, here.CostTo(b)), unit, rules)))
                options.Add(new Option([.. WalkTo(unit, here, b), new Use(unit, Hide, new Target()), new EndTurn(unit)], HiddenWorth,
                    Nearest(b), 0, here.CostTo(b)));

        var best = options.OrderByDescending(o => o.Score).ThenBy(o => o.Tiebreak).ThenBy(o => o.Opportunities).ThenBy(o => o.Walk)
            .FirstOrDefault();
        return best is { SetsUp: true } ? best.Commands : null;
    }

    /// <summary>Where the unit can walk with the Move it has left (nowhere when it can't move), as the phone offers it.</summary>
    private static PathMap Walks(BattleState s, UnitId unit, Terrain terrain, RulesPack rules)
    {
        var me = s.Units[unit];
        var budget = Modifiers.For(s, unit, rules).CantMove ? 0 : me.Economy.MoveLeft;
        var others = s.Units.Values.Where(u => u.Id != unit && DownedRules.InBattle(u)).ToArray();
        return Paths.From(terrain, me.Pos, new Mover(budget, me.Sheet.Jump,
            [.. others.Where(u => !Movement.Hostile(me, u)).Select(u => u.Pos)],
            [.. others.Where(u => Movement.Hostile(me, u)).Select(u => u.Pos)]));
    }

    /// <summary>The walk to <paramref name="to"/>, or nothing when the unit is already there.</summary>
    private static IEnumerable<Command> WalkTo(UnitId unit, PathMap map, Tile to)
        => to == map.Origin ? [] : [new Move(unit, [map.Origin, .. map.PathTo(to)])];

    private static BattleState Moved(BattleState s, UnitId unit, Tile to, int cost)
        => Change(s, unit, u => u with { Pos = to, Economy = u.Economy with { MoveLeft = u.Economy.MoveLeft - cost } });

    /// <summary>After its attack: the action spent and the hiding condition gone (attacking reveals it).</summary>
    private static BattleState Revealed(BattleState s, UnitId unit, ConditionId hiding)
        => Change(s, unit, u => u with { Economy = u.Economy with { Action = false }, Conditions = u.Conditions.RemoveAll(c => c.Id == hiding) });

    /// <summary>After hiding: the bonus action spent and the hiding condition on.</summary>
    private static BattleState Hiding(BattleState s, UnitId unit, ConditionId hiding)
        => Change(s, unit, u => u with
        {
            Economy = u.Economy with { Bonus = false },
            Conditions = u.Conditions.RemoveAll(c => c.Id == hiding)
                .Add(new ActiveCondition(hiding, unit, RoundsLeft: 1, UntilSave: false, SaveDc: 0, Stat.Con, Concentration: false)),
        });

    private static BattleState Change(BattleState s, UnitId unit, Func<UnitState, UnitState> change)
        => s with { Units = s.Units.SetItem(unit, change(s.Units[unit])) };

    private static bool CanHide(BattleState s, UnitId unit, RulesPack rules)
        => Battle.Check(s, new Use(unit, Hide, new Target()), rules) is null;

    /// <summary>The standing foes the weapon attack may strike from where the unit is, in unit order.</summary>
    private static UnitState[] Struck(BattleState s, UnitId unit, Use attack, RulesPack rules)
        => [.. s.Units.Values.Where(u => Movement.Hostile(s.Units[unit], u) && u.Life == LifeState.Up
                                         && Battle.Check(s, attack with { Target = new Target(Unit: u.Id) }, rules) is null)];

    /// <summary>
    /// Whether the attack roll would have advantage, as <see cref="AttackStep"/> counts it: the user's own-attack edges and
    /// invisibility, the target's attacked-in-melee or at-range edges and invisibility, a flank in melee and the user's mark.
    /// </summary>
    private static bool Advantage(BattleState s, UnitId unit, UnitId target, RulesPack rules)
    {
        var ranged = s.Units[unit].Sheet.Weapon.Ranged;
        var (advantages, disadvantages) = (0, 0);
        foreach (var m in Modifiers.Sources(s, unit, rules))
        {
            advantages += (m.OwnAttacks == Edge.Advantage ? 1 : 0) + (m.Invisible ? 1 : 0);
            disadvantages += m.OwnAttacks == Edge.Disadvantage ? 1 : 0;
        }
        foreach (var m in Modifiers.Sources(s, target, rules))
        {
            var against = ranged ? m.AttackedRanged : m.AttackedMelee;
            advantages += against == Edge.Advantage ? 1 : 0;
            disadvantages += (against == Edge.Disadvantage ? 1 : 0) + (m.Invisible ? 1 : 0);
        }
        if (!ranged && Modifiers.Flanked(s, unit, target, rules))
            advantages++;
        if (MarkBlock.MarkedBy(s.Units[target], unit))
            advantages++;
        return Rolls.Combine(advantages, disadvantages) == Edge.Advantage;
    }

    /// <summary>The foes able to react that stand next to a tile of <paramref name="walk"/> before its last: each may strike as the unit leaves.</summary>
    private static int Opportunities(BattleState s, UnitId unit, ImmutableArray<Tile> walk)
        => s.Units.Values.Count(u => Movement.Hostile(s.Units[unit], u) && u.Life == LifeState.Up && u.Economy.Reaction
                                     && walk[..^1].Any(t => Reach.Distance(t, u.Pos) == 1));
}
