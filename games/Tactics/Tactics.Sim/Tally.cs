using System.Collections.Immutable;
using Tactics.Logic;

namespace Tactics.Sim;

/// <summary>
/// Reads one battle's events, in order, into the simulator's measures. Nothing is re-derived from the rules: who did
/// something is the latest <see cref="AbilityUsed"/>, <see cref="PassiveTriggered"/> or <see cref="AttackResolved"/>
/// source (the unit whose turn started, at a turn start), a summon counting as its summoner. Party members are the
/// party's own characters (class from their sheet's <c>class:</c> tag), not summons or NPCs.
/// <list type="bullet">
/// <item><b>Damage:</b> the HP a member took off units on other sides: each <see cref="Damaged"/> credits the HP the unit
/// actually lost (its HP before, tracked from the events, less <see cref="Damaged.HpAfter"/>), so overkill doesn't count and
/// damage soaked by temporary HP doesn't either (that is mitigation, the target's).</item>
/// <item><b>Healing:</b> <see cref="Healed"/> amounts a member caused on its own side, plus the HP of a
/// <see cref="Revived"/> that no heal caused (Help).</item>
/// <item><b>Mitigated:</b> the damage a member soaked (HP lost plus temporary HP spent, no overkill), plus the hostile attacks
/// on it that missed (<see cref="AttackResolved"/>) times the battle's mean soaked damage of a hostile hit on a party
/// member.</item>
/// <item><b>Turns denied:</b> an enemy's <see cref="TurnSkipped"/> for <c>"incapacitated"</c>, credited to the member that
/// last applied an incapacitating condition to it (<see cref="ConditionApplied"/>).</item>
/// <item><b>Abilities:</b> for each member turn (from <see cref="BeginTurn"/> to its <see cref="TurnEnded"/>), every legal
/// ability counts one legal turn and every one it used (<see cref="AbilityUsed"/> as the turn's unit) one used turn; a used
/// ability is counted legal too. Reactions (the ability after a <see cref="Reacted"/>, or any with a Reaction cost) aren't
/// counted: they answer someone else's action, so "legal turns" means nothing for them.</item>
/// <item><b>Downs and Outs:</b> members' <see cref="Downed"/> and <see cref="UnitOut"/> events.</item>
/// </list>
/// </summary>
internal sealed class Tally(RulesPack rules)
{
    private sealed class Member(string cls)
    {
        public string Class { get; } = cls;
        public double Damage { get; set; }
        public double Healing { get; set; }
        public double Taken { get; set; }
        public int MissedBy { get; set; }
        public int Denied { get; set; }
    }

    private readonly Dictionary<UnitId, Member> _members = [];
    private readonly Dictionary<UnitId, Side> _sides = [];
    private readonly Dictionary<UnitId, UnitId> _summoners = [];
    private readonly Dictionary<UnitId, UnitId> _disabledBy = [];
    private readonly Dictionary<ConditionId, bool> _incapacitates = [];
    private readonly SortedDictionary<AbilityId, (int Used, int Legal)> _abilities = [];
    private readonly Dictionary<UnitId, int> _hp = [];
    private readonly Dictionary<UnitId, int> _temp = [];

    private UnitId? _source;
    private UnitId? _hitTarget;
    private BattleEvent? _previous;
    private bool _reacting;
    private double _hostileHitDamage;
    private int _hostileHits;

    private UnitId? _turn;
    private ImmutableSortedSet<AbilityId> _legal = [];
    private readonly SortedSet<AbilityId> _used = [];

    /// <summary>Party members Downed, counting each time.</summary>
    public int Downs { get; private set; }

    /// <summary>Party members that went Out.</summary>
    public int Outs { get; private set; }

    /// <summary>True when <paramref name="unit"/> is one of the party's own characters.</summary>
    public bool IsMember(BattleState state, UnitId unit)
    {
        Learn(state);
        return _members.ContainsKey(unit);
    }

