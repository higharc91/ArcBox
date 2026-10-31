using System.Numerics;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Beats;
using Tactics.Presentation.Board;
using Tactics.Presentation.Tests.Board;

namespace Tactics.Presentation.Tests.Beats;

/// <summary><see cref="BeatPlayer"/>: beats play one at a time, each is reported, and the view is only the truth once they're done.</summary>
public sealed class BeatPlayerTests
{
    private const double Frame = 1 / 60.0;

    private static readonly Lazy<FxArt> Fx = new(() => FxArt.Load(Pack.Root));
    private static readonly Lazy<UnitArt> Art = new(() => UnitArt.Load(Pack.Root));
    private static readonly BoardTerrain River = new(Pack.Map("river_crossing"), null);
    private static readonly UnitId Hero = new("hero1");
    private static readonly UnitId Foe = new("goblin.1");

    private static UnitView Unit(UnitId id, string side, int x, int y, int hp = 10)
        => new(id, id.Value, side, "characters.class_fighter", new Tile(x, y), hp, 10, 0, "Up", 0, [], null);

    private static BattleView View(int heroX, int foeHp, params Beat[] beats)
        => new("e", "river_crossing", 1, [], null, [Unit(Hero, "Party", heroX, 3), Unit(Foe, "Enemy", 9, 3, foeHp)], [.. beats], null, BoardChanges.None);

    private static Beat Walk(int seq, int from, int to) => new(seq, Hero, [new Moved(Hero, [new Tile(from, 3), new Tile(to, 3)])]);

    private static BeatPlayer Player() => new(Fx.Value, Art.Value);

    private static List<int> Run(BeatPlayer player, double seconds)
    {
        var shown = new List<int>();
        for (var t = 0.0; t < seconds; t += Frame)
            shown.AddRange(player.Tick(Frame).Shown);
        return shown;
    }

    [Fact]
    public void Each_beat_is_reported_once_after_it_has_played()
    {
        var player = Player();
        player.Receive(View(3, 10));
        player.Receive(View(4, 10, Walk(1, 3, 4)));

        Assert.Empty(Run(player, BeatTiming.Step * 0.5));
        Assert.Equal([1], Run(player, BeatTiming.Step));
        Assert.Empty(Run(player, 2));
    }

    [Fact]
    public void Beats_play_one_at_a_time_in_order()
    {
        var player = Player();
        player.Receive(View(3, 10));
        player.Receive(View(5, 10, Walk(1, 3, 4), Walk(2, 4, 5)));

        Assert.Equal([1], Run(player, BeatTiming.Step + Frame * 2));
        Assert.Equal([2], Run(player, BeatTiming.Step + BeatTiming.Rest + Frame * 3));
    }

    [Fact]
    public void A_long_frame_does_not_skip_a_beat()
    {
        var player = Player();
        player.Receive(View(3, 10));
        player.Receive(View(6, 10, Walk(1, 3, 4), Walk(2, 4, 5), Walk(3, 5, 6)));

        var first = player.Tick(1).Shown;

        Assert.Empty(first);
        Assert.Equal([1], Run(player, BeatTiming.Step));
    }

    [Fact]
    public void The_shown_view_stays_behind_the_live_one_until_the_beat_says_so()
    {
        var player = Player();
        player.Receive(View(3, 10));
        player.Receive(View(4, 10, Walk(1, 3, 4)));

        player.Tick(Frame);

        Assert.Equal(new Tile(3, 3), player.Shown!.Units[0].At);
        Run(player, BeatTiming.Step + Frame * 2);
        Assert.Equal(new Tile(4, 3), player.Shown!.Units[0].At);
    }

    [Fact]
    public void A_unit_walking_is_drawn_between_its_tiles_facing_the_way_it_goes()
    {
        var player = Player();
        player.Receive(View(3, 10));
        player.Receive(View(4, 10, Walk(1, 3, 4)));

        Run(player, BeatTiming.Step / 2);
        var frame = player.Frame(River);

        Assert.InRange(frame.Places[Hero].Map.X, 3.6f, 4.4f);
        Assert.Equal("walk", frame.Poses[Hero].Anim);
        Assert.True(frame.Poses[Hero].Facing!.Value.X > 0);
    }

    [Fact]
    public void Between_beats_nobody_is_placed_by_the_player()
    {
        var player = Player();
        player.Receive(View(3, 10));

        var frame = player.Frame(River);

        Assert.Empty(frame.Places);
        Assert.Empty(frame.Poses);
        Assert.Empty(frame.Draws);
    }

