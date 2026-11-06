using Tactics.Logic;
using static Tactics.Game.Tests.TacticsGameTests;
using static Tactics.Game.Tests.RestTests;

namespace Tactics.Game.Tests;

public sealed class CampWatchTests
{
    [Fact]
    public void Every_shipped_campfire_passes_the_load_time_watch_warning()
        => Assert.DoesNotContain(Rules.Warnings, w => w.Message.Contains("watch range"));

    [Theory]
    [InlineData("ashen_ford", "af_ford_campfire", null)]
    [InlineData("ashen_ford", "af_road_campfire", null)]
    [InlineData("ashen_ford", "af_road_campfire", "ch1_skarrow_slain")]
    [InlineData("ashen_ford", "af_road_campfire", "ch1_skarrow_spared")]
    [InlineData("cinder_hollow", "hollow_safe_fire", null)]
    public void Watch_finds_existing_secrets_from_every_shipped_campfire(string region, string fireId, string? flag)
    {
        var fire = Rules.InteractablesIn(region).Single(t => t.Id == fireId);
        var ground = Rules.GroundFor(region)!;
        var at = Rules.ReachFor(region)!.Where(t => t != fire.At && Interactables.InReach(ground.Terrain, t, fire)).Order().First();
        var original = ShopTests.AtShop();
        var doc = original.Game.Document;
        doc = doc with { World = doc.World! with { Region = region, At = at, Facing = fire.At, RestReady = true, Flags = flag is null ? doc.World.Flags : doc.World.Flags.Add(flag) } };
        var s = Again(doc, rules: Rules);
        s.Send(0, "interact", new { target = fireId });
        Vote(s);
        s.Send(0, "campJob", new { job = "watch" });
        for (var i = 1; i < 4; i++) s.Send(i, "campJob", new { job = "rest" });
        Finish(s);
        var secrets = Rules.InteractablesIn(region).Where(t => t.Hidden is not null).Select(t => t.Id).ToHashSet();
        Assert.Contains(s.Game.Document.World!.Things!, entry => secrets.Contains(entry.Key) && entry.Value.Found);
        var found = s.Game.Document.World.Things!.Where(t => t.Value.Found).Select(t => t.Key).ToArray();
        s = Again(s.Game.Document, rules: Rules);
        Assert.All(found, id => Assert.True(s.Game.Document.World!.Things![id].Found));
    }
}
