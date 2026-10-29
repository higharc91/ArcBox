using System.Numerics;
using ArcBox.Sdk;
using Redline.Logic;
using Redline.Presentation.Demo;
using Redline.Presentation.Hud;

namespace Redline.Presentation.Tests;

public class TowerTests
{
    private static DriverRow Row(string code, int pos, int dist, string? player = null, DriverStatus status = DriverStatus.Ready,
        Throttle? throttle = null, RoundOutcome? outcome = null, int prev = -1, int change = 0, string team = "ferrari") =>
        new(code, 16, team, player is null ? null : new PlayerId(player), player, pos, change, dist, prev < 0 ? dist : prev, status,
            throttle, null, outcome, false, true);

    private static RedlineHostView View(RacePhase phase, params DriverRow[] rows) =>
        new(phase, 4, 15, 28, "cota", null, null, rows, null, null, "credit", Payouts.From(new RedlineRules()));

    [Fact]
    public void Gap_reads_lead_tied_and_minus()
    {
        var rows = Tower.Rows(View(RacePhase.Question, Row("NOR", 1, 10), Row("ANT", 1, 10), Row("VER", 3, 9), Row("BOT", 4, 1)));
        Assert.Equal(["LEAD", "TIED", "−1", "−9"], rows.Select(r => r.Gap));
        Assert.Equal([1, 1, 3, 4], rows.Select(r => r.Position));
    }

    [Fact]
    public void Rows_keep_the_view_ranking_order_as_slots()
    {
        var rows = Tower.Rows(View(RacePhase.Question, Row("NOR", 1, 10), Row("ANT", 1, 10), Row("VER", 3, 9)));
        Assert.Equal(["NOR", "ANT", "VER"], rows.Select(r => r.Code));
        Assert.Equal([0, 1, 2], rows.Select(r => r.Slot));
    }

    [Fact]
    public void Grid_and_driver_select_have_no_distance_gaps()
    {
        Assert.Equal("GRID", Tower.Rows(View(RacePhase.Grid, Row("NOR", 1, 0)))[0].Gap);
        Assert.Equal("", Tower.Rows(View(RacePhase.DriverSelect, Row("NOR", 1, 0)))[0].Gap);
    }

    [Fact]
    public void Throttle_phase_shows_status_never_the_throttle()
    {
        var view = View(RacePhase.Throttle,
            Row("NOR", 1, 5, "JO", DriverStatus.Locked, throttle: Throttle.Redline),
            Row("VER", 2, 4, "SAM", DriverStatus.Choosing),
            Row("LEC", 3, 3, "ALEX", DriverStatus.Limited));
        Assert.Equal(
            [new Chip("LOCKED", ChipKind.Locked), new Chip("CHOOSING", ChipKind.Wait), new Chip("PIT", ChipKind.Pit)],
            Tower.Rows(view).Select(r => r.Chip!.Value));
    }

    [Fact]
    public void Question_phase_shows_throttles_and_answer_marks()
    {
        var rows = Tower.Rows(View(RacePhase.Question,
            Row("NOR", 1, 5, "JO", DriverStatus.Answered, Throttle.Cruise),
            Row("VER", 2, 4, "SAM", DriverStatus.Answering, Throttle.Push),
            Row("LEC", 3, 3, null, DriverStatus.Answered, Throttle.Redline)));
        Assert.Equal(["CRUISE", "PUSH", "REDLINE"], rows.Select(r => r.Chip!.Value.Text));
        Assert.Equal([ChipKind.Cruise, ChipKind.Push, ChipKind.Redline], rows.Select(r => r.Chip!.Value.Kind));
        Assert.Equal([AnswerMark.Answered, AnswerMark.Answering, AnswerMark.Answered], rows.Select(r => r.Mark));
    }

