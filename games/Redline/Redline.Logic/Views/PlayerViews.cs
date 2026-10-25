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

    // ---------------------------------------------------------------- Rounds (§3.3)

    public const string PitLimiterMessage = "Pit limiter: you're on Cruise this round.";
    public const string AnsweredMessage = "Answer locked. Eyes on the TV!";

    /// <summary>Maps a throttle button id (cruise / push / redline) to its throttle; null for anything else.</summary>
    public static Throttle? ParseThrottle(string? id) => id switch
    {
        "cruise" => Throttle.Cruise,
        "push" => Throttle.Push,
        "redline" => Throttle.Redline,
        _ => null,
    };

    /// <summary>The wire id of answer option <paramref name="index"/> (0..3 → a..d) in the question's shuffled order.</summary>
    public static string AnswerId(int index) => ((char)('a' + index)).ToString();

    /// <summary>Maps an answer option id (a..d) to its index 0..3; null for anything else.</summary>
    public static int? ParseAnswer(string? id) => id switch
    {
        "a" => 0,
        "b" => 1,
        "c" => 2,
        "d" => 3,
        _ => null,
    };

    public static string RoundHeader(int position, int round, int maxRounds) => $"P{position} · ROUND {round}/{maxRounds}";

    /// <summary>"CRUISE", "PUSH" or "REDLINE".</summary>
    public static string Name(Throttle t) => t.ToString().ToUpperInvariant();

    public static ButtonsView ChooseThrottle(TriviaQuestion q, string header, DateTimeOffset? deadline) =>
        new($"{q.Category} · {q.Difficulty}. How hard do you push?",
        [
            new Option("cruise", "CRUISE", "+1 if right · nothing lost"),
            new Option("push", "PUSH", "+2 if right · −1 if wrong"),
            new Option("redline", "REDLINE", "+4 if right · spin out if wrong"),
        ]) { Header = header, DeadlineUtc = deadline };

    public static WaitView ThrottleLocked(Throttle t, string header, DateTimeOffset? deadline) =>
        new($"Locked in: {Name(t)}. Waiting for the other drivers…") { Header = header, DeadlineUtc = deadline };

    public static WaitView PitLimited(string header, DateTimeOffset? deadline) =>
        new(PitLimiterMessage) { Header = header, DeadlineUtc = deadline };

    /// <param name="answers">The four answers in shuffled order; option ids a–d follow it.</param>
    public static ChoiceView Answer(string text, IReadOnlyList<string> answers, Throttle t, string header, DateTimeOffset? deadline) =>
        new(text, [.. answers.Select((a, i) => new Option(AnswerId(i), a))]) { Header = $"{Name(t)} · {header}", DeadlineUtc = deadline };

    public static WaitView Answered(string header, DateTimeOffset? deadline) =>
        new(AnsweredMessage) { Header = header, DeadlineUtc = deadline };

    /// <summary>The §3.3 Reveal line for one driver after scoring (Outcome, Throttle, PreviousDistance, Distance, Position).</summary>
    public static WaitView Result(Driver d, RedlineRules rules, string header, DateTimeOffset? deadline) =>
        new(ResultMessage(d, rules)) { Header = header, DeadlineUtc = deadline };

    public static string ResultMessage(Driver d, RedlineRules rules)
    {
        var p = d.Position;
        if (d.Outcome == RoundOutcome.Correct) return $"Correct! +{d.Distance - d.PreviousDistance} · now P{p}";
        var miss = d.Outcome == RoundOutcome.NoAnswer ? "No answer" : "Wrong answer";
        return d.Throttle switch
        {
            Throttle.Redline => $"Spun out! −{rules.RedlineLoss} · pit limiter next round · now P{p}",
            Throttle.Push => $"{miss}. −{rules.PushLoss} · now P{p}",
            _ => $"{miss}. No change · P{p}",
        };
    }
}

/// <summary>
/// Everything a player's view depends on. The game rebuilds a view only when this key changes, so the
/// instance (and the ViewId the server mints for it) stays stable otherwise. Extend it as rounds add state.
/// </summary>
/// <remarks>
/// Driver Select uses <see cref="FreeSeatsVersion"/> and <see cref="Header"/>. The round phases use the player's own
/// <see cref="Position"/> (shown in the header), <see cref="Throttle"/> (locked in, or forced by the limiter),
/// <see cref="Answered"/>, <see cref="PitLimiter"/> and, in Reveal, <see cref="Outcome"/>; these are default elsewhere.
/// </remarks>
internal readonly record struct PlayerViewKey(
    RacePhase Phase, int Round, string? Code, int FreeSeatsVersion, string? Header,
    int Position = 0, Throttle? Throttle = null, bool Answered = false, bool PitLimiter = false, RoundOutcome? Outcome = null);
