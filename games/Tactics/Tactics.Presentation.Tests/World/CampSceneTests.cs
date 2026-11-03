using Tactics.Game;
using Tactics.Presentation.Board;
using Tactics.Presentation.World;

namespace Tactics.Presentation.Tests.World;

public sealed class CampSceneTests
{
    private static CampView Camp(string stage) => new("camp", "af_road_campfire", stage, new(3, 3), 0,
        [new("a", "Ana", "cook", new(1, 3), false), new("b", "Bo", null, new(4, 3), false)], null, 1, null);
    [Fact]
    public void Progress_counts_saved_choices_and_results_instead_of_elapsed_time()
    {
        Assert.Equal("1 / 2 jobs chosen", CampScene.Progress(Camp("jobs")));
        Assert.Equal("1 / 2 jobs resolved", CampScene.Progress(Camp("results")));
        Assert.Equal("The party settles down to sleep", CampScene.Progress(Camp("sleep")));
    }
    [Fact]
    public void Night_is_distinct_from_dawn_even_when_the_region_is_already_at_dusk()
    {
        var ambient = new Rgb(.56f, .44f, .54f);
        var night = CampLight.Grade(Camp("jobs"), 30, ambient, false).Times(ambient);
        var dawn = CampLight.Grade(Camp("dawn"), 5, ambient, false).Times(ambient);
        Assert.InRange(night.R, .129f, .131f);
        Assert.InRange(night.G, .159f, .161f);
        Assert.InRange(dawn.G, .87f, .89f);
        Assert.True(dawn.G > night.G * 4);
        Assert.Equal(CampLight.Grade(null, 0, ambient, true), CampLight.Grade(Camp("dawn"), 8, ambient, true));
        Assert.Equal(new Rgb(1, 1, 1), CampLight.Grade(null, 0, ambient, false));
    }
    [Fact]
    public void Jobs_sleep_and_morning_choose_real_pack_poses()
    {
        var camp = Camp("jobs"); var person = camp.People[0];
        Assert.Equal("walk", CampScene.Animation(camp, person, 1));
        Assert.Equal("item", CampScene.Animation(camp, person, 3));
        Assert.Equal("kneel", CampScene.Animation(camp, person with { Job = "tend" }, 3));
        Assert.Equal("talk", CampScene.Animation(camp, person with { Job = "rally" }, 3));
        Assert.Equal("sit", CampScene.Animation(camp, person with { Job = null }, 3));
        Assert.Equal("sit", CampScene.Animation(Camp("results"), person with { Outcome = "success" }, 3));
        Assert.Equal("doze", CampScene.Animation(Camp("sleep"), person, 1));
        Assert.Equal("idle", CampScene.Animation(Camp("dawn"), person, 5));
    }
}
