using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Testing;
using Redline.Logic;

namespace Redline.Logic.Tests;

/// <summary>
/// Qualifying spec §2, §4.2, §5: QualifyingReading → QualifyingQuestion → QualifyingResult per lap between Driver Select and
/// the Grid, the server-clamped lap times, the classification that becomes the grid, and the TV and phone views.
/// </summary>
public class QualifyingTests
{
    private static readonly string[] Codes = ["NOR", "VER", "LEC", "ANT", "HAM"];

    /// <summary>Every AI answers every qualifying question wrong, so only the players set times.</summary>
    private static readonly RedlineRules WrongAi = new() { Ai = new AiRules { MinP = 0, MaxP = 0, Easy = 0 } };

    /// <summary>Every AI answers every qualifying question right, so there are AI lap times to keep secret.</summary>
    private static readonly RedlineRules PerfectAi = new() { Ai = new AiRules { MinP = 1, MaxP = 1 } };

    /// <summary>The seed-1 start order of a 4-player room before qualifying existed (players NOR, VER, LEC, ANT).</summary>
    private static readonly string[] SeededGridSeed1 =
        ["LAW", "LIN", "COL", "ANT", "GAS", "SAI", "ALO", "ALB", "BEA", "NOR", "LEC", "BOT",
         "HAM", "VER", "RUS", "BOR", "OCO", "STR", "HUL", "PIA", "PER", "HAD"];

    /// <summary>Qualifying on; seats players 0..n-1 in <see cref="Codes"/> order (or <paramref name="codes"/>) into lap 1.</summary>
    private static GameHarness<RedlineGame> ToQualifying(
        int players = 4, RedlineRules? rules = null, bool reading = false, string[]? codes = null)
    {
        var (h, _) = RedlineKit.Start(players: players, rules: rules, reading: reading, qualify: true);
        RedlineKit.SeatAllAndGoToQualifying(h, codes ?? Codes[..players]);
        return h;
    }

    private static QualifyingSession Quali(GameHarness<RedlineGame> h) =>
        RedlineKit.View(h).Qualifying ?? throw new InvalidOperationException($"No qualifying view in {h.Game.Phase}.");

    /// <summary>The qualifying row of the car driven by <c>h.Players[player]</c>.</summary>
    private static QualifyingRow QRow(GameHarness<RedlineGame> h, int player)
    {
        var code = RedlineKit.Row(h, player).Code;
        return Quali(h).Rows.Single(r => r.Code == code);
    }

    private static void Wait(GameHarness<RedlineGame> h, int ms) => h.Advance(TimeSpan.FromMilliseconds(ms));

    /// <summary>Runs the clock exactly to the current phase's deadline, so the next phase starts at <c>h.Now</c>.</summary>
    private static void EndPhase(GameHarness<RedlineGame> h) => h.Advance(RedlineKit.View(h).DeadlineUtc!.Value - h.Now);

    /// <summary>Runs the clock through the rest of the current qualifying lap's Question and its Result.</summary>
    private static void FinishLap(GameHarness<RedlineGame> h)
    {
        if (h.Game.Phase == RacePhase.QualifyingQuestion) EndPhase(h);
        Assert.Equal(RacePhase.QualifyingResult, h.Game.Phase);
        EndPhase(h);
    }

    private static string Snapshot(GameHarness<RedlineGame> h) => JsonSerializer.Serialize(h.Game.HostView(), ArcJson.Options);

    private static string Message(GameHarness<RedlineGame> h, int player) => Assert.IsType<WaitView>(h.PlayerView(player)).Message;

    private static string Header(int lap) => $"QUALIFYING · LAP {lap}/3";

    // ---------------------------------------------------------------- Flow

