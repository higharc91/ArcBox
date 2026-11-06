using Redline.Logic;
using Redline.Presentation.Audio;

namespace Redline.Presentation.Tests.Audio;

/// <summary>
/// Issue #24: when the TV parks REDLINE, only a race cut off mid-phase has its sound stopped (narration, effects); a race
/// that ran to its end keeps today's behaviour, its last line or cork pop ringing out over the results.
/// </summary>
public class RaceInterruptionTests
{
    [Theory]
    [InlineData(RacePhase.Champagne)]   // the podium's last phase
    [InlineData(RacePhase.Chequered)]   // the last phase when nobody takes the podium
    public void A_race_that_ran_to_its_end_is_not_interrupted(RacePhase last) =>
        Assert.False(RaceInterruption.WasInterrupted(last));

    [Fact]
    public void Nothing_on_air_is_not_interrupted() => Assert.False(RaceInterruption.WasInterrupted(null));

    [Theory]
    [InlineData(RacePhase.QualifyingQuestion)]
    [InlineData(RacePhase.Question)]
    [InlineData(RacePhase.Reveal)]
    [InlineData(RacePhase.Anthem)]
    [InlineData(RacePhase.Champagne)]
    [InlineData(RacePhase.Chequered)]
    public void A_server_restart_interrupts_even_the_last_podium_phase(RacePhase phase) =>
        Assert.True(RaceInterruption.WasInterrupted(phase, "Game interrupted: the server restarted."));

    [Fact]
    public void A_normal_podium_result_keeps_the_last_cork_and_line_ringing() =>
        Assert.False(RaceInterruption.WasInterrupted(RacePhase.Champagne, "Ann wins the race"));

    [Fact]
    public void Every_other_phase_is_interrupted()
    {
        var midRace = Enum.GetValues<RacePhase>().Where(p => p is not (RacePhase.Champagne or RacePhase.Chequered)).ToList();

        Assert.Contains(RacePhase.Question, midRace);
        Assert.Contains(RacePhase.Anthem, midRace);
        Assert.All(midRace, p => Assert.True(RaceInterruption.WasInterrupted(p), $"{p}"));
    }
}
