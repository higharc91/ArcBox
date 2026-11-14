namespace Meridian.Logic;

/// <summary>The scenarios a pack can choose from, by id.</summary>
public sealed class ScenarioSet(IEnumerable<IScenario> scenarios)
{
    private readonly Dictionary<string, IScenario> _byId = scenarios.ToDictionary(s => s.Info.Id);

    public static ScenarioSet Empty { get; } = new([]);

    public IReadOnlyCollection<IScenario> All => _byId.Values;
    public IScenario? Find(string id) => _byId.GetValueOrDefault(id);
    public IScenario Get(string id) => _byId[id];
}

public static class ScenarioRegistry
{
    /// <summary>The id the Breach falls back to when nothing else can run (spec §9).</summary>
    public const string Fallback = "decaying_orbit";

    /// <summary>Every scenario that ships. Stage M3 adds the five S1 scenarios here, one task at a time.</summary>
    public static ScenarioSet Default { get; } = new([new ThePassenger(), new OneOfUsIsnt(), new TillerSaysNo(), new DecayingOrbit(),
                                                      new SalvageRights()]);

    /// <summary>
    /// What's wrong between the code and the content: a listed scenario with no class, a class with no content, or a role
    /// with no bot. A scenario without a bot for every role can't be simulated or played when a phone drops (spec §4.2).
    /// </summary>
    public static IReadOnlyList<ContentError> Check(RulesPack pack)
    {
        const string file = PackLoader.BreachFile;
        var errors = new List<ContentError>();
        foreach (var id in pack.Breach.Scenarios)
        {
            if (pack.Scenarios.Find(id) is not { } scenario)
            {
                errors.Add(new(file, $"scenario {id}", "is listed but has no scenario class"));
                continue;
            }
            foreach (var role in pack.ScenarioDef(id).Roles)
            {
                try
                {
                    _ = scenario.Bot(role.Id) ?? throw new InvalidOperationException();
                }
                catch (Exception)
                {
                    errors.Add(new(file, $"scenario {id}", $"role {role.Id} has no bot"));
                }
            }
        }
        foreach (var scenario in pack.Scenarios.All.Where(s => !pack.Breach.Scenarios.Contains(s.Info.Id)))
        {
            errors.Add(new(file, $"scenario {scenario.Info.Id}", "has a class but isn't listed, so it has no content"));
        }
        return errors;
    }
}
