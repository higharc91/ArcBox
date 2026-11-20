using Meridian.Presentation.Board;
using Meridian.Presentation.Beats;
using Meridian.Presentation.Hud;

namespace Meridian.Presentation.Tests.Hud;

public sealed class HudModelTests
{
    private static readonly MeridianRules Rules = new();

    private static MeridianHostView View(Presented? now = null)
        => new("explore", "Ann's turn",
               [new SeatCard("p1", "Ann", "Ines Okafor", 0, true, false, true), new SeatCard("p2", "Bo", "Tobe Raske", 1, false, true, false)],
               [], [], 40, 3, 2, now, ["one", "two", "three", "four"]);

    [Fact]
    public void Before_the_breach_the_hud_counts_rounds_anomalies_and_tiles()
    {
        var hud = HudModel.Of(View(), null);
        Assert.Equal(["Round 2", "Anomalies 3", "Tiles left 40"], hud.Stats.Select(s => $"{s.Label} {s.Value}"));
        Assert.Equal(["two", "three", "four"], hud.Log);
        Assert.Null(hud.Panel);
    }

    [Fact]
    public void The_seat_strip_shows_tags_and_marks_but_never_a_side()
    {
        var hud = HudModel.Of(View(), null);
        Assert.Equal(("A", true, false, false), (hud.Seats[0].Mark, hud.Seats[0].Active, hud.Seats[0].Auto, hud.Seats[0].Offline));
        Assert.Equal(("B", false, true, true), (hud.Seats[1].Mark, hud.Seats[1].Active, hud.Seats[1].Auto, hud.Seats[1].Offline));
        Assert.DoesNotContain(typeof(HudSeat).GetProperties(), p => p.Name is "Role" or "Side");
    }

    [Fact]
    public void A_card_beat_shows_the_card_in_its_decks_colour_and_a_roll_shows_dice()
    {
        var card = new Presented(5, "card", new SeatId("p1"), "Hull Groan", "The hull shifts.", 9, Deck: CardKind.Incident);
        var panel = HudModel.Of(View(card), Beat.For(card, Rules)).Panel!;
        Assert.Equal(("Hull Groan", "#ff3b3b"), (panel.Title, panel.Accent));
        var roll = new Presented(6, "roll", new SeatId("p1"), "Tech check", "3 dice: 4", 1.5, Faces: [1, 2, 1]);
        Assert.Equal([1, 2, 1], HudModel.Of(View(roll), Beat.For(roll, Rules)).Panel!.Faces);
    }

    [Fact]
    public void A_brief_beat_after_a_reconnect_shows_no_panel()
    {
        var card = new Presented(5, "card", new SeatId("p1"), "Hull Groan", "The hull shifts.", 9, Deck: CardKind.Incident);
        Assert.Null(HudModel.Of(View(card), Beat.For(card, Rules, brief: true)).Panel);
    }

    [Fact]
    public void The_wait_counts_down_and_a_vote_names_nobody()
    {
        Assert.Equal("Waiting for Ann… 12 s", HudModel.WaitText(new HostWait("pause", "p1", "Ann", 60), 48.2));
        Assert.Equal("Waiting for the vote… 0 s", HudModel.WaitText(new HostWait("pause", null, null, 30), 45));
    }

    [Fact]
    public void Counters_get_room_labels_and_bookkeeping_is_hidden()
    {
        string Names(string seat) => seat == "p2" ? "Bo" : seat;
        var looks = ScenarioLooks.Of(BasePack.Pack);   // the labels are the scenarios' content (M7 plan, Task 1)
        Assert.Null(looks.Counter("voteRound", Names));
        Assert.Equal("Suspicion: Bo", looks.Counter("suspect-p2", Names));
        Assert.Equal("Memory banks pulled", looks.Counter("banks", Names));
        Assert.Equal("Hazard", looks.Counter("hazard", Names));   // undeclared: its id, capitalised
    }

    [Theory]
    [MemberData(nameof(AllScenarios.Ids), MemberType = typeof(AllScenarios))]
    public void Every_phase_of_a_real_game_has_a_hud(string scenario)
    {
        var game = new Harness(players: 4, seed: 2, scenario).Board();
        var sawRoundsLeft = false;
        game.PlayUntil(() =>
        {
            var view = (MeridianHostView)game.Game.HostView();
            var beat = view.Now is { } now ? Beat.For(now, game.Pack.Rules) : null;
            var hud = HudModel.Of(view, beat);
            Assert.Equal(view.Seats.Select(s => s.Id), hud.Seats.Select(s => s.Id));
            Assert.Equal(hud.Seats.Count, hud.Seats.Select(s => s.Mark).Distinct().Count());
            sawRoundsLeft |= hud.Stats.Any(s => s.Label is "Rounds left" or "Final round");
            return false;
        }, steps: 6000);
        Assert.True(sawRoundsLeft);
    }

    /// <summary>
    /// Per-seat counters ride on their seat's chip, never in the figures: six of them (The Last Clean Sample at six
    /// players) crowded the figures off the screen (M7 TV review).
    /// </summary>
    [Fact]
    public void Per_seat_counters_go_on_their_seats_chip_not_in_the_figures()
    {
        var looks = ScenarioLooks.Of(BasePack.Pack);
        var view = new MeridianHostView("endgame", "", [new SeatCard("p1", "Ann", null, 0, true, false, true), new SeatCard("p2", "Bo", null, 1, true, false, false)],
            [], [], 0, 0, 3, null, [],
            new ScenarioPanel("The Last Clean Sample", 6, [], [new CounterCard("infection-p1", 4), new CounterCard("infection-p2", 5)], []));
        var hud = HudModel.Of(view, null, 0, looks);
        Assert.Equal([new HudStat("Infection", "4")], hud.Seats[0].Badges);
        Assert.Equal([new HudStat("Infection", "5")], hud.Seats[1].Badges);
        Assert.DoesNotContain(hud.Stats, s => s.Label.StartsWith("Infection", StringComparison.Ordinal));
        Assert.Contains(hud.Stats, s => s.Label == "Rounds left");
        Assert.Equal(("Suspicion", "p3"), looks.PerSeat("suspect-p3"));
        Assert.Null(looks.PerSeat("orbit"));
    }
}
