using Meridian.Presentation.Art;
using Meridian.Presentation.Audio;
using Meridian.Presentation.Beats;
using Meridian.Presentation.Fx;

namespace Meridian.Presentation.Tests.Audio;

public sealed class SoundPlanTests
{
    private static readonly MeridianRules Rules = BasePack.Pack.Rules;

    private static Presented Now(string kind, double seconds = 1.5, CardKind? deck = null, string? card = null, string? outcome = null,
                                 string text = "")
        => new(1, kind, new SeatId("s1"), "Title", text, seconds, Deck: deck, Target: "s2", Attacker: "s1", Outcome: outcome, Card: card,
               Compartment: "c1");

    private static MeridianHostView View(string phase, ScenarioPanel? panel = null)
        => new(phase, "", [], [], [], 0, 0, 1, null, [], panel);

    private static ScenarioPanel Over(bool crewWon)
        => new("T", 0, [], [], [], Reveal: [new RevealCard("s1", "Crew", crewWon)], Outcome: "Done", CrewWon: crewWon);

    [Fact]
    public void Each_phase_has_its_music_and_the_breach_runs_into_the_endgame()
    {
        Assert.Equal(new MusicCue("music.explore"), SoundPlan.Music(View("boarding"), null));
        Assert.Equal(new MusicCue("music.explore"), SoundPlan.Music(View("explore"), null));
        var breach = Beat.For(Now("breach", 6, text: "Something aboard has woken."), Rules);
        Assert.Equal(new MusicCue("music.breach", "music.endgame"), SoundPlan.Music(View("explore"), breach));
        Assert.Equal(new MusicCue("music.breach", "music.endgame"), SoundPlan.Music(View("briefing"), null));
        Assert.Equal(new MusicCue("music.endgame"), SoundPlan.Music(View("endgame"), null));
        // A TV that connects mid-Breach sees it briefly: no sting before the Breach has fired.
        Assert.Equal(new MusicCue("music.explore"), SoundPlan.Music(View("explore"), Beat.For(breach.Now, Rules, brief: true)));
        // The Breach beat over but the phase not yet moved on: still the sting, never back to the ambience.
        var fired = new ScenarioPanel("T", 10, [], [], []);
        Assert.Equal(new MusicCue("music.breach", "music.endgame"), SoundPlan.Music(View("explore", fired), null));
    }

    [Fact]
    public void The_debrief_plays_the_crews_win_or_loss()
    {
        Assert.Equal("music.debriefWon", SoundPlan.Music(View("debrief", Over(crewWon: true)), null)!.Id);
        Assert.Equal("music.debriefLost", SoundPlan.Music(View("debrief", Over(crewWon: false)), null)!.Id);
    }

    [Fact]
    public void Each_beat_step_has_its_sound()
    {
        string Ids(Presented now) => string.Join(",", SoundPlan.For(Beat.For(now, Rules)).Select(c => c.Id));
        Assert.Equal("sfx.reveal,sfx.hatchOpen", Ids(Now("reveal", 1.2)));
        Assert.Equal("sfx.hatchSeal", Ids(Now("sealed", 0.8)));
        Assert.Equal("sfx.diceRoll,sfx.diceSettle", Ids(Now("roll")));
        Assert.Equal("sfx.diceRoll,sfx.hit", Ids(Now("fight", outcome: "hit")));
        Assert.Equal("sfx.diceRoll,sfx.hit", Ids(Now("fight", outcome: "stunned")));
        Assert.Equal("sfx.diceRoll,sfx.stagger", Ids(Now("fight", outcome: "staggered")));
        Assert.Equal("sfx.diceRoll,sfx.miss", Ids(Now("fight", outcome: "tie")));
        Assert.Equal("sfx.alarm", Ids(Now("breach", 6, text: "Something aboard has woken.")));
        Assert.Equal("sfx.death,sfx.echo", Ids(Now("death", 3, text: "They stay as an echo.")));
        Assert.Equal("sfx.cardIncident,sfx.vent", Ids(Now("card", 4, CardKind.Incident, "hull_groan", text: "The ship lurches.")));
        Assert.Equal("sfx.cardSalvage", Ids(Now("card", 4, CardKind.Salvage, "x", text: "A thing.")));
        Assert.Equal("sfx.cardAnomaly", Ids(Now("card", 4, CardKind.Anomaly, "x", text: "A thing.")));
        Assert.Empty(SoundPlan.For(Beat.For(Now("roll"), Rules, brief: true)));
    }

