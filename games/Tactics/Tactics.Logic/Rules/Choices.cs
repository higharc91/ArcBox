using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>A tile the unit can walk to this turn: the Move it costs and the path <see cref="Move"/> takes.</summary>
public sealed record MoveChoice(Tile To, int Cost, ImmutableArray<Tile> Path);

/// <summary>What one use is expected to do to a unit it affects: average damage (after resistances) and healing.</summary>
public sealed record UnitPreview(UnitId Unit, double Damage, double Heal);

/// <summary>A legal target for an ability: what to send in <see cref="Use.Target"/>, where it lands and whom it affects.</summary>
public sealed record TargetChoice(Target Target, Tile Aim, ImmutableArray<UnitPreview> Affected);

/// <summary>
/// An ability or item the unit can use now, with its tooltip, its cost and every legal target. <see cref="Item"/> is set
/// for an item, used as <see cref="Battle.UseItem"/>.
/// </summary>
public sealed record AbilityChoice(AbilityId Ability, ItemId? Item, string Name, string Tooltip, CostDef Cost,
    ImmutableArray<TargetChoice> Targets);

/// <summary>Everything a unit can do on its turn as things stand (<see cref="Choices.For"/>).</summary>
public sealed record TurnChoices(UnitId Unit, TurnEconomy Economy, ImmutableArray<MoveChoice> Moves,
    ImmutableArray<AbilityChoice> Abilities, bool CanDelay);

/// <summary>What a phone offers a player on their turn.</summary>
public static class Choices
{
    /// <summary>
    /// The unit's options now: the tiles it can walk to, and every ability and item it can use with each legal target and
    /// what that is expected to do (from the same maths as the AI). Every option is one <see cref="Battle.Check"/> accepts,
    /// so the phone never offers a command <see cref="Battle.Apply"/> refuses. Reactions aren't listed: they're answered
    /// when offered. Empty when it isn't the unit's turn or a decision is pending.
    /// </summary>
    public static TurnChoices For(BattleState state, UnitId unit, RulesPack rules)
    {
        if (state.Outcome is not null || state.Pending is not null || state.Active != unit || !state.Units.TryGetValue(unit, out var me))
            return new TurnChoices(unit, state.Units.GetValueOrDefault(unit)?.Economy ?? new TurnEconomy(0, false, false, false, 0), [], [], false);

        var w = new BattleWriter(state, rules);
        var scorer = new Scorer(state, rules, unit);
        return new TurnChoices(unit, me.Economy, Moves(state, me, w, rules), Abilities(state, me, w, scorer, rules),
            CanDelay: Battle.Check(state, new Delay(unit, 1), rules) is null);
    }

    private static ImmutableArray<MoveChoice> Moves(BattleState state, UnitState me, BattleWriter w, RulesPack rules)
    {
        if (me.Economy.MoveLeft <= 0 || Modifiers.For(w, me.Id, rules).CantMove)
            return [];
        var others = state.Units.Values.Where(u => u.Id != me.Id && DownedRules.InBattle(u)).ToArray();
        var mover = new Mover(me.Economy.MoveLeft, me.Sheet.Jump,
            [.. others.Where(u => !Movement.Hostile(me, u)).Select(u => u.Pos)],
            [.. others.Where(u => Movement.Hostile(me, u)).Select(u => u.Pos)]);
        var paths = Paths.From(w.Terrain, me.Pos, mover);
        var moves = ImmutableArray.CreateBuilder<MoveChoice>();
        foreach (var to in paths.Reachable.Where(t => t != me.Pos).Order())
        {
            ImmutableArray<Tile> path = [me.Pos, .. paths.PathTo(to)];   // PathTo leaves out the origin; Move starts at it
            if (path.Length >= 2 && Battle.Check(state, new Move(me.Id, path), rules) is null)
                moves.Add(new MoveChoice(to, paths.CostTo(to), path));
        }
        return moves.ToImmutable();
    }

