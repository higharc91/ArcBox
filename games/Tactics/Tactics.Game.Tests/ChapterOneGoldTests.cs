using Tactics.Logic;
using static Tactics.Game.Tests.TacticsGameTests;

namespace Tactics.Game.Tests;

/// <summary>The shipped Chapter 1 rewards fund real purchases, survive restart, and never pay on a loss.</summary>
public sealed class ChapterOneGoldTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public void The_council_advance_is_paid_once_and_can_fund_a_purchase_from_each_players_own_purse()
    {
        var s = StoryTests.Telling(StoryTests.Mayor);
        StoryVoteTests.Until(s, () => StoryTests.Cursor(s)?.Node == "close", "the council advance", TimeSpan.FromMinutes(10));
        Assert.All(s.Game.Document.Members.Values, m => Assert.Equal(50, m.Gold));

        s = Resume(s.Game.Document, new TacticsGame(Rules, lore: StoryTests.Lore));
        StoryTests.PlayOut(s);
        Assert.Contains(StoryTests.Mayor, s.Game.Document.World!.Beats);
        Assert.All(s.Game.Document.Members.Values, m => Assert.Equal(50, m.Gold));

        s = Near(s.Game.Document, new InteractableDef("shop", "stall", new Tile(26, 64)));
        s.Send(0, "interact", new { target = "shop:af_market_herbalist" });
        var buyer = s.Profiles[1]!.Value;
        var before = s.Game.Document.For(buyer)!.Character.Bag.Single(x => x.Item.Value == "potion_of_healing").Count;
        s.Send(1, "shopBuy", new { shop = "af_market_herbalist", item = "potion_of_healing", count = 1, trade = "council-funded" });
        Assert.Equal(0, s.Game.Document.For(buyer)!.Gold);
        Assert.Equal(before + 1, s.Game.Document.For(buyer)!.Character.Bag.Single(x => x.Item.Value == "potion_of_healing").Count);
        Assert.All(s.Game.Document.Members.Where(m => m.Key != buyer.Value), m => Assert.Equal(50, m.Value.Gold));
    }

    [Theory]
    [InlineData("ford_assault", BattleOutcome.Won, 30)]
    [InlineData("river_crossing", BattleOutcome.Won, 30)]
    [InlineData("ford_assault", BattleOutcome.Lost, 0)]
    [InlineData("river_crossing", BattleOutcome.Lost, 0)]
    public void Either_ford_route_pays_the_same_reward_only_on_victory_and_keeps_it_in_the_checkpoint(string encounter, BattleOutcome outcome, int share)
    {
        var s = new Session();
        s.ToBattle(encounter);
        StageEndTests.Ended(s, outcome);
        Assert.All(s.Game.Document.Members.Values, m => Assert.Equal(share, m.Gold));
        var checkpoint = TacticsDocument.Read(s.Lease.Checkpoints.Single().Doc);
        Assert.All(checkpoint.Members.Values, m => Assert.Equal(share, m.Gold));
        s = Resume(s.Game.Document);
        Assert.All(s.Game.Document.Members.Values, m => Assert.Equal(share, m.Gold));
    }

    [Theory]
    [InlineData("af_reeves_chest", 15, "potion_of_healing")]
    [InlineData("af_ash_market_cache", 10, "smoke_bomb")]
    [Trait("Category", "Integration")]
    public void Shipped_treasure_splits_gold_keeps_item_loot_and_cannot_be_collected_again(string id, int share, string item)
    {
        var s = new Session();
        s.ToWalking();
        var def = Rules.InteractablesIn("ashen_ford").Single(d => d.Id == id);
        var doc = s.Game.Document;
        doc = doc with { World = doc.World! with
        {
            Flags = doc.World.Flags.Add("af_reeves_key"),
            Things = Ordinal.Map<InteractableSave>().SetItem(id, Interactables.Initial(def) with { Found = true }),
        } };
        s = Near(doc, def);
        var opener = s.Profiles[0]!.Value;
        var before = s.Game.Document.For(opener)!.Character.Bag.Where(x => x.Item.Value == item).Sum(x => x.Count);
        s.Send(0, "interact", new { target = id });
        if (s.Game.Phase == TacticsPhase.Story)
        {
            StoryVoteTests.Until(s, () => s.Host.Story?.State == TacticsGame.StoryStates.Asking, "the chest's lock");
            s.Send(0, "vote", new { option = "key" });
            StoryTests.PlayOut(s);
        }
        Assert.All(s.Game.Document.Members.Values, m => Assert.Equal(share, m.Gold));
        Assert.Equal(before + 1, s.Game.Document.For(opener)!.Character.Bag.Where(x => x.Item.Value == item).Sum(x => x.Count));
        s = Resume(s.Game.Document);
        var saved = s.Game.Document.ToJson().GetRawText();
        s.Send(0, "interact", new { target = id });
        Assert.Equal(saved, s.Game.Document.ToJson().GetRawText());
    }

    private static Session Near(TacticsDocument doc, InteractableDef target)
    {
        var ground = Rules.GroundFor("ashen_ford")!.Terrain;
        var at = Rules.ReachFor("ashen_ford")!.Where(t => t != target.At && Interactables.InReach(ground, t, target)).Order().First();
        return Resume(doc with { World = doc.World! with { At = at, Facing = target.At } });
    }

    private static Session Resume(TacticsDocument doc, TacticsGame? game = null)
    {
        var s = new Session(document: doc.ToJson(), game: game);
        s.Harness.Resume(s.Lease);
        for (var i = 0; i < 4; i++) s.Harness.SetConnected(i, true);
        return s;
    }
}
