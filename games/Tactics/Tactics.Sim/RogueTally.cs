using System.Collections.Immutable;
using Tactics.Logic;

namespace Tactics.Sim;

/// <summary>
/// How a battle's party rogues set up Sneak Attack (#108), summed over their own turns: the <see cref="Turns"/>, those in
/// which they used Hide (<see cref="HideTurns"/>), landed Sneak Attack (<see cref="SneakTurns"/>) and used a weapon attack
/// (<see cref="AttackTurns"/>); and of those weapon attacks, the ones used from hiding (<see cref="FromHiding"/>) and the
/// melee ones used flanking their target (<see cref="Flanking"/>).
/// </summary>
public sealed record RogueSetUp(int Turns, int HideTurns, int SneakTurns, int AttackTurns, int FromHiding, int Flanking)
{
    /// <summary>No rogue turns.</summary>
    public static RogueSetUp None { get; } = new(0, 0, 0, 0, 0, 0);

    /// <summary>The two added up.</summary>
    public RogueSetUp Add(RogueSetUp other) => new(Turns + other.Turns, HideTurns + other.HideTurns, SneakTurns + other.SneakTurns,
        AttackTurns + other.AttackTurns, FromHiding + other.FromHiding, Flanking + other.Flanking);
}

/// <summary>
/// Counts <see cref="RogueSetUp"/> as a battle is played: each command a party rogue's turn applies is read with the state
/// it was applied to, so hiding and flanking are judged as the attack was made.
/// </summary>
internal sealed class RogueTally(RulesPack rules)
{
    /// <summary>The rogue's Sneak Attack passive.</summary>
    public const string SneakAttack = "rogue.sneak_attack";

    private RogueSetUp _total = RogueSetUp.None;
    private UnitId? _rogue;
    private bool _hid, _snuck, _struck;

    /// <summary>The counts so far.</summary>
    public RogueSetUp SetUp => _total;

    /// <summary>True for a party member of the rogue class (not a summon or a protected NPC).</summary>
    public static bool IsRogue(UnitState unit)
        => unit.Sheet.Side == Side.Party && unit.SummonedBy is null && !unit.Protected
           && (unit.TrueSheet ?? unit.Sheet).Tags.Contains(SheetBuilder.ClassTag + "rogue");

    /// <summary>A turn of <paramref name="unit"/> starts.</summary>
    public void BeginTurn(BattleState state, UnitId unit)
    {
        _rogue = IsRogue(state.Units[unit]) ? unit : null;
        (_hid, _snuck, _struck) = (false, false, false);
    }

    /// <summary>The turn's <paramref name="command"/>, applied to <paramref name="before"/>, gave <paramref name="events"/>.</summary>
    public void Read(BattleState before, Command command, ImmutableArray<BattleEvent> events)
    {
        if (_rogue is not { } rogue)
            return;
        _hid |= events.Any(e => e is AbilityUsed u && u.Source == rogue && u.Ability == HiddenRogue.Hide);
        _snuck |= events.Any(e => e is PassiveTriggered p && p.Owner == rogue && p.Passive == SneakAttack);
        if (command is not Use { Item: null } use || use.Actor != rogue || !rules.Abilities.TryGetValue(use.Ability, out var ability)
            || !ability.IsWeaponAttack || !events.Any(e => e is AbilityUsed u && u.Source == rogue))
            return;
        _struck = true;
        var me = before.Units[rogue];
        var hiding = Modifiers.For(before, rogue, rules).Invisible;
        var flanking = !me.Sheet.Weapon.Ranged && use.Target.Unit is { } target && before.Units.ContainsKey(target)
                       && Modifiers.Flanked(before, rogue, target, rules);
        _total = _total with { FromHiding = _total.FromHiding + (hiding ? 1 : 0), Flanking = _total.Flanking + (flanking ? 1 : 0) };
    }

    /// <summary>The turn ends.</summary>
    public void EndTurn()
    {
        if (_rogue is null)
            return;
        _total = _total.Add(new RogueSetUp(1, _hid ? 1 : 0, _snuck ? 1 : 0, _struck ? 1 : 0, 0, 0));
        _rogue = null;
    }
}
