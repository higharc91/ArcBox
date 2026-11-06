using System.Collections.Immutable;

namespace Tactics.Logic;

internal sealed partial class Planner
{
    private enum Phase { Main, Bonus }

    /// <summary>
    /// An ability the unit may use on its turn: its definition (an item's own ability), the command's ability id
    /// (<see cref="Battle.UseItem"/> for an item) and item, whether it takes the action or the bonus action, whether it is
    /// a disengage (<see cref="Escape"/>) or a dash, and whether it leaves the map as it is (<see cref="Stable"/>), so a
    /// move planned before it still holds after it. <see cref="Surge"/> marks one that gives the unit an extra action (Action
    /// Surge); <see cref="Hop"/> is how far one that moves the unit to an empty tile (a teleport or a leap) takes it, 0 for
    /// any other.
    /// </summary>
    private sealed record Usable(AbilityDef Ability, AbilityId Command, ItemId? Item, Phase Phase, bool Escape, bool Dash, bool Stable,
        bool Reacts, bool Surge, int Hop);

    /// <summary>
    /// What the unit can use: its sheet's abilities and the built-in <c>common.*</c> actions it can pay for (in id order),
    /// and with <see cref="AiOptions.AllowItems"/> the items in its bag (for a companion, only the ones
    /// <see cref="Companion.MayUse"/> allows). Reactions are left out.
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
                if (_rules.Items.TryGetValue(stack.Item, out var item) && (!_companion || Companion.MayUse(item, _me))
                    && _rules.Abilities.TryGetValue(item.Use, out var ability) && UsableOf(ability, Battle.UseItem, item.Id) is { } u)
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
        var surge = self && top.Any(e => e is ExtraActionEffect { Kind: "action" });
        var hop = ability.Target is { Kind: TargetKind.EmptyTile, Area.Shape: AreaShape.Single }
            ? Math.Min(ability.Target.Range, top.Select(e => e switch { TeleportEffect { Self: true } t => t.Range, JumpToEffect j => j.Range, _ => 0 })
                .DefaultIfEmpty(0).Max())
            : 0;
        var stable = !Scorer.All(top).Any(e => e is PushEffect or PullEffect or SwapEffect or TeleportEffect or DashEffect
            or JumpToEffect or ZoneEffect or HazardEffect or AlterTerrainEffect or SummonEffect or TransformEffect or TurnOrderEffect
            || e is ApplyEffect a && Halts(a.Condition) || e is StatModEffect m && (m.Mods.CantMove || m.Mods.Incapacitated));
        return new Usable(ability, command, item, cost.Action > 0 ? Phase.Main : Phase.Bonus, escape, dash,
            stable && !_scorer.ChangesSurface(ability, _me.Sheet), _scorer.Reacts(ability, _me.Sheet), surge, Math.Max(0, hop));
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
    /// False for a companion's item used anywhere but on itself, or on a tile where the threat map doesn't say it would be
    /// Downed next round (<see cref="Companion.WouldDrop"/>); true for anything else.
    /// </summary>
    private bool ItemOk(Usable usable, Tile at, Action action)
        => usable.Item is null || !_companion
           || (action.Target.Unit is not { } unit || unit == _me.Id) && Companion.WouldDrop(_threat, _me, at);

    /// <summary>What spending <paramref name="usable"/>'s resources costs (<see cref="AiProfile.Spend"/>).</summary>
    private double Hoard(Usable usable) => _p.Spend(_me, usable.Ability.Cost, usable.Item is not null);

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

    /// <summary>
    /// One way to use an ability from a tile: the target, the aim, whom it affects and what that is worth; for a weapon
    /// attack that spreads its attacks, the target of each (<see cref="Use.Targets"/>, default when they all go to
    /// <see cref="Target"/>).
    /// </summary>
    private sealed record Action(Target Target, Tile Aim, ImmutableArray<UnitId> Affected, double Value,
        ImmutableArray<UnitId> Targets = default);

    private IEnumerable<Action> Actions(Usable usable, Tile from) => Actions(usable, from, Hypo(from).State, Hypo(from).W);

