using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Testing;
using Redline.Logic;

namespace Redline.Logic.Tests;

public class RoundTests
{
    private static readonly string[] Codes = ["NOR", "VER", "LEC", "ANT"];

    private static GameHarness<RedlineGame> Round1()
    {
        var (h, _) = RedlineKit.Start();
        RedlineKit.SeatAllAndGoToRound1(h, Codes);
        return h;
    }

    private static string Snapshot(GameHarness<RedlineGame> h) => JsonSerializer.Serialize(h.Game.HostView(), ArcJson.Options);

    private static string Header(GameHarness<RedlineGame> h, int player) =>
        $"P{RedlineKit.Row(h, player).Position} · ROUND {h.Game.Round}/15";

    private static void LockAll(GameHarness<RedlineGame> h, string id = "cruise")
    {
        for (var i = 0; i < h.Players.Count; i++) RedlineKit.Throttle(h, i, id);
    }

    [Theory]
    [InlineData(Throttle.Cruise, true, 5, 6, RoundOutcome.Correct, false)]
    [InlineData(Throttle.Cruise, false, 5, 5, RoundOutcome.Wrong, false)]
    [InlineData(Throttle.Push, true, 5, 7, RoundOutcome.Correct, false)]
    [InlineData(Throttle.Push, false, 5, 4, RoundOutcome.Wrong, false)]
    [InlineData(Throttle.Redline, true, 5, 9, RoundOutcome.Correct, false)]
    [InlineData(Throttle.Redline, false, 5, 3, RoundOutcome.SpunOut, true)]
    [InlineData(Throttle.Redline, null, 1, 0, RoundOutcome.SpunOut, true)]          // floor at 0; no answer counts as wrong
    public void Scoring_table(Throttle t, bool? correct, int from, int to, RoundOutcome outcome, bool spin)
        => Assert.Equal((to, outcome, spin), Scoring.Apply(from, t, correct, new RedlineRules()));

    [Theory]
    [InlineData(Throttle.Cruise, 3, 3)]
    [InlineData(Throttle.Push, 3, 2)]
    [InlineData(Throttle.Push, 0, 0)]                                               // floor at 0
    public void Scoring_no_answer_is_wrong_and_reported_as_no_answer(Throttle t, int from, int to)
        => Assert.Equal((to, RoundOutcome.NoAnswer, false), Scoring.Apply(from, t, null, new RedlineRules()));

    [Fact]
    public void Throttle_phase_hides_question_text_and_all_throttles()
    {
        var h = Round1();
        var v = RedlineKit.View(h);

        Assert.Equal(RacePhase.Throttle, v.Phase);
        Assert.Equal(1, v.Round);
        Assert.NotNull(v.Question);
        Assert.Equal(("Science & Nature", Difficulty.Medium), (v.Question.Category, v.Question.Difficulty));
        Assert.Null(v.Question.Text);
        Assert.Null(v.Question.Answers);
        Assert.Null(v.CorrectIndex);
        Assert.Equal(22, v.Drivers.Count);
        Assert.All(v.Drivers, d => Assert.Null(d.Throttle));
        Assert.All(v.Drivers, d => Assert.Null(d.AnswerIndex));
        Assert.All(v.Drivers, d => Assert.Null(d.Outcome));
        Assert.All(v.Drivers, d => Assert.Equal(0, d.PositionChange));
        Assert.Equal(h.Now + TimeSpan.FromSeconds(10), v.DeadlineUtc);

        // Humans are choosing; AI decided the moment the phase began.
        Assert.All(v.Drivers.Where(d => d.Player is not null), d => Assert.Equal(DriverStatus.Choosing, d.Status));
        Assert.All(v.Drivers.Where(d => d.Player is null), d => Assert.Equal(DriverStatus.Locked, d.Status));

        var phone = Assert.IsType<ButtonsView>(h.PlayerView(0));
        Assert.Equal("Science & Nature · Medium. How hard do you push?", phone.Prompt);
        Assert.Equal(["cruise", "push", "redline"], phone.Buttons.Select(b => b.Id));
        Assert.Equal(
            ["+1 if right · nothing lost", "+2 if right · −1 if wrong", "+4 if right · spin out if wrong"],
            phone.Buttons.Select(b => b.Hint));
        Assert.Equal(Header(h, 0), phone.Header);
        Assert.Equal(h.Now + TimeSpan.FromSeconds(10), phone.DeadlineUtc);

        RedlineKit.Throttle(h, 0, "redline");
        Assert.Equal(DriverStatus.Locked, RedlineKit.Row(h, 0).Status);
        Assert.Null(RedlineKit.Row(h, 0).Throttle);                                    // locked, but still secret
        var locked = Assert.IsType<WaitView>(h.PlayerView(0));
        Assert.Equal("Locked in: REDLINE. Waiting for the other drivers…", locked.Message);
        Assert.Equal(Header(h, 0), locked.Header);
    }

