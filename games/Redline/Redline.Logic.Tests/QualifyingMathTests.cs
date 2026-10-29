namespace Redline.Logic.Tests;

public class QualifyingMathTests
{
    // ---- Lap time clamp (spec §2.1) ----

    [Fact] public void LapTime_uses_phone_time_inside_the_window() =>
        Assert.Equal(2410, Qualifying.LapTimeMs(2410, 2600));

    [Fact] public void LapTime_clamps_to_server_time() =>
        Assert.Equal(2600, Qualifying.LapTimeMs(3000, 2600));

    [Fact] public void LapTime_lower_bound_is_server_minus_one_second() =>
        Assert.Equal(1600, Qualifying.LapTimeMs(500, 2600));

    [Fact] public void LapTime_lower_bound_never_below_200ms()
    {
        Assert.Equal(200, Qualifying.LapTimeMs(50, 900));       // s - 1000 < 200
        Assert.Equal(200, Qualifying.LapTimeMs(50, 1100));      // s - 1000 = 100 < 200
    }

    [Fact] public void LapTime_without_phone_time_is_server_time() =>
        Assert.Equal(2600, Qualifying.LapTimeMs(null, 2600));

    [Fact] public void LapTime_handles_non_finite_and_negative()
    {
        Assert.Equal(2600, Qualifying.LapTimeMs(double.NaN, 2600));
        Assert.Equal(2600, Qualifying.LapTimeMs(double.PositiveInfinity, 2600));
        Assert.Equal(2600, Qualifying.LapTimeMs(double.NegativeInfinity, 2600));
        Assert.Equal(1600, Qualifying.LapTimeMs(-50, 2600));
    }

    [Fact] public void LapTime_rounds_to_whole_milliseconds() =>
        Assert.Equal(2411, Qualifying.LapTimeMs(2410.6, 2600));

    [Fact] public void LapTime_with_a_server_time_below_the_floor_never_throws() =>
        Assert.Equal(100, Qualifying.LapTimeMs(50, 100));

    // ---- AI laps (spec §2.3) ----

    private static readonly RedlineRules AlwaysRight = new() { Ai = new AiRules { MinP = 1, MaxP = 1 } };

    private static double MedianMs(double skill, int seed)
    {
        var rnd = new Random(seed);
        var times = Enumerable.Range(0, 4000)
            .Select(_ => Qualifying.AiLap(skill, 2, AlwaysRight, rnd))
            .Select(l => { Assert.True(l.Correct); return l.Ms; })
            .Where(ms => ms.HasValue).Select(ms => (double)ms!.Value)
            .Order().ToList();
        return times[times.Count / 2];
    }

    [Fact] public void AiLap_medians_follow_skill()
    {
        Assert.InRange(MedianMs(0.85, 7), 3600 * 0.95, 3600 * 1.05);
        Assert.InRange(MedianMs(1.15, 7), 1900 * 0.95, 1900 * 1.05);
    }

    [Fact] public void AiLap_median_is_linear_in_skill_and_clamped_at_the_ends()
    {
        Assert.InRange(MedianMs(1.0, 7), 2750 * 0.95, 2750 * 1.05);      // halfway between 3.6 s and 1.9 s
        Assert.InRange(MedianMs(0.5, 7), 3600 * 0.95, 3600 * 1.05);      // clamped at the slow end
        Assert.InRange(MedianMs(2.0, 7), 1900 * 0.95, 1900 * 1.05);      // clamped at the fast end
    }

    [Fact] public void AiLap_times_stay_inside_the_floor_and_the_window()
    {
        var rules = new RedlineRules { QualifyingAnswer = TimeSpan.FromSeconds(4), Ai = new AiRules { MinP = 1, MaxP = 1 } };
        var rnd = new Random(11);
        var laps = Enumerable.Range(0, 4000).Select(_ => Qualifying.AiLap(0.85, 0, rules, rnd)).ToList();
        Assert.All(laps.Where(l => l.Ms.HasValue), l => Assert.InRange(l.Ms!.Value, 600, 3999));
        Assert.Contains(laps, l => l.Ms is null);      // slow cars reach the window's end: counted as no answer
    }

