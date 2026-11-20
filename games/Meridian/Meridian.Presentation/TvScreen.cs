namespace Meridian.Presentation;

/// <summary>Which of the TV's screens a host view calls for (spec §3.1, the TV column).</summary>
public enum TvMode
{
    /// <summary>The crew roster while seats pick.</summary>
    Boarding,

    /// <summary>The ship: Explore, and the endgame with its scenario panel.</summary>
    Board,

    /// <summary>The full-screen Breach sequence, through the briefings.</summary>
    Breach,

    /// <summary>Who won, every role, the awards and the whole map.</summary>
    Debrief,
}

/// <summary>
/// The TV's top-level model for one host view. The Godot scene switches its screens on <see cref="Mode"/> and draws the
/// rest; nothing here is decided by the scene (M4 plan, Global constraints).
/// </summary>
public sealed record TvScreen(TvMode Mode, string Banner, EqArray<SeatCard> Seats)
{
    public static TvScreen Of(MeridianHostView view) => new(ModeOf(view), view.Banner, view.Seats);

    private static TvMode ModeOf(MeridianHostView view) => view.Phase switch
    {
        "boarding" => TvMode.Boarding,
        "debrief" or "over" => TvMode.Debrief,
        "briefing" => TvMode.Breach,
        // The Breach fires in Explore: its presentations open the sequence before the phase moves to Briefing.
        _ when view.Now?.Kind is "breach" or "scenario" => TvMode.Breach,
        _ => TvMode.Board,
    };
}
