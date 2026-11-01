using System.Collections.Immutable;
using System.Numerics;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Beats;
using Tactics.Presentation.Board;
using Tactics.Presentation.World;

namespace Tactics.Presentation.Tests.Board;

/// <summary><see cref="MarkRule"/>: which of the units' marks are drawn, so none of them reads wrong.</summary>
public sealed class MarkRuleTests
{
    private const double Frame = 1 / 60.0;

    private static readonly Lazy<UnitArt> Art = new(() => UnitArt.Load(Pack.Root));
    private static readonly Lazy<FxArt> Fx = new(() => FxArt.Load(Pack.Root));
    private static readonly BoardTerrain River = new(Pack.Map("river_crossing"), null);
    private static readonly BoardFit Fit = BoardFit.For(River, Pack.Art);
    private static readonly UnitId Hero = new("hero1");
    private static readonly UnitId Other = new("hero2");
    private static readonly UnitId Foe = new("goblin.1");

    private static UnitView Unit(UnitId id, string side, int x, int y, int hp = 10, string life = "Up", int countdown = 0)
        => new(id, id.Value, side, "characters.class_fighter", new Tile(x, y), hp, 10, 0, life, countdown, [], null);

    private static BattleView Battle(ImmutableArray<UnitView> units, params Beat[] beats)
        => new("e", "river_crossing", 1, [], null, units, [.. beats], null, BoardChanges.None);

    private static ImmutableArray<UnitView> Downed(int x = 3, int y = 3) =>
        [Unit(Hero, "Party", x, y, hp: 0, life: "Downed", countdown: 2), Unit(Foe, "Enemy", 9, 3)];

    private static ImmutableArray<BoardUnit> Drawn(BattleView battle, BeatFrame frame)
        => MarkRule.ClearOfPops(UnitPlan.Build(battle, River, Art.Value, Fit, frame.Places, frame.Poses), frame.Pops, Art.Value);

    // A hit on a Downed unit pops its number up just over its own count.
    [Fact]
    public void A_downed_count_goes_while_a_hit_on_that_unit_pops_up_and_comes_back_after()
    {
        var battle = Battle(Downed(), new Beat(1, Foe, [new Damaged(Hero, 10, null, 0)]));
        var player = new BeatPlayer(Fx.Value, Art.Value);
        player.Receive(battle);

        var (popped, cleared, back) = (false, true, false);
        for (var t = 0.0; t < 3; t += Frame)
        {
            player.Tick(Frame);
            var frame = player.Frame(River);
            var hero = Assert.Single(Drawn(player.Shown!, frame), u => u.Id == Hero);
            if (frame.Pops.Any(p => p.Unit == Hero))
                (popped, cleared) = (true, cleared && hero.Countdown is null);
            else if (popped)
                back |= hero.Countdown is { Text: "2" };
        }

        Assert.True(popped);
        Assert.True(cleared);
        Assert.True(back);
    }

    // The hardware check's "-102" (local-windows-checks §5.5) was a single Damaged: a hit's "-10" over the unit it hit, and in the same row,
    // just to its right, the "2" of a Downed hero on the tile up and to the right. Unruled, the two read as one number.
    [Fact]
    public void A_hit_beside_a_downed_neighbour_hides_its_count_which_would_read_as_part_of_the_number()
    {
        ImmutableArray<UnitView> units = [Unit(Hero, "Party", 3, 2, hp: 0, life: "Downed", countdown: 2), Unit(Foe, "Enemy", 3, 3)];
        var player = new BeatPlayer(Fx.Value, Art.Value);
        player.Receive(Battle(units, new Beat(1, Other, [new Damaged(Foe, 10, null, 0)])));
        var digit = Art.Value.Digit('0').Advance * UnitPlan.CountdownScale;

        var (inRow, cleared, back) = (false, true, false);
        for (var t = 0.0; t < 3; t += Frame)
        {
            player.Tick(Frame);
            var frame = player.Frame(River);
            var unruled = Assert.Single(UnitPlan.Build(player.Shown!, River, Art.Value, Fit), u => u.Id == Hero).Countdown!;
            var hero = Assert.Single(Drawn(player.Shown!, frame), u => u.Id == Hero);
            if (frame.Pops.SingleOrDefault() is not { } pop)
            {
                back |= inRow && hero.Countdown is { Text: "2" };
                continue;
            }
            var (topLeft, size) = MarkRule.CountBox(unruled, Art.Value);
            var gap = topLeft.X - (pop.TopLeft.X + pop.Size.X);
            var shared = Math.Min(topLeft.Y + size.Y, pop.TopLeft.Y + pop.Size.Y) - Math.Max(topLeft.Y, pop.TopLeft.Y);
            if (Math.Abs(gap) < digit && shared > size.Y / 2)
                (inRow, cleared) = (true, cleared && hero.Countdown is null);      // "-10" then "2", touching or a digit apart, on one line
        }

        Assert.True(inRow);
        Assert.True(cleared);
        Assert.True(back);
    }

