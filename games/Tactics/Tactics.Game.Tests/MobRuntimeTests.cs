using System.Text.Json;
using Tactics.Logic;
using static Tactics.Game.Tests.ExplorationTests;
using static Tactics.Game.Tests.TacticsGameTests;

namespace Tactics.Game.Tests;

public sealed class MobRuntimeTests
{
    private static RulesPack WithMobs()
    {
        var folder = Path.Combine(Path.GetTempPath(), "arcbox-mob-runtime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "mobs"));
        try
        {
            File.WriteAllText(Path.Combine(folder, "manifest.json"), """
                {"id":"mob-runtime-test","rulesVersion":1,"requires":["base"],"files":["mobs/test.json"]}
                """);
            File.WriteAllText(Path.Combine(folder, "mobs/test.json"), """
                {"region":"cinder_hollow","mobs":[{"id":"test_pack","encounter":"hollow_throat","arena":"hollow_throat","patch":[[7,9],[8,9]],"show":["characters.kobold"],"pace":2,"xp":75,"gold":10}]}
                """);
            return RulesPack.Load([Path.Combine(ContentBase(), "rules"), folder], ArtManifests.Load(ContentBase()));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void A_step_without_nearby_mobs_omits_the_optional_view_fields()
    {
        var rules = WithMobs();
        var groups = rules.MobsIn("ashen_ford");
        var far = rules.ReachFor("ashen_ford")!.OrderByDescending(t => groups.Min(m => m.Patch.Min(p => Math.Max(Math.Abs(t.X - p.X), Math.Abs(t.Y - p.Y))))).First();
        var s = new Session(document: DocumentAt(Saved(far)), game: new TacticsGame(rules));
        s.Harness.StartCampaign(s.Lease);
        Assert.Null(s.Host.Explore!.Mobs);
        Assert.Null(s.Host.Explore.Danger);
        Assert.DoesNotContain("\"mobs\"", JsonSerializer.Serialize(s.Host.Explore, ArcBox.Sdk.ArcJson.Options));
    }

    [Fact]
    public void A_saved_defeated_group_is_absent_after_restart()
    {
        var world = Saved(new(5, 2), "cinder_hollow") with { MobsBeaten = ["test_pack"], MobChapter = 1 };
        var s = new Session(document: DocumentAt(world), game: new TacticsGame(WithMobs()));
        s.Harness.StartCampaign(s.Lease);
        Assert.Null(s.Host.Explore!.Mobs);
        s.Harness.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
    }

    [Fact]
    public void Living_mobs_are_visible_but_do_not_chase_a_distant_party()
    {
        var s = new Session(document: DocumentAt(Saved(new(5, 2), "cinder_hollow")), game: new TacticsGame(WithMobs()));
        s.Harness.StartCampaign(s.Lease);
        Assert.Single(s.Host.Explore!.Mobs!.Value);
        s.Harness.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
        Assert.Contains(s.Host.Explore.Mobs!.Value[0].At, new[] { new Tile(7, 9), new Tile(8, 9) });
    }
    [Fact]
    public void Leader_contact_starts_the_stamped_encounter_and_records_its_group()
    {
        var s = new Session(document: DocumentAt(Saved(new(8, 9), "cinder_hollow")), game: new TacticsGame(WithMobs()));
        s.Harness.StartCampaign(s.Lease);
        s.Harness.Advance(TimeSpan.FromMilliseconds(300));
        Assert.Equal("test_pack", TacticsDocument.Read(s.Lease.Document).World!.MobAmbush);
        s.Harness.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(TacticsPhase.Battle, s.Game.Phase);
        Assert.Equal("test_pack", TacticsDocument.Read(s.Lease.Document).Battle!.Mob);
    }

    [Fact]
    public void Ending_and_restarting_preserves_the_roaming_position_and_cursor()
    {
        var rules = WithMobs();
        var s = new Session(document: DocumentAt(Saved(new(5, 2), "cinder_hollow")), game: new TacticsGame(rules));
        s.Harness.StartCampaign(s.Lease);
        s.Harness.Advance(TimeSpan.FromSeconds(3));
        var before = s.Host.Explore!.Mobs!.Value[0];
        s.Send(0, "endSession");
        var again = new Session(document: s.Lease.Document, game: new TacticsGame(rules));
        again.Harness.StartCampaign(again.Lease);
        Assert.Equal(before.At, again.Host.Explore!.Mobs!.Value[0].At);
        Assert.Equal(before.Step, again.Host.Explore.Mobs.Value[0].Step);
    }

    [Theory]
    [InlineData(BattleOutcome.Won)]
    [InlineData(BattleOutcome.Lost)]
    public void Results_pay_only_winners_and_only_winners_remove_the_group(BattleOutcome outcome)
    {
        var s = new Session(document: DocumentAt(Saved(new(8, 9), "cinder_hollow")), game: new TacticsGame(WithMobs()));
        s.Harness.StartCampaign(s.Lease);
        var before = s.Game.Document;
        s.Harness.Advance(TimeSpan.FromMilliseconds(300));
        s.Harness.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(TacticsPhase.Battle, s.Game.Phase);
        StageEndTests.EndOnNextCommand(s, outcome);
        StageEndTests.PlayOn(s);
        Assert.Equal(TacticsPhase.Results, s.Game.Phase);
        var after = s.Game.Document;
        foreach (var (id, member) in before.Members)
            Assert.Equal(member.Xp + (outcome == BattleOutcome.Won ? 75 : 0), after.Members[id].Xp);
        Assert.Equal(before.Members.Values.Sum(m => m.Gold) + (outcome == BattleOutcome.Won ? 10 : 0), after.Members.Values.Sum(m => m.Gold));
        Assert.Equal(outcome == BattleOutcome.Won, !after.World!.MobsBeaten.IsDefaultOrEmpty && after.World.MobsBeaten.Contains("test_pack"));
        if (outcome == BattleOutcome.Lost) Assert.Equal(WithMobs().RegionFor("cinder_hollow")!.Def.Spawns.Recovery, after.World.At);
    }

    [Fact]
    public void A_later_chapter_clears_the_previous_chapters_defeated_groups()
    {
        var world = Saved(new(5, 2), "cinder_hollow") with { MobsBeaten = ["test_pack"], MobChapter = 0 };
        var s = new Session(document: DocumentAt(world), game: new TacticsGame(WithMobs()));
        s.Harness.StartCampaign(s.Lease);
        Assert.Contains(s.Host.Explore!.Mobs!.Value, m => m.Group == "test_pack");
        Assert.True(s.Game.Document.World!.MobsBeaten.IsDefaultOrEmpty);
    }

    [Fact]
    public void Mob_only_movement_fits_the_explore_patch_packet_budget()
    {
        var s = new Session(document: DocumentAt(Saved(new(5, 2), "cinder_hollow")), game: new TacticsGame(WithMobs()));
        s.Harness.StartCampaign(s.Lease);
        var before = JsonSerializer.SerializeToElement(s.Game.HostView(), ArcBox.Sdk.ArcJson.Options);
        s.Harness.Advance(TimeSpan.FromSeconds(3));
        var after = JsonSerializer.SerializeToElement(s.Game.HostView(), ArcBox.Sdk.ArcJson.Options);
        var patch = ArcBox.Sdk.Protocol.HostViewPatch.Diff(before, after)!;
        Assert.InRange(System.Text.Encoding.UTF8.GetByteCount(patch.ToJsonString()), 1, 1420);
        Assert.True(ArcBox.Sdk.Protocol.HostViewPatch.SameJson(after,
            ArcBox.Sdk.Protocol.HostViewPatch.Apply(before, JsonDocument.Parse(patch.ToJsonString()).RootElement)));
    }

    [Fact]
    public void A_mob_encounter_cannot_be_farmed_by_submitting_its_id_to_the_battle_list()
    {
        var rules = WithMobs();
        var s = new Session(document: DocumentAt(Saved(new(5, 2), "cinder_hollow")), game: new TacticsGame(rules, battleList: true));
        s.Harness.StartCampaign(s.Lease);
        s.Send(0, "battles");
        Assert.Equal(TacticsPhase.EncounterPick, s.Game.Phase);
        s.Send(0, "pickEncounter", new { encounter = "mob_pine_wolves" });
        Assert.Equal(TacticsPhase.EncounterPick, s.Game.Phase);
        Assert.Null(s.Game.Document.Battle);
    }

    [Fact]
    public void A_party_step_into_contact_stops_for_the_ambush()
    {
        var s = new Session(document: DocumentAt(Saved(new(5, 9), "cinder_hollow")), game: new TacticsGame(WithMobs()));
        s.Harness.StartCampaign(s.Lease);
        s.Send(0, "walk", new { dir = "se" });
        Assert.Equal("test_pack", s.Game.Document.World!.MobAmbush);
        Assert.False(s.Host.Explore!.Held);
    }

    [Fact]
    public void A_mob_step_into_contact_can_attack_a_stationary_leader()
    {
        var s = new Session(document: DocumentAt(Saved(new(9, 9), "cinder_hollow")), game: new TacticsGame(WithMobs()));
        s.Harness.StartCampaign(s.Lease);
        for (var i = 0; i < 20 && s.Game.Document.World!.MobAmbush is null; i++) s.Harness.Advance(TimeSpan.FromSeconds(2.1));
        Assert.Equal(new Tile(9, 9), s.Host.Explore!.At);
        Assert.Equal("test_pack", s.Game.Document.World!.MobAmbush);
    }

    [Fact]
    public void Reading_the_journal_pauses_roaming()
    {
        var rules = WithMobs();
        var world = Saved(new(5, 2), "cinder_hollow") with { Flags = ["ch1_ford_settled", "sq_ashmaw_posted", "sq_ashmaw_accepted", "sq_ashmaw_entered"] };
        var s = new Session(document: DocumentAt(world), game: new TacticsGame(rules, lore: LoreBook.Load(ContentBase())));
        s.Harness.StartCampaign(s.Lease);
        s.Send(0, "questLog", new { action = "open" });
        Assert.NotNull(s.Host.Explore!.QuestLog);
        var before = s.Host.Explore.Mobs!.Value.Single(m => m.Group == "test_pack");
        for (var i = 0; i < 5; i++)
        {
            s.Harness.Advance(TimeSpan.FromSeconds(2));
            s.Send(0, "questLog", new { action = "keepOpen" });
        }
        Assert.NotNull(s.Host.Explore.QuestLog);
        Assert.Equal(before, s.Host.Explore.Mobs!.Value.Single(m => m.Group == "test_pack"));
    }

    [Fact]
    public void A_followers_movement_request_cannot_trigger_contact()
    {
        var s = new Session(document: DocumentAt(Saved(new(5, 9), "cinder_hollow")), game: new TacticsGame(WithMobs()));
        s.Harness.StartCampaign(s.Lease);
        var follower = (StageBattleTests.Lead(s) + 1) % 4;
        s.Send(follower, "walk", new { dir = "se" });
        Assert.Equal(new Tile(5, 9), s.Host.Explore!.At);
        Assert.Null(s.Game.Document.World!.MobAmbush);
        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
    }

    [Fact]
    public void Story_scenes_stop_the_roaming_clock()
    {
        var s = StoryTests.Near("ch1_mayors_request");
        s.Harness.Advance(TimeSpan.FromSeconds(3));
        var before = s.Game.Document.World!.MobActors;
        Assert.NotNull(before);
        s.Send(StageBattleTests.Lead(s), "interact");
        Assert.Equal(TacticsPhase.Story, s.Game.Phase);
        s.Harness.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(TacticsPhase.Story, s.Game.Phase);
        Assert.Equal(before, s.Game.Document.World!.MobActors);
    }

}
