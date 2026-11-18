namespace Meridian.Logic;

/// <summary>
/// Salvage Rights (free-for-all; M3 plan's scenario table). The Breach's Anomaly is the Prize. Whoever ends their turn in
/// the Airlock carrying it wins alone. The carrier is slowed; a hit in combat takes the Prize; a dead carrier drops it.
/// The Prize is an object, not an item: its card leaves its finder's items and rides on the prize token (which keeps
/// every card counted once), so it can't be used up, traded or dropped like gear.
/// </summary>
public sealed class SalvageRights : IScenario
{
    public const string Id = "salvage_rights";
    public const string Salvager = "salvager";

    public ScenarioInfo Info { get; } = new(Id, "free-for-all", 3, 6);

    /// <summary>Someone must be carrying an Anomaly to fight over.</summary>
    public bool CanRun(RulesPack pack, GameState state, BreachInfo breach) => PrizeHolder(pack, state, breach) is not null;

    private static (SeatId Seat, CardId Card)? PrizeHolder(RulesPack pack, GameState state, BreachInfo breach)
    {
        if (breach.Anomaly is { } anomaly && state.CrewOf(breach.Seat).Items.Contains(anomaly))
        {
            return (breach.Seat, anomaly);
        }
        foreach (var crew in state.Crew)
        {
            if (crew.Items.FirstOrDefault(i => pack.Card(i).Kind == CardKind.Anomaly) is { Value: not null } card)
            {
                return (crew.Seat, card);
            }
        }
        return null;
    }

    public GameState Setup(RulesPack pack, GameState state, BreachInfo breach)
    {
        var def = pack.ScenarioDef(Id);
        var (holder, card) = PrizeHolder(pack, state, breach)!.Value;
        var finder = state.CrewOf(holder);
        state = state.WithCrew(finder with { Items = finder.Items.Remove(card) });
        return state.WithBoard(new ScenarioBoard(Id,
            state.Crew.Select(c => new RoleCard(c.Seat, Salvager, c.Seat.Value, Audience.Everyone)).ToEqArray(),
            [new Token("prize", "prize", null, Audience.Everyone, Carrier: holder, Card: card)],
            [new Counter("escaped", 0, Audience.Everyone)],
            [new Objective(Salvager, def.Role(Salvager).Objectives[0], ObjectiveState.Open, Audience.Everyone)],
            def.RoundLimit, state.Round));
    }

    private static Token Prize(GameState state) => state.Board!.Tokens.First(t => t.Id == "prize");
    private static CompartmentId Airlock(GameState state) => Primitives.FirstOf(state, "airlock")!.Value;

    public GameState On(RulesPack pack, GameState state, GameEvent e, List<GameEvent> events)
    {
        // The carrier is slowed (never below 1), a hit takes the Prize, and a dead carrier drops it where they fell.
        state = Primitives.Carried(state, "prize", pack.ScenarioDef(Id).Number("slow"), "The Prize changes hands.", e, events);
        // Ending a turn in the Airlock with the Prize wins.
        var prize = Prize(state);
        if (e is TurnEnded t && prize.Carrier == t.Seat && state.CrewOf(t.Seat).Aboard && state.CrewOf(t.Seat).At == Airlock(state))
        {
            events.Add(new ScenarioActed(t.Seat, "escape", null, "The Airlock cycles. The Prize is gone, and so are they."));
            return state.WithBoard(state.Board!.WithCounter("escaped", 1));
        }
        return state;
    }

    public IEnumerable<ScenarioAction> ActionsFor(RulesPack pack, GameState state, SeatId seat)
    {
        var prize = Prize(state);
        if (prize.At is { } at && state.CrewOf(seat).Aboard && state.CrewOf(seat).At == at)
        {
            yield return new ScenarioAction("takePrize", "Take the Prize");
        }
    }

    public GameState Act(RulesPack pack, GameState state, ScenarioAct act, List<GameEvent> events)
    {
        events.Add(new ScenarioActed(act.From, act.Action, null, "Picked up. Everyone saw."));
        return state.WithBoard(state.Board!.WithToken(Prize(state) with { At = null, Carrier = act.From }));
    }

    public Outcome? Evaluate(RulesPack pack, GameState state)
    {
        var prize = Prize(state);
        if (state.Board!.Value("escaped") > 0)
        {
            return new Outcome([prize.Carrier!.Value], "Salvage rights claimed. Nobody else gets a share.");
        }
        return Primitives.Aboard(state).Any() ? null : new Outcome([], "Nobody walked out with it. The Meridian keeps its Prize.");
    }

    public Outcome AtLimit(RulesPack pack, GameState state) => new([], "Nobody walked out with it. The Meridian keeps its Prize.");

    public IBotPolicy Bot(string role) => role == Salvager
        ? new PolicyBot(ctx =>
        {
            var prize = Prize(ctx.State);
            if (prize.Carrier == ctx.Seat)
            {
                return ctx.Me.At == Airlock(ctx.State) ? new EndTurn(ctx.Seat) : BotMoves.Toward(ctx, Airlock(ctx.State));
            }
            var where = prize.At ?? ctx.State.CrewOf(prize.Carrier!.Value).At;
            return BotMoves.Act(ctx, "takePrize")
                   ?? BotMoves.Attack(ctx, t => prize.Carrier?.Value == t)
                   ?? BotMoves.Toward(ctx, where);
        })
        : throw new ArgumentException($"Salvage Rights deals no role {role}.", nameof(role));
}
