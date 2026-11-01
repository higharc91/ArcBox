using Tactics.Presentation.World;

namespace Tactics.Presentation.Tests.World;

/// <summary><see cref="WorldCover"/>: what the screen's own nodes do over the world, which Godot only applies.</summary>
public sealed class WorldCoverTests
{
    [Fact]
    public void While_the_world_builds_the_whole_title_card_is_up_and_no_panels()
    {
        var cover = new WorldCover();
        cover.Frame(0.5, WorldStage.Building, 0);

        Assert.Equal(new CoverLook(1, true, true, 0), cover.Look(walking: true, battle: false));
    }

    [Fact]
    public void Once_revealed_only_the_world_and_its_panels_show()
    {
        var cover = new WorldCover();
        cover.Frame(0.1, WorldStage.Open, 1);

        var look = cover.Look(walking: true, battle: false);
        Assert.Equal(new CoverLook(0, false, false, 1), look);
        Assert.True(look.PanelsShown);
    }

    [Fact]
    public void In_a_fight_the_board_has_the_header_and_tables_place_and_the_backdrop_stays_under_it()
    {
        var cover = new WorldCover();
        cover.Frame(0.1, WorldStage.Open, 1);

        Assert.Equal(new CoverLook(1, true, false, 0), cover.Look(walking: false, battle: true));
    }

    [Fact]
    public void Off_the_world_and_out_of_a_fight_the_whole_screen_shows()
        => Assert.Equal(new CoverLook(1, true, true, 0), new WorldCover().Look(walking: false, battle: false));

    [Fact]
    public void Back_from_a_fight_the_card_goes_up_again_and_comes_down_with_the_fade()
    {
        var cover = new WorldCover();
        cover.Frame(0.1, WorldStage.Open, 1);

        cover.Opened();
        Assert.Equal(WorldReveal.Loading, cover.Reveal);
        cover.Frame(0.1, WorldStage.Open, 0.5);
        Assert.Equal(0.5f, cover.Look(walking: true, battle: false).Card, 3);
    }

    [Fact]
    public void A_world_that_fails_is_given_up()
        => Assert.False(new WorldCover().Frame(0.01, WorldStage.Failed, 0));

    // Review: a world that never opens kept the card up for ever. It's given up as a failed one is, after OpenTimeout of frames.
    [Fact]
    public void A_world_that_never_opens_is_given_up_after_the_timeout()
    {
        var cover = new WorldCover();
        var (frames, frame) = (0, 1 / 30.0);
        while (cover.Frame(frame, WorldStage.Building, 0) && frames < 2 * WorldTuning.OpenTimeout / frame)
            frames++;

        Assert.InRange(frames * frame, WorldTuning.OpenTimeout - 2 * frame, WorldTuning.OpenTimeout);
    }

    [Fact]
    public void An_open_world_never_times_out_and_opening_again_starts_the_wait_again()
    {
        var cover = new WorldCover();
        Assert.True(cover.Frame(WorldTuning.OpenTimeout * 0.9, WorldStage.Building, 0));
        Assert.True(cover.Frame(WorldTuning.OpenTimeout * 5, WorldStage.Open, 1));

        cover.Opened();
        Assert.True(cover.Frame(WorldTuning.OpenTimeout * 0.9, WorldStage.Building, 0));
        Assert.False(cover.Frame(WorldTuning.OpenTimeout * 0.2, WorldStage.Building, 0));
    }
}
