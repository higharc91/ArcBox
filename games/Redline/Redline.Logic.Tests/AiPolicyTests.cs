namespace Redline.Logic.Tests;

public class AiPolicyTests
{
    [Fact] public void Accuracy_is_base_times_skill_clamped()
    {
        var r = new AiRules();
        Assert.Equal(0.72 * 1.15, AiPolicy.Accuracy(1.15, Difficulty.Easy, r), 6);
        Assert.Equal(0.92, AiPolicy.Accuracy(2.0, Difficulty.Easy, r), 6);
        Assert.Equal(0.32 * 0.85, AiPolicy.Accuracy(0.85, Difficulty.Hard, r), 6);
    }

    [Fact] public void Answers_hit_p_within_two_percent_over_10000_draws()
    {
        var rnd = new Random(7); var p = 0.52 * 1.09;
        var hits = Enumerable.Range(0, 10_000).Count(_ => AiPolicy.Answer(p, 2, rnd) == 2);
        Assert.InRange(hits / 10_000.0, p - 0.02, p + 0.02);
    }

    [Fact] public void Wrong_answers_never_pick_the_correct_index_and_spread_over_all_three()
    {
        var rnd = new Random(3);
        var wrong = Enumerable.Range(0, 3000).Select(_ => AiPolicy.Answer(0.0001, 1, rnd)).ToList();
        Assert.DoesNotContain(1, wrong);
        Assert.Equal([0, 2, 3], wrong.Distinct().Order());
    }

    [Fact] public void No_redline_below_floor_unless_chasing()
    {
        var rnd = new Random(1); var r = new AiRules();
        Assert.DoesNotContain(Throttle.Redline, Enumerable.Range(0, 2000).Select(_ => AiPolicy.ChooseThrottle(0.29, 0, 10, r, rnd)));
        Assert.Contains(Throttle.Redline, Enumerable.Range(0, 2000).Select(_ => AiPolicy.ChooseThrottle(0.29, 6, 5, r, rnd)));
    }

    [Fact] public void Chase_mode_boundary_is_gap_6_and_5_rounds_left()
    {   // compare Redline frequency: (gap 6, left 5) chases; (gap 5, left 5) and (gap 6, left 6) do not
        double Freq(int gap, int left) { var rnd = new Random(9); return Enumerable.Range(0, 4000).Count(_ => AiPolicy.ChooseThrottle(0.5, gap, left, new AiRules(), rnd) == Throttle.Redline) / 4000.0; }
        Assert.True(Freq(6, 5) > Freq(5, 5) + 0.05);
        Assert.True(Freq(6, 5) > Freq(6, 6) + 0.05);
    }

    [Fact] public void Same_seed_same_decisions()
    {
        var a = new Random(42); var b = new Random(42);
        Assert.Equal(Enumerable.Range(0, 100).Select(_ => AiPolicy.ChooseThrottle(0.6, 0, 10, new(), a)),
                     Enumerable.Range(0, 100).Select(_ => AiPolicy.ChooseThrottle(0.6, 0, 10, new(), b)));
    }

    [Fact] public void Rules_defaults_match_the_spec()
    {
        var r = new RedlineRules();
        Assert.Equal(20, r.FinishLine); Assert.Equal(15, r.MaxRounds); Assert.Equal("cota", r.CircuitId);
        Assert.Equal(TimeSpan.FromSeconds(20), r.DriverSelect); Assert.Equal(TimeSpan.FromSeconds(6), r.Grid);
        Assert.Equal(TimeSpan.FromSeconds(10), r.Throttle); Assert.Equal(TimeSpan.FromSeconds(20), r.Question);
        Assert.Equal(TimeSpan.FromSeconds(7), r.Reveal); Assert.Equal(TimeSpan.FromSeconds(10), r.Chequered);
        Assert.Equal((1, 2, 1, 4, 2), (r.CruiseGain, r.PushGain, r.PushLoss, r.RedlineGain, r.RedlineLoss));
    }
}