    [Fact]
    public void A_unit_killed_in_the_beat_is_on_the_board_until_it_falls()
    {
        var player = Player();
        var dead = new Beat(1, Hero, [new Damaged(Foe, 10, null, 0), new Died(Foe)]);
        player.Receive(View(3, 10));
        var live = View(3, 0, dead);
        live = live with { Units = [live.Units[0], live.Units[1] with { Life = "Dead" }] };
        player.Receive(live);

        Run(player, 0.05);
        Assert.Equal("Up", player.Shown!.Units[1].Life);

        Run(player, 2);
        Assert.Equal("Dead", player.Shown!.Units[1].Life);
    }

    [Fact]
    public void A_view_with_no_new_beats_is_the_truth_at_once_when_nothing_is_playing()
    {
        var player = Player();
        player.Receive(View(3, 10));

        player.Receive(View(7, 2));

        Assert.Equal(new Tile(7, 3), player.Shown!.Units[0].At);
    }

    [Fact]
    public void A_view_arriving_mid_beat_does_not_jump_the_board()
    {
        var player = Player();
        player.Receive(View(3, 10));
        player.Receive(View(4, 10, Walk(1, 3, 4)));
        Run(player, Frame);

        player.Receive(View(5, 10, Walk(1, 3, 4)));

        Assert.Equal(new Tile(3, 3), player.Shown!.Units[0].At);
    }

    [Fact]
    public void Once_the_queue_is_empty_the_view_heals_any_error()
    {
        var player = Player();
        player.Receive(View(3, 10));
        var live = View(4, 4, Walk(1, 3, 4));   // the foe's 4 HP is in no event

        player.Receive(live);
        Run(player, 1);

        Assert.Equal(4, player.Shown!.Units[1].Hp);
    }

    [Fact]
    public void The_board_is_held_until_its_event_plays()
    {
        var tile = new Tile(5, 5);
        var player = Player();
        var before = View(3, 10);
        var live = View(3, 10, new Beat(1, Hero,
        [
            new Moved(Hero, [new Tile(3, 3), new Tile(4, 3)]), new Said("narrator", "Down it goes!"), new TerrainAltered([tile], -1),
        ])) with { Board = new BoardChanges([], [new HeightChange(tile, -1)], [tile]) };
        player.Receive(before);
        player.Receive(live);

        Assert.False(player.Tick(Frame).BoardChanged);
        Assert.Empty(player.Shown!.Board!.Sunk);

        var changed = false;
        for (var t = 0.0; t < 4; t += Frame)
            changed |= player.Tick(Frame).BoardChanged;

        Assert.True(changed);
        Assert.Equal([tile], player.Shown!.Board!.Sunk);
    }

    [Fact]
    public void A_line_comes_out_once_and_its_caption_stays_a_while()
    {
        var player = Player();
        player.Receive(View(3, 10));
        player.Receive(View(3, 10, new Beat(1, null, [new Said("narrator", "Three words here.")])));

        var lines = new List<SpokenLine>();
        for (var t = 0.0; t < 0.5; t += Frame)
            lines.AddRange(player.Tick(Frame).Lines);

        Assert.Equal("Three words here.", Assert.Single(lines).Text);
        Assert.Equal("Three words here.", player.Caption?.Text);

        Run(player, 10);
        Assert.Null(player.Caption);
    }

    [Fact]
    public void Numbers_pop_as_art_over_the_unit()
    {
        var player = Player();
        player.Receive(View(3, 10));
        player.Receive(View(3, 6, new Beat(1, null, [new Damaged(Foe, 4, null, 6)])));

        Run(player, 0.1);
        var draws = player.Frame(River).Draws;

        Assert.Equal(2, draws.Count(d => d.Path == UnitArt.NumbersPath));   // "-" and "4"
    }

    [Fact]
    public void A_flight_draws_between_its_ends()
    {
        var player = Player();
        player.Receive(View(3, 10));
        player.Receive(View(3, 10, new Beat(1, Hero,
        [
            new AbilityUsed(Hero, new AbilityId("a"), new Target(Unit: Foe), new PresentationDef("characters.shoot", null, "projectiles.arrow")),
        ])));

        Vector2? seen = null;
        for (var t = 0.0; t < 1 && seen is null; t += Frame)
        {
            player.Tick(Frame);
            seen = player.Frame(River).Draws.FirstOrDefault(d => d.Path == "projectiles/arrow.png")?.TopLeft;
        }

        Assert.NotNull(seen);
    }

