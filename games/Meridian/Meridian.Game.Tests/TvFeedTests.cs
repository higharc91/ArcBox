namespace Meridian.Game.Tests;

/// <summary>
/// What the TV needs from the server (M4 plan, Task 2): reading-time fallbacks, the waiting banner, scenario
/// announcements, round warnings, the Debrief's awards and the briefing's ready count.
/// </summary>
public sealed class TvFeedTests
{
    private static MeridianHostView Tv(Harness game) => (MeridianHostView)game.Game.HostView();

    private static int Words(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>How long Tiller takes to say something, at the rate the rules assume, plus the lead.</summary>
    private static double Speaking(MeridianRules rules, string text)
        => rules.ReadingLead.TotalSeconds + Words(text) / rules.ReadingWordsPerSecond;

    // ---- Reading time (review focus 4)

    [Fact]
    public void Every_card_waits_at_least_as_long_as_it_takes_to_read()
    {
        var pack = BasePack.Pack;
        foreach (var card in pack.Cards)
        {
            var seconds = MeridianGame.ReadingSeconds(pack.Rules, card.Title, card.Text);
            Assert.True(seconds >= Speaking(pack.Rules, $"{card.Title} {card.Text}"), $"{card.Id}: {seconds:0.0} s");
        }
    }

    [Fact]
    public void Every_announcement_waits_at_least_as_long_as_it_takes_to_read()
    {
        var pack = BasePack.Pack;
        foreach (var def in pack.ScenarioDefs)
        {
            var seconds = MeridianGame.ReadingSeconds(pack.Rules, def.Title, def.Announcement);
            Assert.True(seconds >= Speaking(pack.Rules, $"{def.Title} {def.Announcement}"), $"{def.Id}: {seconds:0.0} s");
        }
    }

    [Fact]
    public void A_card_presented_in_a_real_game_carries_its_reading_time()
    {
        var game = new Harness(seed: 3).Board();
        game.PlayUntil(() => game.Session.Queue is [{ Kind: "card" }, ..]);
        var shown = game.Session.Queue[0];
        Assert.Equal(MeridianGame.ReadingSeconds(game.Pack.Rules, shown.Title, shown.Text), shown.Seconds, 3);
        Assert.Equal(game.H.Now.AddSeconds(shown.Seconds), game.Game.Deadline);
    }

    // ---- The waiting banner (review focus 7)

    [Fact]
    public void The_tv_names_who_explore_is_waiting_for_during_the_grace()
    {
        var game = new Harness().Board();
        Assert.Null(Tv(game).Wait);
        var actor = game.Actor;
        game.H.SetConnected(actor, false);
        var wait = Tv(game).Wait!;
        Assert.Equal("grace", wait.Kind);
        Assert.Equal(game.Session.Seats[actor].Player.Value, wait.Seat);
        Assert.Equal(game.Session.Seats[actor].Name, wait.Name);
        Assert.Equal(20, wait.Seconds);
        game.Advance(20);
        Assert.Null(Tv(game).Wait);   // the bot has the seat now
    }

    [Theory]
    [InlineData(OneOfUsIsnt.Id)]
    [InlineData(ThePassenger.Id)]
    public void The_endgame_pause_looks_the_same_whoever_drops(string scenario)
    {
        // Each living seat in turn drops on its own endgame turn; the TV's banner differs only in the name.
        var shapes = new HashSet<string>();
        for (var who = 0; who < 4; who++)
        {
            var game = new Harness(players: 4, seed: 2, scenario).Board();
            game.PlayUntil(() => game.Session is { Phase: SessionPhase.Endgame, Queue.IsEmpty: true }
                                 && game.State.Waiting is null && game.Actor == who && game.State.ActiveCrew.Alive);
            if (game.Session.Phase != SessionPhase.Endgame)
            {
                continue;
            }
            game.H.SetConnected(who, false);
            var wait = Tv(game).Wait!;
            Assert.Equal(game.Session.Seats[who].Name, wait.Name);
            shapes.Add($"{wait.Kind}/{wait.Seconds}");
        }
        Assert.Equal(["pause/60"], shapes);
    }

    // ---- Announcements

    [Fact]
    public void The_scenario_presentation_reads_its_announcement()
    {
        var game = new Harness(seed: 1, scenario: TillerSaysNo.Id).Board();
        game.PlayUntil(() => game.Session.Queue is [{ Kind: "scenario" }, ..]);
        var shown = game.Session.Queue[0];
        var def = game.Pack.ScenarioDef(TillerSaysNo.Id);
        Assert.Equal(def.Title, shown.Title);
        Assert.Equal(def.Announcement, shown.Text);
    }

    [Fact]
    public void Every_shipped_scenario_has_a_short_announcement()
    {
        foreach (var def in BasePack.Pack.ScenarioDefs)
        {
            Assert.False(string.IsNullOrWhiteSpace(def.Announcement), def.Id);
            Assert.InRange(Words(def.Announcement), 1, PackLoader.AnnouncementWords);
        }
    }

    // ---- Round warnings

    [Fact]
    public void Tiller_warns_with_three_rounds_left_and_on_the_last_round()
    {
        var game = new Harness(players: 3, seed: 1, DecayingOrbit.Id).Board();
        var warnings = new List<(string Text, int Left)>();
        game.PlayUntil(() =>
        {
            if (game.Session.Queue is [{ Kind: "round" } round, ..] && warnings.All(w => w.Text != round.Text))
            {
                var board = game.State.Board!;
                warnings.Add((round.Text, board.RoundLimit - (game.State.Round - board.StartRound)));
            }
            return false;
        }, steps: 8000);
        Assert.Contains(warnings, w => w.Left == 3);
        Assert.All(warnings, w => Assert.Contains(w.Left, new[] { 3, 1 }));
    }

    // ---- The Debrief's awards and the briefing's ready count

    [Fact]
    public void The_tv_shows_the_awards_only_once_the_game_is_over()
    {
        var game = new Harness(players: 4, seed: 1, OneOfUsIsnt.Id).Board();
        game.PlayUntil(() => game.Session.Phase == SessionPhase.Endgame);
        Assert.Null(Tv(game).Scenario!.Awards);
        game.PlayUntil(() => game.Session.Phase == SessionPhase.Debrief);
        var awards = Tv(game).Scenario!.Awards!.Value;
        Assert.Equal(game.Game.Awards().Count, awards.Count);
        Assert.Equal(awards.Count, awards.Select(a => a.Id).Distinct().Count());
        Assert.All(awards, a => Assert.Contains(game.Session.Seats, s => s.Player.Value == a.Seat));
    }

    [Fact]
    public void The_tv_counts_ready_seats_during_the_briefing_and_names_none()
    {
        var game = new Harness(players: 3, seed: 1, TillerSaysNo.Id).Board();
        Assert.Null(Tv(game).Ready);
        game.PlayUntil(() => game.Session.Phase == SessionPhase.Briefing);
        Assert.Equal(0, Tv(game).Ready);
        game.Send(1, "ready");
        Assert.Equal(1, Tv(game).Ready);
        game.Send(0, "ready");
        game.Send(2, "ready");
        Assert.Null(Tv(game).Ready);   // everyone was ready: the endgame has begun
    }
}
