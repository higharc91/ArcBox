using Redline.Logic;

namespace Redline.Presentation.Hud;

/// <summary>How a tower chip is painted (broadcast.css <c>.chip.*</c>).</summary>
public enum ChipKind
{
    /// <summary>Grey on faint white: CRUISE.</summary>
    Cruise,
    /// <summary>Dark on white: PUSH.</summary>
    Push,
    /// <summary>White on red: REDLINE.</summary>
    Redline,
    /// <summary>Dashed outline: CHOOSING.</summary>
    Wait,
    /// <summary>White on faint white: LOCKED, READY.</summary>
    Locked,
    /// <summary>Dark on white: PIT (pit limiter).</summary>
    Pit,
    /// <summary>Dark on green: a correct answer's gain.</summary>
    Good,
    /// <summary>Red outline: a miss.</summary>
    Bad,
    /// <summary>White on red-orange: a spin-out.</summary>
    Spin,
    /// <summary>White on purple: a qualifying lap that is the session's fastest.</summary>
    Purple,
}

public readonly record struct Chip(string Text, ChipKind Kind);

/// <summary>Small status mark beside the chip in the Question phase.</summary>
public enum AnswerMark { None, Answering, Answered }

/// <summary>One timing-tower row, ready to paint.</summary>
/// <param name="Slot">Row index from the top (the view's ranking order).</param>
/// <param name="Position">The shown position number (shared on ties).</param>
/// <param name="PlayerName">Upper-case player name for humans, null for AI.</param>
/// <param name="Gap">"LEAD", "TIED", "−3", "GRID", or "" when the phase has no gaps. In qualifying, the total time ("6.82", "5.90 · 2/3" after a deleted lap), or "—" before a first correct lap.</param>
/// <param name="Movement">Places gained (+) or lost (−) this round; nonzero only in Reveal and QualifyingResult.</param>
public sealed record TowerRow(
    int Slot, string Code, string TeamId, string Accent, int Position, string? PlayerName, string Gap, int Movement,
    Chip? Chip, AnswerMark Mark, bool Connected)
{
    public bool Human => PlayerName is not null;
}

/// <summary>
/// The compact 22-row timing tower (spec §4.8 HUD): order, gap text, throttle / status / result chips and movement, all
/// from the host view. Secrecy is the server's job, but the tower never shows a throttle before Question either. In qualifying
/// (spec §6) it is the timing board: total in the gap column, the lap's chip (purple, green or DELETED) at the Result.
/// </summary>
public static class Tower
{
    public static IReadOnlyList<TowerRow> Rows(RedlineHostView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (view.Qualifying is { } session && QualifyingText.IsQualifying(view.Phase)) return QualifyingRows(view, session);
        var leader = view.Drivers.Count == 0 ? 0 : view.Drivers.Max(d => d.Distance);
        return [.. view.Drivers.Select((d, i) => new TowerRow(
            i, d.Code, d.TeamId, TeamAccent.ById(d.TeamId), d.Position,
            d.Player is null ? null : (d.PlayerName ?? "PLAYER").ToUpperInvariant(),
            Gap(view.Phase, i, leader - d.Distance),
            view.Phase == RacePhase.Reveal ? d.PositionChange : 0,
            ChipFor(d, view.Phase),
            Mark(d, view.Phase),
            d.Connected))];
    }

    /// <summary>
    /// The qualifying board, in the view's classification order. Gap: the total of the completed correct laps, with the
    /// correct-lap count when the car missed any (<see cref="QualifyingText.TotalColumn"/>). Chip: only at
    /// the Result, this lap's time (purple for the fastest, green otherwise) or DELETED. Mark: who has answered, during the
    /// question. Movement: the places gained or lost at the Result, which is when the rows slide.
    /// </summary>
    private static IReadOnlyList<TowerRow> QualifyingRows(RedlineHostView view, QualifyingSession session)
    {
        var cars = view.Drivers.ToDictionary(d => d.Code);
        var result = view.Phase == RacePhase.QualifyingResult;
        return [.. session.Rows.Select((q, i) =>
        {
            cars.TryGetValue(q.Code, out var d);
            var teamId = d?.TeamId ?? "";
            return new TowerRow(
                i, q.Code, teamId, TeamAccent.ById(teamId), q.Position,
                d?.Player is null ? null : (d.PlayerName ?? "PLAYER").ToUpperInvariant(),
                QualifyingText.TotalColumn(q, view.Phase, session.Lap),
                result ? q.PositionChange : 0,
                result ? LapChip(q) : null,
                view.Phase == RacePhase.QualifyingQuestion ? (q.Answered ? AnswerMark.Answered : AnswerMark.Answering) : AnswerMark.None,
                d?.Connected ?? true);
        })];
    }

