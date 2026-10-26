using System.Text.Json;
using ArcBox.Sdk;
using Redline.Logic;
using Redline.Presentation.Demo;

namespace Redline.Presentation.Tests;

public class DemoRaceTests
{
    private static readonly IReadOnlyList<DemoStep> Steps = DemoRace.Steps();

    private static RedlineHostView Phase(RacePhase phase) => Steps.Single(s => s.View.Phase == phase).View;

    [Fact]
    public void Script_runs_driver_select_to_chequered_in_order()
    {
        var phases = Steps.Select(s => s.View.Phase).Distinct().ToList();

        Assert.Equal([RacePhase.DriverSelect, RacePhase.Grid, RacePhase.Throttle, RacePhase.Question, RacePhase.Reveal, RacePhase.Chequered], phases);
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
    public void Chequered_has_a_winner_over_the_line_and_awards()
    {
        var finish = Phase(RacePhase.Chequered);

        Assert.True(finish.Drivers[0].Distance >= finish.FinishLine);
        Assert.NotNull(finish.Finish);
        Assert.NotEmpty(finish.Finish.Awards);
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
        }
    }
}
