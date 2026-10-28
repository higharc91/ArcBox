using Redline.Logic;
using Redline.Presentation.Hud;

namespace Redline.Presentation.Audio;

public enum CommentaryKind { Intro, LightsOut, RoundIntro, Answer, Event, Finish, Podium }

/// <summary>One spoken line. Lines in a list are in speaking order; a higher <paramref name="Priority"/> matters more.</summary>
public sealed record CommentaryLine(string Text, CommentaryKind Kind, int Priority);

/// <summary>
/// Audio spec §3.2: the race commentator. Turns a host-view transition into at most a couple of short lines, spoken on
/// phase entry only. Players first: an AI is only called when no player did anything worth a line.
/// </summary>
public static class Commentary
{
    /// <summary>Scripted lines: the intro, lights out, the round, the answer, the finish and the podium.</summary>
    public const int Scripted = 2;

    /// <summary>A player's spin, lead, charge or sight of the flag.</summary>
    public const int PlayerEvent = 1;

    /// <summary>An AI lead change: said only when no player event happened.</summary>
    public const int AiEvent = 0;

    /// <summary>Places a player must gain in one round to be called.</summary>
    public const int StormPlaces = 3;

    /// <summary>Distance to the finish line at which a player "can see the flag".</summary>
    public const int FlagDistance = 4;

    public static IReadOnlyList<CommentaryLine> Lines(RedlineHostView view, RedlineHostView? previous, Grid grid)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(grid);
        if (previous is not null && previous.Phase == view.Phase && previous.Round == view.Round) return [];

