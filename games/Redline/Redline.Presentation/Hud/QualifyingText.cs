using Redline.Logic;

namespace Redline.Presentation.Hud;

/// <summary>
/// Qualifying spec §6: every piece of qualifying wording the TV derives from the host view, kept out of Godot so it can be
/// tested. Lap times and totals live here and nowhere else in Presentation: seconds to two decimals, rounded half away
/// from zero, in the invariant culture ("2.41", never "2,41").
/// </summary>
public static class QualifyingText
{
    /// <summary>One lap time: 2405 ms is "2.41".</summary>
    public static string Lap(int ms) => Seconds(ms);

    /// <summary>A running total, in the same format as a lap: 6815 ms is "6.82".</summary>
    public static string Total(int ms) => Seconds(ms);

    /// <summary>
    /// The tower's TOTAL column for a car: the total of its correct laps ("7.05"), with the count of correct laps when it
    /// has missed any of those completed so far ("5.90 · 2/3"), since the classification counts correct laps before time;
    /// <see cref="Tower.NoTime"/> before a first correct lap. Laps completed: the current one at its Result, those before it otherwise.
    /// </summary>
    public static string TotalColumn(QualifyingRow row, RacePhase phase, int lap)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.Correct == 0) return Tower.NoTime;
        var completed = phase == RacePhase.QualifyingResult ? lap : lap - 1;
        return row.Correct < completed ? $"{Total(row.TotalMs)} · {row.Correct}/{completed}" : Total(row.TotalMs);
    }

    /// <summary>"QUALIFYING · LAP 1/3": the question panel's header.</summary>
    public static string Header(int lap, int laps) => $"QUALIFYING · LAP {lap}/{laps}";

    /// <summary>
    /// The name on the provisional pole card, when P1 changed at this QualifyingResult: the new leader. Null in any other
    /// phase, when the leader is unchanged, or when nobody has a time. P1 is compared with the previous view's (the lap's
    /// question, showing the classification before it); without one the leader's own <see cref="QualifyingRow.PositionChange"/>
    /// says whether they were P1 before.
    /// </summary>
    public static string? ProvisionalPole(RedlineHostView view, RedlineHostView? previous)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (view.Phase != RacePhase.QualifyingResult || Leader(view) is not { } pole) return null;
        var changed = previous is { Qualifying.Rows: { Count: > 0 } before } && IsQualifying(previous.Phase)
            ? before.MinBy(r => r.Position)!.Code != pole.Row.Code
            : pole.Row.PositionChange > 0;
        return changed ? pole.Name : null;
    }

    /// <summary>
    /// The pole card after the last lap: the leader's name, team and total. Null when nobody set a time. The name is as the
    /// player typed it, or the driver's surname for an AI; the TV upper-cases it for the card and the commentary speaks it.
    /// </summary>
    public static (string Name, string TeamId, string Total)? Pole(RedlineHostView lastResult)
    {
        ArgumentNullException.ThrowIfNull(lastResult);
        return Leader(lastResult) is { } pole ? (pole.Name, pole.Team, Total(pole.Row.TotalMs)) : null;
    }

    /// <summary>The three qualifying phases (the host view's <see cref="RedlineHostView.Qualifying"/> is set in exactly these).</summary>
    public static bool IsQualifying(RacePhase phase) =>
        phase is RacePhase.QualifyingReading or RacePhase.QualifyingQuestion or RacePhase.QualifyingResult;

    /// <summary>P1 of the classification, if they have a correct lap: the row, their name and their team.</summary>
    private static (QualifyingRow Row, string Name, string Team)? Leader(RedlineHostView view)
    {
        if (view.Qualifying?.Rows.MinBy(r => r.Position) is not { Correct: > 0 } row) return null;
        var driver = view.Drivers.FirstOrDefault(d => d.Code == row.Code);
        var name = driver?.Player is not null
            ? driver.PlayerName is { Length: > 0 } typed ? typed : row.Code
            : HudText.Surname(row.Code);
        return (row, name, driver?.TeamId ?? "");
    }

    private static string Seconds(int ms)
    {
        // Whole milliseconds to hundredths with the half going away from zero, in integers so no float error can tip it.
        var hundredths = (Math.Abs((long)ms) + 5) / 10;
        return string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{(ms < 0 && hundredths > 0 ? "-" : "")}{hundredths / 100}.{hundredths % 100:00}");
    }
}
