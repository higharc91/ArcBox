namespace Meridian.Presentation.Board;

/// <summary>
/// One edge of a compartment as the TV draws it (the art contract's four hatch states, #331): <c>open</c> to a placed
/// neighbour, <c>closed</c> with nothing explored beyond, <c>sealed</c> (welded, unusable), or <c>none</c> (a wall).
/// </summary>
public enum HatchState
{
    None,
    Closed,
    Open,
    Sealed,
}

/// <summary>
/// What to draw for one compartment (M4 plan, Task 3): its tile turned by <see cref="Degrees"/>, one hatch overlay per
/// side, its symbol, and whether it's lit as the compartment being presented. The scene draws art or a placeholder from
/// this, the same either way.
/// </summary>
public sealed record TileDraw(string Id, string Tile, Cell At, int Rotation, EqArray<HatchState> Sides, CardKind? Symbol, bool Lit)
{
    public HatchState Hatch(Side side) => Sides[(int)side];

    /// <summary>The tile's clockwise turn in degrees (the scene's y axis points down, so positive is clockwise).</summary>
    public int Degrees => Rotation * 90;

    /// <summary>A hatch overlay is drawn for the north edge (#331); this turns it, clockwise, to <paramref name="side"/>.</summary>
    public static int OverlayDegrees(Side side) => (int)side * 90;

    /// <summary>The compartments on <paramref name="deck"/>, in the view's order.</summary>
    /// <param name="lit">The compartment being presented, if any.</param>
    public static IReadOnlyList<TileDraw> Of(EqArray<CompartmentCard> rooms, DeckId deck, string? lit = null)
    {
        var byCell = rooms.Where(r => r.At.Deck == deck).ToDictionary(r => r.At);
        return [.. byCell.Values.Select(r => new TileDraw(r.Id, r.Tile, r.At, r.Rotation,
            Meridian.Logic.Sides.All.Select(side => State(r, side, byCell)).ToEqArray(), r.Symbol, r.Id == lit))];
    }

    private static HatchState State(CompartmentCard room, Side side, Dictionary<Cell, CompartmentCard> byCell)
    {
        if (!room.Hatches.Contains(side))
        {
            return HatchState.None;
        }
        if (room.Sealed is { } sealedSides && sealedSides.Contains(side))
        {
            return HatchState.Sealed;
        }
        return byCell.ContainsKey(room.At.Step(side)) ? HatchState.Open : HatchState.Closed;
    }
}
