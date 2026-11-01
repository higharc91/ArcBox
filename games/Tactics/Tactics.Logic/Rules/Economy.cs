namespace Tactics.Logic;

/// <summary>The per-turn action economy.</summary>
public static class Economy
{
    /// <summary>
    /// A turn's full economy: the effective Move (<see cref="Modifiers.Move"/>: the sheet's plus <paramref name="mods"/>, 0
    /// when it can't move), one Action plus <see cref="StaticMods.ExtraActions"/>, one Bonus action and the Reaction back.
    /// </summary>
    public static TurnEconomy Fresh(UnitSheet sheet, StaticMods? mods = null)
        => new(Modifiers.Move(sheet, mods ?? StaticMods.None), Action: true, Bonus: true, Reaction: true,
            ExtraActions: Math.Max(0, mods?.ExtraActions ?? 0));
}
