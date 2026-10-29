using System.Text.Json;
using ArcBox.Sdk;
using Redline.Logic;
using Redline.Presentation.Demo;
using Redline.Presentation.Hud;

namespace Redline.Presentation.Tests;

public class DemoRaceTests
{
    private static readonly IReadOnlyList<DemoStep> Steps = DemoRace.Steps();

    private static RedlineHostView Phase(RacePhase phase) => Steps.Single(s => s.View.Phase == phase).View;

    private static List<RedlineHostView> All(RacePhase phase, IReadOnlyList<DemoStep>? steps = null) =>
        [.. (steps ?? Steps).Where(s => s.View.Phase == phase).Select(s => s.View)];

    [Fact]
    public void Script_runs_driver_select_to_the_podium_in_order()
    {
        var phases = Steps.Select(s => s.View.Phase).Distinct().ToList();

        Assert.Equal([RacePhase.DriverSelect, RacePhase.QualifyingReading, RacePhase.QualifyingQuestion, RacePhase.QualifyingResult,
            RacePhase.Grid, RacePhase.Throttle, RacePhase.Reading, RacePhase.Question, RacePhase.Reveal,
            RacePhase.Chequered, RacePhase.Anthem, RacePhase.Champagne], phases);
        // Each phase's steps sit together, so the podium phases each last as long as the rules say. Qualifying runs its
        // reading, question and result once per lap: three laps repeat the cycle twice more.
        Assert.Equal(phases.Count + 2 * 3, Steps.Select(s => s.View.Phase).Where((p, i) => i == 0 || Steps[i - 1].View.Phase != p).Count());
        Assert.Equal(16, Steps.Where(s => s.View.Phase == RacePhase.Anthem).Sum(s => s.Seconds), 6);
        Assert.Equal(16, Steps.Where(s => s.View.Phase == RacePhase.Champagne).Sum(s => s.Seconds), 6);
        Assert.All(Steps, s => Assert.Equal(22, s.View.Drivers.Count));
        Assert.All(Steps, s => Assert.True(s.Seconds > 0));
    }

    [Fact]
    public void Driver_select_claims_arrive_one_view_at_a_time()
    {
        var select = Steps.Where(s => s.View.Phase == RacePhase.DriverSelect).Select(s => s.View).ToList();
        var claimed = select.Select(v => v.Drivers.Count(d => d.Player is not null)).ToList();

        Assert.Equal(0, claimed[0]);
        Assert.Equal(DemoRace.Humans.Count, claimed[^1]);
        Assert.Equal(claimed.Order(), claimed);
        Assert.Contains(select[^1].Drivers, d => d.PlayerName is { Length: 12 });
    }

    [Fact]
    public void Reveal_follows_the_rules_and_has_a_human_spin_out_and_a_three_car_pack()
    {
        var rules = new RedlineRules();
        var reveal = Phase(RacePhase.Reveal);

        foreach (var d in reveal.Drivers)
        {
            var gain = d.Distance - d.PreviousDistance;
            var expected = (d.Throttle, d.Outcome) switch
            {
                (Throttle.Cruise, RoundOutcome.Correct) => rules.CruiseGain,
                (Throttle.Cruise, _) => 0,
                (Throttle.Push, RoundOutcome.Correct) => rules.PushGain,
                (Throttle.Push, _) => -rules.PushLoss,
                (Throttle.Redline, RoundOutcome.Correct) => rules.RedlineGain,
                _ => -rules.RedlineLoss,
            };
            Assert.Equal(expected, gain);
        }
        Assert.Contains(reveal.Drivers, d => d.Outcome == RoundOutcome.SpunOut && d.Player is not null);
        Assert.Equal(3, reveal.Drivers.Count(d => d.Distance == reveal.Drivers[0].Distance));
        // Ranking order and positions agree with distances.
        Assert.Equal(reveal.Drivers.Select(d => d.Distance).OrderDescending(), reveal.Drivers.Select(d => d.Distance));
        Assert.Equal(reveal.Drivers.Select(d => d.Position).Order(), reveal.Drivers.Select(d => d.Position));
    }

