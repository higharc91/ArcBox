using Tactics.Logic;
using static Tactics.Game.Tests.TacticsGameTests;

namespace Tactics.Game.Tests;

/// <summary>A Development seed starting a new campaign part way through its story.</summary>
public sealed class TacticsSeedTests
{
    private static readonly TacticsSeed Force = new(["ch1_skarrow_slain"], ["ch1_mayors_request", "ch1_the_ford_choice"]);

    private static WorldSave WorldOf(Session s) => TacticsDocument.Read(s.Lease.Document).World!;

    [Fact]
    public void A_seed_applies_once_to_a_new_campaign()
    {
        var first = new Session(game: new TacticsGame(Rules, seed: Force));
        first.ToWalking();

        var world = WorldOf(first);
        Assert.Equal(["ch1_skarrow_slain"], world.Flags);
        Assert.Equal(["ch1_mayors_request", "ch1_the_ford_choice"], world.Beats);
        Assert.Equal(Rules.RegionFor("ashen_ford")!.Def.Spawns.Start, world.At);

        // The next session, with another seed set, carries on from the campaign's own story.
        var next = new Session(document: first.Lease.Document, game: new TacticsGame(Rules, seed: new TacticsSeed(["ch1_skarrow_spared"], [])));
        next.Harness.StartCampaign(next.Lease);
        Assert.Equal(["ch1_skarrow_slain"], WorldOf(next).Flags);
        Assert.Equal(["ch1_mayors_request", "ch1_the_ford_choice"], WorldOf(next).Beats);
    }

    [Fact]
    public void A_seed_leaves_a_campaign_already_in_the_world_alone()
    {
        var at = ExplorationTests.NearStart();
        var s = new Session(document: ExplorationTests.DocumentAt(ExplorationTests.Saved(at)), game: new TacticsGame(Rules, seed: Force));

        s.Harness.StartCampaign(s.Lease);

        Assert.Equal(["a_flag"], WorldOf(s).Flags);
        Assert.Equal(["a_beat"], WorldOf(s).Beats);
        Assert.Equal(at, WorldOf(s).At);
    }

    [Fact]
    public void A_seed_can_start_the_party_somewhere_else()
    {
        var at = ExplorationTests.NearStart(4);
        var s = new Session(game: new TacticsGame(Rules, seed: Force with { Region = "ashen_ford", At = at }));

        s.ToWalking();

        Assert.Equal(at, WorldOf(s).At);
        Assert.Equal(at, s.Host.Explore!.At);
    }

    [Fact]
    public void A_seed_on_a_tile_that_cant_be_stood_on_starts_at_the_start_spawn_with_its_story()
    {
        var s = new Session(game: new TacticsGame(Rules, seed: Force with { At = new Tile(0, 0) }));

        s.ToWalking();

        Assert.Equal(Rules.RegionFor("ashen_ford")!.Def.Spawns.Start, WorldOf(s).At);
        Assert.Equal(["ch1_skarrow_slain"], WorldOf(s).Flags);
    }

    [Fact]
    public void A_seed_in_a_region_the_pack_doesnt_have_starts_in_the_first_region()
    {
        var s = new Session(game: new TacticsGame(Rules, seed: Force with { Region = "nowhere" }));

        s.ToWalking();

        Assert.Equal("ashen_ford", WorldOf(s).Region);
        Assert.Equal(["ch1_skarrow_slain"], WorldOf(s).Flags);
    }

    [Fact]
    public void A_seed_does_nothing_in_a_pack_with_no_region()
    {
        var s = new Session(game: new TacticsGame(ExplorationTests.NoRegion.Value, seed: Force));

        s.Harness.StartCampaign(s.Lease);

        Assert.Null(TacticsDocument.Read(s.Lease.Document).World);
    }

    [Fact]
    public void A_seed_with_no_lists_reads_as_empty()
    {
        var seed = new TacticsSeed(default, default);

        Assert.Empty(seed.Flags);
        Assert.Empty(seed.Beats);
    }

    [Fact]
    public void A_game_with_no_lore_has_an_empty_book()
        => Assert.Same(LoreBook.Empty, new TacticsGame(Rules).Lore);
}