    [Fact]
    public void Reveal_chips_show_the_distance_actually_moved_and_movement()
    {
        var rows = Tower.Rows(View(RacePhase.Reveal,
            Row("LEC", 1, 13, "ALEX", throttle: Throttle.Redline, outcome: RoundOutcome.Correct, prev: 9, change: 2),
            Row("NOR", 2, 10, "JO", throttle: Throttle.Cruise, outcome: RoundOutcome.Wrong, change: -1),
            Row("PIA", 3, 8, null, throttle: Throttle.Push, outcome: RoundOutcome.NoAnswer, prev: 9),
            Row("VER", 4, 7, "SAM", throttle: Throttle.Redline, outcome: RoundOutcome.SpunOut, prev: 9, change: -2),
            Row("BOT", 5, 0, null, throttle: Throttle.Redline, outcome: RoundOutcome.SpunOut, prev: 1)));
        Assert.Equal(["REDLINE +4", "MISS · 0", "MISS −1", "SPUN −2", "SPUN −1"], rows.Select(r => r.Chip!.Value.Text));
        Assert.Equal([ChipKind.Good, ChipKind.Bad, ChipKind.Bad, ChipKind.Spin, ChipKind.Spin], rows.Select(r => r.Chip!.Value.Kind));
        Assert.Equal([2, -1, 0, -2, 0], rows.Select(r => r.Movement));
    }

    [Fact]
    public void Reading_phase_shows_the_throttles_without_answer_marks()
    {
        var view = View(RacePhase.Reading,
            Row("LEC", 1, 9, "ALEX", DriverStatus.Ready, Throttle.Push),
            Row("VER", 2, 8, null, DriverStatus.Ready, Throttle.Redline));
        var rows = Tower.Rows(view);

        Assert.Equal(["PUSH", "REDLINE"], rows.Select(r => r.Chip?.Text));
        Assert.All(rows, r => Assert.Equal(AnswerMark.None, r.Mark));
        Assert.Equal(["LEAD", "−1"], rows.Select(r => r.Gap));
    }

    [Fact]
    public void Movement_is_zero_outside_reveal()
    {
        Assert.Equal(0, Tower.Rows(View(RacePhase.Question, Row("NOR", 1, 5, change: 3)))[0].Movement);
    }

    [Fact]
    public void Humans_get_upper_case_names_and_ai_none()
    {
        var rows = Tower.Rows(View(RacePhase.Question, Row("HUL", 1, 5, "Christabella"), Row("BOT", 2, 4)));
        Assert.Equal("CHRISTABELLA", rows[0].PlayerName);
        Assert.True(rows[0].Human);
        Assert.Null(rows[1].PlayerName);
    }

    [Theory]
    [InlineData("mclaren", "#ff8000")]
    [InlineData("racing-bulls", "#e3102f")]     // white car: stripe colour
    [InlineData("cadillac", "#b9bec4")]
    [InlineData("red-bull", "#1b2550")]
    [InlineData("nobody", "#888888")]
    public void Team_accent_follows_the_mockup_rule(string team, string hex) => Assert.Equal(hex, TeamAccent.ById(team));

    [Fact]
    public void Tower_qualifying_shows_total_lap_chip_purple_and_deleted()
    {
        var result = QualifyingViews.View(RacePhase.QualifyingResult, 2,
            new("NOR", "JO", Correct: 2, TotalMs: 4650, LastMs: 2200, Fastest: true, Change: 1),
            new("PIA", null, Correct: 2, TotalMs: 4800, LastMs: 2500, Change: -1),
            new("HUL", "Christabella", Correct: 1, TotalMs: 2900, LastMs: null),
            new("BOT", null, Correct: 0, TotalMs: 0, LastMs: null));

        var rows = Tower.Rows(result);

        Assert.Equal(["4.65", "4.80", "2.90 · 1/2", "—"], rows.Select(r => r.Gap));
        Assert.Equal([new Chip("2.20", ChipKind.Purple), new Chip("2.50", ChipKind.Good),
            new Chip("DELETED", ChipKind.Bad), new Chip("DELETED", ChipKind.Bad)], rows.Select(r => r.Chip!.Value));
        Assert.Equal([1, -1, 0, 0], rows.Select(r => r.Movement));
        Assert.Equal([1, 2, 3, 4], rows.Select(r => r.Position));
        Assert.Equal(["JO", null, "CHRISTABELLA", null], rows.Select(r => r.PlayerName));
        Assert.All(rows, r => Assert.Equal(AnswerMark.None, r.Mark));

        // Before the Result there is no lap chip and no movement; the question shows who has answered.
        var question = Tower.Rows(QualifyingViews.View(RacePhase.QualifyingQuestion, 2,
            new("NOR", "JO", 1, 2450, Answered: true), new("PIA", null, 1, 2300, Answered: true), new("HUL", "Christabella", 0, 0)));
        Assert.All(question, r => Assert.Null(r.Chip));
        Assert.Equal([AnswerMark.Answered, AnswerMark.Answered, AnswerMark.Answering], question.Select(r => r.Mark));
        Assert.Equal(["2.45", "2.30", "—"], question.Select(r => r.Gap));

        var reading = Tower.Rows(QualifyingViews.View(RacePhase.QualifyingReading, 1, new("NOR", "JO"), new("PIA")));
        Assert.All(reading, r => Assert.Null(r.Chip));
        Assert.All(reading, r => Assert.Equal(AnswerMark.None, r.Mark));
        Assert.All(reading, r => Assert.Equal(0, r.Movement));
    }

