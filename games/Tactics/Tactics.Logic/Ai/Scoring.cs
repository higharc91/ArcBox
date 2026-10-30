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
    public HashSet<ConditionId> Quarrying { get; } = [];
    public EffectContext Context { get; } = new(w, w.Rules, user.Id, null, aim, ability);
}

/// <summary>
/// The AI's view of a battle while it plans one turn: expected outcomes of effect blocks (<see cref="Walk"/>), what they
/// are worth to a profile (<see cref="Worth"/>), each unit's damage per round (<see cref="Dpr"/>) and the threat maps, with
/// every costly lookup (modifiers by unit and tile, sight lines, dice distributions) remembered for the plan. Draws no die.
/// </summary>
internal sealed partial class Scorer
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
