namespace Meridian.Logic;

/// <summary>
/// Properties every reachable state must have. Tests run this after every step of thousands of bot games; any message
/// it returns is a rules bug.
/// </summary>
public static class Invariants
{
    public static IReadOnlyList<string> Check(RulesPack pack, GameState state)
    {
        var errors = new List<string>();
        var ship = state.Ship;
        var radius = pack.Rules.DeckRadius;

        if (ship.Compartments.Select(c => c.At).Distinct().Count() != ship.Compartments.Count)
        {
            errors.Add("two compartments share a cell");
        }
        foreach (var (c, i) in ship.Compartments.Select((c, i) => (c, i)))
        {
            if (c.Id != CompartmentId.Numbered(i + 1))
            {
                errors.Add($"{c.Id} is out of placement order");
            }
            if (!c.At.InBounds(radius))
            {
                errors.Add($"{c.Id} is outside its deck");
            }
            if (c.Hatches != pack.Tile(c.Tile).HatchesAt(c.Rotation))
            {
                errors.Add($"{c.Id}'s hatches don't match its tile and rotation");
            }
            foreach (var side in c.Hatches)
            {
                var next = c.At.Step(side);
                var neighbour = ship.At(next);
                var open = next.InBounds(radius) && (neighbour is null || neighbour.Hatches.Contains(side.Opposite()));
                if (!open && !ship.IsSealed(c.Id, side))
                {
                    errors.Add($"{c.Id}'s {side} hatch faces a wall but isn't sealed");
                }
            }
        }
        if (!ship.Sealed.Items.SequenceEqual(ship.Sealed.Items.Distinct().Order()))
        {
            errors.Add("sealed hatches are not sorted and distinct");
        }
        foreach (var h in ship.Sealed)
        {
            if (ship.Find(h.Compartment) is not { } c || !c.Hatches.Contains(h.Side))
            {
                errors.Add($"a sealed hatch {h} doesn't exist");
            }
        }

        foreach (var crew in state.Crew)
        {
            if (ship.Find(crew.At) is null)
            {
                errors.Add($"{crew.Seat} is in a compartment that doesn't exist");
            }
            foreach (var trait in Traits.All)
            {
                // A living crew member never sits below step 1; the dead may be at 0 (spec §2.8).
                if (crew.Steps[trait] < (crew.Alive ? 1 : 0) || crew.Steps[trait] > pack.Rules.TrackSteps)
                {
                    errors.Add($"{crew.Seat}'s {trait} marker is off its track");
                }
            }
        }

        // Every card exactly once: in a pile, carried, or being resolved.
        var cards = state.Incidents.Draw.Concat(state.Incidents.Discard)
            .Concat(state.Salvage.Draw).Concat(state.Salvage.Discard)
            .Concat(state.Anomalies.Draw).Concat(state.Anomalies.Discard)
            .Concat(state.Crew.SelectMany(c => c.Items))
            .Concat(state.Board?.Tokens.Select(t => t.Card).OfType<CardId>() ?? [])
            .Concat(state.Waiting switch
            {
                RollPending { Reason: RollReason.Check, Card: { } c } => [c],
                ChoicePending c => [c.Card],
                _ => Array.Empty<CardId>(),
            })
            .ToList();
        var everyCard = pack.Cards.Select(c => c.Id).Order().ToList();
        if (!cards.Order().SequenceEqual(everyCard))
        {
            errors.Add("a card is missing or exists twice");
        }

        // Every placeable tile copy exactly once: in the pile, on the ship, or waiting for its rotation.
        var startCount = pack.Start.Place.Count;
        var tiles = state.TilePile.Concat(ship.Compartments.Skip(startCount).Select(c => c.Tile))
            .Concat(state.Waiting is OrientPending o ? [o.Tile] : Array.Empty<TileId>())
            .Order().ToList();
        var everyTile = pack.Tiles.SelectMany(t => Enumerable.Repeat(t.Id, t.Copies)).Order().ToList();
        if (!tiles.SequenceEqual(everyTile))
        {
            errors.Add("a tile is missing or exists twice");
        }

        var anomaliesOut = pack.CardsOf(CardKind.Anomaly).Count() - state.Anomalies.Draw.Count;
        if (state.AnomaliesDrawn != anomaliesOut)
        {
            errors.Add($"AnomaliesDrawn is {state.AnomaliesDrawn} but {anomaliesOut} have left the pile");
        }

        if (state.Active < 0 || state.Active >= state.Crew.Count || state.First < 0 || state.First >= state.Crew.Count)
        {
            errors.Add("the active or first seat is out of range");
        }
        if (state.Waiting is { } w && state.IndexOf(w.From) < 0)
        {
            errors.Add("waiting on a seat that isn't in the game");
        }
        if ((state.Phase != Phase.Explore) != (state.Breach is not null))
        {
            errors.Add("the phase and the Breach record disagree");
        }
        if (state.Phase is Phase.Breach or Phase.Over && state.Waiting is not null)
        {
            errors.Add("waiting on someone while nobody can act");
        }
        if (state.Phase is Phase.Endgame or Phase.Over && state.Board is null)
        {
            errors.Add("an endgame with no scenario board");
        }
        if (state.Phase == Phase.Over && state.Board?.Outcome is null)
        {
            errors.Add("the game is over with no outcome");
        }
        if (state.Board is { } board)
        {
            if (board.Roles.Select(r => r.Seat).Distinct().Count() != board.Roles.Count || board.Roles.Any(r => state.IndexOf(r.Seat) < 0))
            {
                errors.Add("a role is dealt twice or to a seat that isn't in the game");
            }
            if (board.Tokens.Select(t => t.Id).Distinct().Count() != board.Tokens.Count)
            {
                errors.Add("two tokens share an id");
            }
            foreach (var token in board.Tokens)
            {
                if ((token.At is null) == (token.Carrier is null))
                {
                    errors.Add($"token {token.Id} must be in one compartment or carried by one seat");
                }
                if (token.At is { } at && ship.Find(at) is null)
                {
                    errors.Add($"token {token.Id} is in a compartment that doesn't exist");
                }
                if (token.Carrier is { } c && state.IndexOf(c) < 0 || token.Controller is { } k && state.IndexOf(k) < 0)
                {
                    errors.Add($"token {token.Id} names a seat that isn't in the game");
                }
            }
        }
        return errors;
    }
}
