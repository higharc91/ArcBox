using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Tactics.Logic;
using static Tactics.Game.Tests.ExplorationTests;
using static Tactics.Game.Tests.TacticsGameTests;
using static Tactics.Game.Tests.StoryVoteTests;

namespace Tactics.Game.Tests;

public sealed class ConversationQuestTests
{
    private static readonly Lazy<LoreBook> Book = new(() => LoreBook.Load(ContentRules()));
    internal static Session At(string person, params string[] flags)
    {
        var p = Rules.RegionFor("ashen_ford")!.Def.People.Single(p => p.Id == person);
        var document = DocumentAt(Saved(p.At) with { Flags = [.. flags] });
        var s = new Session(document: document, game: new TacticsGame(Rules, lore: Book.Value));
        s.Harness.StartCampaign(s.Lease);
        return s;
    }
    internal static int Lead(Session s) => s.Harness.Players.ToList().FindIndex(p => p.Id == s.Game.Leader!.Id);
    internal static void Talk(Session s, string person) => s.Send(Lead(s), "interact", new { target = "person:" + person });
    internal static void Choose(Session s, string option)
    {
        Assert.Equal(TacticsGame.StoryStates.Asking, s.Host.Story!.State);
        for (var i = 0; i < 4; i++) s.Send(i, "vote", new { option });
    }
    internal static void WalkAgain(Session s) => Until(s, () => s.Game.Phase == TacticsPhase.Exploration, "conversation ending");

    [Fact]
    public void Accepting_a_quest_sets_only_side_flags_and_autopins_it()
    {
        var s = At("halden_varrow");
        Talk(s, "halden_varrow");
        Assert.Equal("offer", s.Game.Document.World!.Story!.Node);
        Assert.NotEmpty(s.Host.Explore!.Cast ?? []);
        Choose(s, "accept"); WalkAgain(s);
        Assert.Equal(["sq_grain_asked"], s.Game.Document.World!.Flags);
        Assert.Equal("sq_missing_grain", s.Game.Document.World.PinnedQuest);
        Assert.Equal("af_granary_sack", Book.Value.Quests.First(q => q.Id == "sq_missing_grain").Stages[0].Target!.Thing);
        Assert.NotNull(s.Host.Explore!.Pinned!.Target);
    }

