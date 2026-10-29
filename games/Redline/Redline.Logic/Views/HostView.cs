using ArcBox.Sdk;

namespace Redline.Logic;

// Records and enums copied from spec §4.4. `Throttle` lives in RedlineRules.cs (Task 4) and is reused here.

public sealed record RedlineHostView(
    RacePhase Phase, int Round, int MaxRounds, int FinishLine, string CircuitId,
    QuestionCard? Question,             // null in Grid; Throttle: Text and Answers are null; set from Reading on and in qualifying (no Answers in QualifyingReading)
    int? CorrectIndex,                  // Reveal and QualifyingResult only
    IReadOnlyList<DriverRow> Drivers,   // all 22; by ranking (§2.3), team order in Driver Select, qualifying classification in qualifying
    DateTimeOffset? DeadlineUtc,
    FinishSummary? Finish,              // Chequered only
    string Attribution,                 // "Questions: Open Trivia DB (CC BY-SA 4.0)" or the fallback credit
    Payouts Payouts,                    // the throttle payouts from RedlineRules, for the TV rules strip
    Podium? Podium = null,              // Anthem and Champagne only (podium spec §2)
    QualifyingSession? Qualifying = null);   // the three Qualifying phases only (qualifying spec §5)

/// <summary>
/// Qualifying spec §5: the session's timing board. Spec §5 names this record <c>Qualifying</c>; that name is the
/// <see cref="Redline.Logic.Qualifying"/> maths class, so the type is <c>QualifyingSession</c> while the host-view property (and
/// its JSON, <c>qualifying</c>) keeps the spec's name.
/// </summary>
/// <param name="Lap">The current lap, 1..<paramref name="Laps"/>.</param>
/// <param name="Rows">All 22 cars in the current classification (the seeded order before lap 1's result).</param>
public sealed record QualifyingSession(int Lap, int Laps, IReadOnlyList<QualifyingRow> Rows);

/// <param name="Position">The classification position, 1..22; never shared.</param>
/// <param name="PositionChange">QualifyingResult only: the previous classification position minus this one; 0 otherwise, and on lap 1 (no previous classification).</param>
/// <param name="Correct">Correct laps among the completed ones.</param>
/// <param name="TotalMs">The sum of the completed correct laps, in milliseconds.</param>
/// <param name="LastLapMs">QualifyingResult only: this lap's time; null when deleted (wrong or no answer).</param>
/// <param name="Answered">QualifyingQuestion only: a live tick, true once the car has answered (AI from the start), no time.</param>
/// <param name="FastestLap">QualifyingResult only: this lap's fastest correct time (purple).</param>
public sealed record QualifyingRow(string Code, int Position, int PositionChange,
    int Correct, int TotalMs,
    int? LastLapMs,
    bool Answered,
    bool FastestLap);

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
public enum RacePhase { DriverSelect, QualifyingReading, QualifyingQuestion, QualifyingResult, Grid, Throttle, Reading, Question, Reveal, Chequered, Anthem, Champagne }
public enum RoundOutcome { Correct, Wrong, NoAnswer, SpunOut }

public sealed record FinishSummary(IReadOnlyList<Award> Awards);
public sealed record Award(string Title, PlayerId Winner, string Name, string Detail);  // e.g. "Boldest driver", "6 Redlines"

/// <summary>
/// Builds the TV view. Secrecy lives here, so a hidden value can never leak through a caller:
/// throttles appear from the Reading phase on, answers and outcomes only in Reveal; in qualifying, lap times,
/// correctness and the correct answer only in QualifyingResult.
/// </summary>
internal static class HostViews
{
    public const string OpenTdbCredit = "Questions: Open Trivia DB (CC BY-SA 4.0)";
    public const string FallbackCredit = "Questions: REDLINE fallback pack";

    /// <summary>§4.6: the OpenTDB credit when any question came from OpenTDB, else the fallback-pack credit.</summary>
    public static string Attribution(IEnumerable<TriviaQuestion> questions) =>
        questions.Any(q => q.Origin == QuestionOrigin.OpenTdb) ? OpenTdbCredit : FallbackCredit;

    /// <summary>The three qualifying phases (qualifying spec §2).</summary>
    public static bool IsQualifying(RacePhase phase) =>
        phase is RacePhase.QualifyingReading or RacePhase.QualifyingQuestion or RacePhase.QualifyingResult;

    /// <param name="standings">
    /// Every car in display order (team order in Driver Select, the qualifying classification in qualifying, ranking afterwards).
    /// </param>
    /// <param name="qualifying">The current lap and its fastest car's code; read in the qualifying phases only.</param>
    public static RedlineHostView Build(
        RacePhase phase, int round, RedlineRules rules, QuestionCard? question, int? correctIndex,
        IEnumerable<Driver> standings, DateTimeOffset? deadline, FinishSummary? finish, string attribution, Podium? podium = null,
        (int Lap, string? FastestLap)? qualifying = null)
    {
        var cars = standings.ToList();
        return new(phase, round, rules.MaxRounds, rules.FinishLine, rules.CircuitId,
            question, phase is RacePhase.Reveal or RacePhase.QualifyingResult ? correctIndex : null,
            [.. cars.Select(d => Row(d, phase))],
            deadline, phase == RacePhase.Chequered ? finish : null, attribution, Payouts.From(rules),
            phase is RacePhase.Anthem or RacePhase.Champagne ? podium : null,
            IsQualifying(phase) && qualifying is var (lap, fastest)
                ? new QualifyingSession(lap, rules.QualifyingLaps, [.. cars.Select(d => QualifyingRow(d, phase, fastest))])
                : null);
    }

    /// <summary>
    /// Qualifying spec §5. <see cref="Driver.QualiCorrect"/>, <see cref="Driver.QualiTotalMs"/> and
    /// <see cref="Driver.QualiLaps"/> change only when a lap is committed at its Result, so they always describe completed
    /// laps; the current lap's pending answer and time are never read here.
    /// </summary>
    /// <param name="fastestLap">The code of this lap's fastest correct car, or null.</param>
    public static QualifyingRow QualifyingRow(Driver d, RacePhase phase, string? fastestLap)
    {
        var result = phase == RacePhase.QualifyingResult;
        return new(d.Code, d.Position, result ? d.PositionAtRoundStart - d.Position : 0,
            d.QualiCorrect, d.QualiTotalMs,
            result && d.QualiLaps is [.., var last] ? last : null,
            phase == RacePhase.QualifyingQuestion && QualiAnswered(d),
            result && d.Code == fastestLap);
    }

    /// <summary>
    /// QualifyingQuestion's tick: a player once their first answer is in. Every AI car counts as answered from the start, as
    /// in the race, so the tick never tells which AI ran out of time.
    /// </summary>
    public static bool QualiAnswered(Driver d) => !d.IsHuman || d.PendingAnswer is not null;

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
        RacePhase.QualifyingQuestion => QualiAnswered(d) ? DriverStatus.Answered : DriverStatus.Answering,
        _ => DriverStatus.Ready,
    };
}
