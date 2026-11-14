namespace Meridian.Logic;

/// <summary>
/// Tiller Says No (co-op against the ship; M3 plan's scenario table). Nobody is a traitor: the ship's computer is. The
/// crew pull three memory banks from Tiller's core; each round's end, Tiller vents the most crowded compartment.
/// </summary>
public sealed class TillerSaysNo : IScenario
{
    public const string Id = "tiller_says_no";
    public const string Crew = "crew";

    public ScenarioInfo Info { get; } = new(Id, "co-op", 3, 6);

    public bool CanRun(RulesPack pack, GameState state, BreachInfo breach) => true;

    public GameState Setup(RulesPack pack, GameState state, BreachInfo breach)
    {
        var def = pack.ScenarioDef(Id);
        var core = Primitives.FirstOf(state, "bridge", "command_landing")!.Value;
        return state.WithBoard(new ScenarioBoard(Id,
            state.Crew.Select(c => new RoleCard(c.Seat, Crew, Crew, Audience.Everyone)).ToEqArray(),
            [new Token("core", "core", core, Audience.Everyone)],
            [new Counter("banks", 0, Audience.Everyone)],
            [new Objective(Crew, def.Role(Crew).Objectives[0], ObjectiveState.Open, Audience.Everyone)],
            def.RoundLimit, state.Round));
    }

    private static CompartmentId Core(GameState state) => state.Board!.Tokens.First(t => t.Id == "core").At!.Value;

    /// <summary>
    /// Tiller vents the compartment with the most crew in it (ties: the lowest id). Each crew member there makes an
    /// Agility check or loses a step from their higher physical trait; a death is dealt by the rules after.
    /// </summary>
    public GameState On(RulesPack pack, GameState state, GameEvent e, List<GameEvent> events)
    {
        if (e is not RoundClosed)
        {
            return state;
        }
        var crowded = Primitives.Aboard(state).GroupBy(c => c.At)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key).FirstOrDefault();
        if (crowded is null)
        {
            return state;
        }
        var save = pack.ScenarioDef(Id).Number("ventSave");
        events.Add(new ScenarioNotice($"Tiller vents the {pack.Tile(state.Ship.Get(crowded.Key).Tile).Name}.", crowded.Key));
        foreach (var crew in crowded.ToList())
        {
            (state, var total) = Primitives.Check(pack, state, crew.Seat, Trait.Agility, events);
            if (total < save)
            {
                var me = state.CrewOf(crew.Seat);
                var trait = me.Steps.Brawn >= me.Steps.Agility ? Trait.Brawn : Trait.Agility;
                events.Add(new TraitChanged(crew.Seat, trait, me.Steps[trait], me.Steps[trait] - 1));
                state = state.WithCrew(me with { Steps = me.Steps.With(trait, me.Steps[trait] - 1) });
            }
        }
        return state;
    }

    public IEnumerable<ScenarioAction> ActionsFor(RulesPack pack, GameState state, SeatId seat)
    {
        if (state.CrewOf(seat).Aboard && state.CrewOf(seat).At == Core(state))
        {
            yield return new ScenarioAction("pullBank", "Pull a memory bank");
        }
    }

    public GameState Act(RulesPack pack, GameState state, ScenarioAct act, List<GameEvent> events)
    {
        var (next, total) = Primitives.Check(pack, state, act.From, Trait.Tech, events);
        var pulled = total >= pack.ScenarioDef(Id).Number("bankTarget");
        events.Add(new ScenarioActed(act.From, act.Action, null, pulled ? "A memory bank slides free. Tiller stutters." : "Tiller locks the bank in place."));
        return pulled ? next.WithBoard(next.Board!.WithCounter("banks", next.Board.Value("banks") + 1)) : next;
    }

    public Outcome? Evaluate(RulesPack pack, GameState state)
    {
        if (state.Board!.Value("banks") >= BanksNeeded(pack, state))
        {
            return new Outcome(state.Crew.Select(c => c.Seat).ToEqArray(), "Tiller goes quiet. The Meridian is just a ship again.");
        }
        return Primitives.Aboard(state).Any() ? null : new Outcome([], "Tiller kept the ship. It always meant to.");
    }

    public Outcome AtLimit(RulesPack pack, GameState state) => new([], "Tiller says no. It will always say no.");

    /// <summary>One bank per player, or the content's minimum if that's more (M3 balance report).</summary>
    public static int BanksNeeded(RulesPack pack, GameState state)
        => Math.Max(pack.ScenarioDef(Id).Number("banksNeeded"), state.Crew.Count);

    public IBotPolicy Bot(string role) => role == Crew
        ? new PolicyBot(ctx => BotMoves.Act(ctx, "pullBank") ?? BotMoves.Toward(ctx, Core(ctx.State)))
        : throw new ArgumentException($"Tiller Says No deals no role {role}.", nameof(role));
}
