namespace Meridian.Presentation.Audio;

/// <summary>How a line joins the narration queue.</summary>
public enum LineUrgency
{
    /// <summary>Waits its turn (the platform's <c>Commentary</c>).</summary>
    Queue,

    /// <summary>Cuts in front of whatever is playing (the platform's <c>Question</c>): the room needs to hear this now.</summary>
    CutIn,
}

/// <summary>One thing Tiller says, in the <c>meridian.tiller</c> voice.</summary>
public sealed record Line(string Text, LineUrgency Urgency);

/// <summary>
/// What Tiller says (spec §5.5; M4 plan, Task 5). Every line is a pure function of the TV's host view, which is built
/// from what everyone may see, so nothing secret can be spoken. Rolls, reveals and fights are silent: the board shows them.
/// </summary>
public static class TillerLines
{
    /// <summary>The line for a presented event, or null if Tiller says nothing.</summary>
    public static Line? For(Presented now)
    {
        var line = now.Kind switch
        {
            // An Incident is read in full; Salvage and Anomaly cards are named, and their text stays on screen.
            "card" when now.Deck is CardKind.Incident or null => Join(now.Title, now.Text),
            "card" => Join(now.Title, ""),
            "breach" => Join(now.Title, now.Text),
            "scenario" => Join(now.Title, now.Text),
            "death" => Join(now.Title, now.Text),
            "round" or "notice" or "vote" or "over" => Join("", now.Text),
            _ => null,
        };
        if (line is null)
        {
            return null;
        }
        var urgency = now.Kind is "card" or "breach" or "scenario" ? LineUrgency.CutIn : LineUrgency.Queue;
        return new Line(line, urgency);
    }

    /// <summary>
    /// The line when the endgame pauses for a dropped seat. It names nobody, so it sounds the same whoever dropped, and
    /// can't tell the room who the traitor is (spec §3.3). The Explore grace is silent: the banner names the seat.
    /// </summary>
    public static Line? ForWait(HostWait wait)
        => wait.Kind == "pause" ? new Line("Holding for a crew member.", LineUrgency.Queue) : null;

    /// <summary>The voice's estimate for a line, at the rules' speaking rate.</summary>
    public static double Seconds(Line line, MeridianRules rules)
        => line.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length / rules.ReadingWordsPerSecond;

    private static string? Join(string title, string text)
    {
        var parts = new[] { title.Trim(), text.Trim() }.Where(p => p.Length > 0).Select(p => p.EndsWith('.') || p.EndsWith('!') || p.EndsWith('?') ? p : p + ".");
        var joined = string.Join(' ', parts);
        return joined.Length == 0 ? null : joined;
    }
}
