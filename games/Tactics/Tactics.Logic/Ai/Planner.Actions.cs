using System.Collections.Immutable;

namespace Tactics.Logic;

internal sealed partial class Planner
{
    private enum Phase { Main, Bonus }

    /// <summary>
    /// An ability the unit may use on its turn: its definition (an item's own ability), the command's ability id
    /// (<see cref="Battle.UseItem"/> for an item) and item, whether it takes the action or the bonus action, whether it is
    /// a disengage (<see cref="Escape"/>) or a dash, and whether it leaves the map as it is (<see cref="Stable"/>), so a
    /// move planned before it still holds after it.
    /// </summary>
    private sealed record Usable(AbilityDef Ability, AbilityId Command, ItemId? Item, Phase Phase, bool Escape, bool Dash, bool Stable);

    /// <summary>
    /// What the unit can use: its sheet's abilities and the built-in <c>common.*</c> actions it can pay for (in id order),
    /// and with <see cref="AiOptions.AllowItems"/> the items in its bag. Reactions are left out.
    /// </summary>
    private ImmutableArray<Usable> Usables()
    {
        var ids = new SortedSet<AbilityId>(_me.Sheet.Abilities);
        ids.UnionWith(_rules.Abilities.Keys.Where(a => a.Value.StartsWith("common.", StringComparison.Ordinal)));
        ids.Remove(Battle.UseItem);
        var usables = ImmutableArray.CreateBuilder<Usable>();
        foreach (var id in ids)
            if (_rules.Abilities.TryGetValue(id, out var ability) && !_scorer.ItemAbilities.Contains(id) && UsableOf(ability, id, null) is { } u)
                usables.Add(u);
        if (_options.AllowItems)
            foreach (var stack in _me.Bag.Where(b => b.Count > 0).DistinctBy(b => b.Item).OrderBy(b => b.Item))
                if (_rules.Items.TryGetValue(stack.Item, out var item) && _rules.Abilities.TryGetValue(item.Use, out var ability)
                    && UsableOf(ability, Battle.UseItem, item.Id) is { } u)
                    usables.Add(u);
        return usables.ToImmutable();
    }

    private Usable? UsableOf(AbilityDef ability, AbilityId command, ItemId? item)
    {
        var cost = ability.Cost;
        if (cost.Reaction > 0 || ability.Ai?.Tags is { IsDefault: false } tags && tags.Any(t => t.StartsWith(Reactions.ReactsToTag, StringComparison.Ordinal)))
            return null;
        var self = ability.Target.Kind == TargetKind.Self;
        var top = ability.Effects.IsDefault ? [] : ability.Effects;
        var escape = self && top.Any(e => e is ApplyEffect { Condition: var c } && c == Movement.Disengaged);
        var dash = self && top.Any(e => e is ExtraActionEffect { Kind: "move" });
        var stable = !Scorer.All(top).Any(e => e is PushEffect or PullEffect or SwapEffect or TeleportEffect or DashEffect
            or JumpToEffect or ZoneEffect or HazardEffect or AlterTerrainEffect or SummonEffect or TransformEffect or TurnOrderEffect
            || e is ApplyEffect a && Halts(a.Condition) || e is StatModEffect m && (m.Mods.CantMove || m.Mods.Incapacitated));
        return new Usable(ability, command, item, cost.Action > 0 ? Phase.Main : Phase.Bonus, escape, dash, stable);
    }

    /// <summary>True when the condition (or one it implies) stops its holder moving or acting.</summary>
    private bool Halts(ConditionId id)
    {
        var seen = new List<ConditionId>();
        for (ConditionId? next = id; next is { } c && !seen.Contains(c) && _rules.Conditions.TryGetValue(c, out var def); next = def.Implies)
        {
            seen.Add(c);
            if (def.Static is { } m && (m.CantMove || m.Incapacitated))
                return true;
        }
        return false;
    }

    private static bool Affordable(UnitState me, Usable usable) => Battle.CostRefusal(me, usable.Ability) is null;

    /// <summary>
    /// What spending <paramref name="usable"/>'s resources costs: hoard × 10 × the share of each pool it takes (a per-battle
    /// use is a share of its uses); an item is worth half a pool.
    /// </summary>
    private double Hoard(Usable usable)
    {
        var cost = usable.Ability.Cost;
        var share = 0.0;
        foreach (var (name, amount) in cost.Resource ?? Ordinal.Map<int>())
            share += (double)amount / Math.Max(1, Math.Max(_me.Sheet.Resources.GetValueOrDefault(name), _me.Resources.GetValueOrDefault(name)));
        if (cost.Uses is { } uses)
            share += 1.0 / Math.Max(1, uses);
        if (usable.Item is not null)
            share += 0.5;
        return _p.Hoard * 10 * share;
    }

    /// <summary>The unit as it would be on <paramref name="t"/>, having walked there.</summary>
    private UnitState Moved(Tile t) => Hypo(t).State.Units[_me.Id];