    [Fact]
    public void A_pop_box_holds_the_numbers_it_draws()
    {
        var player = new BeatPlayer(Fx.Value, Art.Value);
        player.Receive(Battle(Downed(), new Beat(1, Foe, [new Damaged(Hero, 10, null, 0)])));
        for (var t = 0.0; t < 3; t += Frame)
        {
            player.Tick(Frame);
            var frame = player.Frame(River);
            foreach (var pop in frame.Pops)
            {
                var digits = frame.Draws.Where(d => d.Path == UnitArt.NumbersPath).ToArray();
                Assert.Equal(3, digits.Length);     // "-", "1", "0"
                Assert.All(digits, d => Assert.True(d.TopLeft.X >= pop.TopLeft.X - 0.01f && d.TopLeft.X + d.Size.X <= pop.TopLeft.X + pop.Size.X + 0.01f
                    && d.TopLeft.Y >= pop.TopLeft.Y - 0.01f && d.TopLeft.Y + d.Size.Y <= pop.TopLeft.Y + pop.Size.Y + 0.01f));
            }
        }
    }

    [Fact]
    public void A_count_beside_someone_elses_number_goes_and_one_well_away_stays()
    {
        ImmutableArray<UnitView> units = [Unit(Hero, "Party", 3, 3, hp: 0, life: "Downed", countdown: 2), Unit(Other, "Party", 8, 8, hp: 0, life: "Downed", countdown: 1),
            Unit(Foe, "Enemy", 9, 3)];
        var drawn = UnitPlan.Build(Battle(units), River, Art.Value, Fit);
        var count = drawn.Single(u => u.Id == Hero).Countdown!;
        var (topLeft, size) = MarkRule.CountBox(count, Art.Value);
        var beside = new PopBox(Foe, new Vector2(topLeft.X + size.X + 3, topLeft.Y), new Vector2(30, size.Y));     // just right of the "2"
        var above = new PopBox(Foe, new Vector2(topLeft.X, topLeft.Y - size.Y - 6), new Vector2(30, size.Y));      // clear over it

        var nearby = MarkRule.ClearOfPops(drawn, [beside], Art.Value);
        Assert.Null(nearby.Single(u => u.Id == Hero).Countdown);
        Assert.Equal("1", nearby.Single(u => u.Id == Other).Countdown!.Text);

        var clear = MarkRule.ClearOfPops(drawn, [above], Art.Value);
        Assert.Equal("2", clear.Single(u => u.Id == Hero).Countdown!.Text);
    }

    [Fact]
    public void With_no_numbers_up_nothing_changes()
    {
        var drawn = UnitPlan.Build(Battle(Downed()), River, Art.Value, Fit);
        Assert.Equal(drawn, MarkRule.ClearOfPops(drawn, [], Art.Value));
        Assert.Equal(drawn, MarkRule.ClearOfPops(drawn, default, Art.Value));
    }

    [Theory]
    [InlineData(StageStep.Off, true)]           // the plain board
    [InlineData(StageStep.Forming, false)]      // the walk-in
    [InlineData(StageStep.Fighting, true)]
    [InlineData(StageStep.Fading, true)]        // the last blow's numbers still play
    [InlineData(StageStep.Results, false)]
    [InlineData(StageStep.FallingIn, false)]
    [InlineData(StageStep.Waking, false)]
    [InlineData(StageStep.Walking, false)]
    public void Marks_show_only_while_the_fight_is_played(StageStep stage, bool shown)
    {
        Assert.Equal(shown, MarkRule.Shown(stage, covered: false));
        Assert.False(MarkRule.Shown(stage, covered: true));
    }

    [Fact]
    public void Every_step_of_the_timeline_has_a_say() => Assert.Equal(8, Enum.GetValues<StageStep>().Length);     // a new step needs a line above