    [Fact]
    public void Question_phase_reveals_all_throttles_but_no_answers()
    {
        var h = Round1();
        LockAll(h, "redline");

        var v = RedlineKit.View(h);
        Assert.Equal(RacePhase.Question, v.Phase);                                      // advanced early
        Assert.Equal(h.Now + TimeSpan.FromSeconds(20), v.DeadlineUtc);
        Assert.All(v.Drivers, d => Assert.NotNull(d.Throttle));
        Assert.All(v.Drivers, d => Assert.Null(d.AnswerIndex));
        Assert.All(v.Drivers, d => Assert.Null(d.Outcome));
        Assert.All(v.Drivers, d => Assert.Equal(0, d.PositionChange));
        Assert.Null(v.CorrectIndex);
        for (var i = 0; i < 4; i++) Assert.Equal(Throttle.Redline, RedlineKit.Row(h, i).Throttle);

        Assert.NotNull(v.Question);
        Assert.Equal("Q1", v.Question.Text);
        Assert.NotNull(v.Question.Answers);
        Assert.Equal(["right1", "w1", "w2", "w3"], v.Question.Answers.Order());

        // Humans are answering; AI answered the moment the phase began.
        Assert.All(v.Drivers.Where(d => d.Player is not null), d => Assert.Equal(DriverStatus.Answering, d.Status));
        Assert.All(v.Drivers.Where(d => d.Player is null), d => Assert.Equal(DriverStatus.Answered, d.Status));

        var phone = Assert.IsType<ChoiceView>(h.PlayerView(0));
        Assert.Equal("Q1", phone.Prompt);
        Assert.Equal(["a", "b", "c", "d"], phone.Options.Select(o => o.Id));
        Assert.Equal(v.Question.Answers, phone.Options.Select(o => o.Label));            // phone and TV share the shuffled order
        Assert.False(phone.AllowMultiple);
        Assert.Equal($"REDLINE · {Header(h, 0)}", phone.Header);

        RedlineKit.AnswerRight(h, 0);
        Assert.Equal(DriverStatus.Answered, RedlineKit.Row(h, 0).Status);
        Assert.Null(RedlineKit.Row(h, 0).AnswerIndex);                                 // answered, but still secret
        Assert.Null(RedlineKit.Row(h, 0).Outcome);
        Assert.Null(RedlineKit.View(h).CorrectIndex);
        Assert.Equal("Answer locked. Eyes on the TV!", Assert.IsType<WaitView>(h.PlayerView(0)).Message);
    }

