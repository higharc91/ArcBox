using Redline.Logic;

namespace Redline.Presentation.Hud;

/// <summary>The header line above the question: meta items, difficulty pips and the big title.</summary>
/// <param name="Meta">Small upper-case items left to right ("ROUND 4/15", "GEOGRAPHY").</param>
/// <param name="Difficulty">"MEDIUM" with <paramref name="Pips"/> of 3 lit, or null.</param>
/// <param name="ClockCaption">"TIME", "NEXT" or "START" (Grid shows the start lights instead of a time).</param>
public sealed record HeaderText(IReadOnlyList<string> Meta, string? Difficulty, int Pips, string Title, string ClockCaption);

/// <summary>One throttle in the rules strip: the chip and its payout, e.g. PUSH "+3 / −1".</summary>
public sealed record RuleItem(Chip Chip, string Payout);

/// <summary>An awards card on the Chequered screen.</summary>
public sealed record AwardCard(string Title, string Name, string? Code, string Accent, string Detail);

/// <summary>A podium or classification entry.</summary>
public sealed record Finisher(int Position, string Code, string Surname, string? PlayerName, int Distance, string Accent);

/// <summary>Every piece of HUD wording that is derived from the view, kept out of Godot so it can be tested.</summary>
public static class HudText
{
    public static HeaderText Header(RedlineHostView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var round = $"ROUND {Math.Max(1, view.Round)}/{view.MaxRounds}";
        var q = view.Question;
        var difficulty = q?.Difficulty.ToString().ToUpperInvariant();
        var pips = q is null ? 0 : (int)q.Difficulty + 1;
        return view.Phase switch
        {
            RacePhase.DriverSelect => new HeaderText(
                [$"{CircuitShort(view.CircuitId)} · {view.MaxRounds} ROUNDS · {view.Drivers.Count}-CAR GRID"], null, 0, "Driver select", "TIME"),
            RacePhase.Grid => new HeaderText(
                [CircuitName(view.CircuitId).ToUpperInvariant(), $"{view.MaxRounds} ROUNDS · RACE TO THE FLAG"], null, 0,
                "Lights out in a moment…", "START"),
            RacePhase.Throttle => new HeaderText(Meta(round, q), difficulty, pips, "Choose your throttle", "TIME"),
            RacePhase.Question => new HeaderText(Meta(round, q), difficulty, pips, q?.Text ?? "Choose your answer", "TIME"),
            RacePhase.Reveal => new HeaderText(Meta(round, q), difficulty, pips, q?.Text ?? "The answer is…", "NEXT"),
            _ => new HeaderText([$"CHEQUERED FLAG · ROUND {view.Round}/{view.MaxRounds}"], null, 0, Headline(view), ""),
        };
    }

    private static IReadOnlyList<string> Meta(string round, QuestionCard? q) =>
        q is null ? [round] : [round, q.Category.ToUpperInvariant()];

    /// <summary>"Circuit of the Americas" for "cota"; the id itself for an unknown circuit.</summary>
    public static string CircuitName(string circuitId)
    {
        try
        {
            return CircuitData.Load(circuitId).Name;
        }
        catch (KeyNotFoundException)
        {
            return circuitId.ToUpperInvariant();
        }
    }

    public static string CircuitShort(string circuitId) => circuitId.ToUpperInvariant();

    /// <summary>The Grid rules strip: each throttle's payout from the view's <see cref="Payouts"/> (never hard-coded).</summary>
    public static IReadOnlyList<RuleItem> Rules(Payouts p)
    {
        ArgumentNullException.ThrowIfNull(p);
        return
        [
            new(Tower.ThrottleChip(Throttle.Cruise), $"{Tower.Signed(p.CruiseGain)} / 0"),
            new(Tower.ThrottleChip(Throttle.Push), $"{Tower.Signed(p.PushGain)} / {Tower.Signed(-p.PushLoss)}"),
            new(Tower.ThrottleChip(Throttle.Redline), $"{Tower.Signed(p.RedlineGain)} / {Tower.Signed(-p.RedlineLoss)} + pit limiter"),
        ];
    }