    [Fact]
    public void DriverSelect_goes_to_qualifying_when_on()
    {
        Assert.True(new RedlineRules().Qualify);                                         // on by default

        var (h, _) = RedlineKit.Start(reading: true, qualify: true);
        RedlineKit.Claim(h, 0, "NOR"); RedlineKit.Claim(h, 1, "VER"); RedlineKit.Claim(h, 2, "LEC");
        Assert.Equal(RacePhase.DriverSelect, h.Game.Phase);
        Assert.Null(RedlineKit.View(h).Qualifying);
        h.Advance(h.Game.Rules.DriverSelect);                                            // player 3 gets a random seat

        var v = RedlineKit.View(h);
        Assert.Equal((RacePhase.QualifyingReading, 0), (v.Phase, v.Round));
        Assert.Equal((1, 3), (v.Qualifying!.Lap, v.Qualifying.Laps));
        Assert.Equal(22, v.Qualifying.Rows.Count);
        Assert.Equal(v.Drivers.Select(d => d.Code), v.Qualifying.Rows.Select(r => r.Code));
        Assert.Equal(Enumerable.Range(1, 22), v.Drivers.Select(d => d.Position));
        Assert.Equal(Enumerable.Range(1, 22), v.Qualifying.Rows.Select(r => r.Position));
        Assert.All(v.Qualifying.Rows, r => Assert.Equal((0, 0, (int?)null, 0), (r.Correct, r.TotalMs, r.LastLapMs, r.PositionChange)));
        Assert.Equal(4, v.Drivers.Count(d => d.Player is not null));
    }

    [Fact]
    public void Qualify_off_keeps_the_seeded_shuffle_grid()
    {
        var (h, q) = RedlineKit.Start(qualify: false);
        RedlineKit.SeatAllAndGoToQualifying(h, "NOR", "VER", "LEC", "ANT");

        var v = RedlineKit.View(h);
        Assert.Equal(RacePhase.Grid, v.Phase);
        Assert.Null(v.Qualifying);
        Assert.Equal(SeededGridSeed1, v.Drivers.Select(d => d.Code));
        Assert.Equal(Enumerable.Range(1, 22), v.Drivers.Select(d => d.Position));
        Assert.Empty(q.DifficultyTakes);
        Assert.Equal("You're Lando Norris, #1, McLaren. Lights out in a moment…", Message(h, 0));
    }

    [Fact]
    public void AI_only_room_skips_qualifying()
    {
        var (on, _) = RedlineKit.Start(players: 0, qualify: true);
        var (off, _) = RedlineKit.Start(players: 0, qualify: false);
        on.Advance(on.Game.Rules.DriverSelect);
        off.Advance(off.Game.Rules.DriverSelect);

        var v = RedlineKit.View(on);
        Assert.Equal(RacePhase.Grid, v.Phase);
        Assert.Null(v.Qualifying);
        Assert.Equal(RedlineKit.View(off).Drivers.Select(d => d.Code), v.Drivers.Select(d => d.Code));   // the same seeded shuffle

        on.Advance(on.Game.Rules.Grid);
        Assert.Equal((RacePhase.Throttle, 1), (on.Game.Phase, on.Game.Round));
    }

    [Fact]
    public void Reading_then_question_then_result_three_times_then_grid()
    {
        var (h, _) = RedlineKit.Start(reading: true, qualify: true);
        RedlineKit.SeatAllAndGoToQualifying(h, "NOR", "VER", "LEC", "ANT");
        var rules = h.Game.Rules;

        for (var lap = 1; lap <= 3; lap++)
        {
            var text = $"Q{15 + lap}";
            var v = RedlineKit.View(h);
            Assert.Equal((RacePhase.QualifyingReading, 0, lap, 3), (v.Phase, v.Round, v.Qualifying!.Lap, v.Qualifying.Laps));
            Assert.Equal(h.Now + rules.ReadingTime(text), v.DeadlineUtc);
            Assert.Equal(text, v.Question!.Text);
            Assert.Equal(4, v.Question.Answers!.Count);
            Assert.Null(v.CorrectIndex);

            h.Advance(rules.ReadingTime(text));
            v = RedlineKit.View(h);
            Assert.Equal((RacePhase.QualifyingQuestion, lap), (v.Phase, v.Qualifying!.Lap));
            Assert.Equal(h.Now + rules.QualifyingAnswer, v.DeadlineUtc);
            Assert.Equal(text, v.Question!.Text);
            Assert.Null(v.CorrectIndex);

            h.Advance(rules.QualifyingAnswer);
            v = RedlineKit.View(h);
            Assert.Equal((RacePhase.QualifyingResult, lap), (v.Phase, v.Qualifying!.Lap));
            Assert.Equal(h.Now + rules.QualifyingResult, v.DeadlineUtc);
            Assert.Equal(text, v.Question!.Text);
            Assert.NotNull(v.CorrectIndex);
            Assert.Equal("right", v.Question.Answers![v.CorrectIndex!.Value][..5]);

            h.Advance(rules.QualifyingResult);
        }

        var grid = RedlineKit.View(h);
        Assert.Equal((RacePhase.Grid, 0), (grid.Phase, grid.Round));
        Assert.Null(grid.Qualifying);
        Assert.Equal(h.Now + rules.Grid, grid.DeadlineUtc);

        h.Advance(rules.Grid);
        var round1 = RedlineKit.View(h);
        Assert.Equal((RacePhase.Throttle, 1), (round1.Phase, round1.Round));
        Assert.Null(round1.Qualifying);
    }

