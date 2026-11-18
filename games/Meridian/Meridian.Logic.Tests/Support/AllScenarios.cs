namespace Meridian.Logic.Tests;

/// <summary>
/// Every registered scenario, for theories that sweep them all (M7 plan, Task 1). A new scenario is covered by every
/// sweep the moment it's registered: no test lists scenarios by hand.
/// </summary>
internal static class AllScenarios
{
    public static IEnumerable<string> List => ScenarioRegistry.Default.All.Select(s => s.Info.Id).Order(StringComparer.Ordinal);

    public static TheoryData<string> Ids
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var id in List)
            {
                data.Add(id);
            }
            return data;
        }
    }

    /// <summary>Each scenario at every player count it supports.</summary>
    public static TheoryData<string, int> WithPlayers
    {
        get
        {
            var data = new TheoryData<string, int>();
            foreach (var scenario in ScenarioRegistry.Default.All.OrderBy(s => s.Info.Id, StringComparer.Ordinal))
            {
                for (var players = scenario.Info.MinPlayers; players <= scenario.Info.MaxPlayers; players++)
                {
                    data.Add(scenario.Info.Id, players);
                }
            }
            return data;
        }
    }
}
