namespace Meridian.Logic;

public static class ShipBuilder
{
    /// <summary>The start layout (spec §2.3): its tiles placed in file order as c1, c2, …, then its links.</summary>
    public static ShipState Start(RulesPack pack)
    {
        var ship = ShipState.Empty;
        foreach (var p in pack.Start.Place)
        {
            (ship, _) = Placement.Place(ship, pack.Tile(p.Tile), new Cell(p.Deck, p.X, p.Y), p.Rotation, pack.Rules.DeckRadius);
        }
        var links = pack.Start.Links.Select(l => new Link(ship.Compartments[l.From].Id, ship.Compartments[l.To].Id, l.OneWay));
        return ship with { Links = ship.Links.AddRange(links) };
    }

    /// <summary>The compartment the crew start in.</summary>
    public static CompartmentId Entry(RulesPack pack, ShipState ship)
        => ship.Compartments.First(c => c.Tile == pack.Start.Entry).Id;
}
