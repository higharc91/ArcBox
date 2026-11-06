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

    [Fact]
    public void Chapter_I_milestones_trigger_their_eligible_camp_talks_in_sequence_with_persistence_and_non_repetition()
    {
        var s = new Session(); s.ToWalking();
        var fire = Rules.InteractablesIn("ashen_ford").Single(t => t.Id == "af_road_campfire");
        var at = Rules.ReachFor("ashen_ford")!.Where(t => t != fire.At && Interactables.InReach(Rules.GroundFor("ashen_ford")!.Terrain, t, fire)).Order().First();
        var doc = s.Game.Document with { World = s.Game.Document.World! with { At = at, Facing = fire.At, RestReady = true } };

        // Milestone 1: The Ford Choice
        doc = doc with { World = doc.World! with { RestReady = true, Beats = [StoryTests.Mayor, StoryTests.FordChoice] } };
        s = RunCampTalk(doc, fire.Id, "hunger");
        Assert.Equal("ch1_camp_reflection", s.Game.Document.World!.CampScenes[^1]);
        Assert.Contains("camp_ch1_compassion", s.Game.Document.World.Flags);
        Assert.Single(s.Game.Document.World.CampScenes);

        // Milestone 2 (force): The Ford Assault
        doc = s.Game.Document with { World = s.Game.Document.World with { RestReady = true, Beats = [.. s.Game.Document.World.Beats, "ch1_ford_assault"], Flags = s.Game.Document.World.Flags.Add("ch1_skarrow_slain") } };
        s = RunCampTalk(doc, fire.Id, "duty");
        Assert.Equal("ch1_camp_force", s.Game.Document.World!.CampScenes[^1]);
        Assert.Contains("camp_ch1_duty", s.Game.Document.World.Flags);
        Assert.Equal(2, s.Game.Document.World.CampScenes.Length);

        // Milestone 3: Old Maud
        doc = s.Game.Document with { World = s.Game.Document.World with { RestReady = true, Beats = [.. s.Game.Document.World.Beats, StoryTests.OldMaud], Flags = s.Game.Document.World.Flags.Add("ch1_met_maud") } };
        s = RunCampTalk(doc, fire.Id, "seek");
        Assert.Equal("ch1_camp_maud", s.Game.Document.World!.CampScenes[^1]);
        Assert.Contains("camp_ch1_seek", s.Game.Document.World.Flags);
        Assert.Equal(3, s.Game.Document.World.CampScenes.Length);

        // Milestone 4: Ashmaw defeated
        doc = s.Game.Document with { World = s.Game.Document.World with { RestReady = true, Beats = [.. s.Game.Document.World.Beats, "hollow_lair"], Flags = s.Game.Document.World.Flags.Add("sq_ashmaw_slain") } };
        s = RunCampTalk(doc, fire.Id, "pity");
        Assert.Equal("ch1_camp_ashmaw", s.Game.Document.World!.CampScenes[^1]);
        Assert.Contains("camp_ch1_pity", s.Game.Document.World.Flags);
        Assert.Equal(4, s.Game.Document.World.CampScenes.Length);

        // Subsequent camp: all eligible Chapter I camp scenes seen, goes straight to dawn without repeating
        doc = s.Game.Document with { World = s.Game.Document.World with { RestReady = true } };
        s = Resume(doc); s.Send(0, "interact", new { target = fire.Id }); RestTests.Vote(s);
        for (var i = 0; i < 4; i++) s.Send(i, "campJob", new { job = "rest" });
        StoryVoteTests.Until(s, () => s.Game.Document.World!.Camp?.Stage == "dawn", "dawn without repeating camp scenes");
        Assert.Equal(4, s.Game.Document.World!.CampScenes.Length);
    }

    [Fact]
    public void Parley_branch_triggers_parley_specific_camp_talk()
    {
        var s = new Session(); s.ToWalking();
        var fire = Rules.InteractablesIn("ashen_ford").Single(t => t.Id == "af_road_campfire");
        var at = Rules.ReachFor("ashen_ford")!.Where(t => t != fire.At && Interactables.InReach(Rules.GroundFor("ashen_ford")!.Terrain, t, fire)).Order().First();
        var doc = s.Game.Document with { World = s.Game.Document.World! with
        {
            At = at, Facing = fire.At, RestReady = true,
            Beats = [StoryTests.Mayor, StoryTests.FordChoice, "ch1_river_crossing"],
            Flags = s.Game.Document.World.Flags.Add("ch1_skarrow_spared"),
            CampScenes = ["ch1_camp_reflection"]
        } };
        s = RunCampTalk(doc, fire.Id, "trust");
        Assert.Equal("ch1_camp_parley", s.Game.Document.World!.CampScenes[^1]);
        Assert.Contains("camp_ch1_trust", s.Game.Document.World.Flags);
        Assert.Equal(2, s.Game.Document.World.CampScenes.Length);
    }

    private static Session RunCampTalk(TacticsDocument doc, string fireId, string voteOption)
    {
        var s = Resume(doc);
        s.Send(0, "interact", new { target = fireId }); RestTests.Vote(s);
        for (var i = 0; i < 4; i++) s.Send(i, "campJob", new { job = i == 0 ? "watch" : "rest" });
        StoryVoteTests.Until(s, () => s.Game.Document.World!.Camp?.Stage == "talk", "camp talk starts");
        StoryVoteTests.Until(s, () => s.Host.Story?.State == TacticsGame.StoryStates.Asking, "camp vote starts");
        for (var i = 0; i < 4; i++) s.Send(i, "vote", new { option = voteOption });
        RestTests.Finish(s);
        return s;
    }

    private static Session Resume(TacticsDocument doc)
    {
        var s = new Session(document: doc.ToJson(), game: new TacticsGame(Rules, lore: StoryTests.Lore) { CampDie = () => 20 });
        s.Harness.Resume(s.Lease); for (var i = 0; i < 4; i++) s.Harness.SetConnected(i, true); return s;
    }
}
