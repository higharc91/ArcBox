using Tactics.Logic;
using static Tactics.Game.Tests.TacticsGameTests;
using static Tactics.Game.Tests.ExplorationTests;

namespace Tactics.Game.Tests;

public sealed class TownBarkTests
{
    private static readonly Lazy<LoreBook> Book = new(() => LoreBook.Load(ContentRules()));

    private static (Session Session, RegionPerson Person, string Target) AtFarmer()
    {
        var person = Rules.RegionFor("ashen_ford")!.Def.People.First(p => p.Preset == "farmer");
        var s = new Session(document: DocumentAt(Saved(person.At)), game: new TacticsGame(Rules, lore: Book.Value));
        s.Harness.StartCampaign(s.Lease);
        return (s, person, "person:" + person.Id);
    }

    [Fact]
    public void Only_the_leader_can_talk_and_a_bark_does_not_change_phase_or_story()
    {
        var (s, _, target) = AtFarmer();
        var starter = s.Game.Leader!.Id;
        var leader = s.Harness.Players.ToList().FindIndex(p => p.Id == starter);
        s.Send((leader + 1) % 4, "interact", new { target });
        Assert.Null(s.Host.Explore!.Bark);
        s.Send(leader, "interact", new { target });
        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
        Assert.Null(s.Game.Document.World!.Story);
        Assert.Equal("town_farmer_1", s.Host.Explore!.Bark!.Line.Id);
        Assert.Equal(1L, Assert.Single(s.Game.Document.World.BarkCounters!).Value);
        s.Harness.Advance(TimeSpan.FromSeconds(20));
        Assert.Null(s.Host.Explore.Bark);
    }

    [Fact]
    public void Rotation_survives_a_new_session_and_rejects_out_of_reach_people()
    {
        var (s, _, target) = AtFarmer();
        var leader = s.Harness.Players.ToList().FindIndex(p => p.Id == s.Game.Leader!.Id);
        s.Send(leader, "interact", new { target });
        var saved = s.Game.Document.ToJson();
        var next = new Session(document: saved, game: new TacticsGame(Rules, lore: Book.Value));
        next.Harness.StartCampaign(next.Lease);
        leader = next.Harness.Players.ToList().FindIndex(p => p.Id == next.Game.Leader!.Id);
        next.Send(leader, "interact", new { target });
        Assert.Equal("town_farmer_2", next.Host.Explore!.Bark!.Line.Id);
        next.Send(leader, "interact", new { target = "person:not_here" });
        Assert.Equal("town_farmer_2", next.Host.Explore.Bark.Line.Id);
        Assert.Equal(2L, Assert.Single(next.Game.Document.World!.BarkCounters!).Value);
    }
}
