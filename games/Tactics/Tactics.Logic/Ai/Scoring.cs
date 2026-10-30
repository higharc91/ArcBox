using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>
/// What an action is expected to do to one unit (spec §5.2 step 4), computed from the effect blocks with no dice rolled:
/// the distribution of the damage it takes, the HP it is expected to be healed and given as temporary HP, the chance it
/// gets up from Downed, the expected change to how well it fights (<see cref="Offense"/>) and how well it holds up
/// (<see cref="Defense"/>), both in turns' worth (−1 is a lost turn), the tiles it is pushed and the expected damage that
/// bounces on to its side (<see cref="Splash"/>).
/// </summary>
internal sealed record Outcome(DamageDist Damage, double Heal = 0, double TempHp = 0, double Revive = 0, double Offense = 0,
    double Defense = 0, double Push = 0, double Splash = 0)
{
    /// <summary>Nothing happens.</summary>
    public static Outcome None { get; } = new(DamageDist.Zero);

    /// <summary>Both happen, independently: the damage adds up.</summary>
    public Outcome Then(Outcome next) => ReferenceEquals(next, None) ? this : ReferenceEquals(this, None) ? next : new(
        Damage.Plus(next.Damage), Heal + next.Heal, TempHp + next.TempHp, 1 - (1 - Revive) * (1 - next.Revive),
        Offense + next.Offense, Defense + next.Defense, Push + next.Push, Splash + next.Splash);

    /// <summary>One of several outcomes, each with its probability (they add up to 1).</summary>
    public static Outcome Mix(IReadOnlyList<(double Weight, Outcome Outcome)> parts)
    {
        double W(Func<Outcome, double> f) => parts.Sum(p => p.Weight * f(p.Outcome));
        return new(DamageDist.Mix([.. parts.Select(p => (p.Weight, p.Outcome.Damage))]), W(o => o.Heal), W(o => o.TempHp),
            W(o => o.Revive), W(o => o.Offense), W(o => o.Defense), W(o => o.Push), W(o => o.Splash));
    }
}

/// <summary>
/// One unit using effects: the working copy it is imagined in (the user possibly moved to <see cref="From"/>), the ability
/// and its aim, and the once-per-turn passives already counted. <see cref="Neutral"/> ignores the map (cover, high ground):
/// used to rate a unit's damage per round in general.
/// </summary>
internal sealed class Strike(BattleWriter w, UnitState user, AbilityDef? ability, Tile? aim, bool neutral)
{
    public BattleWriter W { get; } = w;
    public UnitState User { get; } = user;
    public Tile From => User.Pos;
    public AbilityDef? Ability { get; } = ability;
    public Tile? Aim { get; } = aim;
    public bool Neutral { get; } = neutral;
    public HashSet<string> Fired { get; } = new(StringComparer.Ordinal);
    public EffectContext Context { get; } = new(w, w.Rules, user.Id, null, aim, ability);
}

/// <summary>
/// The AI's view of a battle while it plans one turn: expected outcomes of effect blocks (<see cref="Walk"/>), what they
/// are worth to a profile (<see cref="Worth"/>), each unit's damage per round (<see cref="Dpr"/>) and the threat maps, with
/// every costly lookup (modifiers by unit and tile, sight lines, dice distributions) remembered for the plan. Draws no die.
/// </summary>
internal sealed class Scorer
{
    private readonly Dictionary<(UnitId, Tile), StaticMods[]> _sources = [];
    private readonly Dictionary<(Tile, Tile, bool), SightResult> _sight = [];
    private readonly Dictionary<(int, int), DamageDist> _dice = [];
    private readonly Dictionary<UnitId, double> _dpr = [];
    private readonly Dictionary<(UnitId, UnitId), ImmutableArray<ThreatOption>> _options = [];
    private readonly Dictionary<ConditionId, (double Offense, double Defense, double OffenseTurns, double DefenseTurns)> _conditions = [];
    private readonly Dictionary<Side, ThreatMap> _threat = [];
    private readonly ImmutableSortedSet<Tile> _standing;
    private readonly ImmutableSortedSet<Tile> _obscured;

    /// <summary>
    /// A scorer for <paramref name="state"/>. With <paramref name="planner"/>, that unit is left out of the units that stand
    /// in the way of sight lines: it is always at one end of the lines it is scored on, wherever it moves.
    /// </summary>
    public Scorer(BattleState state, RulesPack rules, UnitId? planner = null)
    {
        State = state;
        Rules = rules;
        W = new BattleWriter(state, rules);
        Terrain = W.Terrain;
        _standing = [.. state.Units.Values.Where(u => u.Life == LifeState.Up && u.Id != planner).Select(u => u.Pos)];
        _obscured = ZoneRules.Obscured(state);
        ItemAbilities = [.. rules.Items.Values.Select(i => i.Use)];
    }

