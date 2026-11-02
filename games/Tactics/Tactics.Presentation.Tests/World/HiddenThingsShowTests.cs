using System.Numerics;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Beats;
using Tactics.Presentation.Board;
using Tactics.Presentation.Story;
using Tactics.Presentation.Tests.Board;
using Tactics.Presentation.World;
using static Tactics.Presentation.Tests.World.ThingShowTests;
using static Tactics.Presentation.Tests.World.WalkFeed;

namespace Tactics.Presentation.Tests.World;

/// <summary>
/// Hidden things on the TV (interactables design §7.6): a notice noticed pops in with a "?" and its glint, notices and hidden paths draw as
/// markers of their own colours, flat on the ground, until their art exists; and a search's check shows no DC.
/// </summary>
public sealed class HiddenThingsShowTests
{
    private static readonly Lazy<UnitArt> Units = new(() => UnitArt.Load(Pack.Root));
    private static readonly Lazy<ThingArt> Things = new(() => ThingArt.Load(Pack.Root));
    private static readonly Lazy<FxArt> Fx = new(() => FxArt.Load(Pack.Root));
    private static readonly BoardTerrain Ground = WalkFeed.Ground(30, 30);

    private static ThingView Tracks() => new("tracks", "tracks", new Tile(11, 13), "sw", "interactables.tracks", "noticed", null, "wolf tracks", false, false);

    private static ThingView Remains() => new("bones", "remains", new Tile(13, 11), "sw", "interactables.remains", "noticed", "props.ribcage", "remains", false, false);

    private static ThingView Path() => new("ford", "hidden_path", new Tile(9, 9), "sw", "interactables.hidden_path", "found", null, "goat track", false, false);

    private static ThingShow Show() => new(Ground, Units.Value, Things.Value, Fx.Value);

    private static BoardUnit Of(ThingFrame frame, string id) => frame.Units.Single(u => u.Id.Value == $"thing:{id}");

    [Fact]
    public void A_notice_noticed_pops_in_with_a_question_and_a_glint_and_settles()
    {
        var show = Show();
        show.Receive(With([Chest()]));
        show.Frame(Frame);
        show.Receive(With([Chest(), Tracks()], E(1, "tracks", ThingEvents.Noticed)));

        var start = show.Frame(Frame);
        var tracks = Of(start, "tracks");
        Assert.True(tracks.Thing!.Alpha < 0.2f && tracks.Thing.Scale.X < 0.3f);
        Assert.Equal(Units.Value.Emote("question"), tracks.Emote);
        // The pack has no fx.notice yet: the find's puff plays meanwhile.
        Assert.Null(Fx.Value.Clip(ThingShow.NoticeFx));
        Assert.Contains(start.Effects, e => e.Path.StartsWith("fx/poof", StringComparison.Ordinal));
        var settled = Of(show.Frame(ThingShow.FoundSeconds), "tracks");
        Assert.Equal((Vector2.One, 1f), (settled.Thing!.Scale, settled.Thing.Alpha));
        Assert.Null(settled.Emote);
    }

    [Fact]
    public void Notices_and_paths_draw_as_flat_markers_of_their_own_colours_until_their_art_exists()
    {
        var show = Show();
        show.Receive(With([Chest(), Tracks(), Path(), Remains()]));
        var frame = show.Frame(Frame);

        var tracks = Of(frame, "tracks").Thing!;
        Assert.Equal((ThingShow.Clue, ThingShow.Clue), (tracks.Marker!.Line, tracks.Marker.Fill));
        Assert.Equal(["wolf tracks"], tracks.Label!.Lines);
        Assert.Equal(ThingShow.Clue, tracks.Label.Colour);
        var path = Of(frame, "ford").Thing!;
        Assert.Equal(ThingShow.Trail, path.Marker!.Line);
        Assert.Null(path.Marker.Slab);
        Assert.NotEqual(ThingShow.Clue, ThingShow.Gold);
        Assert.NotEqual(ThingShow.Trail, ThingShow.Clue);
        Assert.All(new[] { Tracks(), Path(), Remains() }, t => Assert.True(Things.Value.Pick(t).Flat, t.Id));
        Assert.Equal((ThingSource.StandIn, "props.ribcage"), (Things.Value.Pick(Remains()).Source, Things.Value.Pick(Remains()).Key));
    }

    [Fact]
    public void A_notice_wants_a_quiet_sound_of_its_own_then_the_finds()
        => Assert.Equal(["notice", "secret_found"], ThingSounds.Wanted(ThingEvents.Noticed, "tracks", "noticed"));

    [Fact]
    public void A_search_check_shows_no_dc_and_its_roll_says_what_it_found()
    {
        var check = new StoryCheckView("investigation", null, "Search the area", [], "m1", Rolls.Flat(11, 5, 14) with { Vs = 0 });
        var view = new StoryView(3, InteractScenes.SearchId, "Search", TacticsGame.StoryStates.Rolled, null, null, check, null);
        var panel = StoryScreen.Of(view, Tactics.Presentation.Tests.Story.StoryFeed.Art, null).Check!;
        Assert.Equal(StoryScreen.HiddenDc, panel.Dc);
        Assert.Equal("11 + 5 = 16", panel.Roll!.Sum);
        Assert.Equal("Found something", panel.Roll.Verdict);
        Assert.Equal("Nothing found", StoryScreen.Reveal(Rolls.Flat(2, 1, 14) with { Vs = 0 }, "Bo", StoryScreen.Tumble, hidden: true).Verdict);
        Assert.Equal("14 + 5 = 19 vs DC 15", StoryScreen.Reveal(Rolls.Flat(14, 5, 15), "Bo", StoryScreen.Tumble).Sum);       // any other check as before
    }
}
