using System.Text.Json;
using ArcBox.Sdk;
using Redline.Logic;
using Redline.Presentation.Demo;

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

        Assert.Equal([RacePhase.DriverSelect, RacePhase.Grid, RacePhase.Throttle, RacePhase.Reading, RacePhase.Question, RacePhase.Reveal,
            RacePhase.Chequered, RacePhase.Anthem, RacePhase.Champagne], phases);
        // Each phase's steps sit together, so the podium phases each last as long as the rules say.
        Assert.Equal(phases.Count, Steps.Select(s => s.View.Phase).Where((p, i) => i == 0 || Steps[i - 1].View.Phase != p).Count());
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
}