    public BattleState State { get; }
    public RulesPack Rules { get; }
    public BattleWriter W { get; }
    public Terrain Terrain { get; }

    /// <summary>Abilities that only run through <c>common.use_item</c>.</summary>
    public ImmutableSortedSet<AbilityId> ItemAbilities { get; }

    // ---- Lookups ----------------------------------------------------------------------------------------------------

    /// <summary>Each source of static modifiers on <paramref name="unit"/> were it standing on <paramref name="at"/>.</summary>
    public StaticMods[] Sources(UnitId unit, Tile at)
    {
        if (_sources.TryGetValue((unit, at), out var found))
            return found;
        var u = State.Units[unit];
        var state = u.Pos == at || State.Zones.IsDefaultOrEmpty
            ? State
            : State with { Units = State.Units.SetItem(unit, u with { Pos = at }) };
        return _sources[(unit, at)] = [.. Modifiers.Sources(state, unit, Rules)];
    }

    /// <summary>The static modifiers on <paramref name="unit"/> were it standing on <paramref name="at"/>.</summary>
    public StaticMods Mods(UnitId unit, Tile at) => Modifiers.Sum(Sources(unit, at));

    /// <summary>The static modifiers on <paramref name="unit"/> where it stands.</summary>
    public StaticMods Mods(UnitState unit) => Mods(unit.Id, unit.Pos);

    /// <summary>True when the unit is Up and not incapacitated where it stands.</summary>
    public bool CanAct(UnitState unit) => unit.Life == LifeState.Up && !Mods(unit).Incapacitated;

    /// <summary>A sight line (<see cref="Sight.Trace"/>) with the battle's standing units and obscuring zones.</summary>
    public SightResult Trace(Tile from, Tile to, bool ranged)
    {
        if (!_sight.TryGetValue((from, to, ranged), out var result))
            _sight[(from, to, ranged)] = result = Sight.Trace(Terrain, from, to, _standing, ranged, _obscured);
        return result;
    }

    /// <summary>The cover bonus a unit on <paramref name="to"/> has against something from <paramref name="from"/>.</summary>
    public int Cover(Tile from, Tile to, bool ranged) => from == to ? 0 : Modifiers.CoverBonus(Trace(from, to, ranged).Cover);

    /// <summary>The distribution of <paramref name="dice"/>, the dice rolled twice for a crit.</summary>
    public DamageDist Dist(Dice dice, bool crit)
    {
        var count = crit ? dice.Count * 2 : dice.Count;
        if (!_dice.TryGetValue((count, dice.Sides), out var dist))
            _dice[(count, dice.Sides)] = dist = DamageDist.Dice(count, dice.Sides);
        return dist.Shift(dice.Bonus);
    }

    /// <summary>True when the two units fight on different sides.</summary>
    public static bool Hostile(UnitState a, UnitState b) => a.Sheet.Side != b.Sheet.Side;

    // ---- Expected outcomes of effect blocks -------------------------------------------------------------------------

    /// <summary>
    /// What <paramref name="effects"/> are expected to do to <paramref name="target"/> (spec §5.2): attack and save chances
    /// from the exact d20 maths with advantage, cover and high ground, damage from the dice with crits and resistances,
    /// conditions by what their modifiers do. <paramref name="crit"/> and <paramref name="half"/> are the branch it runs in;
    /// <paramref name="last"/> the roll it follows.
    /// </summary>
    public Outcome Walk(ImmutableArray<EffectDef> effects, Strike st, UnitState target, bool crit = false, bool half = false,
        RollResult? last = null)
    {
        var outcome = Outcome.None;
        foreach (var effect in effects.IsDefault ? [] : effects)
            outcome = outcome.Then(One(effect, st, target, crit, half, last));
        return outcome;
    }