    /// <summary>The total column before a car has a correct lap.</summary>
    public const string NoTime = "—";

    /// <summary>A qualifying lap at the Result: "2.41" (purple when the session's fastest, else green) or a red "DELETED".</summary>
    public static Chip LapChip(QualifyingRow q) => q.LastLapMs is { } ms
        ? new Chip(QualifyingText.Lap(ms), q.FastestLap ? ChipKind.Purple : ChipKind.Good)
        : new Chip("DELETED", ChipKind.Bad);

    /// <summary>Gap to the leader: the first row with no gap leads, later rows level with the leader are TIED.</summary>
    public static string Gap(RacePhase phase, int slot, int behind) => phase switch
    {
        RacePhase.DriverSelect => "",
        _ when QualifyingText.IsQualifying(phase) => "",
        RacePhase.Grid => "GRID",
        _ when behind <= 0 => slot == 0 ? "LEAD" : "TIED",
        _ => $"−{behind}",
    };

    /// <summary>
    /// Throttle: LOCKED / CHOOSING / PIT. Reading and Question: the revealed throttle. Reveal: the result with the distance actually
    /// gained or lost. Grid: READY. Driver Select and Chequered: none.
    /// </summary>
    public static Chip? ChipFor(DriverRow d, RacePhase phase)
    {
        switch (phase)
        {
            case RacePhase.Grid:
                return new Chip("READY", ChipKind.Locked);
            case RacePhase.Throttle:
                return d.Status switch
                {
                    DriverStatus.Limited => new Chip("PIT", ChipKind.Pit),
                    DriverStatus.Choosing => new Chip("CHOOSING", ChipKind.Wait),
                    _ => new Chip("LOCKED", ChipKind.Locked),
                };
            case RacePhase.Reading:
            case RacePhase.Question:
                return d.Throttle is { } t ? ThrottleChip(t) : null;
            case RacePhase.Reveal:
                return ResultChip(d);
            default:
                return null;
        }
    }

    public static Chip ThrottleChip(Throttle t) => t switch
    {
        Throttle.Push => new Chip("PUSH", ChipKind.Push),
        Throttle.Redline => new Chip("REDLINE", ChipKind.Redline),
        _ => new Chip("CRUISE", ChipKind.Cruise),
    };

    /// <summary>"REDLINE +4" (green), "MISS · 0" / "MISS −1" (red outline), "SPUN −2" (red).</summary>
    public static Chip? ResultChip(DriverRow d)
    {
        var delta = d.Distance - d.PreviousDistance;
        return d.Outcome switch
        {
            RoundOutcome.Correct => new Chip($"{ThrottleChip(d.Throttle ?? Throttle.Cruise).Text} {Signed(delta)}", ChipKind.Good),
            RoundOutcome.SpunOut => new Chip($"SPUN {Signed(delta)}", ChipKind.Spin),
            RoundOutcome.Wrong or RoundOutcome.NoAnswer => new Chip(delta == 0 ? "MISS · 0" : $"MISS {Signed(delta)}", ChipKind.Bad),
            _ => null,
        };
    }

    public static AnswerMark Mark(DriverRow d, RacePhase phase) => phase != RacePhase.Question
        ? AnswerMark.None
        : d.Status == DriverStatus.Answered ? AnswerMark.Answered : AnswerMark.Answering;

    /// <summary>"+4", "−2", "0" (with a real minus sign).</summary>
    public static string Signed(int n) => n > 0 ? $"+{n}" : n < 0 ? $"−{-n}" : "0";
}