    [Fact]
    public void A_new_battle_starts_clean()
    {
        var player = Player();
        player.Receive(View(3, 10));
        player.Receive(View(9, 10, Walk(8, 3, 9)));
        Run(player, 3);

        player.Reset();
        Assert.Null(player.Shown);
        player.Receive(View(1, 10, Walk(1, 1, 2)));

        Assert.Equal([1], Run(player, 1));
    }

    [Fact]
    public void Idle_means_nothing_left_to_play()
    {
        var player = Player();
        player.Receive(View(3, 10));
        Assert.True(player.Idle);

        player.Receive(View(4, 10, Walk(1, 3, 4)));
        Assert.False(player.Idle);

        Run(player, 1);
        Assert.True(player.Idle);
    }

    // A beat that can't be scripted, and one whose line throws when it's reached, part way through playing.
    private static Beat Broken(int seq) => new(seq, Hero, [new Moved(Hero, [])]);

    private static Beat BrokenLater(int seq) => new(seq, Hero, [new Said(Hero.Value, null!)]);

    [Fact]
    public void A_beat_that_breaks_while_playing_can_be_dropped_too()
    {
        var player = Player();
        player.Receive(View(3, 10));
        player.Receive(View(4, 10, BrokenLater(1), Walk(2, 3, 4)));

        Assert.ThrowsAny<Exception>(() => Run(player, 1));

        Assert.Equal(1, player.Skip());
        Assert.Equal([2], Run(player, 1));
    }

    [Fact]
    public void A_broken_beat_can_be_dropped_and_the_next_one_plays()
    {
        var player = Player();
        player.Receive(View(3, 10));
        player.Receive(View(4, 10, Broken(1), Walk(2, 3, 4)));

        Assert.ThrowsAny<Exception>(() => Run(player, 1));

        Assert.Equal(1, player.Skip());
        Assert.Equal([2], Run(player, 1));
    }

    [Fact]
    public void Dropping_the_last_beat_heals_to_the_view_and_redraws()
    {
        var player = Player();
        player.Receive(View(3, 10));
        player.Receive(View(4, 10, Broken(1)));
        Assert.ThrowsAny<Exception>(() => Run(player, 1));

        player.Skip();

        Assert.True(player.Idle);
        Assert.Equal(new Tile(4, 3), player.Shown!.Units[0].At);
        Assert.True(player.Tick(Frame).Busy);
        Assert.False(player.Tick(Frame).Busy);
    }

    [Fact]
    public void Dropping_with_nothing_playing_drops_nothing_and_changes_nothing()
    {
        var player = Player();
        player.Receive(View(3, 10));
        player.Tick(Frame);

        Assert.Null(player.Skip());

        Assert.False(player.Tick(Frame).Busy);      // no redraw asked for
        player.Receive(View(4, 10, Walk(1, 3, 4)));
        Assert.NotNull(player.Tick(Frame).Started); // and no rest before the next beat
    }

    private static ReactionOffered Offer(UnitId reactor) => new(new ReactionOffer(reactor, [], "leavesReach"));

    [Fact]
    public void An_offer_reaches_the_shown_battle_with_its_beat_not_before()
    {
        var player = Player();
        player.Receive(View(3, 10));
        var live = View(4, 10, new Beat(1, Foe, [new Moved(Hero, [new Tile(3, 3), new Tile(4, 3)]), Offer(Hero)])) with { Reacting = Hero };

        player.Receive(live);
        Assert.Null(player.Shown!.Reacting);

        player.Tick(Frame);
        Assert.Null(player.Shown!.Reacting);          // the walk that caused it is still playing

        Run(player, BeatTiming.Step + Frame * 2);
        Assert.Equal(Hero, player.Shown!.Reacting);
    }

    [Fact]
    public void A_backlog_holds_the_offer_back_until_the_beats_before_it_have_played()
    {
        var player = Player();
        player.Receive(View(3, 10));
        var offer = new Beat(3, Foe, [Offer(Hero)]);

        player.Receive(View(5, 10, Walk(1, 3, 4), Walk(2, 4, 5), offer) with { Reacting = Hero });

        Run(player, BeatTiming.Step + Frame * 2);
        Assert.Null(player.Shown!.Reacting);
        Run(player, BeatTiming.Step * 2 + BeatTiming.Rest * 3);
        Assert.Equal(Hero, player.Shown!.Reacting);
    }