    /// <summary>A member's turn starts: these abilities are legal in it.</summary>
    public void BeginTurn(UnitId unit, ImmutableSortedSet<AbilityId> legal)
    {
        _turn = unit;
        _legal = legal;
        _used.Clear();
    }

    /// <summary>Reads <paramref name="events"/>, which led to <paramref name="state"/>.</summary>
    public void Read(ImmutableArray<BattleEvent> events, BattleState state)
    {
        Learn(state);
        foreach (var e in events)
        {
            ReadOne(e);
            _previous = e;
        }
        foreach (var (id, unit) in state.Units)     // the state is the truth after each command
            (_hp[id], _temp[id]) = (unit.Hp, unit.TempHp);
    }

    private void ReadOne(BattleEvent e)
    {
        switch (e)
        {
            case RoundStarted:
                (_source, _hitTarget) = (null, null);
                break;
            case TurnStarted t:
                (_source, _hitTarget) = (t.Unit, null);
                break;
            case TurnSkipped skipped:
                (_source, _hitTarget) = (null, null);
                if (skipped.Reason == "incapacitated" && !_members.ContainsKey(skipped.Unit)
                    && _disabledBy.TryGetValue(skipped.Unit, out var by) && _members.TryGetValue(by, out var denier))
                    denier.Denied++;
                break;
            case TurnEnded ended:
                if (ended.Unit == _turn)
                    EndTurn();
                (_source, _hitTarget) = (null, null);
                break;
            case Reacted reacted:
                _reacting = reacted.Choice != Reactions.Pass;
                break;
            case AbilityUsed used:
                (_source, _hitTarget) = (used.Source, null);
                if (used.Source == _turn && !_reacting && rules.Abilities.GetValueOrDefault(used.Ability)?.Cost.Reaction is null or 0)
                    _used.Add(used.Ability);
                _reacting = false;
                break;
            case PassiveTriggered passive:
                // A hook on a hit (sneak attack) still belongs to that hit; a zone's or condition's effects belong to
                // whoever cast the zone or applied the condition, not the unit standing in it or carrying it.
                _source = passive.Source ?? passive.Owner;
                break;
            case AttackResolved attack:
                _source = attack.Source;
                _hitTarget = attack.Hit ? attack.Target : null;
                if (Hostile(attack.Source, attack.Target) && _members.TryGetValue(attack.Target, out var target))
                {
                    if (attack.Hit)
                        _hostileHits++;
                    else
                        target.MissedBy++;
                }
                break;
            case Damaged damaged:
                OnDamaged(damaged);
                break;
            case TempHpGained gained:
                _temp[gained.Unit] = gained.TempHp;
                break;
            case Healed healed:
                _hp[healed.Unit] = healed.HpAfter;
                if (Owner(_source) is { } healer && _members.TryGetValue(healer, out var h) && !Hostile(healer, healed.Unit))
                    h.Healing += healed.Amount;
                break;
            case Revived revived:
                _hp[revived.Unit] = revived.Hp;
                if (_previous is not Healed { } heal || heal.Unit != revived.Unit)
                    if (Owner(_source) is { } helper && _members.TryGetValue(helper, out var m) && !Hostile(helper, revived.Unit))
                        m.Healing += revived.Hp;
                break;
            case ConditionApplied applied:
                if (Owner(_source) is { } applier && _members.ContainsKey(applier) && Hostile(applier, applied.Unit)
                    && Incapacitates(applied.Condition))
                    _disabledBy[applied.Unit] = applier;
                break;
            case Downed down:
                if (_members.ContainsKey(down.Unit))
                    Downs++;
                break;
            case UnitOut gone:
                if (_members.ContainsKey(gone.Unit))
                    Outs++;
                break;
        }
    }

