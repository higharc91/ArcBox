namespace Meridian.Presentation.Board;

/// <summary>
/// How the scenarios' tokens and counters look on the TV, read from their content files (M7 plan, Task 1). Nothing here
/// names a scenario: a new one declares its tokens and counters in its own JSON, and the TV shows them (spec success
/// criterion 8). A kind or counter no scenario declares still shows: a labelled square, or its id capitalised.
/// </summary>
public sealed class ScenarioLooks
{
    private readonly Dictionary<string, TokenLook> _tokens = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> _counters = new(StringComparer.Ordinal);

    public static ScenarioLooks None { get; } = new([]);

    private ScenarioLooks(IEnumerable<ScenarioDef> defs)
    {
        // The loader has checked that a kind or counter reads the same in every scenario that declares it.
        foreach (var def in defs)
        {
            foreach (var (kind, look) in def.Tokens ?? new Dictionary<string, TokenLook>())
            {
                _tokens.TryAdd(kind, look);
            }
            foreach (var (id, label) in def.Counters ?? new Dictionary<string, string?>())
            {
                _counters.TryAdd(id, label);
            }
        }
    }

    public static ScenarioLooks Of(RulesPack pack) => new(pack.ScenarioDefs);

    /// <summary>Every token kind the scenarios declare.</summary>
    public IReadOnlyCollection<string> Kinds => _tokens.Keys;

    /// <summary>A token's glyph: its state's look, else its kind's, else the core look (a dropped item, a labelled square).</summary>
    public Glyph Token(string kind, string? state = null)
    {
        if (!_tokens.TryGetValue(kind, out var look))
        {
            return BoardStyle.Token(kind, state);
        }
        var shown = state is not null && look.States?.GetValueOrDefault(state) is { } inState ? inState : look;
        return new Glyph(shown.Label, shown.Colour);
    }

    /// <summary>Whether a kind walks like crew.</summary>
    public bool Monster(string kind) => _tokens.TryGetValue(kind, out var look) && look.Monster;

    /// <summary>A kind's name on the HUD ("Prize carried by Sam").</summary>
    public string TokenName(string kind)
        => _tokens.TryGetValue(kind, out var look) && look.Name is { } name ? name : Capital(kind);

    /// <summary>
    /// A per-seat counter (declared as a prefix, such as <c>infection-</c>): its seat and a short label for the seat's chip
    /// ("Infection: {seat}" becomes "Infection"). Null for any other counter, or one kept off the TV.
    /// </summary>
    public (string Label, string Seat)? PerSeat(string id)
    {
        foreach (var (prefix, label) in _counters.Where(c => c.Key.EndsWith('-') && id.StartsWith(c.Key, StringComparison.Ordinal)))
        {
            if (label is null)
            {
                return null;
            }
            var shortLabel = label.Replace("{seat}", "", StringComparison.Ordinal).Trim().TrimEnd(':').Trim();
            return (shortLabel, id[prefix.Length..]);
        }
        return null;
    }

    /// <summary>
    /// A counter's label on the TV, or null for one a scenario keeps off it. A per-seat counter (declared as a prefix)
    /// names its seat. An undeclared counter shows its id, capitalised.
    /// </summary>
    public string? Counter(string id, Func<string, string> seatName)
    {
        if (_counters.TryGetValue(id, out var label))
        {
            return label;
        }
        foreach (var (prefix, perSeat) in _counters.Where(c => c.Key.EndsWith('-') && id.StartsWith(c.Key, StringComparison.Ordinal)))
        {
            return perSeat?.Replace("{seat}", seatName(id[prefix.Length..]), StringComparison.Ordinal);
        }
        return Capital(id);
    }

    /// <summary>The first letter in capitals: "prize" → "Prize".</summary>
    public static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
