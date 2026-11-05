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
    public void A_region_without_mobs_has_no_mob_timer_or_view_fields()
    {
        var s = new Session(document: DocumentAt(Saved(NearStart())), game: new TacticsGame(WithMobs()));
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

}