    [Fact]
    public void Tower_qualifying_total_shows_the_correct_lap_count_after_a_deleted_lap()
    {
        QualifyingViews.Car[] cars =
        [
            new("NOR", "JO", Correct: 3, TotalMs: 7050, LastMs: 2400),
            new("LAW", null, Correct: 3, TotalMs: 9510, LastMs: 3120),
            new("HUL", "Christabella", Correct: 2, TotalMs: 5900, LastMs: 2900),
            new("STR", null, Correct: 1, TotalMs: 3000, LastMs: null),
            new("BOT", null, Correct: 0, TotalMs: 0, LastMs: null),
        ];
        // Lap 3's Result: three laps completed.
        Assert.Equal(["7.05", "9.51", "5.90 · 2/3", "3.00 · 1/3", "—"],
            Tower.Rows(QualifyingViews.View(RacePhase.QualifyingResult, 3, cars)).Select(r => r.Gap));
        // Lap 3's question and reading: only two laps completed so far, so STR's single correct lap is 1/2.
        var asked = cars.Select(c => c with { Correct = Math.Min(c.Correct, 2), TotalMs = c.Code == "STR" ? 3000 : c.TotalMs }).ToArray();
        foreach (var phase in new[] { RacePhase.QualifyingReading, RacePhase.QualifyingQuestion })
        {
            Assert.Equal(["7.05", "9.51", "5.90", "3.00 · 1/2", "—"],
                Tower.Rows(QualifyingViews.View(phase, 3, asked)).Select(r => r.Gap));
        }
        // Before lap 1's Result nothing is completed: no counts, only "—".
        Assert.Equal(Tower.NoTime, QualifyingText.TotalColumn(new QualifyingRow("NOR", 1, 0, 0, 0, null, false, false), RacePhase.QualifyingReading, 1));
    }

    [Fact]
    public void Tower_gap_text_is_empty_in_the_qualifying_phases_without_a_qualifying_board()
    {
        foreach (var phase in new[] { RacePhase.QualifyingReading, RacePhase.QualifyingQuestion, RacePhase.QualifyingResult })
        {
            var view = View(phase, Row("NOR", 1, 0), Row("ANT", 2, 0));
            Assert.Equal(["", ""], Tower.Rows(view).Select(r => r.Gap));
            Assert.All(Tower.Rows(view), r => Assert.Null(r.Chip));
        }
    }
}

public class QualifyingTextTests
{
    [Theory]
    [InlineData(2405, "2.41")]
    [InlineData(6815, "6.82")]
    [InlineData(2404, "2.40")]
    [InlineData(2395, "2.40")]
    [InlineData(999, "1.00")]
    [InlineData(10000, "10.00")]
    [InlineData(5, "0.01")]
    [InlineData(4, "0.00")]
    [InlineData(0, "0.00")]
    public void QualifyingText_formats_times_to_hundredths_rounding_half_up(int ms, string text)
    {
        Assert.Equal(text, QualifyingText.Lap(ms));
        Assert.Equal(text, QualifyingText.Total(ms));
    }