    /// <summary>
    /// Every legal target or aim for <paramref name="usable"/> by the unit on <paramref name="from"/> in
    /// <paramref name="state"/> (<paramref name="w"/>, a working copy of it, if there is one), with its worth. Single-target
    /// unit abilities try each unit; area abilities aim only at tiles whose area covers a unit they are meant for (a foe; an
    /// ally for ally abilities); self lines and cones try the four directions. A teleport or leap (<see cref="Usable.Hop"/>)
    /// aims at the best landing tiles (<see cref="Landings"/>) and is worth what landing there gains on staying
    /// (<see cref="Land"/> less <see cref="Place"/>); a summon aims at the empty tiles beside its foes and next to the user,
    /// the ones nearer a foe first. Legality is the engine's own (<see cref="Battle.ResolveTarget"/>).
    /// </summary>
    private IEnumerable<Action> Actions(Usable usable, Tile from, BattleState state, BattleWriter? w = null)
    {
        var ability = usable.Ability;
        var def = ability.Target;
        var area = def.Area;
        var me = state.Units[_me.Id];
        w ??= new BattleWriter(state, _rules);
        var everywhere = area.Shape is AreaShape.AllAllies or AreaShape.AllEnemies;
        var forAllies = def.Kind is TargetKind.Ally or TargetKind.DownedAlly;
        var units = state.Units.Values.Where(DownedRules.InBattle).ToArray();
        var meant = units.Where(u => forAllies ? !Scorer.Hostile(me, u) : Scorer.Hostile(me, u)).ToArray();
        var summons = def.Kind == TargetKind.EmptyTile && Scorer.All(ability.Effects).Any(e => e is SummonEffect);
        // A weapon attack reaches as far as the sheet's weapon, not the ability's nominal range of 1 (Battle.Check decides
        // exactly; this only bounds the candidates, with room for the high-ground bonus).
        var maxRange = (ability.IsWeaponAttack && me.Sheet.Weapon.Ranged ? Math.Max(def.Range, me.Sheet.Weapon.Range) : def.Range) + 5;

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
        else if (area.Shape == AreaShape.Single && usable.Hop > 0)
            targets = Landings(from, usable.Hop).Select(t => new Target(Tile: t));
        else if (area.Shape == AreaShape.Single)
            targets = summons
                ? meant.SelectMany(u => Neighbours(u.Pos)).Concat(Neighbours(from))
                    .Where(t => Reach.Distance(from, t) <= maxRange && _scorer.Terrain.Map.InBounds(t) && _scorer.Terrain.Walkable(t)
                                && !units.Any(u => u.Pos == t))
                    .Distinct().Order().Select(t => new Target(Tile: t))
                : [];   // other empty-tile abilities: nothing the planner knows how to value
        else if (area.Shape is AreaShape.Line or AreaShape.Cone)
            targets = meant.Where(u => Reach.Distance(from, u.Pos) <= maxRange).Select(u => new Target(Tile: u.Pos));
        else
            targets = meant.SelectMany(u => Areas.Tiles(area, u.Pos, u.Pos, _scorer.Terrain)).Distinct().Order()
                .Where(t => Reach.Distance(from, t) <= maxRange).Select(t => new Target(Tile: t));

        if (usable.Reacts && def.Kind == TargetKind.Tile && area.Shape is AreaShape.Single or AreaShape.Burst or AreaShape.Cross or AreaShape.Ring)
            targets = targets.Concat(ReactionAims(usable).Where(t => Reach.Distance(from, t) <= maxRange).Select(t => new Target(Tile: t))).Distinct();

        // An aimed burst, cross or ring without attack rolls is worth the same from any tile, so it is remembered by its aim;
        // not one that can include its caster, which counts only when the caster stands in it.
        var fixedAim = area.Shape is AreaShape.Burst or AreaShape.Cross or AreaShape.Ring && def.Kind != TargetKind.Self
                       && !area.IncludeSelf && Scorer.FirstAttack(ability.Effects) is null;
        foreach (var target in targets)
        {
            if (Battle.ResolveTarget(w, me, ability, target, out var aim, out var affected) is not null)
                continue;
            double value;
            ImmutableArray<UnitId> listed = default;
            if (fixedAim && _aimValues.TryGetValue((usable.Command, usable.Item, aim), out var known))
                value = known;
            else if (Strikes.Aimed(ability) && me.Sheet.Attacks > 1 && affected.Length == 1)
                (value, listed, affected) = Aimed(usable, w, me, w.State.Units[affected[0]]);
            else
            {
                value = Value(usable, w, me, aim, affected);
                if (fixedAim)
                    _aimValues[(usable.Command, usable.Item, aim)] = value;
            }
            if (usable.Hop > 0)
                value += Land(aim) - Place(from);
            else if (summons)
                value -= 0.01 * meant.Select(u => Reach.Distance(u.Pos, aim)).DefaultIfEmpty(0).Min();
            if (usable.Reacts)
            {
                var reaction = _scorer.ReactionWorth(new Strike(w, me, ability, aim, neutral: false), aim, affected, _p);
                if (state.Difficulty == Difficulty.Story && reaction.FriendlyDamage)
                    continue;
                value += reaction.Value;
            }
            yield return new Action(target, aim, affected, value, listed);
        }
    }

