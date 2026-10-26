using Redline.Logic;

namespace Redline.Presentation.Hud;

/// <summary>
/// A readable team colour for tower bars, map dots and tags: the livery's top colour, or its stripe for a white-bodied car
/// (luma of the top colour above 200). Same rule as <c>accent()</c> in the mockups' grid2026.js and the phone skin.
/// </summary>
public static class TeamAccent
{
    private const string Fallback = "#888888";

    private static readonly Lazy<Dictionary<string, GridTeam>> Teams =
        new(() => Grid2026.Load().Teams.ToDictionary(t => t.Id, StringComparer.Ordinal));

    /// <summary>The accent as <c>#rrggbb</c>.</summary>
    public static string Of(GridTeam team)
    {
        ArgumentNullException.ThrowIfNull(team);
        return Luma(team.Livery.Top) > 200 ? team.Livery.Stripe : team.Livery.Top;
    }

    /// <summary>The accent for a <see cref="DriverRow.TeamId"/>; a neutral grey for an unknown id.</summary>
    public static string ById(string? teamId) =>
        teamId is not null && Teams.Value.TryGetValue(teamId, out var team) ? Of(team) : Fallback;

    /// <summary>The team for an id, or null.</summary>
    public static GridTeam? Team(string? teamId) =>
        teamId is not null && Teams.Value.TryGetValue(teamId, out var team) ? team : null;

    /// <summary>Perceived brightness 0–255 of a <c>#rrggbb</c> colour.</summary>
    public static double Luma(string hex)
    {
        var h = hex.TrimStart('#');
        if (h.Length != 6) return 0;
        var n = Convert.ToInt32(h, 16);
        return 0.299 * (n >> 16) + 0.587 * ((n >> 8) & 255) + 0.114 * (n & 255);
    }
}