    [Fact]
    public void Question_matches_the_reveal_starting_grid()
    {
        var question = Phase(RacePhase.Question);
        var reveal = Phase(RacePhase.Reveal);

        foreach (var d in reveal.Drivers)
        {
            var before = question.Drivers.Single(q => q.Code == d.Code);
            Assert.Equal(before.Distance, d.PreviousDistance);
            Assert.Equal(before.Position, d.Position + d.PositionChange);
            Assert.Equal(before.Throttle, d.Throttle);
        }
        Assert.Null(Phase(RacePhase.Throttle).Question!.Text);
    }

    [Fact]
    public void Reading_shows_the_question_and_throttles_for_as_long_as_the_rules_read_it()
    {
        var reading = Steps.Single(s => s.View.Phase == RacePhase.Reading);
        var question = Phase(RacePhase.Question);

        Assert.Equal(question.Question, reading.View.Question);
        Assert.Equal(question.Drivers.Select(d => (d.Code, d.Throttle, d.Distance)), reading.View.Drivers.Select(d => (d.Code, d.Throttle, d.Distance)));
        Assert.All(reading.View.Drivers, d => Assert.Equal(DriverStatus.Ready, d.Status));
        Assert.InRange(reading.Seconds, new RedlineRules().ReadingTime(question.Question!.Text!).TotalSeconds, 10);
    }

    [Fact]
    public void Chequered_has_a_winner_over_the_line_and_awards()
    {
        var finish = Phase(RacePhase.Chequered);

        Assert.True(finish.Drivers[0].Distance >= finish.FinishLine);
        Assert.NotNull(finish.Finish);
        Assert.NotEmpty(finish.Finish.Awards);
    }

    [Fact]
    public void Podium_is_the_top_three_players_with_the_winners_anthem()
    {
        var chequered = Phase(RacePhase.Chequered);
        foreach (var view in All(RacePhase.Anthem).Concat(All(RacePhase.Champagne)))
        {
            var podium = view.Podium!;
            Assert.Equal(("Great Britain", "gb"), (podium.AnthemCountry, podium.AnthemId));
            var players = chequered.Drivers.Where(d => d.Player is not null).Take(3).ToList();
            Assert.Equal(players.Select(d => d.Code), podium.Steps.Select(s => s.Code));
            Assert.Equal([1, 2, 3], podium.Steps.Select(s => s.Step));
            Assert.Equal("JO", podium.Steps[0].Name);
            Assert.All(podium.Steps, s => Assert.Equal(Celebration.Quantise(s.Charge), s.Charge));
        }
        Assert.Equal("NOR", chequered.Drivers[0].Code);
    }

    [Fact]
    public void Anthem_fills_the_bottles_and_nobody_sprays_before_the_champagne()
    {
        var anthem = All(RacePhase.Anthem);
        Assert.All(anthem, v => Assert.All(v.Podium!.Steps, s => Assert.Empty(s.Sprays)));
        for (var step = 0; step < 3; step++)
        {
            var charges = anthem.Select(v => v.Podium!.Steps[step].Charge).ToList();
            Assert.Equal(charges.Order(), charges);
            Assert.True(charges[^1] > charges[0]);
        }
    }

    [Fact]
    public void Champagne_sprays_from_every_step_at_different_strengths_with_one_triple()
    {
        var views = All(RacePhase.Champagne);
        var last = views[^1].Podium!;
        var sprays = last.Steps.SelectMany(s => s.Sprays.Select(p => (s.Step, p.Seq, p.Strength))).OrderBy(x => x.Seq).ToList();

        Assert.Equal(Enumerable.Range(1, sprays.Count), sprays.Select(s => s.Seq));
        Assert.All(last.Steps, s => Assert.NotEmpty(s.Sprays));
        Assert.True(sprays.Select(s => s.Strength).Distinct().Count() >= 4);
        Assert.Contains(sprays, s => s.Strength == 1.0);
        Assert.Contains(sprays, s => s.Strength <= 0.3);
        Assert.All(sprays, s => Assert.InRange(s.Strength, Celebration.MinSprayStrength, 1));
        // Sprays only ever accumulate, and one view adds three at once.
        var counts = views.Select(v => v.Podium!.Steps.Sum(s => s.Sprays.Count)).ToList();
        Assert.Equal(counts.Order(), counts);
        Assert.Contains(counts.Zip(counts.Skip(1), (a, b) => b - a), d => d == 3);
        // Each spray's strength is the charge its step showed in the view before.
        for (var i = 1; i < views.Count; i++)
        {
            foreach (var step in views[i].Podium!.Steps)
            {
                var before = views[i - 1].Podium!.Steps.Single(s => s.Step == step.Step);
                foreach (var spray in step.Sprays.Skip(before.Sprays.Count))
                {
                    Assert.Equal(Celebration.Strength(before.Charge), spray.Strength);
                }
            }
        }
    }

