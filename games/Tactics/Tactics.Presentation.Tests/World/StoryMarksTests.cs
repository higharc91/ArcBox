using System.Numerics;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Board;
using Tactics.Presentation.Tests.Board;
using Tactics.Presentation.World;
using static Tactics.Presentation.Tests.World.WalkFeed;

namespace Tactics.Presentation.Tests.World;

/// <summary><see cref="StoryMarks"/>: the beat on offer in the world, a figure at its spot with the "!" over their head (overworld 5d §2).</summary>
public sealed class StoryMarksTests
{
    private static readonly Lazy<UnitArt> Art = new(() => UnitArt.Load(Pack.Root));
    private static readonly BoardTerrain Ground = WalkFeed.Ground(40, 40, (x, y) => x >= 20 ? 4 : 0);
    private static readonly Tile Spot = new(10, 12);

    private static ExploreView With(params StoryMarker[] markers) => View(new Tile(5, 5)) with { Markers = [.. markers] };

    [Fact]
    public void The_marker_over_the_beat_spot_is_a_figure_with_a_bang_over_their_head()
    {
        var mark = Assert.Single(StoryMarks.Of(With(new StoryMarker("ch1_mayors_request", Spot, "characters.mayor", "alert")), Ground, Art.Value));

        var figure = mark.Figure!;
        Assert.Equal(UnitPlace.On(Ground, Spot).Feet, figure.Feet);
        Assert.Equal(Art.Value.Sprite("characters.mayor"), figure.Sprite);
        Assert.Equal(UnitTone.Neutral, figure.Tone);
        Assert.Equal(0, figure.Ring);                                     // dressing: no ring, so it isn't read as one of the party
        Assert.Equal(Art.Value.Emote("alert"), mark.Emote);
        Assert.Equal(figure.Feet.X, mark.EmoteAt!.Value.X);
        Assert.True(mark.EmoteAt.Value.Y < figure.Feet.Y - figure.Sprite.AnchorY + figure.Sprite.HeadTop);   // above the head
    }

    [Fact]
    public void The_bang_is_the_alert_row_of_the_emotes()
    {
        var alert = Art.Value.Emote("alert")!;

        Assert.Equal(0, alert.Row);
        Assert.Equal("emotes/emotes.png", alert.Path);
        Assert.Equal(Art.Value.Crown, Art.Value.Emote("crown"));
        Assert.Null(Art.Value.Emote("no_such_emote"));
    }

    [Fact]
    public void While_its_scene_plays_the_figure_stays_and_the_bang_goes()
    {
        var mark = Assert.Single(StoryMarks.Of(With(new StoryMarker("b", Spot, "characters.mayor", null)), Ground, Art.Value));

        Assert.NotNull(mark.Figure);
        Assert.Null(mark.Emote);
        Assert.Null(mark.EmoteAt);
    }

    [Fact]
    public void A_spot_with_nobody_to_show_has_the_bang_at_a_figures_height()
    {
        var mark = Assert.Single(StoryMarks.Of(With(new StoryMarker("b", Spot, null, "alert")), Ground, Art.Value));
        var standIn = Art.Value.StandIn(UnitTone.Neutral);
        var feet = UnitPlace.On(Ground, Spot).Feet;

        Assert.Null(mark.Figure);
        Assert.Equal(feet.X, mark.EmoteAt!.Value.X);
        Assert.InRange(mark.EmoteAt.Value.Y, feet.Y - standIn.AnchorY, feet.Y - 1);
        Assert.True(mark.Unit!.MarksOnly);                                // drawn: the "!" alone, with nobody under it
        Assert.Equal(mark.EmoteAt, mark.Unit.EmoteAt);
        Assert.Equal(Art.Value.Emote("alert"), mark.Unit.Emote);
    }

    [Fact]
    public void The_walk_draws_a_bang_with_nobody_under_it()
    {
        var show = new WalkShow(Ground, Art.Value, null);
        show.Receive(With(new StoryMarker("b", Spot, null, "alert")));

        var bang = Assert.Single(show.Frame(1 / 60.0).Units, u => u.Id.Value == "marker:b");

        Assert.True(bang.MarksOnly);
        Assert.NotNull(bang.EmoteAt);
    }