    private Outcome One(EffectDef effect, Strike st, UnitState target, bool crit, bool half, RollResult? last)
    {
        switch (effect)
        {
            case AttackEffect attack:
                return Attack(attack, st, target);
            case SaveEffect save:
                return Save(save, st, target);
            case DamageEffect damage:
                return new Outcome(Damage(damage, st, target, crit, half));
            case HealEffect heal:
                {
                    var amount = Math.Max(0, heal.Dice.At(st.Context.Level).Average + st.Context.Sum(heal.Bonus));
                    return new Outcome(DamageDist.Zero, Heal: amount, Revive: target.Life == LifeState.Downed && amount > 0 ? 1 : 0);
                }
            case TempHpEffect temp:
                return new Outcome(DamageDist.Zero,
                    TempHp: Math.Max(0, temp.Dice.At(st.Context.Level).Average + st.Context.Sum(temp.Bonus)));
            case ReviveEffect:
                return target.Life == LifeState.Downed ? new Outcome(DamageDist.Zero, Revive: 1) : Outcome.None;
            case HelpEffect:
                return target.Life == LifeState.Downed ? new Outcome(DamageDist.Zero, Revive: 1)
                    : Has(target, RulesPack.Helped.Id) ? Outcome.None
                    : new Outcome(DamageDist.Zero, Offense: 0.1);
            case ApplyEffect apply:
                {
                    if (Has(target, apply.Condition))
                        return Outcome.None;   // it has it already: nothing gained
                    var benefit = ConditionBenefit(apply.Condition);
                    var turns = Turns(apply.Rounds, apply.UntilSave);
                    return new Outcome(DamageDist.Zero, Offense: benefit.Offense * Math.Min(turns, benefit.OffenseTurns),
                        Defense: benefit.Defense * Math.Min(turns, benefit.DefenseTurns));
                }
            case StatModEffect statMod:
                {
                    if (st.Ability is { } source && Has(target, new ConditionId("statMod." + source.Id.Value)))
                        return Outcome.None;
                    var (offense, defense) = ModsBenefit(statMod.Mods);
                    var turns = Turns(statMod.Rounds, untilSave: false);
                    return new Outcome(DamageDist.Zero, Offense: offense * turns, Defense: defense * turns);
                }
            case RemoveEffect remove:
                return Removing(target, c => c.Id == remove.Condition);
            case CleanseEffect cleanse:
                return Removing(target, c => Rules.Conditions.TryGetValue(c.Id, out var def) && def.Tags.Overlaps(cleanse.Tags));
            case PushEffect push:
                return new Outcome(DamageDist.Zero, Push: push.Distance);
            case PullEffect pull:
                return new Outcome(DamageDist.Zero, Push: pull.Distance);
            case TauntEffect taunt:
                return new Outcome(DamageDist.Zero, Offense: -0.3 * Turns(taunt.Rounds, untilSave: false));
            case RepeatEffect repeat:
                {
                    var once = Walk(repeat.Effects, st, target, crit, half, last);
                    var total = Outcome.None;
                    for (var i = 0; i < repeat.Times; i++)
                        total = total.Then(once);
                    return total;
                }
            case ChainEffect chain:
                {
                    var first = Walk(chain.Effects, st, target, crit, half, last);
                    return first with { Splash = first.Splash + first.Damage.Mean * chain.Times * 0.5 };
                }
            case IfEffect check:
                {
                    var args = new HookArgs(st.User.Id, target.Id, last, Tile: target.Pos, Ability: st.Ability?.Id);
                    return Walk(Hooks.Holds(check.When, st.W, args, Rules) ? check.Then : check.Else, st, target, crit, half, last);
                }
            case RandomEffect random:
                {
                    var total = random.Table.Sum(e => Math.Max(0, e.Weight));
                    return total <= 0 ? Outcome.None : Outcome.Mix([.. random.Table.Select(e =>
                        ((double)Math.Max(0, e.Weight) / total, Walk(e.Effects, st, target, crit, half, last)))]);
                }
            default:
                return Outcome.None;   // movement, terrain, resources, turn order: no value of their own to the target
        }
    }

