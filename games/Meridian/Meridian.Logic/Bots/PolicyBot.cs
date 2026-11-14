namespace Meridian.Logic;

/// <summary>A bot's own dice, mutable so policies written as lambdas can draw from it.</summary>
public sealed class BotDice(Rng rng)
{
    public Rng Rng { get; private set; } = rng;

    public int Below(int bound)
    {
        (var value, Rng) = Rng.Below(bound);
        return value;
    }

    public T Pick<T>(IReadOnlyList<T> items) => items[Below(items.Count)];
}

/// <summary>What a role's policy decides from: the seat's own view of the game and what it may do.</summary>
public sealed record BotContext(RulesPack Pack, GameState State, SeatId Seat, EqArray<Command> Legal, BotDice Dice)
{
    public CrewState Me => State.CrewOf(Seat);
    public ScenarioBoard Board => State.Board!;
}

/// <summary>
/// A role's bot (spec §6): it answers every kind of wait the same way, then asks the role's policy what to do, then explores,
/// then ends its turn. A policy returns null to defer. Policies only ever see <see cref="Visibility.For"/> of their seat.
/// </summary>
public sealed class PolicyBot(Func<BotContext, Command?> decide, Func<BotContext, VotePending, string?>? vote = null) : IBotPolicy
{
    public Command Choose(RulesPack pack, GameState state, SeatId seat, EqArray<Command> legal, ref Rng rng)
    {
        var dice = new BotDice(rng);
        var ctx = new BotContext(pack, state, seat, legal, dice);
        var command = Answer(ctx) ?? (state.Waiting is null ? decide(ctx) : null)
                      ?? (state.Waiting is null && state.CrewOf(seat).Aboard ? Explore(ctx) : null)
                      ?? Fallback(ctx);
        rng = dice.Rng;
        return command;
    }

    private Command? Answer(BotContext ctx)
    {
        switch (ctx.State.Waiting)
        {
            case VotePending v:
                var choice = vote?.Invoke(ctx, v);
                return choice is not null && ctx.Legal.Contains(new CastVote(ctx.Seat, choice))
                    ? new CastVote(ctx.Seat, choice)
                    : new Timeout(ctx.Seat);
            case AllocatePending:
                return new Timeout(ctx.Seat);   // the default split: even, the higher trait first
            case { } waiting when waiting.From == ctx.Seat:
                var dummy = ctx.Dice.Rng;
                var command = ExploreBot.Instance.Choose(ctx.Pack, ctx.State, ctx.Seat, ctx.Legal, ref dummy);
                return command;
            default:
                return null;
        }
    }

    private static Command? Explore(BotContext ctx)
    {
        var rng = ctx.Dice.Rng;
        var command = ExploreBot.Instance.Choose(ctx.Pack, ctx.State, ctx.Seat, ctx.Legal.Where(c => c is MoveTo or Open or EndTurn or UseItem or UseCompartment).ToEqArray(), ref rng);
        return command is EndTurn ? null : command;
    }

    private static Command Fallback(BotContext ctx)
        => ctx.Legal.Contains(new EndTurn(ctx.Seat)) ? new EndTurn(ctx.Seat) : ctx.Legal.FirstOrDefault() ?? new Timeout(ctx.Seat);
}

/// <summary>Helpers role policies share.</summary>
public static class BotMoves
{
    /// <summary>A scenario action, if it's legal now.</summary>
    public static Command? Act(BotContext ctx, string action, string? data = null)
        => ctx.Legal.Contains(new ScenarioAct(ctx.Seat, action, data)) ? new ScenarioAct(ctx.Seat, action, data) : null;

    /// <summary>The farthest step this turn can afford along the cheapest path to <paramref name="target"/>, or null.</summary>
    public static Command? Toward(BotContext ctx, CompartmentId? target)
    {
        if (target is not { } goal || goal == ctx.Me.At)
        {
            return null;
        }
        var paths = Reach.Paths(ctx.Pack, ctx.State.Ship, ctx.Me.At);
        if (!paths.ContainsKey(goal))
        {
            return null;
        }
        CompartmentId? step = goal;
        while (step is { } s && !ctx.Legal.Contains(new MoveTo(ctx.Seat, s)))
        {
            step = paths[s].Via;
            if (step == ctx.Me.At)
            {
                return null;
            }
        }
        return step is { } go ? new MoveTo(ctx.Seat, go) : null;
    }

    /// <summary>The nearest of these compartments by path cost, or null if none is reachable.</summary>
    public static CompartmentId? Nearest(BotContext ctx, IEnumerable<CompartmentId> places)
    {
        var paths = Reach.Paths(ctx.Pack, ctx.State.Ship, ctx.Me.At);
        return places.Where(paths.ContainsKey).OrderBy(p => paths[p].Cost).ThenBy(p => p).Select(p => (CompartmentId?)p).FirstOrDefault();
    }

    /// <summary>An attack on the first target that <paramref name="wanted"/> picks, if one is legal.</summary>
    public static Command? Attack(BotContext ctx, Func<string, bool> wanted)
        => ctx.Legal.OfType<Attack>().FirstOrDefault(a => wanted(a.Target));

    /// <summary>For a seat commanding monsters: attack if any monster can, else move one towards the nearest target.</summary>
    public static Command? Monsters(BotContext ctx, Func<CrewState, bool> isTarget)
    {
        if (ctx.Legal.OfType<MonsterAttack>().FirstOrDefault(a => isTarget(ctx.State.CrewOf(a.Target))) is { } attack)
        {
            return attack;
        }
        var targets = ctx.State.Crew.Where(c => c.Aboard && isTarget(c)).Select(c => c.At).ToHashSet();
        if (targets.Count == 0)
        {
            return null;
        }
        foreach (var monster in ctx.Legal.OfType<MonsterMove>().GroupBy(m => m.Monster))
        {
            var at = ctx.Board.Tokens.First(t => t.Id == monster.Key).At!.Value;
            var paths = Reach.Paths(ctx.Pack, ctx.State.Ship, at);
            var goal = targets.Where(paths.ContainsKey).OrderBy(t => paths[t].Cost).FirstOrDefault();
            var best = monster.Where(m => paths.ContainsKey(m.To))
                .OrderBy(m => Reach.Paths(ctx.Pack, ctx.State.Ship, m.To).GetValueOrDefault(goal, (999, null)).Cost).FirstOrDefault();
            if (best is not null)
            {
                return best;
            }
        }
        return null;
    }
}