    [Fact]
    public void The_tick_that_clears_the_last_number_asks_for_a_redraw()
    {
        var player = Player();
        player.Receive(View(3, 10));
        player.Receive(View(3, 6, new Beat(1, null, [new Damaged(Foe, 4, null, 6)])));

        // The number outlasts its beat, and nothing follows it. The last busy tick must be the one that left nothing to draw.
        var drawsAfterLastBusy = -1;
        for (var t = 0.0; t < 3; t += Frame)
            if (player.Tick(Frame).Busy)
                drawsAfterLastBusy = player.Frame(River).Draws.Length;

        Assert.Equal(0, drawsAfterLastBusy);
    }

    [Fact]
    public void A_beat_the_ai_finished_badges_its_unit_while_it_plays()
    {
        var player = Player();
        player.Receive(View(3, 10));
        player.Receive(View(4, 10, new Beat(1, Hero, [new Moved(Hero, [new Tile(3, 3), new Tile(4, 3)]) { AiFinished = true }])));

        var tick = player.Tick(Frame);

        Assert.Equal(Hero, player.AiFinished);
        Assert.Contains(BeatPlayer.AiFinishedMoment, tick.Moments);
    }

    [Fact]
    public void The_badge_lasts_long_enough_to_read_even_on_a_beat_that_is_only_an_end_of_turn()
    {
        var player = Player();
        player.Receive(View(3, 10));
        player.Receive(View(3, 10, new Beat(1, Hero, [new TurnEnded(Hero) { AiFinished = true }])));

        Run(player, BeatTiming.AiFinished - 0.1);
        Assert.Equal(Hero, player.AiFinished);

        Run(player, 0.2);
        Assert.Null(player.AiFinished);
    }

    [Fact]
    public void The_tick_that_ends_the_badge_asks_for_a_redraw()
    {
        var player = Player();
        player.Receive(View(3, 10));
        player.Receive(View(3, 10, new Beat(1, Hero, [new TurnEnded(Hero) { AiFinished = true }])));

        UnitId? atLastBusy = Hero;
        for (var t = 0.0; t < 3; t += Frame)
            if (player.Tick(Frame).Busy)
                atLastBusy = player.AiFinished;

        Assert.Null(atLastBusy);
    }

    [Fact]
    public void A_normal_beat_has_no_badge()
    {
        var player = Player();
        player.Receive(View(3, 10));
        player.Receive(View(4, 10, Walk(1, 3, 4)));

        var tick = player.Tick(Frame);

        Assert.Null(player.AiFinished);
        Assert.Empty(tick.Moments);
    }

    [Fact]
    public void Reset_takes_the_badge_away()
    {
        var player = Player();
        player.Receive(View(3, 10));
        player.Receive(View(3, 10, new Beat(1, Hero, [new TurnEnded(Hero) { AiFinished = true }])));
        player.Tick(Frame);

        player.Reset();

        Assert.Null(player.AiFinished);
    }

    [Fact]
    public void The_live_view_can_be_read_while_the_shown_one_lags()
    {
        var player = Player();
        player.Receive(View(3, 10));
        var live = View(4, 10, Walk(1, 3, 4));
        player.Receive(live);

        player.Tick(Frame);

        Assert.Same(live, player.Live);
        Assert.NotSame(live, player.Shown);
    }

    // ---- Catching up (Task 6) ----------------------------------------------------------------------------------------

    private static BattleView Filled(int heroX, params Beat[] beats) => View(heroX, 10, beats) with
    {
        Board = new BoardChanges([], [new HeightChange(new Tile(5, 5), -1)], [new Tile(5, 5)]),
        Units = [Unit(Hero, "Party", heroX, 3) with { Conditions = ["prone"] }, Unit(Foe, "Enemy", 9, 3)],
    };

    private static Beat[] Walks(int from, int to) => [.. Enumerable.Range(from, to - from + 1).Select(i => Walk(i, i, i + 1))];

    [Fact]
    public void A_gap_snaps_to_the_view_and_plays_only_the_newest_beat()
    {
        var player = Player();
        player.Receive(View(1, 10));
        player.Receive(View(2, 10, Walk(1, 1, 2)));
        Run(player, 1);

        player.Receive(Filled(31, Walks(11, 30)));

        Assert.Equal(new Tile(30, 3), player.Shown!.Units[0].At);   // where the newest beat's walk starts
        Assert.Equal(["prone"], player.Shown!.Units[0].Conditions);
        Assert.Equal([new Tile(5, 5)], player.Shown!.Board!.Sunk);
        Assert.Equal([30], Run(player, 3));
        Assert.Equal(new Tile(31, 3), player.Shown!.Units[0].At);
        Assert.True(player.Idle);
    }