    private void OnDamaged(Damaged damaged)
    {
        var unit = damaged.Unit;
        var lost = Math.Max(0, _hp.GetValueOrDefault(unit, damaged.HpAfter) - damaged.HpAfter);
        var temp = _temp.GetValueOrDefault(unit);
        var absorbed = Math.Min(temp, Math.Max(0, damaged.Amount - lost));
        (_hp[unit], _temp[unit]) = (damaged.HpAfter, temp - absorbed);

        var by = Owner(_source);
        if (_members.TryGetValue(unit, out var victim))
        {
            victim.Taken += lost + absorbed;
            if (_hitTarget == unit && by is { } attacker && Hostile(attacker, unit))
                _hostileHitDamage += lost + absorbed;
        }
        if (by is { } dealer && _members.TryGetValue(dealer, out var member) && Hostile(dealer, unit))
            member.Damage += lost;
    }

    private void EndTurn()
    {
        foreach (var id in _legal.Union(_used))
        {
            var (used, legal) = _abilities.GetValueOrDefault(id);
            _abilities[id] = (used + (_used.Contains(id) ? 1 : 0), legal + 1);
        }
        _turn = null;
        _legal = [];
        _used.Clear();
    }

    /// <summary>The per-class measures over a battle of <paramref name="rounds"/> rounds, averaged over each class's members.</summary>
    public ImmutableSortedDictionary<string, ClassStats> PerClass(int rounds)
    {
        var perHit = _hostileHits > 0 ? _hostileHitDamage / _hostileHits : 0;
        return Ordinal.Map(_members.Values.GroupBy(m => m.Class).Select(g =>
        {
            var n = g.Count();
            return (g.Key, new ClassStats(
                DamagePerRound: g.Sum(m => m.Damage) / n / rounds,
                HealingPerRound: g.Sum(m => m.Healing) / n / rounds,
                MitigatedPerRound: g.Sum(m => m.Taken + m.MissedBy * perHit) / n / rounds,
                TurnsDenied: (double)g.Sum(m => m.Denied) / n));
        }));
    }

    /// <summary>For each ability seen on a member's turn, the turns it was used in and the turns it was legal in.</summary>
    public ImmutableSortedDictionary<AbilityId, (int Used, int Legal)> Abilities() => _abilities.ToImmutableSortedDictionary();

    /// <summary>Records every unit's side and summoner, and which units are party members, from <paramref name="state"/>.</summary>
    private void Learn(BattleState state)
    {
        foreach (var (id, unit) in state.Units)
        {
            if (!_sides.TryAdd(id, unit.Sheet.Side))
                continue;
            (_hp[id], _temp[id]) = (unit.Hp, unit.TempHp);
            if (unit.SummonedBy is { } owner)
                _summoners[id] = owner;
            else if (unit.Sheet.Side == Side.Party && !unit.Protected && ClassOf(unit) is { } cls)
                _members[id] = new Member(cls);
        }
    }

    private static string? ClassOf(UnitState unit)
        => (unit.TrueSheet ?? unit.Sheet).Tags.FirstOrDefault(t => t.StartsWith(SheetBuilder.ClassTag, StringComparison.Ordinal))
            ?[SheetBuilder.ClassTag.Length..];

    private UnitId? Owner(UnitId? unit)
    {
        for (var hops = 0; unit is { } u && _summoners.TryGetValue(u, out var owner) && hops < 8; hops++)
            unit = owner;
        return unit;
    }

    private bool Hostile(UnitId a, UnitId b)
        => _sides.TryGetValue(a, out var sa) && _sides.TryGetValue(b, out var sb) && sa != sb;

    /// <summary>True when the condition, or one it implies, incapacitates.</summary>
    private bool Incapacitates(ConditionId condition)
    {
        if (_incapacitates.TryGetValue(condition, out var known))
            return known;
        var result = false;
        var seen = new HashSet<ConditionId>();
        for (ConditionId? next = condition; next is { } c && seen.Add(c) && rules.Conditions.TryGetValue(c, out var def); next = def.Implies)
            result |= def.Static?.Incapacitated == true;
        return _incapacitates[condition] = result;
    }
}
