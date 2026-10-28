using ArcBox.Sdk;

namespace Redline.Logic;

// Records and enums copied from spec §4.4. `Throttle` lives in RedlineRules.cs (Task 4) and is reused here.

public sealed record RedlineHostView(
    RacePhase Phase, int Round, int MaxRounds, int FinishLine, string CircuitId,
    QuestionCard? Question,             // null in Grid; Throttle: Text and Answers are null; set from Reading on
    int? CorrectIndex,                  // Reveal only
    IReadOnlyList<DriverRow> Drivers,   // all 22; sorted by ranking (§2.3), or by team order during Driver Select
    DateTimeOffset? DeadlineUtc,
    FinishSummary? Finish,              // Chequered only
    string Attribution,                 // "Questions: Open Trivia DB (CC BY-SA 4.0)" or the fallback credit
    Payouts Payouts,                    // the throttle payouts from RedlineRules, for the TV rules strip
    Podium? Podium = null);             // Anthem and Champagne only (podium spec §2)

/// <summary>Podium spec §2: the ceremony after the Chequered flag, for the top three players.</summary>
/// <param name="AnthemCountry">The best player's driver's nationality as a display name ("Great Britain").</param>
/// <param name="AnthemId">The anthem's key ("gb"), or null when only the REDLINE theme plays.</param>
/// <param name="Steps">One per podium player, step 1 first.</param>
public sealed record Podium(string AnthemCountry, string? AnthemId, IReadOnlyList<PodiumStep> Steps);

/// <param name="Step">1..3, in race order of the players (ties keep the classification order).</param>
/// <param name="RacePosition">The car's classified position among all 22 cars.</param>
/// <param name="Charge">The bottle's pressure, 0..1, quantised to 0.05.</param>
/// <param name="Sprays">Every spray so far, <see cref="Spray.Seq"/> increasing.</param>
public sealed record PodiumStep(int Step, PlayerId Player, string Name, string Code, string TeamId,
    int RacePosition, double Charge, IReadOnlyList<Spray> Sprays);

/// <param name="Seq">1, 2, 3… across the whole podium, so the TV can tell a new spray from one it has shown.</param>
/// <param name="Strength">0.15..1: the charge when the cork popped, at least <see cref="Celebration.MinSprayStrength"/>.</param>
public sealed record Spray(int Seq, double Strength);

/// <summary>
/// The throttle payouts (§2.2) as the TV shows them: gains when right, losses (positive numbers) when wrong.
/// Built from <see cref="RedlineRules"/> so the rules strip never hard-codes balance.
/// </summary>
public sealed record Payouts(int CruiseGain, int PushGain, int PushLoss, int RedlineGain, int RedlineLoss)
{
    public static Payouts From(RedlineRules rules) =>
        new(rules.CruiseGain, rules.PushGain, rules.PushLoss, rules.RedlineGain, rules.RedlineLoss);
}

public sealed record QuestionCard(string Category, Difficulty Difficulty, string? Text, IReadOnlyList<string>? Answers);

public sealed record DriverRow(
    string Code, int Number, string TeamId,  // e.g. "VER", 3, "red-bull"; the TV looks up the livery
    PlayerId? Player, string? PlayerName,    // null for AI
    int Position, int PositionChange,
    int Distance, int PreviousDistance, DriverStatus Status,
    Throttle? Throttle,                 // null during Grid and Throttle; public from Reading on
    int? AnswerIndex,                   // null until Reveal
    RoundOutcome? Outcome,              // Reveal only
    bool PitLimiter, bool Connected);

public enum DriverStatus { Unclaimed, Seated, Ready, Choosing, Locked, Limited, Answering, Answered }
public enum RacePhase { DriverSelect, Grid, Throttle, Reading, Question, Reveal, Chequered, Anthem, Champagne }
public enum RoundOutcome { Correct, Wrong, NoAnswer, SpunOut }

public sealed record FinishSummary(IReadOnlyList<Award> Awards);
public sealed record Award(string Title, PlayerId Winner, string Name, string Detail);  // e.g. "Boldest driver", "6 Redlines"

/// <summary>
/// Builds the TV view. Secrecy lives here, so a hidden value can never leak through a caller:
/// throttles appear from the Reading phase on, answers and outcomes only in Reveal.
/// </summary>
internal static class HostViews
{
    public const string OpenTdbCredit = "Questions: Open Trivia DB (CC BY-SA 4.0)";
    public const string FallbackCredit = "Questions: REDLINE fallback pack";

    /// <summary>§4.6: the OpenTDB credit when any question came from OpenTDB, else the fallback-pack credit.</summary>
    public static string Attribution(IEnumerable<TriviaQuestion> questions) =>
        questions.Any(q => q.Origin == QuestionOrigin.OpenTdb) ? OpenTdbCredit : FallbackCredit;

    /// <param name="standings">Every car in display order (team order in Driver Select, ranking afterwards).</param>
    public static RedlineHostView Build(
        RacePhase phase, int round, RedlineRules rules, QuestionCard? question, int? correctIndex,
        IEnumerable<Driver> standings, DateTimeOffset? deadline, FinishSummary? finish, string attribution, Podium? podium = null) =>
        new(phase, round, rules.MaxRounds, rules.FinishLine, rules.CircuitId,
            question, phase == RacePhase.Reveal ? correctIndex : null,
            [.. standings.Select(d => Row(d, phase))],
            deadline, phase == RacePhase.Chequered ? finish : null, attribution, Payouts.From(rules),
            phase is RacePhase.Anthem or RacePhase.Champagne ? podium : null);

    public static DriverRow Row(Driver d, RacePhase phase) =>
        new(d.Code, d.Seat.Driver.Number, d.Seat.Team.Id,
            d.PlayerId, d.PlayerName,
            d.Position, phase == RacePhase.Reveal ? d.PositionAtRoundStart - d.Position : 0,
            d.Distance, d.PreviousDistance, Status(d, phase),
            phase is RacePhase.Reading or RacePhase.Question or RacePhase.Reveal ? d.Throttle : null,
            phase == RacePhase.Reveal ? d.AnswerIndex : null,
            phase == RacePhase.Reveal ? d.Outcome : null,
            d.PitLimiter, d.Connected);

    public static DriverStatus Status(Driver d, RacePhase phase) => phase switch
    {
        RacePhase.DriverSelect => d.IsHuman ? DriverStatus.Seated : DriverStatus.Unclaimed,
        RacePhase.Throttle when d.PitLimiter => DriverStatus.Limited,
        RacePhase.Throttle => d.Throttle is null ? DriverStatus.Choosing : DriverStatus.Locked,
        RacePhase.Question => d.AnswerIndex is null ? DriverStatus.Answering : DriverStatus.Answered,
        _ => DriverStatus.Ready,
    };
}