    [Fact]
    public void Reveal_moves_players_by_the_table_and_reports_on_phone()
    {
        var h = Round1();
        RedlineKit.Throttle(h, 0, "redline");
        RedlineKit.Throttle(h, 1, "push");
        RedlineKit.Throttle(h, 3, "cruise");                                           // P2 never picks: Cruise
        h.Advance(h.Game.Rules.Throttle);
        Assert.Equal(RacePhase.Question, h.Game.Phase);
        Assert.Equal(Throttle.Cruise, RedlineKit.Row(h, 2).Throttle);

        var before = RedlineKit.View(h);
        var answers = before.Question!.Answers!;
        RedlineKit.AnswerRight(h, 0);
        RedlineKit.AnswerWrong(h, 1);
        RedlineKit.AnswerRight(h, 2);                                                  // P3 never answers
        h.Advance(TimeSpan.FromSeconds(20));

        var v = RedlineKit.View(h);
        Assert.Equal(RacePhase.Reveal, v.Phase);
        Assert.Equal(h.Now + TimeSpan.FromSeconds(7), v.DeadlineUtc);
        Assert.Equal(answers.ToList().IndexOf("right1"), v.CorrectIndex);
        Assert.Equal([4, 0, 1, 0], Enumerable.Range(0, 4).Select(i => RedlineKit.Row(h, i).Distance));
        Assert.Equal(
            [RoundOutcome.Correct, RoundOutcome.Wrong, RoundOutcome.Correct, RoundOutcome.NoAnswer],
            Enumerable.Range(0, 4).Select(i => RedlineKit.Row(h, i).Outcome!.Value));
        Assert.Equal(v.CorrectIndex, RedlineKit.Row(h, 0).AnswerIndex);
        Assert.Equal(answers.ToList().IndexOf("w1"), RedlineKit.Row(h, 1).AnswerIndex);
        Assert.Null(RedlineKit.Row(h, 3).AnswerIndex);
        Assert.All(v.Drivers, d => Assert.Equal(0, d.PreviousDistance));
        Assert.All(v.Drivers, d => Assert.NotNull(d.Outcome));                         // all 22 scored
        Assert.All(v.Drivers.Where(d => d.Player is null), d => Assert.NotNull(d.AnswerIndex));
        Assert.All(v.Drivers, d => Assert.Equal(DriverStatus.Ready, d.Status));

        // Re-ranked: rows in position order 1..22, distance never increasing, PositionChange from the round start.
        Assert.Equal(Enumerable.Range(1, 22), v.Drivers.Select(d => d.Position));
        Assert.True(v.Drivers.Zip(v.Drivers.Skip(1)).All(p => p.First.Distance >= p.Second.Distance));
        foreach (var row in v.Drivers)
            Assert.Equal(before.Drivers.Single(d => d.Code == row.Code).Position - row.Position, row.PositionChange);

        string Msg(int p) => Assert.IsType<WaitView>(h.PlayerView(p)).Message;
        int Pos(int p) => RedlineKit.Row(h, p).Position;
        Assert.StartsWith("Correct! +4 · now P", Msg(0));
        Assert.Equal($"Correct! +4 · now P{Pos(0)}", Msg(0));
        Assert.Equal($"Wrong answer. −1 · now P{Pos(1)}", Msg(1));
        Assert.Equal($"Correct! +1 · now P{Pos(2)}", Msg(2));
        Assert.Equal($"No answer. No change · P{Pos(3)}", Msg(3));
        Assert.Equal(Header(h, 0), h.PlayerView(0).Header);

        // The Reveal timer starts round 2 from the new distances.
        h.Advance(h.Game.Rules.Reveal);
        v = RedlineKit.View(h);
        Assert.Equal((RacePhase.Throttle, 2), (v.Phase, v.Round));
        Assert.Equal(4, RedlineKit.Row(h, 0).PreviousDistance);
        Assert.All(v.Drivers, d => Assert.Equal(0, d.PositionChange));
        Assert.All(v.Drivers, d => Assert.Null(d.Outcome));
        Assert.Equal(Header(h, 0), h.PlayerView(0).Header);
        Assert.EndsWith(" · ROUND 2/15", h.PlayerView(0).Header);
    }

    [Fact]
    public void Reveal_reports_wrong_cruise_and_no_answer_push_and_redline()
    {
        var h = Round1();
        RedlineKit.Throttle(h, 0, "cruise");
        RedlineKit.Throttle(h, 1, "push");
        RedlineKit.Throttle(h, 2, "redline");
        RedlineKit.Throttle(h, 3, "redline");
        RedlineKit.AnswerWrong(h, 0);
        RedlineKit.AnswerWrong(h, 3);
        h.Advance(h.Game.Rules.Question);

        string Msg(int p) => Assert.IsType<WaitView>(h.PlayerView(p)).Message;
        int Pos(int p) => RedlineKit.Row(h, p).Position;
        Assert.Equal($"Wrong answer. No change · P{Pos(0)}", Msg(0));
        Assert.Equal($"No answer. −1 · now P{Pos(1)}", Msg(1));
        Assert.Equal($"Spun out! −2 · pit limiter next round · now P{Pos(2)}", Msg(2));
        Assert.Equal($"Spun out! −2 · pit limiter next round · now P{Pos(3)}", Msg(3));
        Assert.Equal(RoundOutcome.NoAnswer, RedlineKit.Row(h, 1).Outcome);
        Assert.Equal(RoundOutcome.SpunOut, RedlineKit.Row(h, 2).Outcome);
    }