    /// <summary>An attack roll (see <see cref="AttackStep"/>): hit, crit and miss branches, with the user's on-hit passives.</summary>
    private Outcome Attack(AttackEffect def, Strike st, UnitState target)
    {
        var user = st.User;
        var sheet = user.Sheet;
        var weapon = def.Kind == "weapon";
        var ranged = weapon ? sheet.Weapon.Ranged : (st.Ability?.Target.Range ?? 1) > 1;
        var statModifier = def.Stat is { } stat ? Stats.Mod(sheet.Score(stat))
            : weapon ? Stats.Mod(sheet.Score(AttackBlock.WeaponStat(sheet)))
            : EffectContext.CastingModifierOf(sheet);
        var userSources = Sources(user.Id, user.Pos);
        var targetSources = Sources(target.Id, target.Pos);
        var modifier = statModifier + Stats.Proficiency(sheet.Level) + def.Bonus + Modifiers.Sum(userSources).Attack
                       + (st.Neutral ? 0 : Reach.HighGroundBonus(Terrain, user.Pos, target.Pos));

        var (advantages, disadvantages) = (0, 0);
        foreach (var mods in userSources)
        {
            advantages += (mods.OwnAttacks == Edge.Advantage ? 1 : 0) + (mods.Invisible ? 1 : 0);
            disadvantages += mods.OwnAttacks == Edge.Disadvantage ? 1 : 0;
        }
        foreach (var mods in targetSources)
        {
            var against = ranged ? mods.AttackedRanged : mods.AttackedMelee;
            advantages += against == Edge.Advantage ? 1 : 0;
            disadvantages += (against == Edge.Disadvantage ? 1 : 0) + (mods.Invisible ? 1 : 0);
        }
        if (!st.Neutral && !ranged && Modifiers.Flanked(st.W.State, user.Id, target.Id, Rules))
            advantages++;
        if (!st.Neutral && TauntBlock.TauntedAway(st.W.State, user.Id, target.Id))
            disadvantages++;
        if (user.Conditions.Any(c => c.Id == RulesPack.Helped.Id))
            advantages++;
        var edge = Rolls.Combine(advantages, disadvantages);

        var cover = !st.Neutral && (ranged || def.Kind == "spell") ? Cover(user.Pos, target.Pos, ranged: true) : 0;
        var targetMods = Modifiers.Sum(targetSources);
        var ac = target.Sheet.Ac + targetMods.Ac + cover;
        var pHit = Odds.Hit(modifier, ac, edge);
        var pCrit = targetMods.CritWhenAdjacent && Reach.Distance(user.Pos, target.Pos) == 1 ? pHit : Odds.Crit(edge);

        var hitRoll = new RollResult([], 0, modifier, 0, ac, Success: true, Crit: false, Fumble: false);
        var critRoll = hitRoll with { Crit = true };
        var hooks = OnHitPassives(def, st, target, edge, hitRoll);
        var onHit = def.OnHit.IsDefault ? [] : def.OnHit;
        var hit = Walk(onHit, st, target, last: hitRoll).Then(Walk(hooks, st, target, last: hitRoll));
        var critical = Walk(def.OnCrit.IsDefaultOrEmpty ? onHit : onHit.AddRange(def.OnCrit), st, target, crit: true, last: critRoll)
            .Then(Walk(hooks, st, target, crit: true, last: critRoll));
        var miss = Walk(def.OnMiss, st, target, last: hitRoll with { Success = false });
        return Outcome.Mix([(pCrit, critical), (pHit - pCrit, hit), (1 - pHit, miss)]);
    }

    /// <summary>
    /// The effects of the user's own passives that would fire on hitting <paramref name="target"/> (sneak attack, divine
    /// strike, …): their <see cref="PassiveDef.When"/> is checked as the engine would (<see cref="Hooks.Holds"/>). A
    /// once-per-turn passive counts on the first attack it could fire on only.
    /// </summary>
    private ImmutableArray<EffectDef> OnHitPassives(AttackEffect def, Strike st, UnitState target, Edge edge, RollResult roll)
    {
        var effects = ImmutableArray.CreateBuilder<EffectDef>();
        foreach (var passive in Modifiers.Passives(st.User, Rules))
        {
            if (passive.Hook != HookKind.OnAttackHit || passive.Effects.IsDefaultOrEmpty || passive.Target != PassiveTarget.Other
                || passive.OncePerTurn && (st.User.UsedThisTurn.Contains(passive.Id) || st.Fired.Contains(passive.Id))
                || passive.UsesPerBattle is { } uses && st.User.UsesLeft.GetValueOrDefault(passive.Id, uses) < 1)
                continue;
            var args = new HookArgs(st.User.Id, target.Id, roll, Tile: target.Pos, Ability: st.Ability?.Id, AttackKind: def.Kind,
                Edge: edge);
            if (passive.When is { } when && !Hooks.Holds(when, st.W, args, Rules))
                continue;
            if (passive.OncePerTurn)
                st.Fired.Add(passive.Id);
            effects.AddRange(passive.Effects);
        }
        return effects.ToImmutable();
    }

