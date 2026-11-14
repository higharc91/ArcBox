namespace Meridian.Logic;

/// <summary>
/// A loaded, validated content pack. Lists keep file order, so building piles from them is deterministic; lookups go
/// through the dictionaries.
/// </summary>
public sealed class RulesPack
{
    private readonly Dictionary<TileId, TileDef> _tiles;
    private readonly Dictionary<CrewId, CrewDef> _crew;
    private readonly Dictionary<CardId, CardDef> _cards;

    public RulesPack(MeridianRules rules, IReadOnlyList<TileDef> startTiles, IReadOnlyList<TileDef> tiles,
                     StartLayout start, IReadOnlyList<CrewDef> crew, IReadOnlyList<CardDef> cards,
                     BreachTable? breach = null, IReadOnlyList<ScenarioDef>? scenarioDefs = null, ScenarioSet? scenarios = null,
                     string? forcedScenario = null)
    {
        Breach = breach ?? BreachTable.Empty;
        ScenarioDefs = scenarioDefs ?? [];
        Scenarios = scenarios ?? ScenarioSet.Empty;
        ForcedScenario = forcedScenario;
        Rules = rules;
        StartTiles = startTiles;
        Tiles = tiles;
        Start = start;
        Crew = crew;
        Cards = cards;
        // First definition wins: a duplicate id must reach Validation, which reports it, rather than throw here.
        _tiles = FirstWins(startTiles.Concat(tiles), t => t.Id);
        _crew = FirstWins(crew, c => c.Id);
        _cards = FirstWins(cards, c => c.Id);
    }

    private static Dictionary<TKey, T> FirstWins<TKey, T>(IEnumerable<T> items, Func<T, TKey> key) where TKey : notnull
    {
        var map = new Dictionary<TKey, T>();
        foreach (var item in items)
        {
            map.TryAdd(key(item), item);
        }
        return map;
    }

    public MeridianRules Rules { get; }

    /// <summary>The scenarios that ship and the Breach table (<c>rules/breach.json</c>).</summary>
    public BreachTable Breach { get; }

    public IReadOnlyList<ScenarioDef> ScenarioDefs { get; }

    /// <summary>The scenario classes this pack chooses from.</summary>
    public ScenarioSet Scenarios { get; }

    /// <summary>Development only: the scenario every Breach picks (the <c>Meridian:Scenario</c> setting).</summary>
    public string? ForcedScenario { get; }

    public ScenarioDef ScenarioDef(string id) => ScenarioDefs.First(d => d.Id == id);

    /// <summary>Tiles placed before the game, never drawn.</summary>
    public IReadOnlyList<TileDef> StartTiles { get; }

    /// <summary>Tiles that go in the tile pile, each <see cref="TileDef.Copies"/> times.</summary>
    public IReadOnlyList<TileDef> Tiles { get; }

    public StartLayout Start { get; }
    public IReadOnlyList<CrewDef> Crew { get; }
    public IReadOnlyList<CardDef> Cards { get; }

    public TileDef Tile(TileId id) => _tiles[id];
    public CrewDef CrewMember(CrewId id) => _crew[id];
    public CardDef Card(CardId id) => _cards[id];
    public bool HasTile(TileId id) => _tiles.ContainsKey(id);
    public bool HasCrew(CrewId id) => _crew.ContainsKey(id);

    public IEnumerable<CardDef> CardsOf(CardKind kind) => Cards.Where(c => c.Kind == kind);

    /// <summary>A copy with other rule numbers, for the simulator and tests.</summary>
    public RulesPack WithRules(MeridianRules rules)
        => new(rules, StartTiles, Tiles, Start, Crew, Cards, Breach, ScenarioDefs, Scenarios, ForcedScenario);

    /// <summary>A copy choosing from other scenario classes (tests use stubs).</summary>
    public RulesPack WithScenarios(ScenarioSet scenarios, BreachTable? breach = null, IReadOnlyList<ScenarioDef>? defs = null)
        => new(Rules, StartTiles, Tiles, Start, Crew, Cards, breach ?? Breach, defs ?? ScenarioDefs, scenarios, ForcedScenario);

    public RulesPack WithForcedScenario(string? id)
        => new(Rules, StartTiles, Tiles, Start, Crew, Cards, Breach, ScenarioDefs, Scenarios, id);
}