    [Fact]
    public void Ai_never_blocks_early_advance()
    {
        var h = Round1();
        var start = h.Now;
        LockAll(h);
        Assert.Equal(RacePhase.Question, h.Game.Phase);
        for (var i = 0; i < 4; i++) RedlineKit.AnswerRight(h, i);
        Assert.Equal(RacePhase.Reveal, h.Game.Phase);
        Assert.Equal(start, h.Now);
    }

    [Fact]
    public void Spin_out_sets_pit_limiter_for_one_round_only()
    {
        var h = Round1();
        RedlineKit.Throttle(h, 0, "redline");
        for (var i = 1; i < 4; i++) RedlineKit.Throttle(h, i, "cruise");
        for (var i = 0; i < 4; i++) RedlineKit.AnswerWrong(h, i);
        h.Advance(h.Game.Rules.Reveal);

        Assert.Equal((RacePhase.Throttle, 2), (h.Game.Phase, h.Game.Round));
        Assert.Equal(DriverStatus.Limited, RedlineKit.Row(h, 0).Status);
        Assert.True(RedlineKit.Row(h, 0).PitLimiter);
        Assert.Null(RedlineKit.Row(h, 0).Throttle);                                    // still secret in Throttle
        Assert.Equal(DriverStatus.Choosing, RedlineKit.Row(h, 1).Status);
        var wait = Assert.IsType<WaitView>(h.PlayerView(0));
        Assert.Equal("Pit limiter: you're on Cruise this round.", wait.Message);
        Assert.Equal(Header(h, 0), wait.Header);

        var view = h.PlayerView(0);
        RedlineKit.Throttle(h, 0, "redline");                                          // ignored
        Assert.Same(view, h.PlayerView(0));
        Assert.Equal(DriverStatus.Limited, RedlineKit.Row(h, 0).Status);

        for (var i = 1; i < 4; i++) RedlineKit.Throttle(h, i, "cruise");
        Assert.Equal(RacePhase.Question, h.Game.Phase);                                 // P0 counted as locked
        Assert.Equal(Throttle.Cruise, RedlineKit.Row(h, 0).Throttle);
        Assert.Equal($"CRUISE · {Header(h, 0)}", h.PlayerView(0).Header);

        for (var i = 0; i < 4; i++) RedlineKit.AnswerWrong(h, i);
        Assert.False(RedlineKit.Row(h, 0).PitLimiter);                                  // Cruise can't spin
        h.Advance(h.Game.Rules.Reveal);

        Assert.Equal((RacePhase.Throttle, 3), (h.Game.Phase, h.Game.Round));
        Assert.Equal(DriverStatus.Choosing, RedlineKit.Row(h, 0).Status);
        Assert.False(RedlineKit.Row(h, 0).PitLimiter);
        Assert.IsType<ButtonsView>(h.PlayerView(0));
    }

    [Fact]
    public void When_every_connected_human_is_limited_throttle_ends_at_once()
    {
        var h = Round1();
        LockAll(h, "redline");
        for (var i = 0; i < 4; i++) RedlineKit.AnswerWrong(h, i);
        h.Advance(h.Game.Rules.Reveal);

        Assert.Equal((RacePhase.Question, 2), (h.Game.Phase, h.Game.Round));         // limited drivers count as locked
        Assert.All(Enumerable.Range(0, 4), i => Assert.Equal(Throttle.Cruise, RedlineKit.Row(h, i).Throttle));
        Assert.All(Enumerable.Range(0, 4), i => Assert.True(RedlineKit.Row(h, i).PitLimiter));
    }

