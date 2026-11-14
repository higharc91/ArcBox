namespace Meridian.Logic.Tests;

/// <summary>Games of the shipped pack with a scenario forced, played by bots.</summary>
internal static class ScenarioRun
{
    public static RulesPack Pack(string id) => BasePack.Pack.WithForcedScenario(id);

    /// <summary>Bots explore to the Breach; then the endgame begins. Returns the state at its first turn.</summary>
    public static GameState Begun(string id, int players = 4, ulong seed = 1)
    {
        var pack = Pack(id);
        var state = Rules.NewGame(pack, BotRunner.Seats(pack, players), seed);
        foreach (var step in BotRunner.Play(pack, state, ExploreBot.Instance, seed, 600))
        {
            state = step.Result.State;
        }
        Assert.Equal(id, state.Board!.Scenario);
        return Rules.Step(pack, state, new Begin(state.ActiveSeat)).State;
    }

    /// <summary>A whole bot game. Returns the last state and anything wrong: an illegal command or a broken invariant.</summary>
    public static (GameState Final, IReadOnlyList<string> Errors) ToEnd(string id, int players, ulong seed, int maxCommands = 4000)
    {
        var pack = Pack(id);
        var state = Rules.NewGame(pack, BotRunner.Seats(pack, players), seed);
        var errors = new List<string>();
        foreach (var step in BotRunner.PlayToEnd(pack, state, seed, maxCommands))
        {
            if (step.Result.Events.FirstOrDefault() is Rejected r)
            {
                errors.Add($"illegal {step.Command}: {r.Reason}");
                break;
            }
            state = step.Result.State;
            if (Invariants.Check(pack, state) is { Count: > 0 } broken)
            {
                errors.Add(string.Join("; ", broken));
                break;
            }
        }
        if (errors.Count == 0 && state.Phase != Phase.Over)
        {
            errors.Add($"no outcome within {maxCommands} commands (phase {state.Phase}, round {state.Round})");
        }
        return (state, errors);
    }

    /// <summary>Bots play every seed at every player count to an outcome, with every invariant held.</summary>
    public static void Sweep(string id, int seeds)
    {
        var failures = new List<string>();
        foreach (var players in new[] { 3, 4, 5, 6 })
        {
            for (ulong seed = 1; seed <= (ulong)seeds && failures.Count < 5; seed++)
            {
                var (_, errors) = ToEnd(id, players, seed);
                failures.AddRange(errors.Select(e => $"{players} players, seed {seed}: {e}"));
            }
        }
        Assert.Empty(failures);
    }
}