    /// <summary>The short payouts for the Throttle strip: "CRUISE +1 / 0", "PUSH +3 / −1", "REDLINE +4 / SPIN".</summary>
    public static IReadOnlyList<Chip> ThrottleLegend(Payouts p)
    {
        ArgumentNullException.ThrowIfNull(p);
        return
        [
            new($"CRUISE {Tower.Signed(p.CruiseGain)} / 0", ChipKind.Cruise),
            new($"PUSH {Tower.Signed(p.PushGain)} / {Tower.Signed(-p.PushLoss)}", ChipKind.Push),
            new($"REDLINE {Tower.Signed(p.RedlineGain)} / SPIN", ChipKind.Redline),
        ];
    }

    /// <summary>"First to 28 or most distance after 15 rounds".</summary>
    public static string RaceLine(RedlineHostView view) =>
        $"First to {view.FinishLine} or most distance after {view.MaxRounds} rounds";

    /// <summary>Throttle phase: "4 OF 6 LOCKED · QUESTION DROPS WHEN ALL DRIVERS ARE IN" (humans; AI are always in).</summary>
    public static string LockedLine(RedlineHostView view)
    {
        var humans = view.Drivers.Where(d => d.Player is not null).ToList();
        if (humans.Count == 0) return "THE AI FIELD IS LOCKED IN";
        var decided = humans.Count(d => d.Status is DriverStatus.Locked or DriverStatus.Limited);
        return decided == humans.Count
            ? "ALL DRIVERS LOCKED · HERE COMES THE QUESTION"
            : $"{decided} OF {humans.Count} LOCKED · QUESTION DROPS WHEN ALL DRIVERS ARE IN";
    }

    /// <summary>"ALEX WINS" for a human winner, "VERSTAPPEN WINS" for an AI, "DEAD HEAT" when several share P1.</summary>
    public static string Headline(RedlineHostView view)
    {
        var winners = view.Drivers.Where(d => d.Position == 1).ToList();
        if (winners.Count == 0) return "CHEQUERED FLAG";
        if (winners.Count > 1) return "DEAD HEAT";
        var w = winners[0];
        return $"{(w.PlayerName is { Length: > 0 } name ? name.ToUpperInvariant() : Surname(w.Code).ToUpperInvariant())} WINS";
    }

    /// <summary>"Charles Leclerc · Ferrari · 29" under the headline.</summary>
    public static string WinnerLine(RedlineHostView view)
    {
        var w = view.Drivers.FirstOrDefault();
        if (w is null) return "";
        var seat = Seat(w.Code);
        var name = seat?.Driver.Name ?? w.Code;
        var team = seat?.Team.Name ?? w.TeamId;
        return $"{name} · {team} · {w.Distance}";
    }

    /// <summary>The top <paramref name="count"/> of the classification (view order).</summary>
    public static IReadOnlyList<Finisher> Classification(RedlineHostView view, int count = int.MaxValue) =>
        [.. view.Drivers.Take(count).Select(d => new Finisher(d.Position, d.Code, Surname(d.Code), d.PlayerName?.ToUpperInvariant(),
            d.Distance, TeamAccent.ById(d.TeamId)))];

    /// <summary>The Chequered awards, upper-cased, with the winner's car code and team colour from the view.</summary>
    public static IReadOnlyList<AwardCard> Awards(RedlineHostView view)
    {
        if (view.Finish is not { } finish) return [];
        return [.. finish.Awards.Select(a =>
        {
            var row = view.Drivers.FirstOrDefault(d => d.Player == a.Winner);
            return new AwardCard(a.Title.ToUpperInvariant(), a.Name.ToUpperInvariant(), row?.Code,
                TeamAccent.ById(row?.TeamId), a.Detail);
        })];
    }

    /// <summary>The last word of the driver's full name ("Hülkenberg"); the code for an unknown driver.</summary>
    public static string Surname(string code) =>
        Seat(code)?.Driver.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries) is [.., var last] ? last : code;

    public static Seat? Seat(string code) =>
        Grid2026.Load().Seats.FirstOrDefault(s => s.Driver.Code == code);
}