    /// <summary>
    /// A share of the first target's HP at which it is "predicted" dead: once the attacks so far drop it at least this
    /// likely, the rest may go to another foe.
    /// </summary>
    private const double PredictedKill = 0.5;

    /// <summary>
    /// What an aimed weapon attack of several attacks (<see cref="Strikes.Aimed"/>) at <paramref name="first"/> is worth, and
    /// where its attacks go. All of them at <paramref name="first"/> is the default. Once the first k attacks are expected to
    /// drop it at least <see cref="PredictedKill"/> likely, the other attacks may go to another foe the ability can reach
    /// from here (<see cref="Battle.ResolveTarget"/>; plain attacks after a <see cref="AbilityDef.RiderOnce"/> rider,
    /// <see cref="Scorer.FollowUps"/>): every such k and foe is tried, and the split worth most is kept when it beats all of
    /// them at <paramref name="first"/>. Ties keep the fewer attacks moved, then the lower unit id.
    /// </summary>
    private (double Value, ImmutableArray<UnitId> Targets, ImmutableArray<UnitId> Affected) Aimed(Usable usable, BattleWriter w,
        UnitState me, UnitState first)
    {
        var ability = usable.Ability;
        var count = me.Sheet.Attacks;
        var st = new Strike(w, me, ability, first.Pos, neutral: false);
        var onFirst = _scorer.StrikeOutcomes(ability, count, st, first);
        var best = _scorer.Worth(me, first, onFirst[^1], _p);
        ImmutableArray<UnitId> targets = default;
        ImmutableArray<UnitId> affected = [first.Id];

        var hp = Math.Max(1, first.Hp + first.TempHp);
        var from = Enumerable.Range(1, count - 1).FirstOrDefault(k => onFirst[k - 1].Damage.AtLeast(hp) >= PredictedKill);
        if (from == 0 || !Scorer.Hostile(me, first))
            return (best, targets, affected);
        var others = w.State.Units.Values
            .Where(u => u.Id != first.Id && DownedRules.InBattle(u) && Scorer.Hostile(me, u) && u.Sheet.Side != Side.Neutral
                        && Battle.ResolveTarget(w, me, ability, new Target(Unit: u.Id), out _, out var hit) is null && hit.Contains(u.Id))
            .ToArray();
        var fired = st.Fired.ToArray();
        foreach (var other in others)
        {
            st.Fired.Clear();
            st.Fired.UnionWith(fired);
            var follow = _scorer.FollowUps(ability, count - from, st, other);
            for (var k = count - 1; k >= from; k--)
            {
                var value = _scorer.Worth(me, first, onFirst[k - 1], _p) + _scorer.Worth(me, other, follow[count - k - 1], _p);
                if (Round(value) <= Round(best))
                    continue;
                best = value;
                targets = [.. Enumerable.Repeat(first.Id, k), .. Enumerable.Repeat(other.Id, count - k)];
                affected = [first.Id, other.Id];
            }
        }
        return (best, targets, affected);
    }

    private readonly Dictionary<AbilityId, ImmutableArray<Tile>> _reactionAims = [];

