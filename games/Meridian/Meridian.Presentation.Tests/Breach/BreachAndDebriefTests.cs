using Meridian.Presentation.Beats;
using Meridian.Presentation.Board;
using Meridian.Presentation.Breach;
using Meridian.Presentation.Debrief;

namespace Meridian.Presentation.Tests.Breach;

public sealed class BreachAndDebriefTests
{
    /// <summary>
    /// Plays a real game's Breach through a Director as the TV would, in frames, signalling as the scene does, and
    /// returns the stages shown in order and the presented events signalled.
    /// </summary>
    private static (List<BreachStage> Stages, List<string> Signalled) Watch(string scenario)
    {
        var game = new Harness(players: 3, seed: 1, scenario).Board();
        game.PlayUntil(() => game.Session.Queue is [{ Kind: "breach" }, ..]);
        var signalled = new List<string>();
        Director director = null!;
        director = new Director(game.Pack.Rules, seq =>
        {
            signalled.Add(game.Session.Queue.First(p => p.Seq == seq).Kind);
            game.Presented(seq);
        });
        director.Receive(Empty);   // the TV has been there all game
        var stages = new List<BreachStage>();
        for (var frame = 0; frame < 60 * 60 && game.Session.Phase is SessionPhase.Explore or SessionPhase.Briefing; frame++)
        {
            director.Receive((MeridianHostView)game.Game.HostView());
            if (director.Current is { Speaks: true } speaking && frame % 30 == 0)
            {
                director.SpeechFinished(speaking.Seq);   // a voice that finishes promptly
            }
            director.Tick(1.0 / 60);
            if (BreachSequence.Of(director.Shown!, director.Current, director.Position) is { } shown
                && (stages.Count == 0 || stages[^1] != shown.Stage))
            {
                stages.Add(shown.Stage);
            }
            if (game.Session.Phase == SessionPhase.Briefing && stages[^1] == BreachStage.Phones)
            {
                break;
            }
            game.Advance(1.0 / 60);
        }
        return (stages, signalled);
    }

    /// <summary>A view with nothing presented, as the TV had before the Breach.</summary>
    private static readonly MeridianHostView Empty = new("explore", "", [], [], [], 0, 0, 1, null, []);

    [Theory]
    [InlineData(TillerSaysNo.Id)]
    [InlineData(OneOfUsIsnt.Id)]
    public void The_breach_runs_lights_breach_announcement_then_phones(string scenario)
    {
        var (stages, signalled) = Watch(scenario);
        Assert.Equal([BreachStage.Lights, BreachStage.Breach, BreachStage.Announcement, BreachStage.Phones], stages);
        Assert.Equal(["breach", "scenario"], signalled.Where(k => k is "breach" or "scenario"));
    }

    [Fact]
    public void The_phones_screen_counts_ready_seats_and_names_none()
    {
        var view = new MeridianHostView("briefing", "Read your briefing", [new SeatCard("p1", "Ann", null, 0, true, false, false)], [], [], 0, 0, 1,
                                        null, [], Ready: 1);
        var frame = BreachSequence.Of(view, null, null)!;
        Assert.Equal((BreachStage.Phones, 1, 1), (frame.Stage, frame.Ready, frame.Seats));
        Assert.Null(BreachSequence.Of(view with { Phase = "endgame", Ready = null }, null, null));
    }

    [Theory]
    [MemberData(nameof(AllScenarios.Ids), MemberType = typeof(AllScenarios))]
    public void The_debrief_reveals_every_seat_and_names_the_award_winners(string scenario)
    {
        var game = new Harness(players: 4, seed: 1, scenario).Board();
        game.PlayUntil(() => game.Session.Phase == SessionPhase.Endgame);
        Assert.Null(DebriefModel.Of((MeridianHostView)game.Game.HostView()));   // nothing revealed mid-game
        game.PlayUntil(() => game.Session.Phase == SessionPhase.Debrief);
        var view = (MeridianHostView)game.Game.HostView();
        var debrief = DebriefModel.Of(view)!;
        Assert.Equal(4, debrief.Rows.Count);
        Assert.Equal(view.Scenario!.Reveal!.Value.Count(r => r.Won), debrief.Rows.Count(r => r.Won));
        Assert.All(debrief.Awards, a => Assert.Contains(view.Seats, s => s.Name == a.Name));
        Assert.False(string.IsNullOrWhiteSpace(debrief.Outcome));
    }

    [Fact]
    public void The_debrief_lays_out_text_and_three_decks_side_by_side()
    {
        var (text, command, habitat, engineering) = DebriefModel.Layout(new TvSize(1920, 1080));
        Assert.Equal(text.X + text.W, command.X, 3);
        Assert.Equal(command.X + command.W, habitat.X, 3);
        Assert.Equal(1920, engineering.X + engineering.W, 3);
    }
}