    [Fact]
    public void ReadQuestions_off_skips_QualifyingReading()
    {
        var h = ToQualifying();
        Assert.Equal((RacePhase.QualifyingQuestion, 1), (h.Game.Phase, Quali(h).Lap));
        Assert.Equal(h.Now + h.Game.Rules.QualifyingAnswer, RedlineKit.View(h).DeadlineUtc);
        Assert.IsType<ChoiceView>(h.PlayerView(0));

        FinishLap(h);
        Assert.Equal((RacePhase.QualifyingQuestion, 2), (h.Game.Phase, Quali(h).Lap));
    }

    // ---------------------------------------------------------------- Questions

    [Fact]
    public void Qualifying_uses_three_easy_questions_after_the_race_fifteen()
    {
        var (h, q) = RedlineKit.Start(qualify: true, questions: new FakeQuestionSource(Difficulty.Hard));
        Assert.Equal(1, q.TakeCalls);                                                    // the race's 15, once
        Assert.Equal(new[] { (3, Difficulty.Easy) }, q.DifficultyTakes);
        RedlineKit.SeatAllAndGoToQualifying(h, "NOR", "VER", "LEC", "ANT");

        for (var lap = 1; lap <= 3; lap++)
        {
            var card = RedlineKit.View(h).Question!;
            Assert.Equal(($"Q{15 + lap}", Difficulty.Easy), (card.Text, card.Difficulty));
            Assert.Equal($"Q{15 + lap}", Assert.IsType<ChoiceView>(h.PlayerView(0)).Prompt);
            FinishLap(h);
        }

        h.Advance(h.Game.Rules.Grid);
        var round1 = RedlineKit.View(h).Question!;
        Assert.Equal(("Science & Nature", Difficulty.Hard), (round1.Category, round1.Difficulty));   // the race's own questions
        for (var i = 0; i < 4; i++) RedlineKit.Throttle(h, i, "cruise");
        Assert.Equal("Q1", RedlineKit.View(h).Question!.Text);
    }

    // ---------------------------------------------------------------- Answering

    [Fact]
    public void Question_ends_early_when_every_connected_player_answered()
    {
        var h = ToQualifying();
        RedlineKit.QualiAnswer(h, 0, right: true, 900);
        RedlineKit.QualiAnswer(h, 1, right: false, 900);
        RedlineKit.QualiAnswer(h, 2, right: true, null);
        Assert.Equal(RacePhase.QualifyingQuestion, h.Game.Phase);                       // player 3 still to answer

        h.SetConnected(3, false);                                                        // nobody left to wait for
        Assert.Equal(RacePhase.QualifyingResult, h.Game.Phase);
        Assert.Equal(h.Now + h.Game.Rules.QualifyingResult, RedlineKit.View(h).DeadlineUtc);

        h.Advance(h.Game.Rules.QualifyingResult);
        Assert.Equal((RacePhase.QualifyingQuestion, 2), (h.Game.Phase, Quali(h).Lap));
        for (var i = 0; i < 3; i++) RedlineKit.QualiAnswer(h, i, right: true, 500);       // the answer ends it this time
        Assert.Equal((RacePhase.QualifyingResult, 2), (h.Game.Phase, Quali(h).Lap));
    }

