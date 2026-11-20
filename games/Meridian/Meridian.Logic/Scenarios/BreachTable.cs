namespace Meridian.Logic;

/// <summary>
/// A scenario's text and numbers (<c>rules/scenarios/&lt;id&gt;.json</c>): its title, round limit, each role's briefing
/// and objectives, and the named numbers its rules read (check targets, counts), so tuning never needs a recompile.
/// </summary>
/// <param name="Announcement">
/// What Tiller announces to the room when the scenario begins (M4 plan, Task 2): public, at most
/// <see cref="PackLoader.AnnouncementWords"/> words. It is shown on the TV and spoken aloud, so it must hold no secret.
/// </param>
/// <param name="Tokens">
/// How each token kind the scenario places looks before its art lands, and whether it walks like crew (a monster). The
/// TV reads this, so a new scenario never touches TV code (spec success criterion 8).
/// </param>
/// <param name="Counters">
/// Each counter's label on the TV, or null to keep it off the TV (bookkeeping). A key ending in <c>-</c> is a prefix for
/// per-seat counters (<c>suspect-</c>), and its label may say <c>{seat}</c> for the seat's name.
/// </param>
/// <param name="Votes">The question each of its votes asks on the phones, by the vote's purpose.</param>
public sealed record ScenarioDef(string Id, string Title, int RoundLimit, EqArray<RoleDef> Roles,
                                 IReadOnlyDictionary<string, int>? Numbers = null, string Announcement = "",
                                 IReadOnlyDictionary<string, TokenLook>? Tokens = null,
                                 IReadOnlyDictionary<string, string?>? Counters = null,
                                 IReadOnlyDictionary<string, string>? Votes = null)
{
    public RoleDef Role(string id) => Roles.First(r => r.Id == id);
    public int Number(string key) => Numbers is not null && Numbers.TryGetValue(key, out var n)
        ? n : throw new KeyNotFoundException($"Scenario {Id} has no number '{key}'.");
}

/// <summary>
/// A token kind's placeholder look: a short label on a colour (hex), an optional name for the HUD ("carried by"), whether
/// it walks like crew, and a different look per state (a repair point broken or fixed).
/// </summary>
public sealed record TokenLook(string Label, string Colour, string? Name = null, bool Monster = false,
                               IReadOnlyDictionary<string, TokenLook>? States = null);

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