    [Fact]
    public void Times_use_the_invariant_culture()
    {
        var before = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Equal("2.41", QualifyingText.Lap(2405));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void Header_names_the_lap()
    {
        Assert.Equal("QUALIFYING · LAP 1/3", QualifyingText.Header(1, 3));
        Assert.Equal("QUALIFYING · LAP 3/3", QualifyingText.Header(3, 3));
    }

    [Fact]
    public void Provisional_pole_is_the_new_leader_only_when_p1_changed_at_a_result()
    {
        var before = QualifyingViews.View(RacePhase.QualifyingQuestion, 2, new("PIA", null, 1, 2300), new("NOR", "JO", 1, 2450));
        var changed = QualifyingViews.View(RacePhase.QualifyingResult, 2,
            new("NOR", "JO", 2, 4650, 2200, Fastest: true, Change: 1), new("PIA", null, 2, 4800, 2500, Change: -1));
        var held = QualifyingViews.View(RacePhase.QualifyingResult, 2,
            new("PIA", null, 2, 4700, 2400), new("NOR", "JO", 2, 4900, 2450));

        Assert.Equal("JO", QualifyingText.ProvisionalPole(changed, before));
        Assert.Null(QualifyingText.ProvisionalPole(held, before));
        // An AI leader is named by surname.
        Assert.Equal("Piastri", QualifyingText.ProvisionalPole(held, changed with { Phase = RacePhase.QualifyingQuestion }));
        // Only a Result announces; without a previous view the leader's own position change says it.
        Assert.Null(QualifyingText.ProvisionalPole(before, null));
        Assert.Equal("JO", QualifyingText.ProvisionalPole(changed, null));
        Assert.Null(QualifyingText.ProvisionalPole(held, null));
    }

    [Fact]
    public void Pole_is_the_leader_with_team_and_total()
    {
        var last = QualifyingViews.View(RacePhase.QualifyingResult, 3,
            new("NOR", "JO", 3, 7050, 2400), new("PIA", null, 3, 7400, 2600));
        var ai = QualifyingViews.View(RacePhase.QualifyingResult, 3, new("PIA", null, 3, 7400, 2600), new("NOR", "JO", 3, 7500, 2400));

        Assert.Equal(("JO", "mclaren", "7.05"), QualifyingText.Pole(last));
        Assert.Equal(("Piastri", "mclaren", "7.40"), QualifyingText.Pole(ai));
        // Nobody set a time: no pole to announce.
        Assert.Null(QualifyingText.Pole(QualifyingViews.View(RacePhase.QualifyingResult, 3, new("NOR", "JO"), new("PIA"))));
    }
}

public class QualifyingHudTests
{
    private static RedlineHostView View(RacePhase phase, int lap) =>
        QualifyingViews.View(phase, lap, new("NOR", "JO", 1, 2450), new("PIA", null, 1, 2300));

    [Fact]
    public void Header_says_qualifying_lap_with_the_question_as_the_title()
    {
        var reading = HudText.Header(View(RacePhase.QualifyingReading, 2));
        Assert.Equal(["QUALIFYING · LAP 2/3", "GEOGRAPHY"], reading.Meta);
        Assert.Equal(("EASY", 1, "READING", "LISTEN"), (reading.Difficulty, reading.Pips, reading.ClockCaption, reading.ClockText));
        Assert.Equal(QualifyingViews.Card.Text, reading.Title);

        var question = HudText.Header(View(RacePhase.QualifyingQuestion, 3));
        Assert.Equal(["QUALIFYING · LAP 3/3", "GEOGRAPHY"], question.Meta);
        Assert.Equal(("TIME", (string?)null, QualifyingViews.Card.Text), (question.ClockCaption, question.ClockText, question.Title));

        var result = HudText.Header(View(RacePhase.QualifyingResult, 1));
        Assert.Equal(["QUALIFYING · LAP 1/3", "GEOGRAPHY"], result.Meta);
        Assert.Equal("NEXT", result.ClockCaption);

        // No question yet: the race's own placeholders.
        var bare = View(RacePhase.QualifyingReading, 1) with { Question = null };
        var bareHeader = HudText.Header(bare);
        Assert.Equal(("Listen…", 0), (bareHeader.Title, bareHeader.Pips));
        Assert.Equal(["QUALIFYING · LAP 1/3"], bareHeader.Meta);
    }

    [Fact]
    public void The_clock_runs_the_qualifying_phases_at_their_own_lengths()
    {
        var rules = new RedlineRules();
        Assert.Equal(rules.QualifyingAnswer.TotalSeconds, HudClock.PhaseSeconds(RacePhase.QualifyingQuestion));
        Assert.Equal(rules.QualifyingResult.TotalSeconds, HudClock.PhaseSeconds(RacePhase.QualifyingResult));
        // Without the view, the longest: a 10 s read-out plus lap 1's room for the opener.
        Assert.Equal(HudClock.MaxReadingSeconds + rules.QualifyingIntro.TotalSeconds, HudClock.PhaseSeconds(RacePhase.QualifyingReading));
        // Lap 1 reads its question after the opener; laps 2 and 3 only read it.
        var reading = rules.ReadingTime(QualifyingViews.Card.Text!).TotalSeconds;
        Assert.Equal(reading + rules.QualifyingIntro.TotalSeconds, HudClock.PhaseSeconds(View(RacePhase.QualifyingReading, 1)));
        Assert.Equal(reading, HudClock.PhaseSeconds(View(RacePhase.QualifyingReading, 2)));
    }
}

public class HudClockTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Remaining_clamps_to_the_phase_length_and_zero()
    {
        Assert.Equal(14, HudClock.Remaining(Now.AddSeconds(14), Now, 20));
        Assert.Equal(20, HudClock.Remaining(Now.AddSeconds(95), Now, 20));    // TV clock behind the server
        Assert.Equal(0, HudClock.Remaining(Now.AddSeconds(-3), Now, 20));     // TV clock ahead
        Assert.Null(HudClock.Remaining(null, Now, 20));
    }