    [Fact]
    public void A_fallback_winner_keeps_the_country_but_has_no_anthem()
    {
        var steps = DemoRace.Steps(winner: "PER");
        var podium = All(RacePhase.Anthem, steps)[0].Podium!;

        Assert.Equal(("Mexico", (string?)null), (podium.AnthemCountry, podium.AnthemId));
        Assert.Equal(("PER", "KAI"), (podium.Steps[0].Code, podium.Steps[0].Name));
        Assert.Equal("PER", steps.Single(s => s.View.Phase == RacePhase.Chequered).View.Drivers[0].Code);
        Assert.Throws<ArgumentException>(() => DemoRace.Steps(winner: "PIA"));
    }

    [Fact]
    public void Views_round_trip_through_the_wire_format()
    {
        foreach (var step in Steps)
        {
            var json = JsonSerializer.SerializeToElement(step.View, ArcJson.Options);
            var back = json.Deserialize<RedlineHostView>(ArcJson.Options)!;
            Assert.Equal(step.View.Phase, back.Phase);
            Assert.Equal(step.View.Drivers.Select(d => (d.Code, d.Distance, d.Outcome)), back.Drivers.Select(d => (d.Code, d.Distance, d.Outcome)));
            Assert.Equal(step.View.Podium?.Steps.Select(s => (s.Code, s.Charge, s.Sprays.Count)), back.Podium?.Steps.Select(s => (s.Code, s.Charge, s.Sprays.Count)));
        }
    }