    // Review Focus 3
    [Fact]
    public void Pit_limited_player_still_answers_and_gains_cruise()
    {
        var h = Round1();
        RedlineKit.Throttle(h, 0, "redline");
        for (var i = 1; i < 4; i++) RedlineKit.Throttle(h, i, "cruise");
        for (var i = 0; i < 4; i++) RedlineKit.AnswerWrong(h, i);
        h.Advance(h.Game.Rules.Reveal);
        Assert.Equal(DriverStatus.Limited, RedlineKit.Row(h, 0).Status);
        var before = RedlineKit.Row(h, 0).Distance;

        for (var i = 1; i < 4; i++) RedlineKit.Throttle(h, i, "push");
        Assert.IsType<ChoiceView>(h.PlayerView(0));
        RedlineKit.AnswerRight(h, 0);
        Assert.Equal(DriverStatus.Answered, RedlineKit.Row(h, 0).Status);
        h.Advance(h.Game.Rules.Question);

        Assert.Equal(RacePhase.Reveal, h.Game.Phase);
        Assert.Equal(before + 1, RedlineKit.Row(h, 0).Distance);
        Assert.Equal(RoundOutcome.Correct, RedlineKit.Row(h, 0).Outcome);
        Assert.StartsWith("Correct! +1 · now P", Assert.IsType<WaitView>(h.PlayerView(0)).Message);
    }

    [Fact]
    public void Disconnect_of_last_undecided_player_advances_immediately()
    {
        var h = Round1();
        for (var i = 0; i < 3; i++) RedlineKit.Throttle(h, i, "push");
        Assert.Equal(RacePhase.Throttle, h.Game.Phase);
        h.SetConnected(3, false);
        Assert.Equal(RacePhase.Question, h.Game.Phase);
        Assert.Equal(Throttle.Cruise, RedlineKit.Row(h, 3).Throttle);                  // default for the absent driver
        Assert.False(RedlineKit.Row(h, 3).Connected);

        // Same in the Question phase: P3 is gone, P0 answers, P1 and P2 drop out.
        RedlineKit.AnswerRight(h, 0);
        h.SetConnected(1, false);
        Assert.Equal(RacePhase.Question, h.Game.Phase);
        h.SetConnected(2, false);
        Assert.Equal(RacePhase.Reveal, h.Game.Phase);
        Assert.Equal(RoundOutcome.NoAnswer, RedlineKit.Row(h, 3).Outcome);
    }

    [Fact]
    public void With_nobody_connected_phases_run_on_timers_and_defaults()
    {
        var h = Round1();
        for (var i = 0; i < 4; i++) h.SetConnected(i, false);
        Assert.Equal(RacePhase.Throttle, h.Game.Phase);
        h.Advance(h.Game.Rules.Throttle);
        Assert.Equal(RacePhase.Question, h.Game.Phase);
        h.Advance(h.Game.Rules.Question);
        Assert.Equal(RacePhase.Reveal, h.Game.Phase);
        Assert.All(Enumerable.Range(0, 4), i => Assert.Equal(RoundOutcome.NoAnswer, RedlineKit.Row(h, i).Outcome));
        h.Advance(h.Game.Rules.Reveal);
        Assert.Equal((RacePhase.Throttle, 2), (h.Game.Phase, h.Game.Round));
    }

    [Fact]
    public void Reveal_of_the_last_round_goes_to_chequered()
    {
        var (h, _) = RedlineKit.Start(rules: new RedlineRules { MaxRounds = 2 });
        RedlineKit.SeatAllAndGoToRound1(h, Codes);
        for (var round = 1; round <= 2; round++)
        {
            Assert.Equal((RacePhase.Throttle, round), (h.Game.Phase, h.Game.Round));
            LockAll(h);
            for (var i = 0; i < 4; i++) RedlineKit.AnswerWrong(h, i);
            Assert.Equal(RacePhase.Reveal, h.Game.Phase);
            h.Advance(h.Game.Rules.Reveal);
        }
        Assert.Equal((RacePhase.Chequered, 2), (h.Game.Phase, h.Game.Round));
    }

