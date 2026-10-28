namespace Redline.Logic;

/// <summary>A driver nationality: its ISO code, the name the TV shows, and the anthem played for it.</summary>
/// <param name="Code">ISO 3166-1 alpha-2, as in <see cref="GridDriver.Nationality"/> ("GB").</param>
/// <param name="Name">Display name ("Great Britain").</param>
/// <param name="AnthemId">The anthem's key ("gb"), or null when only the REDLINE theme plays (podium spec §1).</param>
public sealed record Country(string Code, string Name, string? AnthemId);

/// <summary>
/// The nationalities on the 2026 grid (podium spec §1). Every anthem here is a public-domain composition except Thailand's
/// and Mexico's, which fall back to the REDLINE theme. A new nationality on the grid needs a row here.
/// </summary>
public static class Countries
{
    public static IReadOnlyList<Country> All { get; } =
    [
        new("GB", "Great Britain", "gb"),
        new("AU", "Australia", "au"),
        new("MC", "Monaco", "mc"),
        new("NL", "Netherlands", "nl"),
        new("FR", "France", "fr"),
        new("IT", "Italy", "it"),
        new("ES", "Spain", "es"),
        new("CA", "Canada", "ca"),
        new("AR", "Argentina", "ar"),
        new("NZ", "New Zealand", "nz"),
        new("DE", "Germany", "de"),
        new("BR", "Brazil", null),     // Brazilian law (Lei 5.700/1971, art. 34) restricts arrangements of the anthem
        new("FI", "Finland", "fi"),
        new("TH", "Thailand", null),   // public-domain status outside Thailand unclear
        new("MX", "Mexico", null),     // Mexican law forbids arrangements of the anthem
    ];

    private static readonly Dictionary<string, Country> ByCode = All.ToDictionary(c => c.Code, StringComparer.Ordinal);

    /// <summary>The country for an ISO code, or null for one not on the grid.</summary>
    public static Country? Find(string? code) => code is not null && ByCode.TryGetValue(code, out var c) ? c : null;

    /// <summary>The country for an ISO code; an unknown code names itself and plays the theme only.</summary>
    public static Country For(string code) => Find(code) ?? new Country(code, code, null);
}
