using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Testing;
using Redline.Logic;

namespace Redline.Logic.Tests;

/// <summary>
/// Issue #25: the TV's <see cref="RedlineGame.NarrationDone"/> {seq} ends a read-out early. The Reading timer stays as the
/// fallback, so with no signal the timing is exactly as before, and a signal can only shorten the wait.
/// </summary>
public class NarrationSignalTests
{
    private static readonly string[] Codes = ["NOR", "VER", "LEC", "ANT"];

    /// <summary>Twelve words: 0.6 s + 12 × 0.36 s = 4.92 s of Reading.</summary>
    private const string TwelveWords = "Which planet in our solar system has the most known moons today?";

    private static GameHarness<RedlineGame> ReadingRound1()
    {
        var (h, _) = RedlineKit.Start(reading: true, questions: new FakeQuestionSource(text: i => i == 1 ? TwelveWords : $"Q{i}"));
        RedlineKit.SeatAllAndGoToRound1(h, Codes);
        for (var i = 0; i < h.Players.Count; i++) RedlineKit.Throttle(h, i, "push");
        Assert.Equal(RacePhase.Reading, h.Game.Phase);
        return h;
    }

    private static int Seq(GameHarness<RedlineGame> h) => RedlineKit.View(h).ReadSeq ?? throw new Xunit.Sdk.XunitException("no readSeq");

    [Fact]
    public void A_signal_for_the_current_read_out_opens_the_question_at_once_with_a_full_answer_clock()
    {
        var h = ReadingRound1();
        h.Advance(TimeSpan.FromSeconds(2));

        h.HostSignal(RedlineGame.NarrationDone, new { seq = Seq(h) });

        Assert.Equal(RacePhase.Question, h.Game.Phase);
        Assert.Equal(h.Now + h.Game.Rules.Question, RedlineKit.View(h).DeadlineUtc);
        // The Reading timer is gone: its old deadline passing doesn't skip the question.
        h.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(RacePhase.Question, h.Game.Phase);
    }

    [Fact]
    public void Without_a_signal_the_read_out_lasts_its_estimate()
    {
        var h = ReadingRound1();
        h.Advance(TimeSpan.FromMilliseconds(4919));
        Assert.Equal(RacePhase.Reading, h.Game.Phase);
        h.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(RacePhase.Question, h.Game.Phase);
    }

    [Fact]
    public void Stale_wrong_or_malformed_signals_are_ignored()
    {
        var h = ReadingRound1();
        var seq = Seq(h);

        h.HostSignal(RedlineGame.NarrationDone, new { seq = seq - 1 });
        h.HostSignal(RedlineGame.NarrationDone, new { seq = seq + 1 });
        h.HostSignal(RedlineGame.NarrationDone, new { seq = "x" });
        h.HostSignal(RedlineGame.NarrationDone, new { });
        h.HostSignal(RedlineGame.NarrationDone, 7);
        h.HostSignal("animationDone", new { seq });

        Assert.Equal(RacePhase.Reading, h.Game.Phase);
    }

    [Fact]
    public void A_duplicate_signal_after_the_question_opened_changes_nothing()
    {
        var h = ReadingRound1();
        var seq = Seq(h);
        h.HostSignal(RedlineGame.NarrationDone, new { seq });
        var deadline = RedlineKit.View(h).DeadlineUtc;

        h.HostSignal(RedlineGame.NarrationDone, new { seq });

        Assert.Equal(RacePhase.Question, h.Game.Phase);
        Assert.Equal(deadline, RedlineKit.View(h).DeadlineUtc);
    }

    [Fact]
    public void Each_read_out_has_its_own_seq_and_other_phases_leave_it_out_of_the_json()
    {
        var h = ReadingRound1();
        var first = Seq(h);
        h.HostSignal(RedlineGame.NarrationDone, new { seq = first });
        Assert.DoesNotContain("readSeq", JsonSerializer.Serialize(h.Game.HostView(), ArcJson.Options));

        for (var i = 0; i < h.Players.Count; i++) RedlineKit.AnswerRight(h, i);
        h.Advance(h.Game.Rules.Reveal);
        for (var i = 0; i < h.Players.Count; i++) RedlineKit.Throttle(h, i, "push");
        Assert.Equal((RacePhase.Reading, 2), (h.Game.Phase, h.Game.Round));
        Assert.Equal(first + 1, Seq(h));

        // Round 1's late signal doesn't end round 2's read-out.
        h.HostSignal(RedlineGame.NarrationDone, new { seq = first });
        Assert.Equal(RacePhase.Reading, h.Game.Phase);
    }

    [Fact]
    public void A_signal_ends_a_qualifying_read_out_too()
    {
        var (h, _) = RedlineKit.Start(reading: true, qualify: true);
        RedlineKit.SeatAllAndGoToQualifying(h, Codes);
        Assert.Equal(RacePhase.QualifyingReading, h.Game.Phase);

        h.HostSignal(RedlineGame.NarrationDone, new { seq = Seq(h) });

        Assert.Equal(RacePhase.QualifyingQuestion, h.Game.Phase);
        Assert.Equal(h.Now + h.Game.Rules.QualifyingAnswer, RedlineKit.View(h).DeadlineUtc);
    }
}