    [Fact]
    public void Completing_pays_once_in_the_same_document_and_includes_away_members()
    {
        var s = At("halden_varrow", "sq_grain_asked", "sq_grain_clue", "sq_grain_tally");
        s.Harness.SetConnected(3, false);
        var before = s.Game.Document;
        Talk(s, "halden_varrow"); Choose(s, "quiet"); WalkAgain(s);
        var after = TacticsDocument.Read(s.Lease.Document);
        Assert.Contains("sq_grain_done", after.World!.Flags);
        Assert.Equal(["sq_missing_grain"], after.World.QuestsRewarded);
        Assert.Equal(30, after.Members.Values.Sum(m => m.Gold) - before.Members.Values.Sum(m => m.Gold));
        foreach (var (id, member) in after.Members) Assert.Equal(before.Members[id].Xp + 100, member.Xp);
        Assert.Equal(before.Members["pr_member4"].Gold, after.Members["pr_member4"].Gold);
        var next = new Session(document: after.ToJson(), game: new TacticsGame(Rules, lore: Book.Value));
        next.Harness.StartCampaign(next.Lease); Talk(next, "halden_varrow"); WalkAgain(next);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(after.Members), System.Text.Json.JsonSerializer.Serialize(next.Game.Document.Members));
    }

    [Fact]
    public void An_item_reward_goes_to_the_starter_and_does_not_relight_the_ember()
    {
        var s = At("tobin_fell", "sq_forge_asked", "sq_forge_fuel");
        var by = s.Game.Leader!.Profile!.Value.Value;
        var before = s.Game.Document.Members[by].Character.Bag.Where(i => i.Item.Value == "oil_flask").Sum(i => i.Count);
        Talk(s, "tobin_fell"); Choose(s, "give"); WalkAgain(s);
        Assert.Equal(before + 1, s.Game.Document.Members[by].Character.Bag.Where(i => i.Item.Value == "oil_flask").Sum(i => i.Count));
        Assert.DoesNotContain("ch1_ember_found", s.Game.Document.World!.Flags);
    }

    [Fact]
    public void A_conversation_restores_the_pending_ford_cursor_and_resumes_its_own_saved_line()
    {
        var s = At("halden_varrow", "sq_grain_asked");
        var original = s.Game.Document with { World = s.Game.Document.World! with { Story = new StorySave("ch1_ford_assault", StorySave.Fight, null, null, 0, 1) } };
        var next = new Session(document: original.ToJson(), game: new TacticsGame(Rules, lore: Book.Value));
        next.Harness.StartCampaign(next.Lease); Talk(next, "halden_varrow");
        var talking = next.Game.Document;
        Assert.Equal("waiting", talking.World!.Story!.Node);
        var again = new Session(document: talking.ToJson(), game: new TacticsGame(Rules, lore: Book.Value));
        again.Harness.StartCampaign(again.Lease);
        Assert.Equal("waiting", again.Game.Document.World!.Story!.Node);
        WalkAgain(again);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(original.World.Story), System.Text.Json.JsonSerializer.Serialize(again.Game.Document.World!.Story));
    }

    [Fact]
    public void Shared_log_permissions_are_rechecked_and_pin_survives_a_new_session()
    {
        var s = At("halden_varrow", "sq_grain_asked");
        var lead = Lead(s); var follower = (lead + 1) % 4;
        s.Send(follower, "questLog", new { action = "open" }); Assert.Null(s.Host.Explore!.QuestLog);
        s.Send(follower, "questLog", new { action = "pin", quest = "sq_missing_grain" }); Assert.Null(s.Game.Document.World!.PinnedQuest);
        s.Send(lead, "questLog", new { action = "open" }); Assert.NotNull(s.Host.Explore!.QuestLog);
        s.Send(follower, "questLog", new { action = "close" }); Assert.NotNull(s.Host.Explore!.QuestLog);
        var at = s.Game.Document.World!.At;
        s.Send(lead, "walk", new { dir = "e" }); s.Harness.Advance(TimeSpan.FromSeconds(1)); Assert.Equal(at, s.Game.Document.World.At);
        s.Send(lead, "questLog", new { action = "pin", quest = "sq_missing_grain" });
        var next = new Session(document: s.Game.Document.ToJson(), game: new TacticsGame(Rules, lore: Book.Value));
        next.Harness.StartCampaign(next.Lease);
        Assert.Null(next.Host.Explore!.QuestLog); Assert.Equal("sq_missing_grain", next.Host.Explore.Pinned!.Id);
        s.Harness.SetConnected(lead, false); Assert.Null(s.Host.Explore!.QuestLog);
        Assert.Equal("sq_missing_grain", s.Game.Document.World.PinnedQuest);
    }

    [Fact]
    public void A_completed_pin_briefly_shows_the_result_then_unpins()
    {
        var s = At("halden_varrow", "sq_grain_asked", "sq_grain_tally");
        s.Send(Lead(s), "questLog", new { action = "pin", quest = "sq_missing_grain" });
        Talk(s, "halden_varrow"); Choose(s, "quiet");
        Until(s, () => s.Game.Document.World!.Flags.Contains("sq_grain_done"), "completion");
        Assert.Equal("done", s.Host.Explore!.Pinned!.Status);
        WalkAgain(s);
        Assert.Equal("complete", s.Host.Explore.QuestEvent!.Kind);
        s.Harness.Advance(TimeSpan.FromSeconds(8));
        Assert.Null(s.Game.Document.World!.PinnedQuest); Assert.Null(s.Host.Explore!.Pinned);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_waypoint_never_points_to_an_unfound_thing_or_an_absent_person(bool person)
    {
        var rules = EditedRules(dir =>
        {
            if (person)
            {
                var path = Path.Combine(dir, "world", "ashen_ford.json");
                var root = JsonNode.Parse(File.ReadAllText(path))!;
                var npc = root["people"]!.AsArray().Single(p => p!["id"]!.GetValue<string>() == "celandine_marrowby")!;
                npc["unless"] = new JsonArray("sq_grain_tally"); File.WriteAllText(path, root.ToJsonString());
            }
            else
            {
                var path = Path.Combine(dir, "interactables", "ashen_ford.json");
                var root = JsonNode.Parse(File.ReadAllText(path))!;
                var thing = root["interactables"]!.AsArray().Single(t => t!["id"]!.GetValue<string>() == "af_granary_sack")!;
                thing["hidden"] = 30; File.WriteAllText(path, root.ToJsonString());
            }
        });
        var s = new Session(document: DocumentAt(Saved(new Tile(29,62)) with { Flags = person ? ["sq_grain_asked", "sq_grain_tally"] : ["sq_grain_asked"], PinnedQuest = "sq_missing_grain" }),
            game: new TacticsGame(rules, lore: Book.Value));
        s.Harness.StartCampaign(s.Lease);
        Assert.Null(s.Host.Explore!.Pinned!.Target);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Every_quest_can_finish_after_force_parley_or_a_ford_loss(int ending)
    {
        var flag = ending == 1 ? "ch1_skarrow_spared" : "ch1_skarrow_slain";
        foreach (var (person, flags, answer, done) in new[]
        {
            ("halden_varrow", new[] { "sq_grain_asked", "sq_grain_tally" }, "quiet", "sq_grain_done"),
            ("tobin_fell", new[] { "sq_forge_asked", "sq_forge_fuel" }, "give", "sq_forge_done"),
            ("pip_ashdown", new[] { "sq_whistle_asked", "sq_whistle_found" }, "return", "sq_whistle_done"),
        })
        {
            var at = Rules.RegionFor("ashen_ford")!.Def.People.Single(p => p.Id == person).At;
            var under = ending == 2 ? new StorySave("ch1_ford_assault", StorySave.Fight, null, null, 0, 0) : null;
            var world = Saved(at) with { Flags = [flag, ..flags], Beats = ending == 2 ? [] : [ending == 1 ? "ch1_river_crossing" : "ch1_ford_assault"], Story = under };
            var s = new Session(document: DocumentAt(world), game: new TacticsGame(Rules, lore: Book.Value));
            s.Harness.StartCampaign(s.Lease); Talk(s,person); Choose(s,answer); WalkAgain(s);
            Assert.Contains(done,s.Game.Document.World!.Flags);
            Assert.Equal(ending == 2 ? [flag] : new[] { flag, "ch1_ford_settled" }, s.Game.Document.World.Flags.Where(f => f.StartsWith("ch",StringComparison.Ordinal)));
            Assert.Equal(under?.Beat,s.Game.Document.World.Story?.Beat);
        }
    }

    [Fact]
    public void A_saved_party_check_resumes_the_roller_pick_and_never_rolls_twice()
    {
        var s = At("corvin_ashby", "sq_forge_asked"); Talk(s,"corvin_ashby"); Choose(s,"arcana");
        Until(s,()=>s.Host.Story!.State==TacticsGame.StoryStates.PickRoller,"roller pick");
        var next = new Session(document:s.Game.Document.ToJson(),game:new TacticsGame(Rules,lore:Book.Value));
        next.Harness.StartCampaign(next.Lease);
        Assert.Equal(TacticsGame.StoryStates.PickRoller,next.Host.Story!.State);
        Assert.Equal("arcana",next.Game.Document.World!.Story!.Checking);
        for(var n=0;n<4;n++)next.Send(n,"pickRoller",new {member="pr_member1"});
        Until(next,()=>next.Game.Document.World!.Story!.Roll is not null,"saved roll");
        var roll=next.Game.Document.World!.Story!.Roll;
        var again=new Session(document:next.Game.Document.ToJson(),game:new TacticsGame(Rules,lore:Book.Value));
        again.Harness.StartCampaign(again.Lease);
        Assert.Equal(roll,again.Game.Document.World!.Story!.Roll);
    }

    [Fact]
    public void End_session_keeps_the_conversation_for_the_next_night()
    {
        var s=At("halden_varrow");Talk(s,"halden_varrow");
        var node=s.Game.Document.World!.Story!.Node;
        s.Send(0,"endSession");Assert.True(s.Harness.IsOver);Assert.Empty(s.Harness.ActiveTimers);
        var next=new Session(document:s.Lease.Document,game:new TacticsGame(Rules,lore:Book.Value));
        next.Harness.StartCampaign(next.Lease);
        Assert.Equal(node,next.Game.Document.World!.Story!.Node);Assert.NotNull(next.Game.Document.World.Story.Person);
    }

    [Fact]
    public void An_explicit_failure_choice_shows_its_result_then_unpins_without_rewards()
    {
        var folder=Directory.CreateTempSubdirectory("tactics-failed-quest-");
        try
        {
            var from=Path.Combine(ContentRules(),"lore");
            foreach(var file in Directory.EnumerateFiles(from,"*.json",SearchOption.AllDirectories))
            {
                var to=Path.Combine(folder.FullName,"lore",Path.GetRelativePath(from,file));
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);File.Copy(file,to);
            }
            var questPath=Path.Combine(folder.FullName,"lore","quests.json");
            var quests=JsonNode.Parse(File.ReadAllText(questPath))!;
            quests["quests"]![0]!["failed"]=JsonNode.Parse("""{"when":["sq_grain_failed"],"log":"We withdrew the accusation."}""");
            File.WriteAllText(questPath,quests.ToJsonString());
            var path=Path.Combine(folder.FullName,"lore","conversations","halden_varrow.json");
            var convo=JsonNode.Parse(File.ReadAllText(path))!;
            convo["nodes"]!.AsArray().Single(n=>n!["id"]!.GetValue<string>()=="report")!["options"]!.AsArray()
                .Add(JsonNode.Parse("""{"id":"withdraw","label":"Withdraw the accusation.","to":"goodbye","sets":["sq_grain_failed"]}"""));
            File.WriteAllText(path,convo.ToJsonString());
            var book=LoreBook.Load(folder.FullName);
            var world=Saved(new Tile(29,62)) with {Flags=["sq_grain_asked","sq_grain_tally"],PinnedQuest="sq_missing_grain"};
            var s=new Session(document:DocumentAt(world),game:new TacticsGame(Rules,lore:book));s.Harness.StartCampaign(s.Lease);
            var before=s.Game.Document.Members.Values.Sum(m=>m.Xp);Talk(s,"halden_varrow");Choose(s,"withdraw");WalkAgain(s);
            Assert.Equal("failed",s.Host.Explore!.Pinned!.Status);Assert.Equal(before,s.Game.Document.Members.Values.Sum(m=>m.Xp));
            Assert.True(s.Game.Document.World!.QuestsRewarded.IsDefaultOrEmpty);
            s.Harness.Advance(TimeSpan.FromSeconds(8));Assert.Null(s.Game.Document.World.PinnedQuest);
        }
        finally {folder.Delete(recursive:true);}
    }
}