    [Theory]
    [InlineData(14.0, "0:14")]
    [InlineData(13.2, "0:14")]
    [InlineData(0.01, "0:01")]
    [InlineData(0, "0:00")]
    [InlineData(-2, "0:00")]
    [InlineData(75, "1:15")]
    public void Format_rounds_up(double seconds, string text) => Assert.Equal(text, HudClock.Format(seconds));

    [Fact]
    public void Reading_length_comes_from_the_question_text()
    {
        var rules = new RedlineRules();
        var view = DemoRace.Steps().Last(s => s.View.Phase == RacePhase.Reading).View;

        Assert.Equal(rules.ReadingTime(view.Question!.Text!).TotalSeconds, HudClock.PhaseSeconds(view));
        Assert.Equal(20, HudClock.PhaseSeconds(view with { Phase = RacePhase.Question }));
        // Without the text, the longest reading time (so a clamped deadline is never cut short).
        Assert.Equal(10, HudClock.PhaseSeconds(RacePhase.Reading));
        Assert.Equal(10, HudClock.PhaseSeconds(view with { Question = null }));
    }

    [Fact]
    public void Phase_lengths_come_from_the_rules()
    {
        Assert.Equal(20, HudClock.PhaseSeconds(RacePhase.Question));
        Assert.Equal(10, HudClock.PhaseSeconds(RacePhase.Throttle));
        Assert.Equal(7, HudClock.PhaseSeconds(RacePhase.Reveal));
        Assert.Equal(10, HudClock.PhaseSeconds(RacePhase.Chequered));
        Assert.Equal(16, HudClock.PhaseSeconds(RacePhase.Anthem));
        Assert.Equal(16, HudClock.PhaseSeconds(RacePhase.Champagne));
        Assert.Equal(12, HudClock.PhaseSeconds(RacePhase.Champagne, new RedlineRules { Champagne = TimeSpan.FromSeconds(12) }));
        Assert.Equal(0.5, HudClock.Fraction(10, 20));
        Assert.Equal(1, HudClock.Fraction(30, 20));
    }
}

public class HudTextTests
{
    private static RedlineHostView Demo(RacePhase phase) => DemoRace.Steps().Last(s => s.View.Phase == phase).View;

    [Fact]
    public void Rules_strip_uses_the_payouts_in_the_view()
    {
        var items = HudText.Rules(new Payouts(1, 3, 1, 4, 2));
        Assert.Equal(["CRUISE", "PUSH", "REDLINE"], items.Select(i => i.Chip.Text));
        Assert.Equal(["+1 / 0", "+3 / −1", "+4 / −2 + pit limiter"], items.Select(i => i.Payout));

        var tuned = HudText.Rules(new Payouts(2, 5, 3, 7, 4));
        Assert.Equal(["+2 / 0", "+5 / −3", "+7 / −4 + pit limiter"], tuned.Select(i => i.Payout));
        Assert.Equal(["CRUISE +2 / 0", "PUSH +5 / −3", "REDLINE +7 / SPIN"], HudText.ThrottleLegend(new Payouts(2, 5, 3, 7, 4)).Select(c => c.Text));
    }

    [Fact]
    public void Race_line_uses_finish_line_and_rounds()
    {
        Assert.Equal("First to 28 or most distance after 15 rounds", HudText.RaceLine(Demo(RacePhase.Grid)));
    }