    [Fact]
    public void DemoRace_qualifying_step_has_a_deleted_lap_and_a_pole_change()
    {
        var qualifying = Steps.Where(s => s.Label == "qualifying").ToList();
        var views = qualifying.Select(s => s.View).ToList();

        // Between Driver Select and Grid, one reading, question and result per lap.
        var first = Steps.ToList().IndexOf(qualifying[0]);
        Assert.Equal(RacePhase.DriverSelect, Steps[first - 1].View.Phase);
        Assert.Equal(RacePhase.Grid, Steps[first + qualifying.Count].View.Phase);
        Assert.Equal(Enumerable.Range(1, 3).SelectMany(_ => new[] { RacePhase.QualifyingReading, RacePhase.QualifyingQuestion, RacePhase.QualifyingResult }),
            views.Select(v => v.Phase));
        Assert.Equal([1, 1, 1, 2, 2, 2, 3, 3, 3], views.Select(v => v.Qualifying!.Lap));
        Assert.All(qualifying, s => Assert.True(s.Seconds > 0));
        Assert.All(views, v => Assert.Equal((0, 3, 22), (v.Round, v.Qualifying!.Laps, v.Drivers.Count)));
        Assert.All(views, v => Assert.Equal(v.Drivers.Select(d => (d.Code, d.Position)), v.Qualifying!.Rows.Select(r => (r.Code, r.Position))));
        Assert.All(views, v => Assert.Equal(1, v.Qualifying!.Rows[0].Position));

        // Nothing is shown before the result: no lap times, fastest lap, movement or answer.
        foreach (var v in views.Where(v => v.Phase != RacePhase.QualifyingResult))
        {
            Assert.Null(v.CorrectIndex);
            Assert.All(v.Qualifying!.Rows, r => Assert.Equal((null, false, 0), (r.LastLapMs, r.FastestLap, r.PositionChange)));
        }
        Assert.All(views.Where(v => v.Phase != RacePhase.QualifyingQuestion), v => Assert.All(v.Qualifying!.Rows, r => Assert.False(r.Answered)));
        var question = views.First(v => v.Phase == RacePhase.QualifyingQuestion);
        Assert.Contains(question.Qualifying!.Rows, r => r.Answered);
        Assert.Contains(question.Drivers, d => d.Player is not null && d.Status == DriverStatus.Answering);
        Assert.All(question.Drivers.Where(d => d.Player is null), d => Assert.Equal(DriverStatus.Answered, d.Status));
        Assert.NotNull(question.Question!.Text);

        var results = views.Where(v => v.Phase == RacePhase.QualifyingResult).ToList();
        Assert.All(results, v => Assert.NotNull(v.CorrectIndex));

        // Lap 1 deletes a human's lap.
        var humans = results[0].Drivers.Where(d => d.Player is not null).Select(d => d.Code).ToHashSet();
        Assert.Contains(results[0].Qualifying!.Rows, r => humans.Contains(r.Code) && r.LastLapMs is null);

        // Lap 2 takes pole from the lap 1 leader, and the card for it appears.
        var lap1Leader = results[0].Qualifying!.Rows[0].Code;
        var lap2Leader = results[1].Qualifying!.Rows[0].Code;
        Assert.NotEqual(lap1Leader, lap2Leader);
        var lap2Question = views[4];
        Assert.NotNull(QualifyingText.ProvisionalPole(results[1], lap2Question));
        Assert.Equal("JO", QualifyingText.Pole(results[2])!.Value.Name);
        Assert.Equal(results[2].Qualifying!.Rows[0].Code, results[1].Qualifying!.Rows[0].Code);

        // Each result adds its lap to the totals, the classification follows correct laps then time, and one lap is purple.
        for (var i = 0; i < results.Count; i++)
        {
            var rows = results[i].Qualifying!.Rows;
            var before = (i == 0 ? views[1] : results[i - 1]).Qualifying!.Rows.ToDictionary(r => r.Code);
            foreach (var r in rows)
            {
                Assert.Equal(before[r.Code].TotalMs + (r.LastLapMs ?? 0), r.TotalMs);
                Assert.Equal(before[r.Code].Correct + (r.LastLapMs is null ? 0 : 1), r.Correct);
                Assert.Equal(before[r.Code].Position - r.Position, r.PositionChange);
            }
            Assert.Equal(rows.OrderByDescending(r => r.Correct).ThenBy(r => r.TotalMs).Select(r => r.Code).Take(rows.Count(r => r.Correct > 0)),
                rows.Where(r => r.Correct > 0).Select(r => r.Code));
            Assert.Equal(rows.Where(r => r.LastLapMs is not null).MinBy(r => r.LastLapMs)!.Code, Assert.Single(rows, r => r.FastestLap).Code);
        }
        Assert.Contains(results, v => v.Qualifying!.Rows.Any(r => r.FastestLap && v.Drivers.Single(d => d.Code == r.Code).Player is not null));

        // The last result is the grid: the same cars in the same slots.
        Assert.Equal(Phase(RacePhase.Grid).Drivers.Select(d => d.Code), results[2].Drivers.Select(d => d.Code));
        Assert.Equal(Phase(RacePhase.Grid).Drivers.Select(d => (d.Player, d.PlayerName)), results[2].Drivers.Select(d => (d.Player, d.PlayerName)));
    }

    [Fact]
    public void Qualifying_views_round_trip_through_the_wire_format()
    {
        foreach (var step in Steps.Where(s => s.Label == "qualifying"))
        {
            var back = JsonSerializer.SerializeToElement(step.View, ArcJson.Options).Deserialize<RedlineHostView>(ArcJson.Options)!;
            Assert.Equal(step.View.Qualifying!.Lap, back.Qualifying!.Lap);
            Assert.Equal(step.View.Qualifying.Rows, back.Qualifying.Rows);
        }
    }
}