    [Fact]
    public void First_answer_counts_later_ignored()
    {
        var h = ToQualifying(rules: WrongAi);
        var options = Assert.IsType<ChoiceView>(h.PlayerView(0)).Options;
        var wrong = options.First(o => o.Label == "w1").Id;

        Wait(h, 1000);
        RedlineKit.QualiAnswer(h, 0, right: true, 1000);
        var answered = h.PlayerView(0);
        Assert.Equal("Lap time 1.00 s — checking…", Assert.IsType<WaitView>(answered).Message);

        Wait(h, 500);
        h.Input(0, new { selected = new[] { wrong }, elapsedMs = 10.0 });
        h.Input(0, new { selected = new[] { options.First(o => o.Label.StartsWith("right")).Id }, elapsedMs = 1400.0 });
        Assert.Same(answered, h.PlayerView(0));

        FinishLap(h);
        Assert.Equal((RacePhase.QualifyingQuestion, 2), (h.Game.Phase, Quali(h).Lap));
        Assert.Equal((1, 1000), (QRow(h, 0).Correct, QRow(h, 0).TotalMs));
    }

    [Fact]
    public void Answer_after_the_window_is_ignored()
    {
        var h = ToQualifying(rules: WrongAi);
        var right = Assert.IsType<ChoiceView>(h.PlayerView(0)).Options.First(o => o.Label.StartsWith("right")).Id;

        h.Advance(h.Game.Rules.QualifyingAnswer - TimeSpan.FromMilliseconds(50));      // the player taps here…
        Assert.Equal(RacePhase.QualifyingQuestion, h.Game.Phase);
        Wait(h, 50);                                                                     // …but the timer fires first
        Assert.Equal(RacePhase.QualifyingResult, h.Game.Phase);

        var snapshot = Snapshot(h);
        var view = h.PlayerView(0);
        h.Input(0, new { selected = new[] { right }, elapsedMs = 9_960.0 });             // arrives after the window
        Assert.Equal(snapshot, Snapshot(h));
        Assert.Same(view, h.PlayerView(0));

        var row = QRow(h, 0);
        Assert.Equal((0, 0, (int?)null, false), (row.Correct, row.TotalMs, row.LastLapMs, row.FastestLap));
        Assert.StartsWith("✖ Lap deleted · P", Message(h, 0));
    }

    [Fact]
    public void Unparseable_answer_is_ignored()
    {
        var h = ToQualifying(rules: WrongAi);
        Wait(h, 1500);
        var snapshot = Snapshot(h);
        var view = h.PlayerView(0);

        h.Input(0, new { selected = new[] { "e" }, elapsedMs = 100.0 });                // not one of the four
        h.Input(0, new { selected = Array.Empty<string>(), elapsedMs = 100.0 });
        h.Input(0, new { selected = "a" });                                              // JsonException
        h.Input(0, new { nope = 1 });
        h.Input(0, new { pressed = "push" });
        Assert.Equal(snapshot, Snapshot(h));
        Assert.Same(view, h.PlayerView(0));
        Assert.False(QRow(h, 0).Answered);

        RedlineKit.QualiAnswer(h, 0, right: true, 1500);                                 // still free to answer
        Assert.True(QRow(h, 0).Answered);
        FinishLap(h);
        Assert.Equal((1, 1500), (QRow(h, 0).Correct, QRow(h, 0).TotalMs));
    }

    // ---------------------------------------------------------------- Timing

    [Fact]
    public void Phone_time_is_clamped_against_server_time()
    {
        var h = ToQualifying(players: 5, rules: WrongAi);
        Wait(h, 2600);                                                                   // s = 2600 ms for everyone

        RedlineKit.QualiAnswer(h, 0, right: true, 500);                                  // floored at s − 1000
        Assert.Equal("Lap time 1.60 s — checking…", Message(h, 0));
        RedlineKit.QualiAnswer(h, 1, right: true, null);                                 // no phone time: s
        RedlineKit.QualiAnswer(h, 2, right: true, 99_999);                               // capped at s
        RedlineKit.QualiAnswer(h, 3, right: true, 2100);                                 // inside the bounds: the phone's time
        RedlineKit.QualiAnswer(h, 4, right: true, -5);
        Assert.Equal(RacePhase.QualifyingResult, h.Game.Phase);

        Assert.Equal(new int?[] { 1600, 2600, 2600, 2100, 1600 }, Enumerable.Range(0, 5).Select(i => QRow(h, i).LastLapMs));
        Assert.Equal([1600, 2600, 2600, 2100, 1600], Enumerable.Range(0, 5).Select(i => QRow(h, i).TotalMs));
    }