    /// <summary>A saving throw (see <see cref="SaveBlock"/>): fail and success branches, half damage, evasion.</summary>
    private Outcome Save(SaveEffect def, Strike st, UnitState target)
    {
        var sheet = target.Sheet;
        var sources = Sources(target.Id, target.Pos);
        var mods = Modifiers.Sum(sources);
        var dc = def.Dc ?? (def.DcStat is { } dcStat ? st.Context.DcFor(dcStat) : st.Context.SaveDc);
        var proficient = sheet.SaveProficiencies.Contains(def.Stat) || mods.SaveProficiencies?.Contains(def.Stat) == true;
        var origin = st.Ability?.Target.Area.Shape is AreaShape.Burst or AreaShape.Cross or AreaShape.Ring && st.Aim is { } aim
            ? aim
            : st.User.Pos;
        var cover = def.Stat == Stat.Dex && !st.Neutral ? Cover(origin, target.Pos, ranged: false) : 0;
        var bonus = Stats.Mod(sheet.Score(def.Stat)) + (proficient ? Stats.Proficiency(sheet.Level) : 0)
                    + (mods.Saves?.GetValueOrDefault(def.Stat) ?? 0) + cover;
        var advantages = sources.Count(m => m.SaveEdges?.GetValueOrDefault(def.Stat) == Edge.Advantage)
                         + (target.Conditions.Any(c => c.Id == RulesPack.Helped.Id) ? 1 : 0);
        var disadvantages = sources.Count(m => m.SaveEdges?.GetValueOrDefault(def.Stat) == Edge.Disadvantage);
        var pFail = mods.AutoFailSaves?.Contains(def.Stat) == true
            ? 1
            : Odds.SaveFail(bonus, dc, Rolls.Combine(advantages, disadvantages));

        var evades = def.Stat == Stat.Dex && def.HalfOnSuccess && mods.Evasion;
        var failed = new RollResult([], 0, bonus, 0, dc, Success: false, Crit: false, Fumble: false);
        var saved = failed with { Success = true };
        var onFail = Walk(def.OnFail, st, target, half: evades, last: failed);
        var onSuccess = Walk(def.OnSuccess, st, target, last: saved);
        if (def.HalfOnSuccess && !evades && !def.OnFail.IsDefault)
            onSuccess = Walk([.. def.OnFail.OfType<DamageEffect>()], st, target, half: true, last: saved).Then(onSuccess);
        return Outcome.Mix([(pFail, onFail), (1 - pFail, onSuccess)]);
    }

    /// <summary>A damage block's damage (see <see cref="DamageBlock"/> and <see cref="Hp.Damage"/>).</summary>
    private DamageDist Damage(DamageEffect def, Strike st, UnitState target, bool crit, bool half)
    {
        var sheet = st.User.Sheet;
        var dist = DamageDist.Zero;
        var flat = st.Context.Sum(def.Bonus);
        if (def.Weapon)
        {
            dist = Dist(sheet.Weapon.Damage, crit);
            flat += Stats.Mod(sheet.Score(AttackBlock.WeaponStat(sheet))) + Mods(st.User).Damage;
        }
        if (def.Dice is { } dice)
            dist = dist.Plus(Dist(dice.At(st.Context.Level), crit));
        dist = dist.Shift(flat);
        if (dist.Min < 0)
            dist = dist.Map(v => Math.Max(0, v));
        if (half)
            dist = dist.Map(v => v / 2);

        if ((def.Type ?? (def.Weapon ? sheet.Weapon.DamageType : null)) is { } type)
        {
            var mods = Mods(target);
            if (target.Sheet.Immune.Contains(type) || mods.Immune?.Contains(type) == true)
                return DamageDist.Zero;
            if (target.Sheet.Resist.Contains(type) || mods.Resist?.Contains(type) == true)
                dist = dist.Map(v => v / 2);
            if (target.Sheet.Vulnerable.Contains(type))
                dist = dist.Map(v => v * 2);
        }
        return dist;
    }

    /// <summary>Ending the target's conditions that match: worth the opposite of what they do to it.</summary>
    private Outcome Removing(UnitState target, Func<ActiveCondition, bool> matches)
    {
        var (offense, defense) = (0.0, 0.0);
        foreach (var id in target.Conditions.Where(matches).Select(c => c.Id).Distinct())
        {
            var benefit = ConditionBenefit(id);
            offense -= benefit.Offense * Math.Min(1.5, benefit.OffenseTurns);
            defense -= benefit.Defense * Math.Min(1.5, benefit.DefenseTurns);
        }
        return new Outcome(DamageDist.Zero, Offense: offense, Defense: defense);
    }

    /// <summary>True when the unit has the condition on it (not counting implied ones).</summary>
    private static bool Has(UnitState unit, ConditionId id) => !unit.Conditions.IsDefault && unit.Conditions.Any(c => c.Id == id);

    /// <summary>
    /// How many of the target's turns an effect lasting <paramref name="rounds"/> is expected to matter for: until a save,
    /// 1.5; for the whole battle, 3; until the end of the current turn, a quarter; otherwise the rounds, at most 3.
    /// </summary>
    private static double Turns(int? rounds, bool untilSave)
        => untilSave ? 1.5 : rounds is not { } n ? 3 : n <= 0 ? 0.25 : Math.Min(n, 3);

