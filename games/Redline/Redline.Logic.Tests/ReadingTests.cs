using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Testing;
using Redline.Logic;

namespace Redline.Logic.Tests;

/// <summary>Audio spec §2: the Reading phase between Throttle and Question holds the answer clock while the question is read.</summary>
public class ReadingTests
{
    private static readonly string[] Codes = ["NOR", "VER", "LEC", "ANT"];

    /// <summary>Twelve words: 0.6 s + 12 × 0.36 s = 4.92 s of Reading.</summary>
    private const string TwelveWords = "Which planet in our solar system has the most known moons today?";

    private static readonly TimeSpan TwelveWordReading = TimeSpan.FromMilliseconds(4920);

    private static GameHarness<RedlineGame> Round1(string? text = null)
    {
        var (h, _) = RedlineKit.Start(reading: true,
            questions: new FakeQuestionSource(text: text is null ? null : i => i == 1 ? text : $"Q{i}"));
        RedlineKit.SeatAllAndGoToRound1(h, Codes);
        return h;
    }

    private static void LockAll(GameHarness<RedlineGame> h, string id = "push")
    {
        for (var i = 0; i < h.Players.Count; i++) RedlineKit.Throttle(h, i, id);
    }

    private static string Snapshot(GameHarness<RedlineGame> h) => JsonSerializer.Serialize(h.Game.HostView(), ArcJson.Options);

    private static string Header(GameHarness<RedlineGame> h, int player) =>
        $"P{RedlineKit.Row(h, player).Position} · ROUND {h.Game.Round}/15";

    [Fact]
    public void Reading_follows_throttle_and_shows_the_question_on_phone()
    {
        var h = Round1(TwelveWords);
        Assert.True(new RedlineRules().ReadQuestions);                                 // on by default
        LockAll(h);

        Assert.Equal((RacePhase.Reading, 1), (h.Game.Phase, h.Game.Round));            // early advance from Throttle still works
        for (var i = 0; i < 4; i++)
        {
            var phone = Assert.IsType<WaitView>(h.PlayerView(i));
            Assert.Equal(TwelveWords, phone.Message);
            Assert.Equal($"PUSH · {Header(h, i)}", phone.Header);
            Assert.Equal(h.Now + TwelveWordReading, phone.DeadlineUtc);
        }
    }

    [Fact]
    public void Reading_lasts_ReadingTime_then_opens_question()
    {
        var h = Round1(TwelveWords);
        LockAll(h);
        var reading = RedlineKit.View(h);
        Assert.Equal(h.Now + TwelveWordReading, reading.DeadlineUtc);

        h.Advance(TwelveWordReading - TimeSpan.FromMilliseconds(1));
        Assert.Equal(RacePhase.Reading, h.Game.Phase);
        h.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(RacePhase.Question, h.Game.Phase);

        // The answer clock starts only now, at its full length.
        var v = RedlineKit.View(h);
        Assert.Equal(h.Now + h.Game.Rules.Question, v.DeadlineUtc);
        var phone = Assert.IsType<ChoiceView>(h.PlayerView(0));
        Assert.Equal(TwelveWords, phone.Prompt);
        Assert.Equal(v.Question!.Answers, phone.Options.Select(o => o.Label));
        Assert.Equal(h.Now + h.Game.Rules.Question, phone.DeadlineUtc);

        // AI answers when the question opens; the round plays on as before.
        Assert.All(v.Drivers.Where(d => d.Player is null), d => Assert.Equal(DriverStatus.Answered, d.Status));
        for (var i = 0; i < 4; i++) RedlineKit.AnswerRight(h, i);
        Assert.Equal(RacePhase.Reveal, h.Game.Phase);
        Assert.All(Enumerable.Range(0, 4), i => Assert.Equal(RoundOutcome.Correct, RedlineKit.Row(h, i).Outcome));
    }

    [Fact]
    public void Inputs_during_reading_are_ignored()
    {
        var h = Round1();
        LockAll(h);
        Assert.Equal(RacePhase.Reading, h.Game.Phase);
        var snapshot = Snapshot(h);
        var view = h.PlayerView(0);

        foreach (var id in new[] { "a", "b", "c", "d" }) h.Input(0, new { selected = new[] { id } });
        h.Input(1, new { selected = new[] { "a" } });
        h.Input(0, new { pressed = "redline" });
        h.Input(0, new { nope = 1 });
        h.Input(0, new { selected = "a" });                                            // JsonException
        Assert.Equal(snapshot, Snapshot(h));
        Assert.Same(view, h.PlayerView(0));
        Assert.Equal(Throttle.Push, RedlineKit.Row(h, 0).Throttle);

        h.Advance(h.Game.Rules.ReadingTime("Q1"));
        Assert.Equal(RacePhase.Question, h.Game.Phase);
        Assert.IsType<ChoiceView>(h.PlayerView(0));                                    // still free to answer
        Assert.IsType<ChoiceView>(h.PlayerView(1));
        Assert.Equal(DriverStatus.Answering, RedlineKit.Row(h, 0).Status);
    }

