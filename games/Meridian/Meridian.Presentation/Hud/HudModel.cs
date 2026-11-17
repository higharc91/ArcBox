using Meridian.Presentation.Beats;
using Meridian.Presentation.Board;

namespace Meridian.Presentation.Hud;

/// <summary>A seat on the TV's strip. It shows who is active, who a bot plays and who is offline; never a side or a role.</summary>
public sealed record HudSeat(string Id, string Name, string? Crew, string Mark, int Colour, bool Active, bool Auto, bool Offline);

/// <summary>One figure on the HUD: a label and its value.</summary>
public sealed record HudStat(string Label, string Value);

/// <summary>
/// The panel over the board for the event being shown: a card or a line of text (<see cref="Faces"/> empty), or dice
/// (<see cref="Faces"/> set). <see cref="Accent"/> is a hex colour: the card's deck, or the kind's.
/// </summary>
public sealed record HudPanel(string Kind, string Title, string Text, string Accent, EqArray<int> Faces);

/// <summary>
/// Everything the TV's HUD draws (M4 plan, Task 7), projected from the host view and the beat being shown. It is a
/// projection, not a source: nothing here that the view doesn't already say.
/// </summary>
public sealed record HudModel(
    EqArray<HudSeat> Seats,
    string Banner,
    EqArray<HudStat> Stats,
    string? Scenario,
    EqArray<string> Objectives,
    HudPanel? Panel,
    EqArray<string> Log,
    string? Wait)
{
    /// <summary>How many log lines the ticker shows.</summary>
    public const int LogLines = 3;

    /// <param name="waited">Seconds since the view carrying <see cref="MeridianHostView.Wait"/> arrived: the TV counts down.</param>
    public static HudModel Of(MeridianHostView view, Beat? beat, double waited = 0)
    {
        var marks = BoardStyle.Marks([.. view.Seats.Select(s => s.Name)]);
        var seats = view.Seats.Select((s, i) => new HudSeat(s.Id, s.Name, s.Crew, marks[i], s.Colour, s.Active, s.Auto, !s.Connected)).ToEqArray();
        var stats = view.Scenario is { } panel ? ScenarioStats(view, panel) : ExploreStats(view);
        var log = view.Log.Skip(Math.Max(0, view.Log.Count - LogLines)).ToEqArray();
        return new HudModel(seats, view.Banner, stats, view.Scenario?.Title,
                            view.Scenario?.Objectives.Where(o => o.State == ObjectiveState.Open).Select(o => o.Text).ToEqArray() ?? [],
                            beat is { Short: false } ? PanelFor(beat.Now) : null, log, WaitText(view.Wait, waited, view.Seats.Count));
    }

    private static EqArray<HudStat> ExploreStats(MeridianHostView view)
        => [new("Round", $"{view.Round}"), new("Anomalies", $"{view.AnomaliesDrawn}"), new("Tiles left", $"{view.TilesLeft}")];

    private static EqArray<HudStat> ScenarioStats(MeridianHostView view, ScenarioPanel panel)
    {
        string Name(string seat) => view.Seats.FirstOrDefault(s => s.Id == seat)?.Name ?? seat;
        var stats = new List<HudStat> { new(panel.RoundsLeft == 1 ? "Final round" : "Rounds left", $"{panel.RoundsLeft}") };
        foreach (var counter in panel.Counters)
        {
            if (CounterLabel(counter.Id, Name) is { } label)
            {
                stats.Add(new HudStat(label, $"{counter.Value}"));
            }
        }
        stats.AddRange(panel.Tokens.Where(t => t.Carrier is not null)
                                   .Select(t => new HudStat(Capital(t.Kind), $"carried by {Name(t.Carrier!)}")));
        return [.. stats];
    }

    /// <summary>A counter's label on the TV, or null for the scenario's own bookkeeping, which means nothing to the room.</summary>
    public static string? CounterLabel(string id, Func<string, string> seatName) => id switch
    {
        "voteRound" => null,
        "banks" => "Memory banks pulled",
        "doses" => "Doses",
        "injected" => "Injected",
        "engines" => "Engine restarts",
        "escaped" => "Escaped",
        "exposed" => "Exposed",
        "orbit" => "Orbit",
        "pods" => "Pods left",
        "votes" => "Votes called",
        _ when id.StartsWith("suspect-", StringComparison.Ordinal) => $"Suspicion: {seatName(id["suspect-".Length..])}",
        _ => Capital(id),
    };

    private static HudPanel? PanelFor(Presented now) => now.Kind switch
    {
        "roll" or "fight" => new HudPanel(now.Kind, now.Title, now.Text, "#ffd447", now.Faces),
        "card" => new HudPanel("card", now.Title, now.Text, now.Deck is { } deck ? BoardStyle.Symbol(deck).Colour : "#9a9aa8", []),
        "notice" or "vote" or "death" or "round" or "over" or "sealed" => new HudPanel(now.Kind, now.Title, now.Text, KindAccent(now.Kind), []),
        _ => null,   // a reveal has its callout; the Breach has its own sequence
    };

    private static string KindAccent(string kind) => kind switch
    {
        "death" or "sealed" => "#ff3b3b",
        "round" => "#ffd447",
        "vote" => "#3ec7ff",
        _ => "#c08cff",
    };

    /// <summary>
    /// The waiting banner, counting down from what the view said. A vote's wait names nobody; after a restart it counts
    /// the phones back, never naming them.
    /// </summary>
    public static string? WaitText(HostWait? wait, double waited, int seats = 0)
    {
        if (wait is null)
        {
            return null;
        }
        var left = Math.Max(0, (int)Math.Ceiling(wait.Seconds - waited));
        if (wait.Kind == "resume")
        {
            return $"Reconnecting… {wait.Back ?? 0} of {seats} phones back · {left} s";
        }
        return wait.Name is { } name ? $"Waiting for {name}… {left} s" : $"Waiting for the vote… {left} s";
    }

    private static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