    /// <summary>
    /// What a condition does to its holder (its modifiers and those it implies), split into fighting and holding up, and how
    /// many of the holder's turns each part can matter for: one that ends itself as the holder's turn starts (prone, Dodge)
    /// never touches how it fights and guards it for one round.
    /// </summary>
    private (double Offense, double Defense, double OffenseTurns, double DefenseTurns) ConditionBenefit(ConditionId id)
    {
        if (_conditions.TryGetValue(id, out var found))
            return found;
        var (offense, defense, offenseTurns, defenseTurns) = (0.0, 0.0, 3.0, 3.0);
        var seen = new List<ConditionId>();
        for (ConditionId? next = id; next is { } c && !seen.Contains(c) && Rules.Conditions.TryGetValue(c, out var def); next = def.Implies)
        {
            seen.Add(c);
            var statics = new List<StaticMods>();
            if (def.Static is { } own)
                statics.Add(own);
            foreach (var passive in def.Passives.IsDefault ? [] : def.Passives)
            {
                if (passive.Static is { } passiveMods)
                    statics.Add(passiveMods);
                var effects = passive.Effects.IsDefault ? [] : passive.Effects;
                if (passive.Hook == HookKind.OnTurnStart && effects.Any(e => e is DamageEffect))
                    defense -= 0.3;
                if (passive.Hook == HookKind.OnTurnStart && effects.Any(e => e is RemoveEffect r && r.Condition == id))
                    (offenseTurns, defenseTurns) = (0, 1);
            }
            foreach (var mods in statics)
            {
                var (o, d) = ModsBenefit(mods);
                offense += o;
                defense += d;
            }
        }
        return _conditions[id] = (offense, defense, offenseTurns, defenseTurns);
    }

    /// <summary>
    /// What static modifiers do to their holder, in turns' worth: how much better it fights (an incapacitated unit loses its
    /// turn: −1) and how much better it holds up (attacks against it with advantage: −0.3).
    /// </summary>
    public static (double Offense, double Defense) ModsBenefit(StaticMods m)
    {
        static double EdgeWorth(Edge edge, double worth) => edge switch
        {
            Edge.Advantage => worth,
            Edge.Disadvantage => -worth,
            _ => 0,
        };

        var offense = (m.Incapacitated ? -1.0 : 0) + (m.CantMove ? -0.3 : 0) + EdgeWorth(m.OwnAttacks, 0.3)
                      + 0.05 * m.Attack + 0.04 * m.Damage + 0.8 * m.ExtraActions + 0.04 * m.Move + (m.Invisible ? 0.3 : 0);
        var defense = 0.06 * m.Ac - EdgeWorth(m.AttackedMelee, 0.3) - EdgeWorth(m.AttackedRanged, 0.15) + (m.Invisible ? 0.3 : 0)
                      - 0.1 * (m.AutoFailSaves?.Count ?? 0) - (m.CritWhenAdjacent ? 0.4 : 0)
                      + 0.01 * (m.Saves?.Values.Sum() ?? 0)
                      + 0.03 * (m.SaveEdges?.Values.Sum(e => EdgeWorth(e, 1)) ?? 0)
                      + 0.05 * ((m.Resist?.Count ?? 0) + (m.Immune?.Count ?? 0)) + (m.Evasion ? 0.05 : 0)
                      + 0.02 * (m.SaveProficiencies?.Count ?? 0);
        return (offense, defense);
    }

    // ---- What outcomes are worth ------------------------------------------------------------------------------------

