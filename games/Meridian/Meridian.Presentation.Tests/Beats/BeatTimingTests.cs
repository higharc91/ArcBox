using Meridian.Presentation.Beats;

namespace Meridian.Presentation.Tests.Beats;

/// <summary>
/// Review focus 4, from the TV's side: a present TV always finishes inside the server's fallback, so the fallback only
/// ever fires for a missing or stalled TV. A spoken beat keeps a second to spare for the voice to start; a silent one
/// finishes within 85% of its fallback.
/// </summary>
public sealed class BeatTimingTests
{
    private const double VoiceStart = 1.0;

    [Fact]
    public void Every_beat_of_real_games_finishes_inside_its_fallback()
    {
        var kinds = new HashSet<string>();
        foreach (var scenario in AllScenarios.List)
        {
            var game = new Harness(players: 4, seed: 3, scenario).Board();
            var rules = game.Pack.Rules;
            game.PlayUntil(() =>
            {
                if (game.Session.Queue is [var now, ..])
                {
                    var beat = Beat.For(now, rules);
                    kinds.Add(now.Kind);
                    // A text beat: its timeline, plus the voice at the rules' rate.
                    var tv = beat.Seconds + (beat.Speaks ? beat.SpeakSeconds : 0);
                    var spare = beat.Speaks ? VoiceStart : 0.15 * now.Seconds;
                    Assert.True(tv + spare <= now.Seconds + 1e-9, $"{now.Kind} '{now.Title}': the TV takes {tv:0.00} s of {now.Seconds:0.00} s");
                    // The worst case: the voice never reports finishing, so the beat waits its estimate and the grace.
                    var silent = beat.Seconds + beat.SpeakSeconds + Beat.VoiceGrace;
                    Assert.True(!beat.Speaks || silent + 0.25 <= now.Seconds + 1e-9, $"{now.Kind} '{now.Title}': a silent voice takes {silent:0.00} s of {now.Seconds:0.00} s");
                }
                return false;
            }, steps: 6000);
        }
        Assert.Superset(new HashSet<string> { "reveal", "card", "roll", "breach", "scenario", "over" }, kinds);
    }
}