    // ---------------------------------------------------------------- Deleted laps

    [Fact]
    public void Wrong_or_missing_answer_deletes_the_lap()
    {
        var h = ToQualifying(rules: WrongAi);
        Wait(h, 1000);
        RedlineKit.QualiAnswer(h, 0, right: true, null);
        RedlineKit.QualiAnswer(h, 1, right: false, 800);
        h.SetConnected(3, false);                                                        // player 2 just never answers
        EndPhase(h);
        Assert.Equal(RacePhase.QualifyingResult, h.Game.Phase);

        Assert.Equal((1, 1000, (int?)1000), (QRow(h, 0).Correct, QRow(h, 0).TotalMs, QRow(h, 0).LastLapMs));
        foreach (var i in new[] { 1, 2, 3 })
        {
            Assert.Equal((0, 0, (int?)null), (QRow(h, i).Correct, QRow(h, i).TotalMs, QRow(h, i).LastLapMs));
            Assert.Equal($"✖ Lap deleted · P{QRow(h, i).Position} so far", Message(h, i));
        }
        var ai = Quali(h).Rows.Where(r => !Codes[..4].Contains(r.Code)).ToList();
        Assert.Equal(18, ai.Count);
        Assert.All(ai, r => Assert.Equal((0, 0, (int?)null), (r.Correct, r.TotalMs, r.LastLapMs)));   // a wrong AI keeps no time
        Assert.Equal(1, QRow(h, 0).Position);
        Assert.True(QRow(h, 0).FastestLap);

        EndPhase(h);
        RedlineKit.QualiAnswer(h, 0, right: false, 700);                                // lap 2: deleted, lap 1 still counts
        EndPhase(h);
        Assert.Equal((1, 1000, (int?)null, false), (QRow(h, 0).Correct, QRow(h, 0).TotalMs, QRow(h, 0).LastLapMs, QRow(h, 0).FastestLap));
        Assert.DoesNotContain(Quali(h).Rows, r => r.FastestLap);                         // nobody set a time this lap
    }

    // ---------------------------------------------------------------- Secrecy

    [Fact]
    public void Nothing_revealed_before_Result()
    {
        var h = ToQualifying(rules: PerfectAi);
        Wait(h, 1500);
        RedlineKit.QualiAnswer(h, 0, right: true, null);
        RedlineKit.QualiAnswer(h, 1, right: false, null);
        EndPhase(h);
        var lap1 = Quali(h).Rows.ToDictionary(r => r.Code);
        Assert.Contains(lap1.Values, r => r.LastLapMs is not null && r.Code != RedlineKit.Row(h, 0).Code);   // AI set times
        EndPhase(h);

        // Lap 2's question is open. Before anyone answers…
        var before = RedlineKit.View(h);
        Assert.Equal((RacePhase.QualifyingQuestion, 2), (before.Phase, before.Qualifying!.Lap));
        Assert.Null(before.CorrectIndex);
        Assert.Equal(lap1.Values.OrderBy(r => r.Position).Select(r => r.Code), before.Drivers.Select(d => d.Code));
        var humans = Enumerable.Range(0, 4).Select(i => RedlineKit.Row(h, i).Code).ToHashSet();
        foreach (var r in before.Qualifying.Rows)
        {
            Assert.Null(r.LastLapMs);
            Assert.False(r.FastestLap);
            Assert.Equal(0, r.PositionChange);
            Assert.Equal((lap1[r.Code].Correct, lap1[r.Code].TotalMs, lap1[r.Code].Position), (r.Correct, r.TotalMs, r.Position));
            Assert.Equal(!humans.Contains(r.Code), r.Answered);                          // AI answer when the question opens
        }
        Assert.All(before.Drivers, d =>
        {
            Assert.Null(d.AnswerIndex);
            Assert.Null(d.Outcome);
            Assert.Null(d.Throttle);
        });

        // …and after a right and a wrong answer, only those two players' ticks change.
        Wait(h, 800);
        RedlineKit.QualiAnswer(h, 0, right: true, null);
        RedlineKit.QualiAnswer(h, 1, right: false, null);
        var after = RedlineKit.View(h);
        var answered = new[] { RedlineKit.Row(h, 0).Code, RedlineKit.Row(h, 1).Code };
        Assert.Null(after.CorrectIndex);
        Assert.Equal(
            before.Qualifying.Rows.Select(r => answered.Contains(r.Code) ? r with { Answered = true } : r),
            after.Qualifying!.Rows);
        Assert.Equal(
            before.Drivers.Select(d => answered.Contains(d.Code) ? d with { Status = DriverStatus.Answered } : d),
            after.Drivers);
        Assert.All(after.Drivers.Where(d => humans.Contains(d.Code) && !answered.Contains(d.Code)),
            d => Assert.Equal(DriverStatus.Answering, d.Status));

        EndPhase(h);
        var result = RedlineKit.View(h);
        Assert.NotNull(result.CorrectIndex);
        Assert.Equal(800, QRow(h, 0).LastLapMs);
        Assert.All(result.Qualifying!.Rows, r => Assert.False(r.Answered));
    }

