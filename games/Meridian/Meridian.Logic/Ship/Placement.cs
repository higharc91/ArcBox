namespace Meridian.Logic;

/// <summary>Where a tile can go, which way round, and what placing it seals (spec §2.3).</summary>
public static class Placement
{
    /// <summary>
    /// The rotations of <paramref name="tile"/> at <paramref name="at"/> that put a hatch on <paramref name="toward"/>,
    /// the side facing the compartment the crew member comes from. Rotations giving the same hatches are offered once,
    /// as the lowest of them: a four-way junction has one choice, not four.
    /// </summary>
    public static EqArray<int> ValidRotations(TileDef tile, Side toward)
    {
        var seen = new HashSet<EqArray<Side>>();
        var result = new List<int>();
        for (var r = 0; r < 4; r++)
        {
            var hatches = tile.HatchesAt(r);
            if (hatches.Contains(toward) && seen.Add(hatches))
            {
                result.Add(r);
            }
        }
        return [.. result];
    }

    /// <summary>The rotation a timeout picks: the most usable hatches once placed, then the lowest rotation.</summary>
    public static int DefaultRotation(ShipState ship, TileDef tile, Cell at, Side toward, int radius)
    {
        var best = -1;
        var bestUsable = -1;
        foreach (var r in ValidRotations(tile, toward))
        {
            var usable = tile.HatchesAt(r).Count(side => IsUsable(ship, at, side, radius));
            if (usable > bestUsable)
            {
                (best, bestUsable) = (r, usable);
            }
        }
        return best >= 0 ? best : throw new InvalidOperationException($"{tile.Id} has no hatch that can face {toward}.");
    }

    /// <summary>
    /// A hatch of a tile at <paramref name="at"/> facing <paramref name="side"/> that would be open once placed: the next
    /// cell is inside the deck and either empty or holds a compartment with a hatch facing back.
    /// </summary>
    public static bool IsUsable(ShipState ship, Cell at, Side side, int radius)
    {
        var next = at.Step(side);
        if (!next.InBounds(radius))
        {
            return false;
        }
        return ship.At(next) is not { } neighbour
               || (neighbour.Hatches.Contains(side.Opposite()) && !ship.IsSealed(neighbour.Id, side.Opposite()));
    }

    /// <summary>
    /// Places the tile as the next compartment, and seals every hatch that now faces a wall: the new tile's hatches that
    /// face the deck's edge or a neighbour's wall, and neighbours' hatches that face the new tile's walls. A
    /// <see cref="DropToRule"/> adds its one-way link to the target deck's first compartment.
    /// </summary>
    public static (ShipState Ship, Compartment Placed) Place(ShipState ship, TileDef tile, Cell at, int rotation, int radius)
    {
        if (ship.At(at) is not null)
        {
            throw new InvalidOperationException($"{at} already holds a compartment.");
        }

        var placed = new Compartment(CompartmentId.Numbered(ship.Compartments.Count + 1), tile.Id, at, rotation,
                                     tile.HatchesAt(rotation));
        var sealedHatches = ship.Sealed.Items.ToBuilder();
        foreach (var side in Sides.All)
        {
            var next = at.Step(side);
            var neighbour = ship.At(next);
            var mine = placed.Hatches.Contains(side);
            if (mine && (!next.InBounds(radius) || (neighbour is not null && !neighbour.Hatches.Contains(side.Opposite()))))
            {
                sealedHatches.Add(new HatchRef(placed.Id, side));
            }
            if (!mine && neighbour is not null && neighbour.Hatches.Contains(side.Opposite())
                && !ship.IsSealed(neighbour.Id, side.Opposite()))
            {
                sealedHatches.Add(new HatchRef(neighbour.Id, side.Opposite()));
            }
        }
        sealedHatches.Sort();

        var links = ship.Links;
        if (tile.Rule is DropToRule drop && ship.Compartments.FirstOrDefault(c => c.At.Deck == drop.Deck) is { } target)
        {
            links = links.Add(new Link(placed.Id, target.Id, OneWay: true));
        }

        return (ship with
        {
            Compartments = ship.Compartments.Add(placed),
            Sealed = new EqArray<HatchRef>(sealedHatches.ToImmutable()),
            Links = links,
        }, placed);
    }
}