    [Fact]
    public void Views_are_reference_stable_while_state_unchanged()
    {
        var h = Round1();
        var p0 = h.PlayerView(0);
        var p1 = h.PlayerView(1);
        Assert.Same(p0, h.PlayerView(0));

        RedlineKit.Throttle(h, 0, "push");
        Assert.Same(p1, h.PlayerView(1));                                              // P1's state unchanged
        Assert.NotSame(p0, h.PlayerView(0));
        Assert.Equal("Locked in: PUSH. Waiting for the other drivers…", Assert.IsType<WaitView>(h.PlayerView(0)).Message);
        Assert.Same(h.PlayerView(0), h.PlayerView(0));

        for (var i = 1; i < 4; i++) RedlineKit.Throttle(h, i, "cruise");
        p0 = h.PlayerView(0);
        p1 = h.PlayerView(1);
        RedlineKit.AnswerRight(h, 0);
        Assert.Same(p1, h.PlayerView(1));
        Assert.NotSame(p0, h.PlayerView(0));
        Assert.Same(h.PlayerView(0), h.PlayerView(0));
    }

    [Fact]
    public void Malformed_and_wrong_phase_input_is_ignored()
    {
        var h = Round1();
        var snapshot = Snapshot(h);
        var view = h.PlayerView(0);

        h.Input(0, new { nope = 1 });
        h.Input(0, new { pressed = 5 });                                               // JsonException
        h.Input(0, new { pressed = "turbo" });
        h.Input(0, new { selected = new[] { "a" } });                                  // an answer during Throttle
        h.Input(0, 42);
        Assert.Equal(snapshot, Snapshot(h));
        Assert.Same(view, h.PlayerView(0));

        RedlineKit.Throttle(h, 0, "push");
        RedlineKit.Throttle(h, 0, "redline");                                          // already decided
        for (var i = 1; i < 4; i++) RedlineKit.Throttle(h, i, "cruise");
        Assert.Equal(RacePhase.Question, h.Game.Phase);
        Assert.Equal(Throttle.Push, RedlineKit.Row(h, 0).Throttle);

        snapshot = Snapshot(h);
        view = h.PlayerView(0);
        h.Input(0, new { pressed = "redline" });                                       // a throttle during Question
        h.Input(0, new { selected = new[] { "e" } });
        h.Input(0, new { selected = Array.Empty<string>() });
        h.Input(0, new { nope = 1 });
        h.Input(0, new { selected = "a" });                                            // JsonException
        Assert.Equal(snapshot, Snapshot(h));
        Assert.Same(view, h.PlayerView(0));

        RedlineKit.AnswerWrong(h, 0);
        var answered = Snapshot(h);
        var rightId = "abcd"[RedlineKit.View(h).Question!.Answers!.ToList().IndexOf("right1")].ToString();
        h.Input(0, new { selected = new[] { rightId } });                              // already answered
        Assert.Equal(answered, Snapshot(h));
        for (var i = 1; i < 4; i++) RedlineKit.AnswerRight(h, i);
        Assert.Equal(RoundOutcome.Wrong, RedlineKit.Row(h, 0).Outcome);

        snapshot = Snapshot(h);                                                        // Reveal takes no input
        h.Input(0, new { pressed = "push" });
        h.Input(0, new { selected = new[] { "a" } });
        Assert.Equal(snapshot, Snapshot(h));
    }

    [Fact]
    public void Answers_are_shuffled_once_per_seed()
    {
        static IReadOnlyList<string> Answers(int seed)
        {
            var (h, _) = RedlineKit.Start(seed: seed);
            RedlineKit.SeatAllAndGoToRound1(h, Codes);
            LockAll(h);
            return RedlineKit.View(h).Question!.Answers!;
        }

        Assert.Equal(Answers(3), Answers(3));
        var orders = Enumerable.Range(1, 12).Select(s => string.Join(",", Answers(s))).Distinct().Count();
        Assert.True(orders > 1, "answer order never varies with the seed");
    }
}