    /// <summary>The battle as it would be with the unit on <paramref name="t"/> (its Move spent getting there), and a
    /// working copy of it for the engine's targeting checks.</summary>
    private (BattleState State, BattleWriter W) Hypo(Tile t)
    {
        if (_hypo.TryGetValue(t, out var found))
            return found;
        var cost = _spots.TryGetValue(t, out var spot) ? spot.Cost : 0;
        var me = _me with { Pos = t, Economy = _me.Economy with { MoveLeft = _me.Economy.MoveLeft - cost } };
        var state = _s with { Units = _s.Units.SetItem(me.Id, me) };
        return _hypo[t] = (state, new BattleWriter(state, _rules));
    }

    /// <summary>One way to use an ability from a tile: the target, the aim, whom it affects and what that is worth.</summary>
    private sealed record Action(Target Target, Tile Aim, ImmutableArray<UnitId> Affected, double Value);

    private IEnumerable<Action> Actions(Usable usable, Tile from) => Actions(usable, from, Hypo(from).State);

    /// <summary>
    /// Every legal target or aim for <paramref name="usable"/> by the unit on <paramref name="from"/> in
    /// <paramref name="state"/>, with its worth. Single-target unit abilities try each unit; area abilities aim only at tiles
    /// whose area covers a unit they are meant for (a foe; an ally for ally abilities); self lines and cones try the four
    /// directions. Legality is the engine's own (<see cref="Battle.ResolveTarget"/>).
    /// </summary>
    private IEnumerable<Action> Actions(Usable usable, Tile from, BattleState state)
    {
        var ability = usable.Ability;
        var def = ability.Target;
        var area = def.Area;
        var me = state.Units[_me.Id];
        var w = ReferenceEquals(state, Hypo(from).State) ? Hypo(from).W : new BattleWriter(state, _rules);
        var everywhere = area.Shape is AreaShape.AllAllies or AreaShape.AllEnemies;
        var forAllies = def.Kind is TargetKind.Ally or TargetKind.DownedAlly;
        var units = state.Units.Values.Where(DownedRules.InBattle).ToArray();
        var meant = units.Where(u => forAllies ? !Scorer.Hostile(me, u) : Scorer.Hostile(me, u)).ToArray();
        var maxRange = def.Range + 5;

        IEnumerable<Target> targets;
        if (everywhere)
            targets = [new Target()];
        else if (def.Kind == TargetKind.Self)
            targets = area.Shape is AreaShape.Line or AreaShape.Cone
                ? Neighbours(from).Where(_scorer.Terrain.Map.InBounds).Select(t => new Target(Tile: t))
                : [new Target()];
        else if (area.Shape == AreaShape.Single && def.Kind is TargetKind.Ally or TargetKind.Enemy or TargetKind.AnyUnit or TargetKind.DownedAlly)
            targets = units.Where(u => Reach.Distance(from, u.Pos) <= maxRange).Select(u => new Target(Unit: u.Id));
        else if (area.Shape == AreaShape.Single && def.Kind == TargetKind.Tile)
            targets = meant.Where(u => Reach.Distance(from, u.Pos) <= maxRange).Select(u => new Target(Tile: u.Pos));
        else if (area.Shape == AreaShape.Single)
            targets = [];   // empty-tile abilities move the user or place something; the planner leaves them be
        else if (area.Shape is AreaShape.Line or AreaShape.Cone)
            targets = meant.Where(u => Reach.Distance(from, u.Pos) <= maxRange).Select(u => new Target(Tile: u.Pos));
        else
            targets = meant.SelectMany(u => Areas.Tiles(area, u.Pos, u.Pos, _scorer.Terrain)).Distinct().Order()
                .Where(t => Reach.Distance(from, t) <= maxRange).Select(t => new Target(Tile: t));

        // An aimed burst, cross or ring without attack rolls is worth the same from any tile, so it is remembered by its aim;
        // not one that can include its caster, which counts only when the caster stands in it.
        var fixedAim = area.Shape is AreaShape.Burst or AreaShape.Cross or AreaShape.Ring && def.Kind != TargetKind.Self
                       && !area.IncludeSelf && Scorer.FirstAttack(ability.Effects) is null;
        foreach (var target in targets)
        {
            if (Battle.ResolveTarget(w, me, ability, target, out var aim, out var affected) is not null)
                continue;
            double value;
            if (fixedAim && _aimValues.TryGetValue((usable.Command, usable.Item, aim), out var known))
                value = known;
            else
            {
                value = Value(usable, w, me, aim, affected);
                if (fixedAim)
                    _aimValues[(usable.Command, usable.Item, aim)] = value;
            }
            yield return new Action(target, aim, affected, value);
        }
    }

    private static IEnumerable<Tile> Neighbours(Tile t) => [t with { Y = t.Y - 1 }, t with { X = t.X + 1 }, t with { Y = t.Y + 1 }, t with { X = t.X - 1 }];