    // ---------------------------------------------------------------- Grid order

    /// <summary>
    /// Three players, AI always wrong. The players answer at 1.0, 2.0 and 3.0 s on every lap, the fastest being the one
    /// furthest back in the seeded order. Returns the players in their expected qualifying order and the seeded order.
    /// </summary>
    private static (GameHarness<RedlineGame> H, int[] Order, string[] Seeded) QualifyThree()
    {
        var h = ToQualifying(players: 3, rules: WrongAi);
        var seeded = RedlineKit.View(h).Drivers.Select(d => d.Code).ToArray();
        var order = Enumerable.Range(0, 3).OrderByDescending(i => RedlineKit.Row(h, i).Position).ToArray();
        for (var lap = 1; lap <= 3; lap++)
        {
            foreach (var player in order)
            {
                Wait(h, 1000);
                RedlineKit.QualiAnswer(h, player, right: true, null);
            }
            Assert.Equal(RacePhase.QualifyingResult, h.Game.Phase);
            Assert.Equal(order.Select(i => RedlineKit.Row(h, i).Code), RedlineKit.View(h).Drivers.Take(3).Select(d => d.Code));
            h.Advance(h.Game.Rules.QualifyingResult);
        }
        return (h, order, seeded);
    }

    [Fact]
    public void Grid_order_equals_the_classification()
    {
        var (h, order, seeded) = QualifyThree();
        var grid = RedlineKit.View(h);
        Assert.Equal(RacePhase.Grid, grid.Phase);

        var players = order.Select(i => RedlineKit.Row(h, i).Code).ToArray();
        Assert.Equal([.. players, .. seeded.Except(players)], grid.Drivers.Select(d => d.Code));   // AI: the seeded tie order
        Assert.Equal(Enumerable.Range(1, 22), grid.Drivers.Select(d => d.Position));
        Assert.Equal([1, 2, 3], order.Select(i => RedlineKit.Row(h, i).Position));
        Assert.All(grid.Drivers, d => Assert.Equal(0, d.PositionChange));
        Assert.NotEqual(seeded.Take(3), grid.Drivers.Take(3).Select(d => d.Code));
    }

    [Fact]
    public void Positions_carry_into_round_1_tiebreak()
    {
        var (h, order, _) = QualifyThree();
        var grid = RedlineKit.View(h).Drivers.Select(d => d.Code).ToArray();
        h.Advance(h.Game.Rules.Grid);

        var round1 = RedlineKit.View(h);
        Assert.Equal((RacePhase.Throttle, 1), (round1.Phase, round1.Round));
        Assert.Equal(grid, round1.Drivers.Select(d => d.Code));
        Assert.Equal(Enumerable.Range(1, 22), round1.Drivers.Select(d => d.Position));
        Assert.Equal("P1 · ROUND 1/15", h.PlayerView(order[0]).Header);

        // The two fastest qualifiers tie on distance and correct answers: the qualifying order keeps them apart.
        for (var i = 0; i < 3; i++) RedlineKit.Throttle(h, i, "cruise");
        RedlineKit.AnswerRight(h, order[1]);
        RedlineKit.AnswerRight(h, order[0]);
        RedlineKit.AnswerWrong(h, order[2]);
        Assert.Equal(RacePhase.Reveal, h.Game.Phase);
        var reveal = RedlineKit.View(h);
        Assert.Equal(new[] { order[0], order[1] }.Select(i => RedlineKit.Row(h, i).Code), reveal.Drivers.Take(2).Select(d => d.Code));
        Assert.Equal([1, 1], reveal.Drivers.Take(2).Select(d => d.Position));
    }