    [Fact] public void AiLap_accuracy_matches_AiPolicy_for_easy()
    {
        var rnd = new Random(7);
        var rules = new RedlineRules();
        var right = Enumerable.Range(0, 4000).Count(_ => Qualifying.AiLap(1.0, 1, rules, rnd).Correct);
        Assert.InRange(right / 4000.0, 0.72 - 0.03, 0.72 + 0.03);
    }

    [Fact] public void AiLap_is_deterministic_for_a_seed()
    {
        var rules = new RedlineRules();
        var a = new Random(42); var b = new Random(42);
        Assert.Equal(
            Enumerable.Range(0, 200).Select(i => Qualifying.AiLap(0.85 + i % 4 * 0.1, i % 4, rules, a)),
            Enumerable.Range(0, 200).Select(i => Qualifying.AiLap(0.85 + i % 4 * 0.1, i % 4, rules, b)));
    }

    [Fact] public void AiLap_consumes_a_fixed_number_of_draws()
    {   // Correctness first, then the time: the stream position after a lap must not depend on the outcome.
        var rules = new RedlineRules();
        var a = new Random(5); var b = new Random(5);
        for (var i = 0; i < 50; i++) Qualifying.AiLap(1.0, 0, rules, a);
        for (var i = 0; i < 50 * 4; i++) b.NextDouble();
        Assert.Equal(b.NextDouble(), a.NextDouble());
    }

    // ---- Classification (spec §2.2) ----

    private static readonly GridTeam Team = new("t", "Team", "c", "f",
        new Livery("#fff", "#000", "#f00", "#fff", "#fff", "#fff", "#000", "#000"), []);

    private static Driver Car(string code, int correct, int totalMs, int tieOrder, int?[]? laps = null)
    {
        var d = new Driver(new Seat(Team, new GridDriver(code, code, code, 1, "x", 0, 1.0, "GB")))
        {
            QualiCorrect = correct,
            QualiTotalMs = totalMs,
            QualiTieOrder = tieOrder,
        };
        d.QualiLaps.AddRange(laps ?? []);
        return d;
    }

    [Fact] public void Classify_orders_by_correct_then_total_then_tie_order()
    {
        var field = new[]
        {
            Car("AAA", 3, 7000, 0), Car("BBB", 3, 6500, 1), Car("CCC", 2, 3000, 2),
            Car("DDD", 0, 0, 5), Car("EEE", 0, 0, 2),
        };
        Assert.Equal(["BBB", "AAA", "CCC", "EEE", "DDD"], Qualifying.Classify(field).Select(d => d.Code));
    }

    [Fact] public void All_deleted_laps_fall_back_to_the_seeded_order()
    {
        var field = new[]
        {
            Car("AAA", 0, 0, 3, [null, null, null]), Car("BBB", 0, 0, 0, [null, null, null]), Car("CCC", 0, 0, 2, [null, null, null]),
        };
        Assert.Equal(["BBB", "CCC", "AAA"], Qualifying.Classify(field).Select(d => d.Code));
    }

    // ---- Fastest lap ----

    [Fact] public void FastestLap_ignores_deleted_laps_and_breaks_ties_by_classification()
    {
        // Latest laps: AAA 2000, BBB deleted, CCC 1500, DDD 1500. Earlier laps never count.
        var field = new[]
        {
            Car("AAA", 2, 4000, 0, [100, 2000]),
            Car("BBB", 1, 900, 1, [900, null]),
            Car("CCC", 2, 3500, 2, [1800, 1500]),
            Car("DDD", 1, 2000, 3, [1000, 1500]),
        };
        var classified = Qualifying.Classify(field);
        Assert.Equal("CCC", Qualifying.FastestLap(classified));      // CCC is classified ahead of DDD
        Assert.Equal("DDD", Qualifying.FastestLap(classified.Reverse()));
    }

    [Fact] public void FastestLap_is_null_when_nobody_set_a_time()
    {
        Assert.Null(Qualifying.FastestLap([Car("AAA", 0, 0, 0, [null]), Car("BBB", 0, 0, 1)]));
        Assert.Null(Qualifying.FastestLap([]));
    }
}
