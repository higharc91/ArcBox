using Redline.Logic;
using Redline.Presentation.Audio;
using Redline.Presentation.Demo;

namespace Redline.Presentation.Tests.Audio;

public class AudioCuesTests
{
    private static readonly IReadOnlyList<DemoStep> Steps = DemoRace.Steps();

    private static RedlineHostView Demo(RacePhase phase) => Steps.Last(s => s.View.Phase == phase).View;

    private static RedlineHostView WithRows(RedlineHostView v, Func<DriverRow, int, DriverRow> change) =>
        v with { Drivers = [.. v.Drivers.Select(change)] };

    [Fact]
    public void Lights_out_when_the_grid_gives_way_to_the_first_throttle()
    {
        Assert.Contains(AudioCues.LightsOut, AudioCues.ForView(Demo(RacePhase.Throttle), Demo(RacePhase.Grid)));
        Assert.DoesNotContain(AudioCues.LightsOut, AudioCues.ForView(Demo(RacePhase.Throttle), Demo(RacePhase.Reveal)));
    }

    [Fact]
    public void Whoosh_and_reading_when_the_question_appears()
    {
        var reading = Demo(RacePhase.Reading);
        var throttle = Demo(RacePhase.Throttle);
        var question = Demo(RacePhase.Question);

        Assert.Equal([AudioCues.Whoosh], AudioCues.ForView(reading, throttle));
        Assert.True(AudioCues.ReadsQuestion(reading, throttle));

        // Reading on: the question was already read and the whoosh played when Reading began.
        Assert.Empty(AudioCues.ForView(question, reading));
        Assert.False(AudioCues.ReadsQuestion(question, reading));

        // Reading off: Throttle goes straight to Question, which is read out then.
        Assert.Equal([AudioCues.Whoosh], AudioCues.ForView(question, throttle));
        Assert.True(AudioCues.ReadsQuestion(question, throttle));
    }

    [Fact]
    public void A_repeated_view_or_a_mid_phase_join_never_reads_the_question()
    {
        var reading = Demo(RacePhase.Reading);
        Assert.False(AudioCues.ReadsQuestion(reading, reading));
        Assert.False(AudioCues.ReadsQuestion(Demo(RacePhase.Question), null));
        Assert.True(AudioCues.ReadsQuestion(reading, null));
    }

    [Fact]
    public void A_tick_when_a_player_locks_a_throttle_or_answers()
    {
        var throttle = WithRows(Demo(RacePhase.Throttle), (d, _) => d.Player is not null ? d with { Status = DriverStatus.Choosing } : d);
        var first = throttle.Drivers.First(d => d.Player is not null).Code;
        var oneMore = WithRows(throttle, (d, _) => d.Code == first ? d with { Status = DriverStatus.Locked } : d);
        Assert.Equal([AudioCues.Lock], AudioCues.ForView(oneMore, throttle));
        Assert.Empty(AudioCues.ForView(throttle, throttle));

        var question = Demo(RacePhase.Question);
        var answered = WithRows(question, (d, _) => d.Player is not null ? d with { Status = DriverStatus.Answered } : d);
        Assert.Equal([AudioCues.Answer], AudioCues.ForView(answered, question));

        // AI answering is silent.
        var aiOnly = WithRows(question, (d, _) => d.Player is null ? d with { Status = DriverStatus.Answered } : d);
        Assert.Empty(AudioCues.ForView(aiOnly, question));
    }

    [Fact]
    public void Reveal_plays_one_global_sting_or_buzz_and_a_cheer_on_a_lead_change()
    {
        var question = Demo(RacePhase.Question);
        var reveal = Demo(RacePhase.Reveal);

        var cues = AudioCues.ForView(reveal, question);
        Assert.Equal(AudioCues.Correct, cues[0]);
        Assert.Equal(cues.Contains(AudioCues.Cheer), reveal.Drivers.Count(d => d.Position == 1) == 1 && reveal.Drivers[0].PositionChange > 0);

        var allWrong = WithRows(reveal, (d, _) => d.Player is not null ? d with { Outcome = RoundOutcome.Wrong } : d);
        Assert.Equal(AudioCues.Wrong, AudioCues.ForView(allWrong, question)[0]);

        var newLeader = WithRows(reveal, (d, i) => i == 0 ? d with { Position = 1, PositionChange = 2 } : d with { Position = Math.Max(2, d.Position) });
        Assert.Contains(AudioCues.Cheer, AudioCues.ForView(newLeader, question));
        var heldLead = WithRows(newLeader, (d, i) => i == 0 ? d with { PositionChange = 0 } : d);
        Assert.DoesNotContain(AudioCues.Cheer, AudioCues.ForView(heldLead, question));
    }

    [Fact]
    public void The_crowd_cheers_when_the_champagne_begins_but_not_for_the_anthem()
    {
        var anthem = Steps.First(s => s.View.Phase == RacePhase.Anthem).View;
        var champagne = Steps.First(s => s.View.Phase == RacePhase.Champagne).View;

        Assert.Empty(AudioCues.ForView(anthem, Demo(RacePhase.Chequered)));
        Assert.Equal([AudioCues.Cheer], AudioCues.ForView(champagne, Demo(RacePhase.Anthem)));
        Assert.Empty(AudioCues.ForView(Demo(RacePhase.Champagne), champagne));
    }

