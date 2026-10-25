using ArcBox.Sdk;

namespace Redline.Logic;

/// <summary>Phone view factories with the exact §3.3 wording.</summary>
internal static class PlayerViews
{
    public const string PickPrompt = "Pick your driver";

    public static ControllerView Idle() => new WaitView("Waiting for the race…");

    public static ChoiceView PickDriver(IEnumerable<Seat> freeSeats, string? header, DateTimeOffset? deadline) =>
        new(PickPrompt, [.. freeSeats.Select(PickOption)]) { Header = header, DeadlineUtc = deadline };

    public static Option PickOption(Seat s) =>
        new(s.Driver.Code, $"#{s.Driver.Number} {s.Driver.Name}", $"{s.Team.Name} · {s.Team.Chassis}");

    public static string TakenHeader(string code) => $"{code} was just taken: pick another";

    public static WaitView Seated(Seat s, DateTimeOffset? deadline) =>
        new($"You're {s.Driver.Name}. Waiting for the grid…") { DeadlineUtc = deadline };

    public static WaitView OnGrid(Seat s, DateTimeOffset? deadline) =>
        new($"You're {s.Driver.Name}, #{s.Driver.Number}, {s.Team.Name}. Lights out in a moment…") { DeadlineUtc = deadline };
}

/// <summary>
/// Everything a player's view depends on. The game rebuilds a view only when this key changes, so the
/// instance (and the ViewId the server mints for it) stays stable otherwise. Extend it as rounds add state.
/// </summary>
internal readonly record struct PlayerViewKey(RacePhase Phase, int Round, string? Code, int FreeSeatsVersion, string? Header);