    // ---------------------------------------------------------------- Phone views

    [Fact]
    public void Phone_views_follow_spec_4_2()
    {
        // The seeded tie order is today's shuffle (§2.2): HAM's slot in it puts a deleted lap 1 at P14, behind the three
        // correct players.
        var h = ToQualifying(rules: WrongAi, reading: true, codes: ["NOR", "VER", "LEC", "HAM"]);
        Assert.Equal(SeededGridSeed1, RedlineKit.View(h).Drivers.Select(d => d.Code));

        // QualifyingReading: the question, then the note.
        var reading = Assert.IsType<WaitView>(h.PlayerView(0));
        Assert.Equal((Header(1), "Q16\nAnswers in a moment…"), (reading.Header, reading.Message));
        Assert.Equal(RedlineKit.View(h).DeadlineUtc, reading.DeadlineUtc);
        h.Advance(h.Game.Rules.ReadingTime("Q16"));

        // QualifyingQuestion: the question and its four answers.
        var question = Assert.IsType<ChoiceView>(h.PlayerView(0));
        Assert.Equal((Header(1), "Q16"), (question.Header, question.Prompt));
        Assert.Equal(RedlineKit.View(h).Question!.Answers, question.Options.Select(o => o.Label));
        Assert.Equal(["a", "b", "c", "d"], question.Options.Select(o => o.Id));
        Assert.Equal(h.Now + h.Game.Rules.QualifyingAnswer, question.DeadlineUtc);

        Wait(h, 1000); RedlineKit.QualiAnswer(h, 1, right: true, null);
        Wait(h, 1000); RedlineKit.QualiAnswer(h, 2, right: true, null);
        Wait(h, 405); RedlineKit.QualiAnswer(h, 0, right: true, null);                  // 2405 ms: rounds half up to 2.41
        var answered = Assert.IsType<WaitView>(h.PlayerView(0));
        Assert.Equal((Header(1), "Lap time 2.41 s — checking…"), (answered.Header, answered.Message));
        RedlineKit.QualiAnswer(h, 3, right: false, null);

        // QualifyingResult, lap 1.
        Assert.Equal(RacePhase.QualifyingResult, h.Game.Phase);
        var result = Assert.IsType<WaitView>(h.PlayerView(0));
        Assert.Equal((Header(1), "✔ 2.41 s · P3 so far"), (result.Header, result.Message));
        Assert.Equal("✔ 1.00 s · P1 so far · Purple!", Message(h, 1));
        Assert.Equal("✖ Lap deleted · P14 so far", Message(h, 3));

        // Lap 2: only player 0 is right, so they lead and set the fastest lap.
        h.Advance(h.Game.Rules.QualifyingResult);
        Assert.Equal((Header(2), "Q17\nAnswers in a moment…"), (h.PlayerView(0).Header, Message(h, 0)));
        h.Advance(h.Game.Rules.ReadingTime("Q17"));
        Wait(h, 2405); RedlineKit.QualiAnswer(h, 0, right: true, 2405);
        for (var i = 1; i < 4; i++) RedlineKit.QualiAnswer(h, i, right: false, null);
        Assert.Equal((Header(2), "✔ 2.41 s · P1 so far · Purple!"), (h.PlayerView(0).Header, Message(h, 0)));

        // Lap 3: only player 3 is right, slowly: one correct lap, behind 0 (two) and 1 and 2 (one each, faster).
        h.Advance(h.Game.Rules.QualifyingResult);
        h.Advance(h.Game.Rules.ReadingTime("Q18"));
        Wait(h, 5000); RedlineKit.QualiAnswer(h, 3, right: true, null);
        for (var i = 0; i < 3; i++) RedlineKit.QualiAnswer(h, i, right: false, null);
        Assert.Equal((Header(3), "✔ 5.00 s · P4 so far · Purple!"), (h.PlayerView(3).Header, Message(h, 3)));
        Assert.Equal("✖ Lap deleted · P1 so far", Message(h, 0));

        // Grid: the existing message, then the qualifying result.
        h.Advance(h.Game.Rules.QualifyingResult);
        Assert.Equal(RacePhase.Grid, h.Game.Phase);
        Assert.Equal("You're Lewis Hamilton, #44, Ferrari. Lights out in a moment… · Qualified P4", Message(h, 3));
        Assert.Equal("You're Lando Norris, #1, McLaren. Lights out in a moment… · Qualified P1", Message(h, 0));
    }