        // Only the phases with lines; Driver Select, Question and anything newer (Reading) are silent.
        switch (view.Phase)
        {
            case RacePhase.Grid:
                return [Intro(view)];
            case RacePhase.Throttle:
                return view.Round == 1
                    ? [new("And it's lights out!", CommentaryKind.LightsOut, Scripted), RoundIntro(view)]
                    : [RoundIntro(view)];
            case RacePhase.Reveal:
                return Reveal(view, grid);
            case RacePhase.Chequered:
                return Finish(view, grid);
            case RacePhase.Anthem:
                return Anthem(view, grid);
            case RacePhase.Champagne:
                return [new("Let the champagne fly!", CommentaryKind.Podium, Scripted)];
            default:
                return [];
        }
    }

    /// <summary>Podium spec §5: the anthem's country, for the player on the top step and their team.</summary>
    private static IReadOnlyList<CommentaryLine> Anthem(RedlineHostView view, Grid grid)
    {
        if (view.Podium is not { Steps: [var top, ..] } podium) return [];
        var name = top.Name is { Length: > 0 } typed ? SpokenName(typed) : top.Code;
        var team = Seat(grid, top.Code)?.Team.Name ?? "car";
        return [new($"The national anthem of {CountryPhrase(podium.AnthemCountry)}, for {name} in the {team}!", CommentaryKind.Podium, Scripted)];
    }

    /// <summary>A country as it is said mid-sentence: "the Netherlands", otherwise the name as shown.</summary>
    private static string CountryPhrase(string country) => country == "Netherlands" ? "the Netherlands" : country;

    private static CommentaryLine Intro(RedlineHostView view)
    {
        var players = view.Drivers.Count(d => d.Player is not null);
        return new($"Welcome to the {HudText.CircuitName(view.CircuitId)}. {Capitalise(Words(view.Drivers.Count))} cars, " +
                   $"{players} {(players == 1 ? "player" : "players")}. Lights out in a moment.",
            CommentaryKind.Intro, Scripted);
    }

    private static CommentaryLine RoundIntro(RedlineHostView view) => new(
        view.Question is { } q
            ? $"Round {view.Round}. {q.Category}, {q.Difficulty.ToString().ToLowerInvariant()}."
            : $"Round {view.Round}.",
        CommentaryKind.RoundIntro, Scripted);

    private static IReadOnlyList<CommentaryLine> Reveal(RedlineHostView view, Grid grid)
    {
        var lines = new List<CommentaryLine>(2);
        if (view is { CorrectIndex: { } i, Question.Answers: { } answers } && i >= 0 && i < answers.Count)
            lines.Add(new($"The answer is {answers[i]}.", CommentaryKind.Answer, Scripted));
        if ((PlayerEventLine(view, grid) ?? AiLeadLine(view, grid)) is { } line)
            lines.Add(line);
        return lines;
    }

    /// <summary>The single most dramatic player event: spin-out, then taking the lead, then a charge, then the flag.</summary>
    private static CommentaryLine? PlayerEventLine(RedlineHostView view, Grid grid)
    {
        var players = view.Drivers.Where(d => d.Player is not null).ToList();

        // The best-placed spin, as the camera's trackside shot shows it.
        if (players.Where(d => d.Outcome == RoundOutcome.SpunOut).MinBy(d => d.Position) is { } spun)
            return Event($"{Name(spun)} spins the {Seat(grid, spun.Code)?.Team.Name ?? "car"}!");

        if (NewLeader(view) is { Player: not null } leader)
            return Event($"{Name(leader)} takes the lead!");

        if (players.Where(d => d.PositionChange >= StormPlaces).MinBy(d => (-d.PositionChange, d.Position)) is { } charger)
            return Event($"{Name(charger)} storms up {charger.PositionChange} places!");

        // Newly inside the last few: said once per player, not every round from then on.
        if (players.Where(d => Remaining(view, d.Distance) is > 0 and <= FlagDistance && Remaining(view, d.PreviousDistance) > FlagDistance)
                .MinBy(d => d.Position) is { } close)
            return Event($"{Name(close)} can see the flag!");

        return null;

        static CommentaryLine Event(string text) => new(text, CommentaryKind.Event, PlayerEvent);
    }

    private static CommentaryLine? AiLeadLine(RedlineHostView view, Grid grid) =>
        NewLeader(view) is { Player: null } leader
            ? new($"{Surname(grid, leader.Code)} leads the race.", CommentaryKind.Event, AiEvent)
            : null;

    /// <summary>The sole P1 when they were not leading at the start of the round; null on a shared lead.</summary>
    private static DriverRow? NewLeader(RedlineHostView view)
    {
        var leaders = view.Drivers.Where(d => d.Position == 1).Take(2).ToList();
        return leaders is [{ PositionChange: > 0 } leader] ? leader : null;
    }

    private static IReadOnlyList<CommentaryLine> Finish(RedlineHostView view, Grid grid)
    {
        if (view.Drivers.Count == 0) return [];
        var winner = view.Drivers[0];
        if (winner.Player is not null)
            return [new($"Chequered flag! {Name(winner)} wins at the {HudText.CircuitName(view.CircuitId)}!", CommentaryKind.Finish, Scripted)];

        var text = $"{Surname(grid, winner.Code)} takes the win.";
        if (view.Drivers.Where(d => d.Player is not null).MinBy(d => d.Position) is { } best)
            text += $" Best of the players, {Name(best)} in P{best.Position}.";
        return [new(text, CommentaryKind.Finish, Scripted)];
    }

    private static int Remaining(RedlineHostView view, int distance) => view.FinishLine - distance;

    /// <summary>The player's name as typed (spec §4), or the driver code if the view has none.</summary>
    private static string Name(DriverRow d) => d.PlayerName is { Length: > 0 } name ? SpokenName(name) : d.Code;

    /// <summary>
    /// How a typed name is spoken: an all-caps word of two or more letters ("SAM") becomes "Sam", because a voice
    /// tends to spell capitals out letter by letter. Any other name is read exactly as typed.
    /// </summary>
    public static string SpokenName(string typed) =>
        typed.Length >= 2 && typed.All(char.IsLetter) && typed.All(char.IsUpper)
            ? typed[0] + typed[1..].ToLowerInvariant()
            : typed;

    private static Seat? Seat(Grid grid, string code) => grid.Seats.FirstOrDefault(s => s.Driver.Code == code);

    /// <summary>The last word of the driver's name ("Hülkenberg"); the code for an unknown driver.</summary>
    private static string Surname(Grid grid, string code) =>
        Seat(grid, code)?.Driver.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries) is [.., var last] ? last : code;

    private static readonly string[] Units =
        ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve",
         "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen"];

    private static readonly string[] Tens = ["", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"];

    /// <summary>"twenty-two" for 22; digits outside 0–99.</summary>
    internal static string Words(int n) => n switch
    {
        < 0 or > 99 => n.ToString(System.Globalization.CultureInfo.InvariantCulture),
        < 20 => Units[n],
        _ when n % 10 == 0 => Tens[n / 10],
        _ => $"{Tens[n / 10]}-{Units[n % 10]}",
    };

    private static string Capitalise(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
