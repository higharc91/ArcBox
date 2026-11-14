namespace Meridian.Logic;

/// <summary>A placed tile. <see cref="Hatches"/> are after rotation, in N-E-S-W order.</summary>
public sealed record Compartment(CompartmentId Id, TileId Tile, Cell At, int Rotation, EqArray<Side> Hatches);

/// <summary>One side of one compartment.</summary>
public readonly record struct HatchRef(CompartmentId Compartment, Side Side) : IComparable<HatchRef>
{
    public int CompareTo(HatchRef other)
    {
        var byCompartment = Compartment.CompareTo(other.Compartment);
        return byCompartment != 0 ? byCompartment : Side.CompareTo(other.Side);
    }
}

/// <summary>A way between compartments other than a hatch: the lift, or a one-way shaft.</summary>
public sealed record Link(CompartmentId From, CompartmentId To, bool OneWay = false);

/// <summary>
/// The ship as placed so far. Compartments are in placement order, which is also their id order (c1, c2, …), so ids
/// never depend on anything but the order of play. <see cref="Sealed"/> is kept sorted.
/// </summary>
public sealed record ShipState(EqArray<Compartment> Compartments, EqArray<HatchRef> Sealed, EqArray<Link> Links)
{
    public static ShipState Empty { get; } = new([], [], []);

    public Compartment Get(CompartmentId id)
        => Find(id) ?? throw new KeyNotFoundException($"No compartment {id}.");

    public Compartment? Find(CompartmentId id)
    {
        foreach (var c in Compartments)
        {
            if (c.Id == id)
            {
                return c;
            }
        }
        return null;
    }

    public Compartment? At(Cell cell)
    {
        foreach (var c in Compartments)
        {
            if (c.At == cell)
            {
                return c;
            }
        }
        return null;
    }

    public bool IsSealed(CompartmentId compartment, Side side)
        => Sealed.Items.AsSpan().BinarySearch(new HatchRef(compartment, side)) >= 0;

    /// <summary>Compartments one hatch away: both sides have a hatch and neither is sealed.</summary>
    public IEnumerable<Compartment> Adjacent(Compartment from)
    {
        foreach (var side in from.Hatches)
        {
            if (At(from.At.Step(side)) is { } next && next.Hatches.Contains(side.Opposite())
                && !IsSealed(from.Id, side) && !IsSealed(next.Id, side.Opposite()))
            {
                yield return next;
            }
        }
    }

    /// <summary>Compartments a link leads to from here.</summary>
    public IEnumerable<CompartmentId> Linked(CompartmentId from)
    {
        foreach (var link in Links)
        {
            if (link.From == from)
            {
                yield return link.To;
            }
            else if (link.To == from && !link.OneWay)
            {
                yield return link.From;
            }
        }
    }

    /// <summary>
    /// Hatches that open onto an empty cell inside the deck: where the ship can grow. Sealed hatches are excluded; whether
    /// a tile is left that fits is the pile's business (see Rules).
    /// </summary>
    public IEnumerable<HatchRef> Frontier(int radius)
    {
        foreach (var c in Compartments)
        {
            foreach (var side in c.Hatches)
            {
                var next = c.At.Step(side);
                if (!IsSealed(c.Id, side) && next.InBounds(radius) && At(next) is null)
                {
                    yield return new HatchRef(c.Id, side);
                }
            }
        }
    }
}
