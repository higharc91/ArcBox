namespace Tactics.Logic;

/// <summary>The per-turn action economy.</summary>
public static class Economy
{
    /// <summary>A turn's full economy: Move tiles, one Action, one Bonus action and the Reaction back.</summary>
    public static TurnEconomy Fresh(UnitSheet sheet) => new(sheet.Move, Action: true, Bonus: true, Reaction: true, ExtraActions: 0);
}