    [Fact]
    public void Headers_per_phase()
    {
        var q = HudText.Header(Demo(RacePhase.Question));
        Assert.Equal(["ROUND 4/15", "GEOGRAPHY"], q.Meta);
        Assert.Equal(("MEDIUM", 2), (q.Difficulty, q.Pips));
        Assert.Equal("Which U.S. state has the longest coastline?", q.Title);
        Assert.Equal("Choose your throttle", HudText.Header(Demo(RacePhase.Throttle)).Title);
        Assert.Equal("NEXT", HudText.Header(Demo(RacePhase.Reveal)).ClockCaption);
        var grid = HudText.Header(Demo(RacePhase.Grid));
        Assert.Equal("CIRCUIT OF THE AMERICAS", grid.Meta[0]);
        Assert.Equal("START", grid.ClockCaption);
        Assert.Null(q.ClockText);
    }

    [Fact]
    public void Podium_headers_name_the_anthem_country_then_the_champagne()
    {
        var chequered = Demo(RacePhase.Chequered);
        var podium = new Podium("Monaco", "mc", [new PodiumStep(1, new PlayerId("p1"), "ALEX", "LEC", "ferrari", 1, 0, [])]);

        var anthem = HudText.Header(chequered with { Phase = RacePhase.Anthem, Finish = null, Podium = podium });
        Assert.Equal(["PODIUM · ROUND 11/15"], anthem.Meta);
        Assert.Equal("National anthem · Monaco", anthem.Title);
        Assert.Equal("", anthem.ClockCaption);

        var champagne = HudText.Header(chequered with { Phase = RacePhase.Champagne, Finish = null, Podium = podium });
        Assert.Equal(["PODIUM · ROUND 11/15"], champagne.Meta);
        Assert.Equal("Champagne!", champagne.Title);
    }

    [Fact]
    public void Reading_header_shows_the_question_and_listen_instead_of_the_clock()
    {
        var reading = HudText.Header(Demo(RacePhase.Reading));

        Assert.Equal(["ROUND 4/15", "GEOGRAPHY"], reading.Meta);
        Assert.Equal(("MEDIUM", 2), (reading.Difficulty, reading.Pips));
        Assert.Equal("Which U.S. state has the longest coastline?", reading.Title);
        Assert.Equal("LISTEN", reading.ClockText);
        Assert.Equal("READING", reading.ClockCaption);
    }

    [Fact]
    public void Locked_line_counts_humans()
    {
        var view = Demo(RacePhase.Throttle);
        var humans = view.Drivers.Where(d => d.Player is not null).ToList();
        var locked = humans.Count(d => d.Status == DriverStatus.Locked);
        Assert.Equal($"{locked} OF {humans.Count} LOCKED · QUESTION DROPS WHEN ALL DRIVERS ARE IN", HudText.LockedLine(view));
    }

    [Fact]
    public void Chequered_headline_names_a_human_winner_by_player_and_an_ai_by_surname()
    {
        var view = Demo(RacePhase.Chequered);
        Assert.Equal("JO WINS", HudText.Headline(view));
        Assert.Equal("Lando Norris · McLaren · 29", HudText.WinnerLine(view));

        var ai = view with { Drivers = [view.Drivers[0] with { Player = null, PlayerName = null }, .. view.Drivers.Skip(1)] };
        Assert.Equal("NORRIS WINS", HudText.Headline(ai));
        var tie = view with { Drivers = [view.Drivers[0], view.Drivers[1] with { Position = 1 }, .. view.Drivers.Skip(2)] };
        Assert.Equal("DEAD HEAT", HudText.Headline(tie));
    }

    [Fact]
    public void Awards_are_upper_cased_with_the_winner_car()
    {
        var cards = HudText.Awards(Demo(RacePhase.Chequered));
        Assert.Equal(["BOLDEST DRIVER", "SHARPEST MIND", "BIGGEST CRASH"], cards.Select(c => c.Title));
        Assert.Equal(["RIA", "ALEX", "SAM"], cards.Select(c => c.Name));
        Assert.Equal(["ANT", "LEC", "VER"], cards.Select(c => c.Code));
        Assert.Equal("5 Redlines", cards[0].Detail);
        Assert.Empty(HudText.Awards(Demo(RacePhase.Reveal)));
    }

    [Fact]
    public void Surname_handles_accents()
    {
        Assert.Equal("Hülkenberg", HudText.Surname("HUL"));
        Assert.Equal("XYZ", HudText.Surname("XYZ"));
    }
}

