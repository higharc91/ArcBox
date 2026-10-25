using ArcBox.Sdk;

namespace Redline.Logic;

/// <summary>
/// Mutable per-car race state for one of the 22 seats. <see cref="PlayerId"/> is null while the seat is
/// unclaimed and stays null for the AI drivers that fill it once Driver Select ends.
/// </summary>
public sealed class Driver(Seat seat)
{
    public Seat Seat { get; } = seat;
    public string Code => Seat.Driver.Code;

    public PlayerId? PlayerId { get; set; }
    public string? PlayerName { get; set; }
    public bool IsHuman => PlayerId is not null;

    /// <summary>The player's connection; always true for AI.</summary>
    public bool Connected { get; set; } = true;

    /// <summary>Displayed position (1-based). Team order in Driver Select, start order on the Grid, then ranking.</summary>
    public int Position { get; set; }
    public int PositionAtRoundStart { get; set; }

    public int Distance { get; set; }
    public int PreviousDistance { get; set; }

    /// <summary>This round's throttle once decided (null while choosing).</summary>
    public Throttle? Throttle { get; set; }
    /// <summary>This round's answer (0..3, index into the shuffled answers) once given.</summary>
    public int? AnswerIndex { get; set; }
    public RoundOutcome? Outcome { get; set; }
    /// <summary>Locked to Cruise this round after a spin-out.</summary>
    public bool PitLimiter { get; set; }

    public int Correct { get; set; }
    public int Redlines { get; set; }
    public int SpinOuts { get; set; }
}
