using Tactics.Logic;
using static Tactics.Game.Tests.TacticsGameTests;

namespace Tactics.Game.Tests;

public sealed class CampLoreTests
{
    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public void Camp_is_a_boolean_tag_and_reflections_are_not_normal_beat_scenes(string value)
    {
        var lore = StoryTests.EditedLore(s => s[0]!["camp"] = System.Text.Json.Nodes.JsonNode.Parse(value));
        var scene = lore.Scenes.Single(s => s.Id == "ch1_mayor");
        Assert.Equal(value == "true", scene.Camp);
        Assert.Equal(!scene.Camp, lore.ScenesOf(StoryTests.Mayor).Any(s => s.Id == scene.Id));
    }

    [Theory]
    [InlineData("\"yes\"")]
    [InlineData("1")]
    [InlineData("null")]
    public void Runtime_rejects_invalid_camp_tags_like_the_checker(string value)
        => Assert.Throws<InvalidDataException>(() => StoryTests.EditedLore(s => s[0]!["camp"] = System.Text.Json.Nodes.JsonNode.Parse(value)));

    [Fact]
    public void A_camp_reflection_cannot_be_a_battle_aftermath()
        => Assert.Throws<InvalidDataException>(() => StoryTests.EditedLore(s =>
        { s[0]!["camp"] = true; s[0]!["after"] = true; }));

    [Fact]
    public void The_chapter_reflection_resumes_its_line_sets_a_vote_flag_and_plays_only_once_without_earning_a_rest()
    {
        var s = new Session(); s.ToWalking();
        var doc = s.Game.Document;
        var fire = Rules.InteractablesIn("ashen_ford").Single(t => t.Id == "af_road_campfire");
        var at = Rules.ReachFor("ashen_ford")!.Where(t => t != fire.At && Interactables.InReach(Rules.GroundFor("ashen_ford")!.Terrain, t, fire)).Order().First();
        doc = doc with { World = doc.World! with { At = at, Facing = fire.At, RestReady = true, Beats = [StoryTests.Mayor, StoryTests.FordChoice] } };
        s = Resume(doc);
        s.Send(0, "interact", new { target = fire.Id }); RestTests.Vote(s);
        for (var i = 0; i < 4; i++) s.Send(i, "campJob", new { job = i == 0 ? "watch" : "rest" });
        StoryVoteTests.Until(s, () => s.Game.Document.World!.Camp?.Stage == "talk", "the camp reflection");
        var view = s.Screen(0)!;
        Assert.Equal("talk", view.Data.GetProperty("campScene").GetProperty("stage").GetString());
        Assert.Equal(4, view.Data.GetProperty("campScene").GetProperty("members").GetArrayLength());
        var cursor = s.Game.Document.World!.Story!;
        Assert.Contains("Player1 takes the first watch", s.Host.Story!.Line!.Text);
        s = Resume(s.Game.Document);
        Assert.Equal(cursor.Node, s.Game.Document.World!.Story!.Node);
        StoryVoteTests.Until(s, () => s.Host.Story?.State == TacticsGame.StoryStates.Asking, "the camp vote");
        Assert.Equal("talk", s.Screen(0)!.Data.GetProperty("campScene").GetProperty("stage").GetString());
        for (var i = 0; i < 4; i++) s.Send(i, "vote", new { option = "hunger" });
        RestTests.Finish(s);
        Assert.Contains("camp_ch1_compassion", s.Game.Document.World!.Flags);
        Assert.Single(s.Game.Document.World.CampScenes);
        Assert.False(s.Game.Document.World.RestReady);
        doc = s.Game.Document with { World = s.Game.Document.World with { RestReady = true } };
        s = Resume(doc); s.Send(0, "interact", new { target = fire.Id }); RestTests.Vote(s);
        for (var i = 0; i < 4; i++) s.Send(i, "campJob", new { job = "rest" });
        StoryVoteTests.Until(s, () => s.Game.Document.World!.Camp?.Stage == "dawn", "dawn without a repeated reflection");
        Assert.Single(s.Game.Document.World!.CampScenes);
    }

    private static Session Resume(TacticsDocument doc)
    {
        var s = new Session(document: doc.ToJson(), game: new TacticsGame(Rules, lore: StoryTests.Lore) { CampDie = () => 20 });
        s.Harness.Resume(s.Lease); for (var i = 0; i < 4; i++) s.Harness.SetConnected(i, true); return s;
    }
}