    [Fact]
    public void Sounds_land_on_their_steps_and_play_once()
    {
        var beat = Beat.For(Now("roll"), Rules);
        Assert.Equal(["sfx.diceRoll"], SoundPlan.Due(beat, 0, 0.1).Select(c => c.Id));
        Assert.Empty(SoundPlan.Due(beat, 0.1, 0.5));
        var land = beat.Steps[0].Seconds;
        Assert.Equal(["sfx.diceSettle"], SoundPlan.Due(beat, land - 0.05, land + 0.05).Select(c => c.Id));
        Assert.Empty(SoundPlan.Due(beat, land, land));   // a held timeline repeats nothing
        Assert.All(SoundPlan.For(beat), c => Assert.InRange(c.At, 0, beat.Seconds));
    }

    [Fact]
    public void The_tick_sounds_once_a_second_for_the_last_ten()
    {
        // A view says 14 seconds left; the TV counts down from there, a frame at a time.
        var ticks = new List<double>();
        for (var t = 0.0; t < 16; t += 1 / 60.0)
        {
            if (SoundPlan.Tick(14, t, t + 1 / 60.0))
            {
                ticks.Add(Math.Round(t + 1 / 60.0, 1));
            }
        }
        Assert.Equal(10, ticks.Count);
        Assert.Equal(4.0, ticks[0], 1);    // 10 seconds left
        Assert.Equal(13.0, ticks[^1], 1);  // 1 second left
        Assert.False(SoundPlan.Tick(null, 0, 20));
        Assert.False(SoundPlan.Tick(30, 0, 5));   // nowhere near the end
    }

    [Fact]
    public void Every_sound_is_in_the_contract_and_a_missing_file_is_skipped()
    {
        var all = new[]
        {
            Now("reveal", 1.2), Now("sealed", 0.8), Now("roll"), Now("fight", outcome: "hit"), Now("fight", outcome: "staggered"),
            Now("fight", outcome: "tie"), Now("breach", 6, text: "x"), Now("death", 3, text: "x"),
            Now("card", 4, CardKind.Incident, "hull_groan", text: "x"), Now("card", 4, CardKind.Salvage, text: "x"),
            Now("card", 4, CardKind.Anomaly, text: "x"),
        }.SelectMany(n => SoundPlan.For(Beat.For(n, Rules)).Select(c => c.Id))
         .Concat(["music.explore", "music.breach", "music.endgame", "music.debriefWon", "music.debriefLost", "sfx.footstep", "sfx.tick"]);
        foreach (var id in all)
        {
            Assert.Contains(id, ArtContract.Sounds);
            Assert.Null(SoundPlan.File(MeridianArt.None, id));
        }
    }

    // ---- Review focus 5: no sound or effect tells a secret

    [Theory]
    [MemberData(nameof(TillerLinesTests.Games), MemberType = typeof(TillerLinesTests))]
    public void Every_sound_and_effect_comes_from_what_the_TV_may_see(string scenario, int players)
    {
        var game = new Harness(players, seed: 1, scenario).Board();
        var lastSeq = 0;
        var heard = 0;
        game.PlayUntil(() =>
        {
            var view = (MeridianHostView)game.Game.HostView();
            if (view.Now is { } now && now.Seq != lastSeq)
            {
                lastSeq = now.Seq;
                // The beat is built from the host view alone, and the host view from Visibility.For(state, null).
                var beat = Beat.For(now, game.Pack.Rules);
                var sounds = SoundPlan.For(beat);
                heard += sounds.Count;
                Assert.All(sounds, c => Assert.Contains(c.Id, ArtContract.Sounds));
                // Every effect lands on something the TV shows: a seat on the strip, a public token, a placed compartment.
                var seats = view.Seats.Select(s => s.Id).ToHashSet();
                var tokens = (view.Scenario?.Tokens ?? []).Select(t => t.Id).ToHashSet();
                var rooms = view.Compartments.Select(c => c.Id).ToHashSet();
                foreach (var cue in EffectPlan.For(beat))
                {
                    var known = cue.Place switch
                    {
                        FxPlace.Screen => true,
                        FxPlace.Actor => seats.Contains(cue.Where!) || tokens.Contains(cue.Where!),
                        _ => rooms.Contains(cue.Where!),
                    };
                    Assert.True(known, $"{scenario}: {cue.Fx} plays on {cue.Where}, which the TV doesn't show");
                }
            }
            // The tick counts only a public decision down.
            if (view.TurnEndsIn is not null)
            {
                Assert.True(game.Session.Game?.Waiting is null or RollPending or OrientPending or AllocatePending or VotePending,
                            $"{scenario}: a countdown during {game.Session.Game?.Waiting?.GetType().Name}");
            }
            return false;
        }, steps: 6000);
        Assert.True(heard > 0, "nothing sounded");
    }
}
