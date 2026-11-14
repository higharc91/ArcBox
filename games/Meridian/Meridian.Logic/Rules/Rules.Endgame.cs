namespace Meridian.Logic;

// The Breach's scenario, the endgame's flow and how it ends (spec §2.6, §2.9; M3 plan, Task 3).
internal sealed partial class Resolver
{
    /// <summary>
    /// Picks the scenario (spec §2.6): the table's candidates for the Anomaly and the compartment it was found in, those
    /// that can run, one by the state's generator. No candidate: any that can run. None at all: the fallback. With no
    /// scenarios registered the board stays empty, as in stage M2.
    /// </summary>
    private void ChooseScenario(BreachInfo breach)
    {
        var set = Pack.Scenarios;
        if (set.All.Count == 0)
        {
            return;
        }

        bool Runs(IScenario s) => s.CanRun(Pack, State, breach)
                                  && State.Crew.Count >= s.Info.MinPlayers && State.Crew.Count <= s.Info.MaxPlayers;

        IScenario? chosen = Pack.ForcedScenario is { } forced ? set.Find(forced) : null;
        if (chosen is null)
        {
            var candidates = breach is { Anomaly: { } anomaly, Compartment: { } at }
                ? Pack.Breach.Candidates(anomaly, State.Ship.Get(at).Tile).Select(set.Find).OfType<IScenario>().ToList()
                : [];
            var runnable = candidates.Where(Runs).ToList();
            if (runnable.Count == 0)
            {
                runnable = set.All.OrderBy(s => s.Info.Id, StringComparer.Ordinal).Where(Runs).ToList();
            }
            if (runnable.Count == 0 && set.Find(ScenarioRegistry.Fallback) is { } fallback)
            {
                runnable = [fallback];
            }
            if (runnable.Count == 0)
            {
                return;
            }
            (var pick, var rng) = State.Rng.Below(runnable.Count);
            State = State with { Rng = rng };
            chosen = runnable[pick];
        }

        // The endgame starts with the seat after the one who fired the Breach, unless the scenario's Setup says otherwise.
        State = State with { Active = (State.IndexOf(breach.Seat) + 1) % State.Crew.Count };
        var def = Pack.ScenarioDef(chosen.Info.Id);
        State = chosen.Setup(Pack, State, breach);
        State = State.WithBoard(State.Board! with { Scenario = def.Id, RoundLimit = def.RoundLimit });
        Events.Add(new ScenarioChosen(def.Id, def.Title));
    }

    /// <summary>The adapter's word that the briefings are read: the endgame's first turn begins (spec §3.1).</summary>
    private void BeginEndgame()
    {
        var crew = State.ActiveCrew;
        State = State.WithBoard(State.Board! with { StartRound = State.Round }) with
        {
            Phase = Phase.Endgame,
            MovesLeft = crew.Aboard ? Rules.Value(Pack, crew, Trait.Agility) : 0,
            Turn = new TurnFlags(),
        };
        Events.Add(new TurnStarted(State.ActiveSeat, State.Round, State.MovesLeft));
    }

    partial void RoundEnded(int round)
    {
        if (State.Phase == Phase.Endgame)
        {
            Events.Add(new RoundClosed(round));
        }
    }

    /// <summary>
    /// After every endgame command: the scenario sees what happened, then whether a side has won is checked, and only then
    /// the round limit. So an objective completed on the last turn beats the limit (M3 review focus 4).
    /// </summary>
    private void EndgameAfterStep(int from)
    {
        if (State.Phase != Phase.Endgame || State.Board is null)
        {
            return;
        }
        var scenario = Pack.Scenarios.Get(State.Board.Scenario);
        var happened = Events.Skip(from).ToList();
        foreach (var e in happened)
        {
            State = scenario.On(Pack, State, e, Events);
        }
        if (scenario.Evaluate(Pack, State) is { } outcome)
        {
            Finish(outcome);
            return;
        }
        if (happened.OfType<RoundClosed>().LastOrDefault() is { } closed
            && closed.Round - State.Board!.StartRound + 1 >= State.Board.RoundLimit)
        {
            Finish(scenario.AtLimit(Pack, State));
        }
    }

    private void Finish(Outcome outcome)
    {
        State = State.WithBoard(State.Board! with { Outcome = outcome }) with { Phase = Phase.Over, Waiting = null };
        Events.Add(new GameOver(outcome));
    }
}