    [Fact]
    public void A_fresh_scene_joining_a_fight_does_not_replay_old_beats()
    {
        var player = Player();

        player.Receive(Filled(21, Walks(1, 20)));

        Assert.Equal(new Tile(5, 5), Assert.Single(player.Shown!.Board!.Sunk));
        Assert.Equal([20], Run(player, 5));
    }

    [Fact]
    public void A_fresh_scene_joining_says_none_of_the_old_lines()
    {
        var player = Player();
        Beat[] beats = [new Beat(7, null, [new Said("narrator", "Old news.")]), new Beat(8, null, [new Said("narrator", "Fresh.")])];

        player.Receive(View(3, 10, beats));

        var lines = new List<string>();
        for (var t = 0.0; t < 2; t += Frame)
            lines.AddRange(player.Tick(Frame).Lines.Select(l => l.Text));
        Assert.Equal(["Fresh."], lines);
    }

    [Fact]
    public void A_view_joined_with_no_beats_plays_nothing()
    {
        var player = Player();

        player.Receive(View(3, 10));

        Assert.Empty(Run(player, 1));
        Assert.Equal(new Tile(3, 3), player.Shown!.Units[0].At);
    }

    [Fact]
    public void The_unit_acting_in_the_first_view_starts_from_where_it_stood()
    {
        var player = Player();
        var opening = new Beat(1, null, [new RoundStarted(1, [Foe, Hero]), new TurnStarted(Foe, 1)]);
        var step = new Beat(2, Foe, [new Moved(Foe, [new Tile(9, 3), new Tile(8, 3)])]);
        var live = View(3, 10, opening, step);
        live = live with { Units = [live.Units[0], live.Units[1] with { At = new Tile(8, 3) }] };

        player.Receive(live);

        Assert.Equal(new Tile(9, 3), player.Shown!.Units[1].At);
        var shown = Run(player, 0.05);
        Assert.Equal(new Tile(9, 3), player.Shown!.Units[1].At);     // not at its destination during beat 1
        shown.AddRange(Run(player, 3));
        Assert.Equal([1, 2], shown);                                 // nobody's missed the opening
        Assert.Equal(new Tile(8, 3), player.Shown!.Units[1].At);
    }

    [Fact]
    public void A_late_blow_lands_on_a_unit_still_standing()
    {
        var player = Player();
        var blow = new Beat(40, Hero, [new Damaged(Foe, 10, null, 0), new Died(Foe)]);
        var live = View(3, 0, Walk(39, 2, 3), blow);
        live = live with { Units = [live.Units[0], live.Units[1] with { Life = "Dead" }] };

        player.Receive(live);

        Assert.Equal("Up", player.Shown!.Units[1].Life);
        Assert.Equal(10, player.Shown!.Units[1].Hp);
        Run(player, 3);
        Assert.Equal("Dead", player.Shown!.Units[1].Life);
    }

    [Fact]
    public void The_beat_on_stage_finishes_before_a_gap_snaps()
    {
        var player = Player();
        player.Receive(View(1, 10));
        player.Receive(View(2, 10, Walk(1, 1, 2)));
        player.Tick(Frame);

        player.Receive(View(31, 10, Walks(11, 30)));

        Assert.Equal([1, 30], Run(player, 3));
        Assert.Equal(new Tile(31, 3), player.Shown!.Units[0].At);
    }

    [Fact]
    public void A_snap_with_the_beat_on_stage_takes_whatever_is_newest_by_then()
    {
        var player = Player();
        player.Receive(View(1, 10));
        player.Receive(View(2, 10, Walk(1, 1, 2)));
        player.Tick(Frame);

        player.Receive(View(31, 10, Walks(11, 30)));
        player.Receive(View(32, 10, Walks(12, 31)));

        Assert.Equal([1, 31], Run(player, 3));
    }

    [Fact]
    public void A_burst_of_a_few_seconds_is_played_whole()
    {
        var player = Player();
        player.Receive(View(1, 10));

        player.Receive(View(7, 10, Walks(1, 6)));

        Assert.Equal([1, 2, 3, 4, 5, 6], Run(player, 6 * (BeatTiming.Step + BeatTiming.Rest) + 0.5));
    }

