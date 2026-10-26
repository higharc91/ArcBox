using Redline.Logic;

namespace Redline.Presentation.Hud;

/// <summary>One seat on the Driver Select board: claimed seats show the player, free ones "AI".</summary>
public sealed record BoardSeat(string Code, int Number, string? PlayerName)
{
    public bool Claimed => PlayerName is not null;
}

/// <summary>One team row: name, colour and its two seats in grid order.</summary>
public sealed record BoardTeam(string TeamId, string Name, string Accent, IReadOnlyList<BoardSeat> Seats);

/// <summary>The claim lower-third (mockup 14): "MO CLAIMED / FERNANDO ALONSO / ASTON MARTIN / AMR26".</summary>
public sealed record ClaimCard(string Code, int Number, string Accent, string NumberInk, string Kicker, string DriverName, string TeamName, string Chassis);

public static class DriverSelectBoard
{
    /// <summary>Teams in the view's order (team order during Driver Select), each with its seats.</summary>
    public static IReadOnlyList<BoardTeam> Teams(RedlineHostView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return [.. view.Drivers
            .GroupBy(d => d.TeamId)
            .Select(g =>
            {
                var team = TeamAccent.Team(g.Key);
                return new BoardTeam(g.Key, (team?.Name ?? g.Key).ToUpperInvariant(), TeamAccent.ById(g.Key),
                    [.. g.Select(d => new BoardSeat(d.Code, d.Number, d.Player is null ? null : (d.PlayerName ?? "PLAYER").ToUpperInvariant()))]);
            })];
    }

    /// <summary>"18 SEATS LEFT".</summary>
    public static string SeatsLeft(RedlineHostView view)
    {
        var free = view.Drivers.Count(d => d.Player is null);
        return free == 1 ? "1 SEAT LEFT · AI TAKES THE REST" : $"{free} SEATS LEFT · AI TAKES THE REST";
    }

    /// <summary>
    /// The card for the newest claim (the same driver the line-up camera holds on), or <paramref name="shown"/> when this
    /// view claims nobody new, as long as that driver still holds the seat.
    /// </summary>
    public static ClaimCard? Claim(RedlineHostView view, RedlineHostView? previous, ClaimCard? shown)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (view.Phase != RacePhase.DriverSelect) return null;
        var code = CameraDirector.NewlyClaimed(view, previous) ?? shown?.Code;
        var row = code is null ? null : view.Drivers.FirstOrDefault(d => d.Code == code && d.Player is not null);
        if (row is null) return null;
        var seat = HudText.Seat(row.Code);
        var accent = TeamAccent.ById(row.TeamId);
        return new ClaimCard(row.Code, row.Number, accent, TeamAccent.Luma(accent) > 150 ? "#111111" : "#ffffff",
            $"{(row.PlayerName ?? "PLAYER").ToUpperInvariant()} CLAIMED",
            (seat?.Driver.Name ?? row.Code).ToUpperInvariant(),
            (seat?.Team.Name ?? row.TeamId).ToUpperInvariant(),
            seat?.Team.Chassis ?? "");
    }
}