    [Fact]
    public void Phone_view_changes_only_when_its_state_does()
    {
        var h = ToQualifying(rules: WrongAi);
        var question = h.PlayerView(0);
        RedlineKit.QualiAnswer(h, 1, right: true, null);                                 // someone else answering
        Assert.Same(question, h.PlayerView(0));

        RedlineKit.QualiAnswer(h, 0, right: true, null);
        var answered = h.PlayerView(0);
        Assert.NotSame(question, answered);
        RedlineKit.QualiAnswer(h, 2, right: true, null);
        Assert.Same(answered, h.PlayerView(0));

        RedlineKit.QualiAnswer(h, 3, right: true, null);
        var result = h.PlayerView(0);
        Assert.NotSame(answered, result);
        Wait(h, 1000);
        Assert.Same(result, h.PlayerView(0));

        h.Advance(h.Game.Rules.QualifyingResult);                                        // lap 2's question is a new view
        Assert.NotSame(result, h.PlayerView(0));
        Assert.Equal(Header(2), h.PlayerView(0).Header);
    }

    [Fact]
    public void Reconnect_mid_qualifying_shows_current_lap_and_keeps_laps()
    {
        var h = ToQualifying(rules: WrongAi);
        Wait(h, 1200);
        RedlineKit.QualiAnswer(h, 0, right: true, null);
        for (var i = 1; i < 4; i++) RedlineKit.QualiAnswer(h, i, right: false, null);
        h.Advance(h.Game.Rules.QualifyingResult);

        // Lap 2: player 0 drops before answering; the others' answers end the question without them.
        h.SetConnected(0, false);
        Assert.False(RedlineKit.Row(h, 0).Connected);
        for (var i = 1; i < 4; i++) RedlineKit.QualiAnswer(h, i, right: false, null);
        Assert.Equal((RacePhase.QualifyingResult, 2), (h.Game.Phase, Quali(h).Lap));
        Assert.Equal((1, 1200, (int?)null), (QRow(h, 0).Correct, QRow(h, 0).TotalMs, QRow(h, 0).LastLapMs));
        h.Advance(h.Game.Rules.QualifyingResult);

        // Lap 3: back, in the same seat, looking at the current lap, with lap 1 still on the board.
        h.SetConnected(0, true);
        Assert.Equal("NOR", RedlineKit.Row(h, 0).Code);
        var view = Assert.IsType<ChoiceView>(h.PlayerView(0));
        Assert.Equal((Header(3), "Q18"), (view.Header, view.Prompt));
        Assert.Equal((1, 1200), (QRow(h, 0).Correct, QRow(h, 0).TotalMs));

        // Answers, drops, comes back: still answered, with the same lap time.
        Wait(h, 900);
        RedlineKit.QualiAnswer(h, 0, right: true, 900);
        h.SetConnected(0, false);
        h.SetConnected(0, true);
        Assert.Equal("Lap time 0.90 s — checking…", Message(h, 0));
        for (var i = 1; i < 4; i++) RedlineKit.QualiAnswer(h, i, right: false, null);
        Assert.Equal((RacePhase.QualifyingResult, 3), (h.Game.Phase, Quali(h).Lap));
        Assert.Equal((2, 2100, (int?)900), (QRow(h, 0).Correct, QRow(h, 0).TotalMs, QRow(h, 0).LastLapMs));
    }
}
