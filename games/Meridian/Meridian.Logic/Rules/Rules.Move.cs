namespace Meridian.Logic;

// Movement, opening hatches and revealing compartments (spec §2.3, §2.4; plan Task 5).
internal sealed partial class Resolver
{
    private static void LegalMoves(RulesPack pack, GameState state, SeatId seat, List<Command> legal)
    {
        var crew = state.CrewOf(seat);
        var reach = Reach.From(pack, state.Ship, crew.At, state.MovesLeft);
        foreach (var c in state.Ship.Compartments)
        {
            if (c.Id != crew.At && reach.ContainsKey(c.Id))
            {
                legal.Add(new MoveTo(seat, c.Id));
            }
        }
    }

    /// <summary>
    /// Open is legal on a hatch of the mover's compartment that faces empty space inside the deck, if they can pay to
    /// step through. It's legal even when no tile is left that fits: opening it is how they find that out.
    /// </summary>
    private static void LegalOpens(RulesPack pack, GameState state, SeatId seat, List<Command> legal)
    {
        var here = state.Ship.Get(state.CrewOf(seat).At);
        if (state.MovesLeft < Reach.StepCost(pack, here))
        {
            return;
        }
        foreach (var side in here.Hatches)
        {
            var next = here.At.Step(side);
            if (!state.Ship.IsSealed(here.Id, side) && next.InBounds(pack.Rules.DeckRadius) && state.Ship.At(next) is null)
            {
                legal.Add(new Open(seat, here.Id, side));
            }
        }
    }

    private void Move(MoveTo move)
    {
        var crew = State.CrewOf(move.From);
        var cost = Reach.From(Pack, State.Ship, crew.At, State.MovesLeft)[move.To];
        State = State.WithCrew(crew with { At = move.To }) with { MovesLeft = State.MovesLeft - cost };
        Events.Add(new Moved(move.From, crew.At, move.To, cost));
    }

    private void OpenHatch(Open open)
    {
        var here = State.Ship.Get(open.At);
        var cell = here.At.Step(open.Side);
        var toward = open.Side.Opposite();

        var index = -1;
        for (var i = 0; i < State.TilePile.Count; i++)
        {
            if (Pack.Tile(State.TilePile[i]).Decks.Contains(cell.Deck))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            // Review focus 1: nothing fits, so the hatch is welded shut. Nothing is spent and the turn goes on.
            Seal(new HatchRef(here.Id, open.Side));
            Events.Add(new HatchSealed(here.Id, open.Side));
            return;
        }

        var tile = Pack.Tile(State.TilePile[index]);
        State = State with { TilePile = State.TilePile.RemoveAt(index) };
        var rotations = Placement.ValidRotations(tile, toward);
        if (rotations.Count == 1)
        {
            PlaceAndEnter(open.From, tile, cell, rotations[0]);
            return;
        }

        State = State with { Waiting = new OrientPending(open.From, tile.Id, cell, toward, rotations) };
        Events.Add(new OrientRequested(open.From, tile.Id, rotations));
    }

    private void OrientTile(int rotation)
    {
        var orient = (OrientPending)State.Waiting!;
        State = State with { Waiting = null };
        PlaceAndEnter(orient.From, Pack.Tile(orient.Tile), orient.At, rotation);
    }

    /// <summary>Places the new compartment and steps the mover into it. A card symbol ends their movement and draws.</summary>
    private void PlaceAndEnter(SeatId seat, TileDef tile, Cell at, int rotation)
    {
        var crew = State.CrewOf(seat);
        var cost = Reach.StepCost(Pack, State.Ship.Get(crew.At));
        var (ship, placed) = Placement.Place(State.Ship, tile, at, rotation, R.DeckRadius);
        State = State with { Ship = ship, MovesLeft = State.MovesLeft - cost };
        State = State.WithCrew(crew with { At = placed.Id });
        Events.Add(new CompartmentRevealed(seat, placed));
        Events.Add(new Moved(seat, crew.At, placed.Id, cost));

        if (tile.Symbol is { } kind)
        {
            State = State with { MovesLeft = 0 };
            DrawFor(kind);
        }
    }

    /// <summary>Draws a card of that kind for the active seat (plan Task 7).</summary>
    partial void DrawFor(CardKind kind);

    private void Seal(HatchRef hatch)
    {
        var items = State.Ship.Sealed.Items;
        var at = items.AsSpan().BinarySearch(hatch);
        if (at < 0)
        {
            State = State with { Ship = State.Ship with { Sealed = items.Insert(~at, hatch) } };
        }
    }
}