    /// <summary>
    /// What <paramref name="outcome"/> on <paramref name="target"/> is worth to <paramref name="user"/> playing
    /// <paramref name="p"/> (spec §5.2, §5.4), in HP-like points:
    /// <list type="bullet">
    /// <item>A hostile: damage (capped at what it has left, more for a hurt one with focus fire), the chance it drops times
    /// what it is worth (<see cref="KillWorth"/>), and control (a lost turn costs it its damage per round). A Downed one is
    /// worth nothing unless the profile is cruel.</item>
    /// <item>An ally (or the user): healing (double below half HP), getting up from Downed, buffs; hurting it costs one and a
    /// half times what hurting a hostile is worth.</item>
    /// <item>A neutral: hurting it costs a little. A protected unit counts the objective weight on top.</item>
    /// </list>
    /// </summary>
    public double Worth(UnitState user, UnitState target, Outcome outcome, AiProfile p)
    {
        var hp = target.Hp + target.TempHp;
        var maxHp = Math.Max(1, target.Sheet.MaxHp);
        double value;
        if (target.Sheet.Side == Side.Neutral || user.Sheet.Side == Side.Neutral && target.Id != user.Id)
            value = -0.5 * outcome.Damage.Mean;
        else if (Hostile(user, target))
        {
            if (target.Life == LifeState.Downed)
                return p.Cruel ? p.Kill * outcome.Damage.AtLeast(1) * KillWorth(target) / Math.Max(1, target.Countdown) : 0;
            var pKill = hp > 0 ? outcome.Damage.AtLeast(hp) : 1;
            var focus = 1 + p.FocusFire * (1 - (double)target.Hp / maxHp);
            value = p.Damage * outcome.Damage.MeanCapped(hp) * focus + p.Kill * pKill * KillWorth(target)
                    + p.Damage * outcome.Splash
                    + (1 - pKill) * p.Control * (-outcome.Offense * Math.Max(1, Dpr(target)) - outcome.Defense * Incoming(target)
                                                 + 0.3 * outcome.Push)
                    - p.Damage * Math.Min(outcome.Heal, target.Sheet.MaxHp - target.Hp);
        }
        else if (target.Life == LifeState.Downed)
        {
            value = p.Revive * outcome.Revive * KillWorth(target) + p.Heal * 0.5 * Math.Min(outcome.Heal, maxHp)
                    - 0.5 * outcome.Damage.AtLeast(1) * KillWorth(target);
        }
        else
        {
            var urgency = target.Hp * 2 < maxHp ? 2 : 1;
            var pKill = hp > 0 ? outcome.Damage.AtLeast(hp) : 0;
            value = p.Heal * urgency * Math.Min(outcome.Heal, target.Sheet.MaxHp - target.Hp)
                    + p.Heal * 0.5 * Math.Max(0, outcome.TempHp - target.TempHp)
                    + p.Control * (outcome.Offense * Math.Max(2, Dpr(target)) + outcome.Defense * Incoming(target) - 0.3 * outcome.Push)
                    - 1.5 * (p.Damage * (outcome.Damage.MeanCapped(hp) + outcome.Splash) + p.Kill * pKill * KillWorth(target));
        }
        return target.Protected ? value * (1 + p.Objective) : value;
    }

    /// <summary>What taking a unit out of the fight is worth: two rounds of its damage, plus a little for any unit.</summary>
    public double KillWorth(UnitState unit) => 2 * Dpr(unit) + 2;

    /// <summary>
    /// The damage the unit's foes are expected to deal to it next round: half the threat map against its side on its tile
    /// (not every foe that could strike it will).
    /// </summary>
    public double Incoming(UnitState unit) => 0.5 * ThreatAgainst(unit.Sheet.Side).At(unit.Pos);

    /// <summary>The threat map against <paramref name="side"/>, built once per plan.</summary>
    public ThreatMap ThreatAgainst(Side side)
    {
        if (!_threat.TryGetValue(side, out var map))
            _threat[side] = map = ThreatMap.Build(this, side, Victim(side));
        return map;
    }

    // ---- Damage per round -------------------------------------------------------------------------------------------

    /// <summary>
    /// The unit's expected damage per round against a typical foe (<see cref="Victim"/>): its best action (an attack or
    /// a damaging ability it can pay for) plus its best bonus action.
    /// </summary>
    public double Dpr(UnitState unit)
    {
        if (_dpr.TryGetValue(unit.Id, out var dpr))
            return dpr;
        var victim = Victim(Foes(unit.Sheet.Side));
        var options = victim is null ? [] : Options(unit, victim);
        dpr = options.Where(o => !o.Bonus).Select(o => o.Ev).DefaultIfEmpty(0).Max()
              + options.Where(o => o.Bonus).Select(o => o.Ev).DefaultIfEmpty(0).Max();
        return _dpr[unit.Id] = dpr;
    }

    /// <summary>The side a unit's foes are on (neutrals count the party as their foes).</summary>
    private static Side Foes(Side side) => side == Side.Party ? Side.Enemy : Side.Party;

    /// <summary>
    /// A typical unit of <paramref name="side"/> still in the battle: the one with the middle AC (ties by id), or null when
    /// there is none.
    /// </summary>
    public UnitState? Victim(Side side)
    {
        var units = State.Units.Values.Where(u => u.Sheet.Side == side && DownedRules.InBattle(u))
            .OrderBy(u => u.Sheet.Ac + Mods(u).Ac).ThenBy(u => u.Id).ToArray();
        return units.Length == 0 ? null : units[units.Length / 2];
    }