public class RoundResultTests
{
    [Fact]
    public void Demo_reveal_card_is_the_spun_human_like_the_camera()
    {
        var view = DemoRace.Steps().Single(s => s.View.Phase == RacePhase.Reveal).View;
        var card = RoundResult.For(view)!;
        var shot = Assert.IsType<TracksideShot>(CameraDirector.Pick(view, null));
        Assert.Equal(shot.Subject, card.Code);
        Assert.Equal("SPUN OUT · SAM", card.Kicker);
        Assert.Equal("VERSTAPPEN", card.Surname);
        Assert.Equal("REDLINE GAMBLE", card.Caption);
        Assert.Equal("−2", card.Delta);
        Assert.False(card.Good);
        var row = view.Drivers.Single(d => d.Code == "VER");
        Assert.Equal((row.Position + row.PositionChange, row.Position), (card.From, card.To));
    }

    [Fact]
    public void Without_a_spin_the_card_is_the_biggest_gainer()
    {
        var view = DemoRace.Steps().Single(s => s.View.Phase == RacePhase.Reveal).View;
        var calm = view with
        {
            Drivers = [.. view.Drivers.Select(d => d.Outcome == RoundOutcome.SpunOut ? d with { Outcome = RoundOutcome.Wrong } : d)],
        };
        var card = RoundResult.For(calm)!;
        Assert.Equal("ANT", card.Code);                // Redline +4, the best-placed of the biggest human gains
        Assert.Equal("BIGGEST MOVER · RIA", card.Kicker);
        Assert.Equal("REDLINE PAYS OFF", card.Caption);
        Assert.Equal("+4", card.Delta);
        Assert.True(card.Good);
    }

    /// <summary>
    /// Found in the TV run: a player's +1 Cruise was the biggest human gain, but the AI gained more, so they dropped
    /// P7 → P12 under a "BIGGEST MOVER" kicker.
    /// </summary>
    [Fact]
    public void A_gain_that_still_loses_places_is_not_the_biggest_mover()
    {
        var view = DemoRace.Steps().Single(s => s.View.Phase == RacePhase.Reveal).View;
        var calm = view with
        {
            Drivers = [.. view.Drivers.Select(d => d.Outcome == RoundOutcome.SpunOut ? d with { Outcome = RoundOutcome.Wrong } : d)],
        };
        var subject = RoundResult.For(calm)!.Code;
        var dropped = calm with
        {
            Drivers = [.. calm.Drivers.Select(d => d.Code == subject ? d with { Position = d.Position + 5, PositionChange = -5 } : d)],
        };

        var card = RoundResult.For(dropped)!;

        Assert.Equal(subject, card.Code);
        Assert.Equal("ROUND RESULT · RIA", card.Kicker);
        Assert.Equal("REDLINE PAYS OFF", card.Caption);
        Assert.Equal(-5, card.Change);
    }

    [Fact]
    public void No_card_outside_reveal() =>
        Assert.Null(RoundResult.For(DemoRace.Steps().Single(s => s.View.Phase == RacePhase.Question).View));
}

public class DriverSelectBoardTests
{
    private static IReadOnlyList<RedlineHostView> Select() =>
        [.. DemoRace.Steps().Where(s => s.View.Phase == RacePhase.DriverSelect).Select(s => s.View)];

    [Fact]
    public void Board_has_eleven_teams_of_two_in_team_order()
    {
        var teams = DriverSelectBoard.Teams(Select()[^1]);
        Assert.Equal(11, teams.Count);
        Assert.All(teams, t => Assert.Equal(2, t.Seats.Count));
        Assert.Equal("MCLAREN", teams[0].Name);
        Assert.Equal(["NOR", "PIA"], teams[0].Seats.Select(s => s.Code));
        Assert.Equal("JO", teams[0].Seats[0].PlayerName);
        Assert.False(teams[0].Seats[1].Claimed);
        Assert.Equal("15 SEATS LEFT · AI TAKES THE REST", DriverSelectBoard.SeatsLeft(Select()[^1]));
    }

