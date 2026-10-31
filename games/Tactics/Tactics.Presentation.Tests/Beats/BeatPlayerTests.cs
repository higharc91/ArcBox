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

        var first = player.Tick(30).Shown;

        Assert.True(first.Length <= 1);
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
}