    [Fact]
    public void Every_spray_pops_and_hisses_and_the_first_has_the_sting()
    {
        Assert.Equal([AudioCues.Sting, AudioCues.CorkPop, AudioCues.SprayHiss], AudioCues.ForSpray(first: true));
        Assert.Equal([AudioCues.CorkPop, AudioCues.SprayHiss], AudioCues.ForSpray(first: false));
    }

    [Fact]
    public void Chequered_plays_the_fanfare_and_the_crowd()
    {
        Assert.Equal([AudioCues.Fanfare, AudioCues.Cheer], AudioCues.ForView(Demo(RacePhase.Chequered), Demo(RacePhase.Reveal)));
    }

    private static RedlineHostView Quali(RacePhase phase, int lap, params QualifyingViews.Car[] cars) => QualifyingViews.View(phase, lap, cars);

    [Fact]
    public void AudioCues_qualifying_result_plays_correct_then_purple()
    {
        var question = Quali(RacePhase.QualifyingQuestion, 2, new("NOR", "JO", 1, 2450), new("PIA", null, 1, 2300), new("HUL", "Christabella", 0, 0));
        var purple = Quali(RacePhase.QualifyingResult, 2,
            new("NOR", "JO", 2, 4650, 2200, Fastest: true, Change: 1), new("PIA", null, 2, 4800, 2500), new("HUL", "Christabella", 0, 0, null));

        Assert.Equal([AudioCues.Correct, AudioCues.Purple], AudioCues.ForView(purple, question));
        Assert.Equal("purple", AudioCues.Purple);

        // A purple lap by an AI car is not the players' chime; the players' laps still sound.
        var aiPurple = Quali(RacePhase.QualifyingResult, 2,
            new("PIA", null, 2, 4500, 2100, Fastest: true), new("NOR", "JO", 2, 4650, 2200), new("HUL", "Christabella", 0, 0, null));
        Assert.Equal([AudioCues.Correct], AudioCues.ForView(aiPurple, question));

        // Every player deleted: the buzz, even when an AI was right (and purple).
        var allDeleted = Quali(RacePhase.QualifyingResult, 2,
            new("PIA", null, 2, 4500, 2100, Fastest: true), new("NOR", "JO", 1, 2450, null), new("HUL", "Christabella", 0, 0, null));
        Assert.Equal([AudioCues.Wrong], AudioCues.ForView(allDeleted, question));

        // A player's deleted lap beside another player's good one is still the sting.
        var mixed = Quali(RacePhase.QualifyingResult, 2,
            new("NOR", "JO", 2, 4650, 2200), new("HUL", "Christabella", 0, 0, null));
        Assert.Equal([AudioCues.Correct], AudioCues.ForView(mixed, question));

        // Only on entering the Result.
        Assert.Empty(AudioCues.ForView(purple, purple));
    }

    [Fact]
    public void Qualifying_reads_the_question_with_a_whoosh_like_the_race()
    {
        var result1 = Quali(RacePhase.QualifyingResult, 1, new("NOR", "JO", 1, 2450, 2450), new("PIA", null, 1, 2300, 2300, Fastest: true));
        var reading = Quali(RacePhase.QualifyingReading, 2, new("PIA", null, 1, 2300), new("NOR", "JO", 1, 2450));
        var question = Quali(RacePhase.QualifyingQuestion, 2, new("PIA", null, 1, 2300), new("NOR", "JO", 1, 2450));
        var select = Demo(RacePhase.DriverSelect);

        // Reading on: the whoosh and the read-out come with the Reading, and the Question is silent.
        Assert.Equal([AudioCues.Whoosh], AudioCues.ForView(reading, result1));
        Assert.True(AudioCues.ReadsQuestion(reading, result1));
        Assert.Empty(AudioCues.ForView(question, reading));
        Assert.False(AudioCues.ReadsQuestion(question, reading));

        // Reading off: the Question follows the Result (or Driver Select, for lap 1) and is read out then.
        Assert.Equal([AudioCues.Whoosh], AudioCues.ForView(question, result1));
        Assert.True(AudioCues.ReadsQuestion(question, result1));
        Assert.Equal([AudioCues.Whoosh], AudioCues.ForView(question, select));
        Assert.True(AudioCues.ReadsQuestion(question, select));

        // A repeated view or a TV joining mid-question never reads it.
        Assert.False(AudioCues.ReadsQuestion(question, question));
        Assert.False(AudioCues.ReadsQuestion(question, null));
        Assert.True(AudioCues.ReadsQuestion(reading, null));
    }