    [Fact]
    public void Bare_units_have_no_bar_count_icons_or_arrow_and_are_otherwise_as_they_were()
    {
        ImmutableArray<UnitView> units = [Unit(Hero, "Party", 3, 3) with { Conditions = ["poisoned"] }, Unit(Other, "Party", 5, 3, hp: 0, life: "Downed", countdown: 2),
            Unit(Foe, "Enemy", 9, 3)];
        var drawn = UnitPlan.Build(Battle(units) with { Active = Hero }, River, Art.Value, Fit);
        Assert.Contains(drawn, u => !u.Icons.IsEmpty);
        Assert.Contains(drawn, u => u.ArrowAt is not null);

        var bare = MarkRule.Bare(drawn);

        Assert.All(bare, u => Assert.True(u.Bar is null && u.Countdown is null && u.Icons.IsEmpty && u.ArrowAt is null));
        Assert.Equal(drawn.Select(u => (u.Id, u.Feet, u.Order, u.Anim, u.Ring)), bare.Select(u => (u.Id, u.Feet, u.Order, u.Anim, u.Ring)));
    }

    // The hardware check (§5.6): in the walk-in every hero had a bar, where the world shows none. Through the director: none in the walk-in,
    // all of them once the fight's on.
    [Fact]
    public void A_walk_in_has_no_bars_and_the_fight_has_them()
    {
        var script = new World.StageScript();
        var director = script.Director();
        director.Receive(World.StageScript.Walking());
        director.Receive(World.StageScript.Fight(World.StageScript.Battle()), script.Walkers(new Tile(12, 12), new Tile(12, 13)));
        var terrain = new BoardTerrain(script.Map, null);
        var fit = BoardFit.For(terrain, Pack.Art);
        ImmutableArray<BoardUnit> Now() => MarkRule.Apply(UnitPlan.Build(World.StageScript.Battle(), terrain, Art.Value, fit, director.Places, director.Poses),
            new BeatFrame(ImmutableDictionary<UnitId, UnitPlace>.Empty, ImmutableDictionary<UnitId, UnitPose>.Empty, [], Vector2.Zero), Art.Value,
            MarkRule.Shown(director.Stage, covered: false)).Units;

        Assert.Equal(StageStep.Forming, director.Stage);
        Assert.All(Now(), u => Assert.Null(u.Bar));
        World.StageScript.Run(director, director.Intro!.End + 0.05);
        Assert.Equal(StageStep.Fighting, director.Stage);
        Assert.All(Now(), u => Assert.NotNull(u.Bar));
    }

    [Fact]
    public void Unshown_the_beats_numbers_and_badges_go_and_its_effects_stay()
    {
        var player = new BeatPlayer(Fx.Value, Art.Value);
        player.Receive(Battle(Downed(), new Beat(1, Foe, [new Damaged(Hero, 10, null, 0)])));
        var (marked, effects) = (false, false);
        for (var t = 0.0; t < 3; t += Frame)
        {
            player.Tick(Frame);
            var frame = player.Frame(River);
            var units = UnitPlan.Build(player.Shown!, River, Art.Value, Fit, frame.Places, frame.Poses);
            var (shownUnits, shown) = MarkRule.Apply(units, frame, Art.Value, true);
            var (bareUnits, bare) = MarkRule.Apply(units, frame, Art.Value, false);

            Assert.Equal(frame.Draws, shown);
            Assert.Equal(frame.Draws.Where(d => !d.Mark), bare);
            Assert.All(bareUnits, u => Assert.True(u.Bar is null && u.Countdown is null));
            Assert.Equal(MarkRule.ClearOfPops(units, frame.Pops, Art.Value), shownUnits);
            marked |= frame.Draws.Any(d => d.Mark);
            effects |= bare.Length > 0;
        }
        Assert.True(marked);        // the "-10"
        Assert.True(effects);       // the spark of the blow
    }

    [Fact]
    public void Numbers_and_die_badges_are_marks_and_bursts_arent()
    {
        var player = new BeatPlayer(Fx.Value, Art.Value);
        player.Receive(Battle(Downed(), new Beat(1, Foe, [new Damaged(Hero, 10, null, 0)])));
        var draws = new List<FxDraw>();
        for (var t = 0.0; t < 3; t += Frame)
        {
            player.Tick(Frame);
            draws.AddRange(player.Frame(River).Draws);
        }
        Assert.All(draws, d => Assert.Equal(d.Path is UnitArt.NumbersPath or UnitArt.DicePath, d.Mark));
    }
}