    private static ImmutableArray<AbilityChoice> Abilities(BattleState state, UnitState me, BattleWriter w, Scorer scorer, RulesPack rules)
    {
        var uses = new List<(AbilityDef Ability, AbilityId Command, ItemId? Item)>();
        var ids = new SortedSet<AbilityId>(me.Sheet.Abilities);
        ids.UnionWith(rules.Abilities.Keys.Where(a => a.Value.StartsWith("common.", StringComparison.Ordinal)));
        ids.Remove(Battle.UseItem);
        foreach (var id in ids)
            if (rules.Abilities.TryGetValue(id, out var ability) && !scorer.ItemAbilities.Contains(id) && !Reacts(ability))
                uses.Add((ability, id, null));
        foreach (var stack in me.Bag.Where(b => b.Count > 0).DistinctBy(b => b.Item).OrderBy(b => b.Item))
            if (rules.Items.TryGetValue(stack.Item, out var item) && rules.Abilities.TryGetValue(item.Use, out var ability))
                uses.Add((ability, Battle.UseItem, item.Id));

        var choices = ImmutableArray.CreateBuilder<AbilityChoice>();
        foreach (var (ability, command, item) in uses)
        {
            var targets = ImmutableArray.CreateBuilder<TargetChoice>();
            foreach (var target in Candidates(state, me, ability, w))
            {
                if (Battle.Check(state, new Use(me.Id, command, target, item), rules) is not null
                    || Battle.ResolveTarget(w, me, ability, target, out var aim, out var affected) is not null)
                    continue;
                targets.Add(new TargetChoice(target, aim, Previews(ability, me, aim, affected, w, scorer)));
            }
            if (targets.Count == 0)
                continue;
            var name = item is { } i ? rules.Items[i].Name : ability.Name;
            choices.Add(new AbilityChoice(command, item, name, Tooltips.For(ability, me.Sheet.Level, rules), ability.Cost,
                targets.ToImmutable()));
        }
        return choices.ToImmutable();
    }

    private static bool Reacts(AbilityDef ability)
        => ability.Cost.Reaction > 0
           || ability.Ai?.Tags is { IsDefault: false } tags && tags.Any(t => t.StartsWith(Reactions.ReactsToTag, StringComparison.Ordinal));

    /// <summary>
    /// The targets worth checking: each unit still in the battle for a unit-aimed ability; the tiles in reach (range plus
    /// the largest high-ground bonus) for a tile-aimed one; the four neighbours for a line or cone from the user; else the
    /// user. <see cref="Battle.Check"/> then keeps only the legal ones.
    /// </summary>
    private static IEnumerable<Target> Candidates(BattleState state, UnitState me, AbilityDef ability, BattleWriter w)
    {
        var def = ability.Target;
        if (def.Area.Shape is AreaShape.AllAllies or AreaShape.AllEnemies)
            return [new Target()];
        if (def.Kind == TargetKind.Self)
            return def.Area.Shape is AreaShape.Line or AreaShape.Cone
                ? new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }.Select(d => new Tile(me.Pos.X + d.Item1, me.Pos.Y + d.Item2))
                    .Where(w.Terrain.Map.InBounds).Select(t => new Target(Tile: t))
                : [new Target()];
        if (def.Kind is TargetKind.Tile or TargetKind.EmptyTile)
        {
            var reach = (ability.IsWeaponAttack && me.Sheet.Weapon.Ranged ? Math.Max(def.Range, me.Sheet.Weapon.Range) : def.Range) + 4;
            var map = w.Terrain.Map;
            return Enumerable.Range(0, map.Width).SelectMany(x => Enumerable.Range(0, map.Height).Select(y => new Tile(x, y)))
                .Where(t => Reach.Distance(me.Pos, t) <= reach).Select(t => new Target(Tile: t));
        }
        return state.Units.Values.Where(DownedRules.InBattle).Select(u => new Target(Unit: u.Id));
    }

    private static ImmutableArray<UnitPreview> Previews(AbilityDef ability, UnitState me, Tile aim, ImmutableArray<UnitId> affected,
        BattleWriter w, Scorer scorer)
    {
        var effects = ability.Effects.IsDefault ? [] : ability.Effects;
        if (ability.IsWeaponAttack)
            effects = [.. Enumerable.Repeat(effects, Math.Max(1, me.Sheet.Attacks)).SelectMany(e => e)];
        var unitEffects = effects.Where(e => !e.OnTile).ToImmutableArray();
        var strike = new Strike(w, me, ability, aim, neutral: true);
        return [.. affected.Select(id =>
        {
            var outcome = scorer.Walk(unitEffects, strike, w.State.Units[id]);
            return new UnitPreview(id, Math.Round(outcome.Damage.Mean, 1), Math.Round(outcome.Heal, 1));
        })];
    }
}
