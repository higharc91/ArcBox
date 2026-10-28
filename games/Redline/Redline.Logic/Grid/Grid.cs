namespace Redline.Logic;

public sealed record Livery(string Top, string Side, string Stripe, string Nose, string Wing, string Endplate, string Halo, string Number);

/// <param name="Nationality">ISO 3166-1 alpha-2 ("GB"); see <see cref="Countries"/>.</param>
public sealed record GridDriver(string Id, string Name, string Code, int Number, string Tcam, double Points, double AiSkill, string Nationality);

public sealed record GridTeam(string Id, string Name, string Chassis, string Finish, Livery Livery, IReadOnlyList<GridDriver> Drivers);

public sealed record Seat(GridTeam Team, GridDriver Driver);

/// <summary>
/// A readable team colour: the livery's top colour, or its stripe for a white-bodied car (luma of the top colour above
/// 200). The TV's tower bars and tags and the phone's team accents use the same rule.
/// </summary>
public static class TeamColors
{
    /// <summary>The accent as <c>#rrggbb</c>.</summary>
    public static string Accent(GridTeam team)
    {
        ArgumentNullException.ThrowIfNull(team);
        return Luma(team.Livery.Top) > 200 ? team.Livery.Stripe : team.Livery.Top;
    }

    /// <summary>Perceived brightness 0–255 of a <c>#rrggbb</c> colour; 0 for anything else.</summary>
    public static double Luma(string hex)
    {
        var h = hex.TrimStart('#');
        if (h.Length != 6) return 0;
        var n = Convert.ToInt32(h, 16);
        return 0.299 * (n >> 16) + 0.587 * ((n >> 8) & 255) + 0.114 * (n & 255);
    }
}

public sealed record Grid(int Season, IReadOnlyList<GridTeam> Teams)
{
    /// <summary>Every seat in team order, then driver order. Derived on each call so a <c>with</c> copy never goes stale.</summary>
    public IReadOnlyList<Seat> Seats =>
        [.. Teams.SelectMany(t => t.Drivers.Select(d => new Seat(t, d)))];

    /// <summary>Finds a seat by three-letter driver code (case-sensitive). Throws <see cref="KeyNotFoundException"/> when absent.</summary>
    public Seat ByCode(string code) =>
        Seats.FirstOrDefault(s => s.Driver.Code == code)
        ?? throw new KeyNotFoundException($"No driver with code '{code}' on the {Season} grid.");
}
