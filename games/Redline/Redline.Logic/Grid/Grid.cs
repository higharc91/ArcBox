namespace Redline.Logic;

public sealed record Livery(string Top, string Side, string Stripe, string Nose, string Wing, string Endplate, string Halo, string Number);

public sealed record GridDriver(string Id, string Name, string Code, int Number, string Tcam, double Points, double AiSkill);

public sealed record GridTeam(string Id, string Name, string Chassis, string Finish, Livery Livery, IReadOnlyList<GridDriver> Drivers);

public sealed record Seat(GridTeam Team, GridDriver Driver);

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
