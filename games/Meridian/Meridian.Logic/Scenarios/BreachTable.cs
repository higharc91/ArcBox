namespace Meridian.Logic;

/// <summary>
/// A scenario's text and numbers (<c>rules/scenarios/&lt;id&gt;.json</c>): its title, round limit, each role's briefing
/// and objectives, and the named numbers its rules read (check targets, counts), so tuning never needs a recompile.
/// </summary>
public sealed record ScenarioDef(string Id, string Title, int RoundLimit, EqArray<RoleDef> Roles,
                                 IReadOnlyDictionary<string, int>? Numbers = null)
{
    public RoleDef Role(string id) => Roles.First(r => r.Id == id);
    public int Number(string key) => Numbers is not null && Numbers.TryGetValue(key, out var n)
        ? n : throw new KeyNotFoundException($"Scenario {Id} has no number '{key}'.");
}

/// <summary>A role's briefing (at most 120 words) and the objectives shown on that seat's phone.</summary>
public sealed record RoleDef(string Id, string Side, string Name, string Briefing, EqArray<string> Objectives = default);

/// <summary>Which scenarios an Anomaly (and, optionally, the compartment it was found in) can lead to (spec §2.6).</summary>
public sealed record BreachEntry(CardId Anomaly, EqArray<string> Scenarios, TileId? Tile = null);

/// <summary><c>rules/breach.json</c>: the scenarios that ship, and the Breach table.</summary>
public sealed record BreachTable(EqArray<string> Scenarios = default, EqArray<BreachEntry> Table = default)
{
    public static BreachTable Empty { get; } = new();

    /// <summary>The candidates for an Anomaly found in a tile: a tile-specific entry first, else the Anomaly's own.</summary>
    public EqArray<string> Candidates(CardId anomaly, TileId tile)
        => (Table.FirstOrDefault(e => e.Anomaly == anomaly && e.Tile == tile)
            ?? Table.FirstOrDefault(e => e.Anomaly == anomaly && e.Tile is null))?.Scenarios ?? [];
}