    /// <summary>
    /// What using <paramref name="usable"/> at <paramref name="aim"/> is worth: for each affected unit, what the ability's
    /// unit blocks (all its attacks, for the Attack action) are expected to do to it (<see cref="Scorer.Worth"/>); plus its
    /// tile blocks at the aim (a zone's effects on the units in it, a summon); less a little for dropping a concentration.
    /// </summary>
    private double Value(Usable usable, BattleWriter w, UnitState me, Tile aim, ImmutableArray<UnitId> affected)
    {
        var ability = usable.Ability;
        var unitEffects = UnitEffects(ability, me);
        var tileEffects = (ability.Effects.IsDefault ? [] : ability.Effects).Where(e => e.OnTile).ToImmutableArray();

        var st = new Strike(w, me, ability, aim, neutral: false);
        var value = 0.0;
        if (!unitEffects.IsEmpty)
            foreach (var id in affected)
            {
                var target = w.State.Units[id];
                value += _scorer.Worth(me, target, _scorer.Walk(unitEffects, st, target), _p);
            }
        foreach (var effect in tileEffects)
            value += TileValue(effect, st, aim);
        if (ability.Cost.Concentration && me.Concentrating is not null)
            value -= 2;
        return value;
    }

    /// <summary>
    /// The ability's blocks that run on each unit it affects, in order: all of them for each attack of the Attack action.
    /// </summary>
    private static ImmutableArray<EffectDef> UnitEffects(AbilityDef ability, UnitState user)
    {
        var effects = ability.Effects.IsDefault ? [] : ability.Effects;
        if (ability.IsWeaponAttack)
            effects = [.. Enumerable.Repeat(effects, Math.Max(1, user.Sheet.Attacks)).SelectMany(e => e)];
        return [.. effects.Where(e => !e.OnTile)];
    }

    /// <summary>True when <paramref name="c"/>'s action may drop <paramref name="unit"/>: its damage can reach the HP left.</summary>
    private bool MayDrop(Candidate c, UnitId unit)
    {
        var (state, w) = Hypo(c.ActTile);
        var me = state.Units[_me.Id];
        var target = state.Units[unit];
        var outcome = _scorer.Walk(UnitEffects(c.Usable!.Ability, me), new Strike(w, me, c.Usable.Ability, c.Action!.Aim, neutral: false),
            target);
        return outcome.Damage.AtLeast(Math.Max(1, target.Hp + target.TempHp)) > 0;
    }

    /// <summary>
    /// A tile block's worth at the aim: a zone's turn-start and enter effects on the units in it now (enter effects at half,
    /// as units already there don't enter it), a difficult zone slowing foes in it, a hazard's enter effects at half, and
    /// a summon's damage per round for two rounds per unit summoned.
    /// </summary>
    private double TileValue(EffectDef effect, Strike st, Tile aim)
    {
        switch (effect)
        {
            case ZoneEffect zone:
                {
                    var tiles = Areas.Tiles(zone.Area, st.From, aim, _scorer.Terrain).ToImmutableSortedSet();
                    var rounds = Math.Min(3, zone.Rounds ?? 3);
                    var value = 0.0;
                    foreach (var unit in st.W.State.Units.Values.Where(u => DownedRules.InBattle(u) && tiles.Contains(u.Pos)))
                    {
                        foreach (var passive in zone.Passives.IsDefault ? [] : zone.Passives)
                        {
                            var weight = passive.Hook switch
                            {
                                HookKind.OnTurnStart => rounds,
                                HookKind.OnEnterTile => 0.5,
                                _ => 0,
                            };
                            if (weight > 0 && passive.Target == PassiveTarget.Self && !passive.Effects.IsDefaultOrEmpty)
                                value += weight * _scorer.Worth(st.User, unit, _scorer.Walk(passive.Effects, st, unit), _p);
                        }
                        if (zone.Difficult && Scorer.Hostile(st.User, unit) && unit.Life == LifeState.Up)
                            value += 0.3 * _p.Control;
                    }
                    return value;
                }
            case HazardEffect hazard:
                {
                    var tiles = Areas.Tiles(hazard.Area, st.From, aim, _scorer.Terrain).ToImmutableSortedSet();
                    return st.W.State.Units.Values.Where(u => DownedRules.InBattle(u) && tiles.Contains(u.Pos))
                        .Sum(u => 0.5 * _scorer.Worth(st.User, u, _scorer.Walk(hazard.OnEnter, st, u), _p));
                }
            case SummonEffect summon:
                return _rules.MonsterSheet(summon.Monster) is { } sheet
                    ? summon.Count * 2 * (1 + sheet.Attacks * Math.Max(0, sheet.Weapon.Damage.Average)) * 0.5 * _p.Damage
                    : 0;
            default:
                return 0;
        }
    }
}