    [Fact]
    public void A_tv_too_slow_to_keep_up_drops_back_to_the_newest_beat_and_stays_near_the_game()
    {
        const double SlowFrame = 0.5;   // 2 fps: a frame counts for 0.1 s of a beat, so a beat takes five times as long
        var player = Player();
        player.Receive(View(1, 10));

        var shown = new List<int>();
        var skipped = false;
        var worst = 0;
        for (var seq = 1; seq <= 300; seq++)
        {
            player.Receive(View(seq + 1, 10, Walk(seq, seq, seq + 1)));
            var tick = player.Tick(SlowFrame);
            shown.AddRange(tick.Shown);
            skipped |= tick.CaughtUp is not null;
            worst = Math.Max(worst, seq - (shown.Count == 0 ? 0 : shown[^1]));
        }

        Assert.True(skipped);
        Assert.InRange(worst, 1, (int)(BeatQueue.MostLag / SlowFrame) + 6);      // a lag of seconds, not the whole battle
        Assert.Equal(shown.Order(), shown);
    }

    [Fact]
    public void A_catch_up_is_reported_once_with_how_much_it_skipped()
    {
        var player = Player();
        player.Receive(View(1, 10));
        player.Receive(View(2, 10, Walk(1, 1, 2)));
        Run(player, 1);

        player.Receive(View(31, 10, Walks(11, 30)));

        Assert.Equal(new BeatCatchUp(30, 28), player.Tick(Frame).CaughtUp);
        Assert.Null(player.Tick(Frame).CaughtUp);
    }

    [Fact]
    public void A_catch_up_after_a_reset_counts_from_the_last_beat_this_tv_played()
    {
        var player = Player();
        player.Receive(View(1, 10));
        player.Receive(View(32, 10, Walks(31, 31)));
        Run(player, 1);

        player.Reset();                                     // as Resuming does: no board for a moment
        player.Receive(View(33, 10, Walks(13, 32)));

        Assert.Equal(new BeatCatchUp(32, 0), player.Tick(Frame).CaughtUp);
    }

    [Fact]
    public void A_snap_redraws_the_board_on_the_next_tick()
    {
        var player = Player();
        player.Receive(View(1, 10));
        player.Receive(View(2, 10, Walk(1, 1, 2)));
        Run(player, 1);

        player.Receive(Filled(31, Walks(11, 30)));

        Assert.True(player.Tick(Frame).BoardChanged);
    }

    [Fact]
    public void A_reset_cannot_replay_old_beats()
    {
        var player = Player();
        player.Receive(View(21, 10, Walks(1, 20)));
        Run(player, 3);

        player.Reset();                                   // as the board failing to draw does
        player.Receive(View(21, 10, Walks(1, 20)));

        Assert.Equal([20], Run(player, 3));
    }

    [Fact]
    public void The_game_starting_again_under_the_tv_plays_the_new_opening()
    {
        var player = Player();
        player.Receive(View(21, 10, Walks(1, 20)));
        Run(player, 3);

        player.Receive(View(2, 10, Walk(1, 1, 2)));

        Assert.Equal([1], Run(player, 3));
    }

    [Fact]
    public void A_broken_beat_at_a_snap_does_not_throw_out_of_the_view()
    {
        var player = Player();

        player.Receive(View(3, 10, Walk(1, 1, 2), Walk(2, 2, 3), Broken(3)));

        Assert.Equal(new Tile(3, 3), player.Shown!.Units[0].At);
        Assert.ThrowsAny<Exception>(() => Run(player, 1));
        Assert.Equal(3, player.Skip());
        Assert.True(player.Idle);
    }

    [Fact]
    public void A_snap_carries_a_reaction_prompt_that_is_waiting()
    {
        var player = Player();

        player.Receive(View(21, 10, Walks(1, 20)) with { Reacting = Hero });

        Assert.Equal(Hero, player.Shown!.Reacting);
    }

    [Fact]
    public void A_snap_to_the_offer_itself_lets_the_offer_arrive_with_its_beat()
    {
        var player = Player();
        Beat[] beats = [Walk(7, 2, 3), new Beat(8, Foe, [new Moved(Hero, [new Tile(3, 3), new Tile(4, 3)]), Offer(Hero)])];

        player.Receive(View(4, 10, beats) with { Reacting = Hero });

        Assert.Null(player.Shown!.Reacting);
        Run(player, 1);
        Assert.Equal(Hero, player.Shown!.Reacting);
    }
}