    /// <summary>Empty aims that can reach a foe through water or put a surface on its route.</summary>
    private ImmutableArray<Tile> ReactionAims(Usable usable)
    {
        if (_reactionAims.TryGetValue(usable.Ability.Id, out var known))
            return known;
        var ability = usable.Ability;
        var types = Scorer.All(ability.Effects).OfType<DamageEffect>()
            .Select(d => d.Type ?? (d.Weapon ? _me.Sheet.Weapon.DamageType : null)).ToHashSet();
        var rules = _rules.Environment.Reactions.Where(r => r.Damage is not null && types.Contains(r.Damage)).ToArray();
        var aims = ImmutableArray.CreateBuilder<Tile>();
        var map = _scorer.Terrain.Map;
        for (var x = 0; x < map.Width; x++)
            for (var y = 0; y < map.Height; y++)
            {
                var aim = new Tile(x, y);
                var tiles = Areas.Tiles(ability.Target.Area, _me.Pos, aim, _scorer.Terrain);
                if (rules.Any(rule => rule.Conduct is { } c
                    ? _scorer.Conducted(_scorer.W, tiles, c.Reach).Any(t => _foes.Any(f => f.Pos == t))
                    : rule.Surface is not null && tiles.Any(t => Surfaces.Matches(_scorer.W, _rules.Environment, rule.Tiles, t)
                        && _foes.Any(f => f.Pos == t || Surfaces.Neighbours(t).Contains(f.Pos)))))
                    aims.Add(aim);
            }
        return _reactionAims[ability.Id] = aims.ToImmutable();
    }

    private static IEnumerable<Tile> Neighbours(Tile t) => [t with { Y = t.Y - 1 }, t with { X = t.X + 1 }, t with { Y = t.Y + 1 }, t with { X = t.X - 1 }];

    /// <summary>
    /// What using <paramref name="usable"/> at <paramref name="aim"/> is worth: for each affected unit, what the ability's
    /// unit blocks (all its attacks, for a weapon attack: <see cref="Expected"/>) are expected to do to it (<see cref="Scorer.Worth"/>); plus its
    /// tile blocks at the aim (a zone's effects on the units in it, a summon); less a little for dropping a concentration.
    /// </summary>
    private double Value(Usable usable, BattleWriter w, UnitState me, Tile aim, ImmutableArray<UnitId> affected)
    {
        var ability = usable.Ability;
        var unitEffects = Scorer.UnitBlocks(ability.Effects);
        var tileEffects = (ability.Effects.IsDefault ? [] : ability.Effects).Where(e => e.OnTile).ToImmutableArray();

        var st = new Strike(w, me, ability, aim, neutral: false);
        var value = 0.0;
        if (!unitEffects.IsEmpty)
            foreach (var id in affected)
            {
                var target = w.State.Units[id];
                value += _scorer.Worth(me, target, Expected(ability, me, st, target), _p);
            }
        foreach (var effect in tileEffects)
            value += _scorer.TileWorth(effect, st, aim, _p);
        if (ability.Cost.Concentration && me.Concentrating is not null)
            value -= 2;
        return value;
    }

    /// <summary>
    /// What <paramref name="ability"/>'s unit blocks are expected to do to <paramref name="target"/>: once, or for a weapon
    /// attack all of the user's attacks at it (<see cref="Scorer.StrikeOutcomes"/>, a <see cref="AbilityDef.RiderOnce"/>
    /// rider counted once).
    /// </summary>
    private Outcome Expected(AbilityDef ability, UnitState me, Strike st, UnitState target)
        => ability.IsWeaponAttack
            ? _scorer.StrikeOutcomes(ability, me.Sheet.Attacks, st, target)[^1]
            : _scorer.Walk(Scorer.UnitBlocks(ability.Effects), st, target);

    /// <summary>
    /// True when <paramref name="usable"/>'s <paramref name="action"/>, taken by the unit as it stands in <paramref name="at"/>,
    /// may drop <paramref name="unit"/>: its damage can reach the HP left.
    /// </summary>
    private bool MayDrop(Usable usable, Action action, (BattleState State, BattleWriter W) at, UnitId unit)
    {
        var me = at.State.Units[_me.Id];
        var target = at.State.Units[unit];
        var outcome = Expected(usable.Ability, me, new Strike(at.W, me, usable.Ability, action.Aim, neutral: false), target);
        return outcome.Damage.AtLeast(Math.Max(1, target.Hp + target.TempHp)) > 0;
    }
}