    [Fact]
    public void Qualifying_ticks_when_a_player_answers_and_counts_down_the_last_five_seconds()
    {
        var waiting = Quali(RacePhase.QualifyingQuestion, 1, new("NOR", "JO"), new("PIA", null, Answered: true), new("LEC", "Alex"));
        var one = Quali(RacePhase.QualifyingQuestion, 1, new("NOR", "JO", Answered: true), new("PIA", null, Answered: true), new("LEC", "Alex"));

        Assert.Equal([AudioCues.Answer], AudioCues.ForView(one, waiting));
        Assert.Empty(AudioCues.ForView(waiting, waiting));
        Assert.Equal(1, AudioCues.CountdownTicks(RacePhase.QualifyingQuestion, 5.2, 4.9));
        Assert.Equal(0, AudioCues.CountdownTicks(RacePhase.QualifyingReading, 5.2, 4.9));
        Assert.Equal(0, AudioCues.CountdownTicks(RacePhase.QualifyingResult, 3.1, 2.9));
    }

    [Fact]
    public void Driver_select_and_grid_have_no_cues()
    {
        Assert.Empty(AudioCues.ForView(Demo(RacePhase.DriverSelect), null));
        Assert.Empty(AudioCues.ForView(Demo(RacePhase.Grid), Demo(RacePhase.DriverSelect)));
    }

    [Theory]
    [InlineData(RacePhase.Throttle, 6.2, 5.9, 0)]
    [InlineData(RacePhase.Throttle, 5.2, 4.9, 1)]     // crosses 5
    [InlineData(RacePhase.Question, 3.1, 2.9, 1)]
    [InlineData(RacePhase.Question, 5.5, 0.5, 5)]     // a long frame crosses several
    [InlineData(RacePhase.Question, 1.0, 1.0, 0)]
    [InlineData(RacePhase.Question, 0.5, 0, 0)]       // time up is not a tick
    [InlineData(RacePhase.Reading, 3.1, 2.9, 0)]
    [InlineData(RacePhase.Reveal, 3.1, 2.9, 0)]
    public void Countdown_ticks_for_the_last_five_seconds_of_throttle_and_question(RacePhase phase, double before, double after, int ticks)
    {
        Assert.Equal(ticks, AudioCues.CountdownTicks(phase, before, after));
    }
}

public class EngineMixTests
{
    private static RedlineHostView Demo(RacePhase phase) => DemoRace.Steps().Last(s => s.View.Phase == phase).View;

    [Fact]
    public void Engines_follow_the_camera_subjects_at_most_three()
    {
        var reveal = Demo(RacePhase.Reveal);
        Assert.Equal(["LEC", "NOR"], EngineMix.Subjects(new HeliShot(["LEC", "NOR"]), reveal));
        var side = EngineMix.Subjects(new TracksideShot("VER"), reveal);
        Assert.Equal("VER", side[0]);
        Assert.Equal(2, side.Count);
        Assert.Equal(EngineMix.MaxVoices, EngineMix.Subjects(new GridShot(), Demo(RacePhase.Grid)).Count);
        Assert.Empty(EngineMix.Subjects(new LineupShot(null), Demo(RacePhase.DriverSelect)));
        Assert.Empty(EngineMix.Subjects(null, reveal));
    }

    [Fact]
    public void Silent_outside_the_race()
    {
        Assert.Null(EngineMix.Target(RacePhase.DriverSelect, 0, 0, 0));
        Assert.Null(EngineMix.Target(RacePhase.Chequered, 0, 0, 0));
    }

    [Fact]
    public void Subtle_while_players_think_and_roaring_in_the_reveal()
    {
        var thinking = EngineMix.Target(RacePhase.Question, 0, 0, 0)!.Value;
        var reading = EngineMix.Target(RacePhase.Reading, 0, 0, 0)!.Value;
        var flatOut = EngineMix.Target(RacePhase.Reveal, 150, 0, 4)!.Value;
        var smallGain = EngineMix.Target(RacePhase.Reveal, 150, 0, 1)!.Value;
        var stopped = EngineMix.Target(RacePhase.Reveal, 0, 0, 4)!.Value;

        Assert.Equal(thinking, reading);
        Assert.True(thinking.GainDb <= -18, $"thinking {thinking.GainDb} dB");
        Assert.True(flatOut.GainDb > thinking.GainDb + 12);
        Assert.True(flatOut.GainDb > smallGain.GainDb, "the roar scales with the biggest gain");
        Assert.True(flatOut.Rpm > 11000 && flatOut.Load > 0.9);
        Assert.True(stopped.GainDb < flatOut.GainDb && stopped.Load < 0.5);
    }

    [Fact]
    public void Grid_revs_rise_with_the_start_lights()
    {
        var revs = Enumerable.Range(0, 6).Select(lit => EngineMix.Target(RacePhase.Grid, 0, lit, 0)!.Value.Rpm).ToList();
        Assert.Equal(revs.Order(), revs);
        Assert.True(revs[^1] > revs[0] + 4000);
    }

    [Fact]
    public void Sliding_backwards_after_a_spin_still_has_revs()
    {
        var sliding = EngineMix.Target(RacePhase.Reveal, -30, 0, 2)!.Value;
        Assert.True(sliding.Rpm > EngineMix.Target(RacePhase.Reveal, 0, 0, 2)!.Value.Rpm);
    }
}