    /// <summary>
    /// The ways <paramref name="attacker"/> can hurt <paramref name="victim"/> on its turn, ignoring the map: the Attack
    /// action and every damaging ability it knows and can pay for, each with its expected damage and its reach.
    /// </summary>
    public ImmutableArray<ThreatOption> Options(UnitState attacker, UnitState victim)
    {
        if (_options.TryGetValue((attacker.Id, victim.Id), out var found))
            return found;
        var options = ImmutableArray.CreateBuilder<ThreatOption>();
        var fresh = attacker with { Economy = Economy.Fresh(attacker.Sheet, Mods(attacker)) };
        var weapon = attacker.Sheet.Weapon;
        if (Rules.Abilities.TryGetValue(RulesPack.CommonAttack.Id, out var attack))
        {
            var ev = Expected(fresh, attack, victim, Math.Max(1, attacker.Sheet.Attacks));
            options.Add(new ThreatOption(ev, weapon.Ranged ? weapon.Range : 1, Melee: !weapon.Ranged, weapon.Reach, Bonus: false,
                AttackModifier(fresh, attack)));
        }
        foreach (var id in attacker.Sheet.Abilities)
        {
            if (!Rules.Abilities.TryGetValue(id, out var ability) || ItemAbilities.Contains(id) || ability.Cost.Reaction > 0
                || ability.Cost.Action == 0 && ability.Cost.Bonus == 0 || !Deals(ability.Effects)
                || ability.Target.Kind is not (TargetKind.Enemy or TargetKind.AnyUnit or TargetKind.Tile or TargetKind.Self)
                || Battle.CostRefusal(fresh, ability) is not null)
                continue;
            var self = ability.Target.Kind == TargetKind.Self;
            var range = self ? Math.Max(1, ability.Target.Area.Size) : Math.Max(1, ability.Target.Range);
            var ev = Expected(fresh, ability, victim, ability.IsWeaponAttack ? Math.Max(1, attacker.Sheet.Attacks) : 1);
            options.Add(new ThreatOption(ev, range, Melee: range <= 1 && !self, Reach: false, Bonus: ability.Cost.Action == 0,
                AttackModifier(fresh, ability)));
        }
        return _options[(attacker.Id, victim.Id)] = options.ToImmutable();
    }

    /// <summary>The expected damage of <paramref name="ability"/> (its effects <paramref name="times"/> over) on <paramref name="victim"/>.</summary>
    private double Expected(UnitState attacker, AbilityDef ability, UnitState victim, int times)
    {
        var st = new Strike(W, attacker, ability, victim.Pos, neutral: true);
        var effects = ability.Effects.IsDefault ? [] : ability.Effects;
        var total = Outcome.None;
        for (var i = 0; i < times; i++)
            total = total.Then(Walk(effects, st, victim));
        return total.Damage.Mean + total.Splash;
    }

    /// <summary>The attack roll modifier of the ability's first attack, if it makes one (to rescale its odds by AC and cover).</summary>
    private static int? AttackModifier(UnitState attacker, AbilityDef ability)
    {
        if (FirstAttack(ability.Effects) is not { } def)
            return null;
        var sheet = attacker.Sheet;
        var stat = def.Stat is { } s ? Stats.Mod(sheet.Score(s))
            : def.Kind == "weapon" ? Stats.Mod(sheet.Score(AttackBlock.WeaponStat(sheet)))
            : EffectContext.CastingModifierOf(sheet);
        return stat + Stats.Proficiency(sheet.Level) + def.Bonus;
    }

    // ---- Effect-tree queries ----------------------------------------------------------------------------------------

    /// <summary>Every block in the tree, nested branches included.</summary>
    public static IEnumerable<EffectDef> All(ImmutableArray<EffectDef> effects)
    {
        foreach (var effect in effects.IsDefault ? [] : effects)
        {
            yield return effect;
            var children = effect switch
            {
                AttackEffect a => All(a.OnHit).Concat(All(a.OnMiss)).Concat(All(a.OnCrit)),
                SaveEffect s => All(s.OnFail).Concat(All(s.OnSuccess)),
                RepeatEffect r => All(r.Effects),
                ChainEffect c => All(c.Effects),
                IfEffect i => All(i.Then).Concat(All(i.Else)),
                RandomEffect r => r.Table.SelectMany(e => All(e.Effects)),
                _ => [],
            };
            foreach (var child in children)
                yield return child;
        }
    }

    /// <summary>True when the tree deals damage.</summary>
    public static bool Deals(ImmutableArray<EffectDef> effects) => All(effects).Any(e => e is DamageEffect);

    /// <summary>The first attack roll in the tree, if any.</summary>
    public static AttackEffect? FirstAttack(ImmutableArray<EffectDef> effects) => All(effects).OfType<AttackEffect>().FirstOrDefault();
}

/// <summary>
/// One way a unit can hurt a foe next turn: the expected damage, its reach in tiles (<see cref="Melee"/>: weapon reach, with
/// a <see cref="Reach"/> weapon two in a line), whether it takes the bonus action, and the attack roll's modifier if it
/// makes one.
/// </summary>
internal sealed record ThreatOption(double Ev, int Range, bool Melee, bool Reach, bool Bonus, int? AttackModifier);