    [Fact]
    public void Reading_does_not_end_early_when_everyone_is_ready()
    {
        var h = Round1(TwelveWords);
        LockAll(h);
        var start = h.Now;

        // Every human is locked and nobody can do anything more, a player drops and comes back, another drops for good:
        // the phase still runs to its timer.
        h.SetConnected(3, false);
        Assert.Equal(RacePhase.Reading, h.Game.Phase);
        h.SetConnected(3, true);
        h.SetConnected(2, false);
        Assert.Equal(RacePhase.Reading, h.Game.Phase);
        h.Advance(TwelveWordReading - TimeSpan.FromMilliseconds(1));
        Assert.Equal(RacePhase.Reading, h.Game.Phase);

        // Even with nobody connected.
        for (var i = 0; i < 4; i++) h.SetConnected(i, false);
        Assert.Equal(RacePhase.Reading, h.Game.Phase);
        h.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(RacePhase.Question, h.Game.Phase);
        Assert.Equal(start + TwelveWordReading, h.Now);
    }

    [Fact]
    public void ReadQuestions_false_skips_reading()
    {
        var q = new FakeQuestionSource();
        var h = new GameHarness<RedlineGame>(new RedlineGame(q, new RedlineRules { ReadQuestions = false }), 4, 1);
        h.Start();
        RedlineKit.SeatAllAndGoToRound1(h, Codes);
        var start = h.Now;
        LockAll(h);

        Assert.Equal(RacePhase.Question, h.Game.Phase);
        Assert.Equal(start + h.Game.Rules.Question, RedlineKit.View(h).DeadlineUtc);
        Assert.IsType<ChoiceView>(h.PlayerView(0));

        // Throttle running out also goes straight to the Question.
        for (var i = 0; i < 4; i++) RedlineKit.AnswerWrong(h, i);
        h.Advance(h.Game.Rules.Reveal);
        h.Advance(h.Game.Rules.Throttle);
        Assert.Equal((RacePhase.Question, 2), (h.Game.Phase, h.Game.Round));
    }

    [Fact]
    public void ReadingTime_is_clamped()
    {
        var rules = new RedlineRules();
        string Words(int n) => string.Join(' ', Enumerable.Repeat("word", n));

        Assert.Equal(TimeSpan.FromSeconds(2.5), rules.ReadingTime("Capital?"));             // 0.96 s → floor
        Assert.Equal(TwelveWordReading, rules.ReadingTime(TwelveWords));                    // 0.6 + 12 × 0.36
        Assert.Equal(TimeSpan.FromSeconds(10), rules.ReadingTime(Words(60)));               // 22.2 s → ceiling
        Assert.Equal(TimeSpan.FromMilliseconds(600 + 26 * 360), rules.ReadingTime(Words(26)));
        Assert.Equal(rules.ReadingTime(Words(12)), rules.ReadingTime("  word word\tword\nword " + Words(8) + "  "));  // any whitespace
        Assert.Equal(TimeSpan.FromSeconds(2.5), rules.ReadingTime(""));
    }

    [Fact]
    public void HostView_in_reading_shows_text_and_answers_but_no_correct_index()
    {
        var h = Round1(TwelveWords);
        RedlineKit.Throttle(h, 0, "redline");
        for (var i = 1; i < 4; i++) RedlineKit.Throttle(h, i, "cruise");

        var v = RedlineKit.View(h);
        Assert.Equal(RacePhase.Reading, v.Phase);
        Assert.NotNull(v.Question);
        Assert.Equal(TwelveWords, v.Question.Text);
        Assert.NotNull(v.Question.Answers);
        Assert.Equal(["right1", "w1", "w2", "w3"], v.Question.Answers.Order());
        Assert.Null(v.CorrectIndex);
        Assert.Equal(h.Now + TwelveWordReading, v.DeadlineUtc);

        // Throttles are public, as in Question; answers and outcomes are not.
        Assert.All(v.Drivers, d => Assert.NotNull(d.Throttle));
        Assert.Equal(Throttle.Redline, RedlineKit.Row(h, 0).Throttle);
        Assert.All(v.Drivers, d => Assert.Null(d.AnswerIndex));
        Assert.All(v.Drivers, d => Assert.Null(d.Outcome));
        Assert.All(v.Drivers, d => Assert.Equal(0, d.PositionChange));
        Assert.All(v.Drivers, d => Assert.Equal(DriverStatus.Ready, d.Status));
        Assert.Contains("\"phase\":\"Reading\"", Snapshot(h));
        Assert.Contains("\"correctIndex\":null", Snapshot(h));
    }
}