    [Fact]
    public void A_figure_with_nothing_over_it_is_drawn_whole_and_a_spot_with_neither_isnt_drawn()
    {
        Assert.False(StoryMarks.Of(With(new StoryMarker("b", Spot, "characters.mayor", null)), Ground, Art.Value)[0].Unit!.MarksOnly);
        Assert.Null(StoryMarks.Of(With(new StoryMarker("b", Spot, null, null)), Ground, Art.Value)[0].Unit);
    }

    [Fact]
    public void A_figure_the_pack_doesnt_draw_is_a_stand_in()
        => Assert.Equal(Art.Value.StandIn(UnitTone.Neutral),
            Assert.Single(StoryMarks.Of(With(new StoryMarker("b", Spot, "characters.no_such", "alert")), Ground, Art.Value)).Figure!.Sprite);

    [Fact]
    public void A_spot_on_high_ground_lifts_the_figure()
    {
        var low = StoryMarks.Of(With(new StoryMarker("b", new Tile(19, 12), "characters.mayor", "alert")), Ground, Art.Value)[0];
        var high = StoryMarks.Of(With(new StoryMarker("b", new Tile(20, 11), "characters.mayor", "alert")), Ground, Art.Value)[0];

        Assert.True(high.Figure!.Feet.Y < low.Figure!.Feet.Y);           // one row back along x + y, but four steps up
    }

    [Fact]
    public void A_spot_off_the_map_isnt_drawn_and_no_markers_draw_nothing()
    {
        Assert.Empty(StoryMarks.Of(With(new StoryMarker("b", new Tile(400, 2), "characters.mayor", "alert")), Ground, Art.Value));
        Assert.Empty(StoryMarks.Of(View(new Tile(5, 5)), Ground, Art.Value));
    }

    [Fact]
    public void The_figure_sorts_among_the_party_by_its_tile()
    {
        var mark = StoryMarks.Of(With(new StoryMarker("b", Spot, "characters.mayor", "alert")), Ground, Art.Value)[0];
        var place = UnitPlace.On(Ground, Spot);
        var walker = UnitPlan.Walkers([new WalkerView("a", "a", "characters.mayor", true, false)],
            new Dictionary<string, UnitPlace> { ["a"] = place }, new Dictionary<string, UnitPose>(), Art.Value)[0];

        Assert.Equal(walker.Order, mark.Figure!.Order);
    }

    [Fact]
    public void The_walk_draws_the_figure_among_the_party_with_its_bang()
    {
        var show = new WalkShow(Ground, Art.Value, null);
        show.Receive(With(new StoryMarker("b", Spot, "characters.mayor", "alert")));

        var frame = show.Frame(1 / 60.0);

        var figure = Assert.Single(frame.Units, u => u.Id.Value == "marker:b");
        Assert.Equal(Art.Value.Emote("alert"), figure.Emote);
        Assert.NotNull(figure.EmoteAt);
        Assert.Equal(4, frame.Units.Length);                              // the party of three, and the figure
        Assert.Equal(frame.Units.OrderBy(u => u.Order).Select(u => u.Id), frame.Units.Select(u => u.Id));

        show.Receive(View(new Tile(5, 5)));                                // nothing on offer
        Assert.DoesNotContain(show.Frame(1 / 60.0).Units, u => u.Id.Value.StartsWith("marker:", StringComparison.Ordinal));
    }

    [Fact]
    public void A_fight_hides_the_figure_with_the_party()
    {
        var show = new WalkShow(Ground, Art.Value, null);
        show.Receive(With(new StoryMarker("b", Spot, "characters.mayor", "alert")));

        Assert.Empty(show.Frame(1 / 60.0, stage: new WalkStage(null, 0, null, true, Vector2.Zero)).Units);
    }

    [Fact]
    public void The_camera_moves_the_figure_and_its_bang_together()
    {
        var shift = new Vector2(7, -3);
        var still = StoryMarks.Of(With(new StoryMarker("b", Spot, "characters.mayor", "alert")), Ground, Art.Value)[0];
        var moved = StoryMarks.Of(With(new StoryMarker("b", Spot, "characters.mayor", "alert")), Ground, Art.Value, (feet, _) => feet + shift)[0];

        Assert.Equal(still.Figure!.Feet + shift, moved.Figure!.Feet);
        Assert.Equal(still.EmoteAt!.Value + shift, moved.EmoteAt);
    }
}