    [Fact]
    public void Claim_card_follows_the_newest_claim_and_holds_it()
    {
        var v = Select();
        Assert.Null(DriverSelectBoard.Claim(v[0], null, null));
        var first = DriverSelectBoard.Claim(v[1], v[0], null)!;
        Assert.Equal(("LEC", 16, "ALEX CLAIMED", "CHARLES LECLERC", "FERRARI", "SF-26"),
            (first.Code, first.Number, first.Kicker, first.DriverName, first.TeamName, first.Chassis));
        Assert.Equal("#ffffff", first.NumberInk);

        Assert.Equal("NOR", DriverSelectBoard.Claim(v[2], v[1], first)!.Code);    // NOR is first in team order
        Assert.Equal("LEC", DriverSelectBoard.Claim(v[1], v[1], first)!.Code);     // nothing new: keep showing
        Assert.Null(DriverSelectBoard.Claim(v[1] with { Phase = RacePhase.Grid }, v[1], first));
    }
}

public class NameTagLayoutTests
{
    private static TagInput Tag(string code, double x, double y, bool behind = false, double dist = 40) =>
        new(code, x, y, behind, dist, 160, 30);

    [Fact]
    public void Behind_offscreen_far_and_occluded_tags_hide()
    {
        var occluder = new ScreenRect(0, 0, 400, 1080);
        var tags = NameTagLayout.Layout(
            [Tag("A", 900, 500), Tag("B", 900, 500, behind: true), Tag("C", 2100, 500), Tag("D", 900, 500, dist: 900), Tag("E", 200, 600)],
            1920, 1080, [occluder]);
        Assert.Equal([true, false, false, false, false], tags.Select(t => t.Visible));
    }

    [Fact]
    public void Tag_sits_above_its_anchor_with_a_stem()
    {
        var t = NameTagLayout.Layout([Tag("A", 900, 500)], 1920, 1080, [])[0];
        Assert.Equal(new ScreenRect(820, 500 - NameTagLayout.Stem - 30, 160, 30), t.Rect);
    }

    [Fact]
    public void Overlapping_tags_stack_with_the_nearest_car_on_its_spot()
    {
        var tags = NameTagLayout.Layout([Tag("FAR", 930, 505, dist: 60), Tag("NEAR", 900, 500, dist: 30)], 1920, 1080, []);
        var near = tags.Single(t => t.Code == "NEAR");
        var far = tags.Single(t => t.Code == "FAR");
        Assert.Equal(500 - NameTagLayout.Stem - 30, near.Rect.Y);
        Assert.True(far.Visible);
        Assert.False(far.Rect.Intersects(near.Rect));
        Assert.True(far.Rect.Bottom <= near.Rect.Y);
    }

    [Fact]
    public void A_tag_pushed_into_a_panel_hides()
    {
        var header = new ScreenRect(0, 0, 1920, 150);
        var tags = NameTagLayout.Layout([Tag("NEAR", 900, 210, dist: 30), Tag("FAR", 910, 212, dist: 50)], 1920, 1080, [header]);
        Assert.True(tags[0].Visible);
        Assert.False(tags[1].Visible);
    }
}

public class TrackMapLayoutTests
{
    [Fact]
    public void Fits_inside_the_box_keeping_aspect_and_centred()
    {
        Vector2[] square = [new(0, 0), new(100, 0), new(100, 50), new(0, 50)];
        var map = new TrackMapLayout(square, 420, 250, 10);
        Assert.Equal(4, map.Scale, 3);                                      // limited by width: 400 / 100
        Assert.Equal(new Vector2(10, 25), map.Line[0]);                     // centred vertically: (250 - 200) / 2
        Assert.Equal(new Vector2(410, 225), map.Map(new Vector2(100, 50)));
    }

    [Fact]
    public void Cota_fits_with_the_padding()
    {
        var map = new TrackMapLayout(CircuitData.Load("cota").Points, 420, 250, 16);
        Assert.All(map.Line, p => Assert.InRange(p.X, 15.9f, 404.1f));
        Assert.All(map.Line, p => Assert.InRange(p.Y, 15.9f, 234.1f));
        var (a, b) = map.StartLine(8);
        Assert.Equal(16, Vector2.Distance(a, b), 3);
    }
}

public class StartLightsTests
{
    [Theory]
    [InlineData(RacePhase.Grid, 0.0, 0)]
    [InlineData(RacePhase.Grid, 0.5, 1)]
    [InlineData(RacePhase.Grid, 1.3, 2)]
    [InlineData(RacePhase.Grid, 4.0, 5)]
    [InlineData(RacePhase.Grid, 30, 5)]
    [InlineData(RacePhase.Throttle, 2, 0)]
    public void Lights_come_on_one_by_one_in_grid(RacePhase phase, double t, int lit) => Assert.Equal(lit, StartLights.Lit(phase, t));
}
