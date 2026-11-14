namespace Meridian.Logic;

/// <summary>
/// The state as one viewer may see it (spec §4.4). Every view (the TV, each phone, each bot) is built from this and never
/// from the raw state, so a secret can only reach a viewer through an audience that includes them.
/// </summary>
public static class Visibility
{
    /// <summary>What a role looks like to someone outside its audience.</summary>
    public const string UnknownRole = "unknown";

    /// <param name="viewer">A seat, or null for the TV, which sees only what everyone sees.</param>
    public static GameState For(GameState state, SeatId? viewer)
    {
        var seen = state with
        {
            // The future is secret: piles keep what's in them but not their order, and the generator that would predict
            // every draw and roll is replaced.
            Rng = Rng.Seeded(0),
            TilePile = state.TilePile.Order().ToEqArray(),
            Incidents = Unordered(state.Incidents),
            Salvage = Unordered(state.Salvage),
            Anomalies = Unordered(state.Anomalies),
            Waiting = state.Waiting is VotePending vote
                ? vote with { Cast = vote.Cast.Where(b => b.Voter == viewer).ToEqArray() }
                : state.Waiting,
        };
        if (state.Board is not { } board)
        {
            return seen;
        }
        return seen.WithBoard(board with
        {
            Roles = board.Roles.Select(r => r.Audience.Includes(state, viewer)
                ? r
                : new RoleCard(r.Seat, UnknownRole, UnknownRole, Audience.Everyone)).ToEqArray(),
            Tokens = board.Tokens.Where(t => t.Audience.Includes(state, viewer)).ToEqArray(),
            Counters = board.Counters.Where(c => c.Audience.Includes(state, viewer)).ToEqArray(),
            Objectives = board.Objectives.Where(o => o.Audience.Includes(state, viewer)).ToEqArray(),
        });
    }

    private static CardPile Unordered(CardPile pile) => new(pile.Draw.Order().ToEqArray(), pile.Discard);
}
