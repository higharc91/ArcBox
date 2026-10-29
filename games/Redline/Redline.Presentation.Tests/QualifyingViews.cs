using ArcBox.Sdk;
using Redline.Logic;

namespace Redline.Presentation.Tests;

/// <summary>Small qualifying host views for the presentation tests: cars in classification order, real 2026 driver codes.</summary>
internal static class QualifyingViews
{
    private static readonly Grid Grid = Grid2026.Load();

    internal static readonly QuestionCard Card = new("Geography", Difficulty.Easy,
        "Which U.S. state has the longest coastline?", ["Florida", "California", "Alaska", "Hawaii"]);

    /// <summary>One car in the classification; <paramref name="Player"/> makes it a human seat (the typed name).</summary>
    internal sealed record Car(string Code, string? Player = null, int Correct = 0, int TotalMs = 0, int? LastMs = null,
        bool Fastest = false, bool Answered = false, int Change = 0);

    /// <summary>The view for <paramref name="phase"/> of lap <paramref name="lap"/>; positions follow the order of <paramref name="cars"/>.</summary>
    internal static RedlineHostView View(RacePhase phase, int lap, params Car[] cars)
    {
        var drivers = cars.Select((c, i) => new DriverRow(c.Code, 1, Grid.ByCode(c.Code).Team.Id,
            c.Player is null ? null : new PlayerId(c.Player.ToLowerInvariant()), c.Player,
            i + 1, 0, 0, 0, phase == RacePhase.QualifyingQuestion && c.Answered ? DriverStatus.Answered : DriverStatus.Ready,
            null, null, null, false, true)).ToList();
        var rows = cars.Select((c, i) => new QualifyingRow(c.Code, i + 1, phase == RacePhase.QualifyingResult ? c.Change : 0,
            c.Correct, c.TotalMs, phase == RacePhase.QualifyingResult ? c.LastMs : null,
            phase == RacePhase.QualifyingQuestion && c.Answered, phase == RacePhase.QualifyingResult && c.Fastest)).ToList();
        return new RedlineHostView(phase, 0, 15, 28, "cota", Card, phase == RacePhase.QualifyingResult ? 2 : null, drivers, null, null,
            "attribution", Payouts.From(new RedlineRules()), null, new QualifyingSession(lap, 3, rows));
    }
}
