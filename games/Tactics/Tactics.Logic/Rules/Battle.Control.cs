namespace Tactics.Logic;

public static partial class Battle
{
    /// <summary>
    /// Hands <paramref name="unit"/> to <paramref name="controller"/>: the adapter's seat changes (a phone drops, so the
    /// companion AI takes over; the player comes back). Only a party member's own character can change hands, not an
    /// enemy, a summon, a helper or a protected NPC. Control isn't a rules event, so nothing is emitted and the battle
    /// otherwise stays as it was. A reaction already offered to the unit stays offered: the adapter answers it (with
    /// <see cref="Ai.React"/> for an AI). Throws <see cref="ArgumentException"/> for any other unit.
    /// </summary>
    public static BattleState Hand(BattleState state, UnitId unit, Controller controller)
    {
        if (!state.Units.TryGetValue(unit, out var u))
            throw new ArgumentException($"There's no unit '{unit.Value}' in this battle.", nameof(unit));
        if (u.Sheet.Side != Side.Party || u.SummonedBy is not null || u.Protected || u.Helper)
            throw new ArgumentException($"'{unit.Value}' isn't a party member's character, so its control can't change.", nameof(unit));
        return u.Controller == controller ? state : state with { Units = state.Units.SetItem(unit, u with { Controller = controller }) };
    }
}
