namespace Meridian.Logic;

/// <summary>
/// The shared Explore policy (spec §6): answer whatever is owed; heal when hurt; use helpful items; open a hatch here
/// if there is one; otherwise walk towards the nearest hatch that can still grow the ship; otherwise end the turn.
/// It never trades. It only has to be adequate: it keeps a game moving and gives the simulator a player.
/// </summary>
public sealed class ExploreBot : IBotPolicy
{
    public static ExploreBot Instance { get; } = new();

    public Command Choose(RulesPack pack, GameState state, SeatId seat, EqArray<Command> legal, ref Rng rng)
    {
        if (state.Waiting is { } waiting)
        {
            return waiting switch
            {
                OrientPending o => new Orient(seat, Placement.DefaultRotation(state.Ship, pack.Tile(o.Tile), o.At, o.Toward, pack.Rules.DeckRadius)),
                RollPending => new Roll(seat),
                ChoicePending => Pick(legal.Where(c => c is Choose).ToList(), ref rng),
                TradePending => new AnswerTrade(seat, false),
                _ => new Timeout(seat),
            };
        }

        if (legal.Contains(new UseCompartment(seat)))
        {
            return new UseCompartment(seat);
        }
        if (legal.OfType<UseItem>().FirstOrDefault(u => Helps(pack, state.CrewOf(seat), u.Item)) is { } use)
        {
            return use;
        }

        var opens = legal.OfType<Open>().Where(o => Fits(pack, state, o.At)).ToList();
        if (opens.Count > 0)
        {
            return Pick(opens, ref rng);
        }

        return Toward(pack, state, seat, legal, ref rng) ?? new EndTurn(seat);
    }

    /// <summary>An item worth using now: a heal with something to heal, a draw, or extra movement.</summary>
    private static bool Helps(RulesPack pack, CrewState crew, CardId item)
    {
        var effects = pack.Card(item).Item!.Use!.Effects;
        if (effects.Any(e => e is Lose or MoveToTile))
        {
            return false;
        }
        var tracks = pack.CrewMember(crew.Crew).Tracks;
        return effects.Any(e => e switch
        {
            Gain g => g.Traits.Members().Any(t => crew.Steps[t] < tracks[t].Start),
            DrawCard or Moves => true,
            _ => false,
        });
    }

    /// <summary>The compartment's deck still has a tile in the pile.</summary>
    private static bool Fits(RulesPack pack, GameState state, CompartmentId at)
    {
        var deck = state.Ship.Get(at).At.Deck;
        return state.TilePile.Any(t => pack.Tile(t).Decks.Contains(deck));
    }

    /// <summary>
    /// Heads for the nearest compartment with a frontier hatch that can still grow (ties: the deck with more tiles left,
    /// then chance), and moves as far along the cheapest path as this turn's movement allows.
    /// </summary>
    private static Command? Toward(RulesPack pack, GameState state, SeatId seat, EqArray<Command> legal, ref Rng rng)
    {
        var here = state.CrewOf(seat).At;
        var paths = Reach.Paths(pack, state.Ship, here);
        var left = Enum.GetValues<DeckId>().ToDictionary(d => d, d => state.TilePile.Count(t => pack.Tile(t).Decks.Contains(d)));

        var targets = state.Ship.Frontier(pack.Rules.DeckRadius)
            .Select(h => h.Compartment).Distinct()
            .Where(c => c != here && paths.ContainsKey(c) && left[state.Ship.Get(c).At.Deck] > 0)
            .Select(c => (Id: c, paths[c].Cost, Left: left[state.Ship.Get(c).At.Deck]))
            .ToList();
        if (targets.Count == 0)
        {
            return null;
        }

        var nearest = targets.Min(t => t.Cost);
        var most = targets.Where(t => t.Cost == nearest).Max(t => t.Left);
        var best = targets.Where(t => t.Cost == nearest && t.Left == most).OrderBy(t => t.Id).ToList();
        (var pick, rng) = rng.Below(best.Count);

        // Walk back from the target to the farthest step on its path that this turn can pay for.
        CompartmentId? step = best[pick].Id;
        while (step is { } s && (paths[s].Cost > state.MovesLeft || !legal.Contains(new MoveTo(seat, s))))
        {
            step = paths[s].Via;
            if (step == here)
            {
                return null;
            }
        }
        return step is { } go ? new MoveTo(seat, go) : null;
    }

    private static T Pick<T>(IReadOnlyList<T> options, ref Rng rng)
    {
        (var i, rng) = rng.Below(options.Count);
        return options[i];
    }
}
