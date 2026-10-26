using Redline.Logic;

namespace Redline.Presentation.Hud;

/// <summary>
/// The Reveal lower-third (mockup 10): who it is about, what happened and the position change.
/// </summary>
/// <param name="Kicker">"BIGGEST MOVER · ALEX" or "SPUN OUT · SAM" ("· AI" for an AI driver).</param>
/// <param name="Surname">"LECLERC".</param>
/// <param name="Caption">"REDLINE PAYS OFF", "REDLINE GAMBLE", "PUSH PAYS OFF"…</param>
/// <param name="Delta">"+4", "−2".</param>
/// <param name="Good">Green-flavoured (a gain) rather than red (a loss).</param>
public sealed record RoundResultCard(
    string Code, string Accent, string Kicker, string Surname, string Caption, string Delta, bool Good,
    int From, int To, int Change);

public static class RoundResult
{
    /// <summary>
    /// The card for the Reveal's camera subject (<see cref="CameraDirector"/>: a spun-out player first, else the biggest
    /// gainer), so the lower-third always matches the trackside shot. Null outside Reveal.
    /// </summary>
    public static RoundResultCard? For(RedlineHostView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (view.Phase != RacePhase.Reveal || CameraDirector.Pick(view, null) is not TracksideShot shot) return null;
        var d = view.Drivers.FirstOrDefault(r => r.Code == shot.Subject);
        if (d is null) return null;

        var delta = d.Distance - d.PreviousDistance;
        var who = d.PlayerName is { Length: > 0 } name ? name.ToUpperInvariant() : "AI";
        var throttle = (d.Throttle ?? Throttle.Cruise).ToString().ToUpperInvariant();
        var (kicker, caption) = d.Outcome switch
        {
            RoundOutcome.SpunOut => ("SPUN OUT", $"{throttle} GAMBLE"),
            RoundOutcome.Correct when d.Throttle == Throttle.Cruise => ("BIGGEST MOVER", "STEADY CRUISE"),
            RoundOutcome.Correct => ("BIGGEST MOVER", $"{throttle} PAYS OFF"),
            RoundOutcome.NoAnswer => ("ROUND RESULT", "NO ANSWER"),
            _ => ("ROUND RESULT", delta < 0 ? $"{throttle} BACKFIRES" : "MISSED IT"),
        };
        var from = d.Position + d.PositionChange;
        return new RoundResultCard(d.Code, TeamAccent.ById(d.TeamId), $"{kicker} · {who}",
            HudText.Surname(d.Code).ToUpperInvariant(), caption, Tower.Signed(delta), delta > 0 || (delta == 0 && d.Outcome == RoundOutcome.Correct),
            from, d.Position, d.PositionChange);
    }
}
