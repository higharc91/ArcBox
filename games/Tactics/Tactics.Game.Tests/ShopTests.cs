using ArcBox.Sdk;
using Tactics.Logic;
using static Tactics.Game.Tests.TacticsGameTests;

namespace Tactics.Game.Tests;

public sealed class ShopTests
{
    [Fact]
    public void Old_documents_have_no_purse_or_shop_state_and_default_fields_are_not_written()
    {
        var json = TacticsDocument.New.ToJson();
        Assert.False(json.TryGetProperty("gold", out _));
        Assert.False(json.TryGetProperty("shops", out _));
        Assert.Equal(0, TacticsDocument.Read(json).Gold);
        Assert.Null(TacticsDocument.Read(json).Shops);
    }

    internal static Session AtShop(int gold = 100)
    {
        var s = new Session();
        s.ToWalking();
        var doc = s.Game.Document;
        var target = new InteractableDef("shop", "stall", new Tile(26, 64));
        var at = Rules.ReachFor("ashen_ford")!.Where(t => t != target.At && Interactables.InReach(Rules.GroundFor("ashen_ford")!.Terrain, t, target)).Order().First();
        doc = doc with { Gold = gold, World = doc.World! with { At = at, Facing = target.At } };
        var next = new Session(document: doc.ToJson());
        next.Harness.Resume(next.Lease);
        for (var i = 0; i < 4; i++) next.Harness.SetConnected(i, true);
        return next;
    }

    [Fact]
    public void Buy_and_sell_are_atomic_and_a_repeated_trade_after_restart_is_a_noop()
    {
        var s = AtShop();
        s.Send(0, "interact", new { target = "shop:af_market_herbalist" });
        Assert.Equal(TacticsPhase.Story, s.Game.Phase);
        var member = s.Profiles[1]!.Value.Value;
        var before = s.Game.Document.Members[member].Character.Bag.Sum(x => x.Item.Value == "potion_of_healing" ? x.Count : 0);
        var trade = new { shop = "af_market_herbalist", item = "potion_of_healing", count = 1, trade = "buy-one" };
        s.Send(1, "shopBuy", trade);
        Assert.Equal(50, s.Game.Document.Gold);
        Assert.Equal(before + 1, s.Game.Document.Members[member].Character.Bag.Sum(x => x.Item.Value == "potion_of_healing" ? x.Count : 0));
        var again = new Session(document: s.Lease.Document);
        again.Harness.Resume(again.Lease);
        for (var i = 0; i < 4; i++) again.Harness.SetConnected(i, true);
        again.Send(1, "shopBuy", trade);
        Assert.Equal(50, again.Game.Document.Gold);
        again.Send(1, "shopSell", new { trade.shop, trade.item, count = 1, member, trade = "sell-one" });
        Assert.Equal(75, again.Game.Document.Gold);
        again.Send(0, "shopLeave", new { trade.shop });
        Assert.Equal(TacticsPhase.Exploration, again.Game.Phase);
    }

    [Fact]
    public void Two_phones_compete_for_last_stock_without_double_spending()
    {
        var s = AtShop(500);
        s.Send(0, "interact", new { target = "shop:af_market_herbalist" });
        s.Send(0, "shopBuy", new { shop = "af_market_herbalist", item = "potion_of_healing", count = 2, trade = "first" });
        var saves = s.Lease.Saves.Count;
        s.Send(1, "shopBuy", new { shop = "af_market_herbalist", item = "potion_of_healing", count = 1, trade = "last" });
        Assert.Equal(saves + 1, s.Lease.Saves.Count);
        var after = s.Lease.Document.GetRawText();
        s.Send(2, "shopBuy", new { shop = "af_market_herbalist", item = "potion_of_healing", count = 1, trade = "too-late" });
        Assert.Equal(after, s.Lease.Document.GetRawText());
        Assert.Equal(350, s.Game.Document.Gold);
    }

    [Fact]
    public void Wrong_shop_negative_counts_other_bags_and_disconnected_phones_cannot_trade()
    {
        var s = AtShop();
        s.Send(0, "interact", new { target = "shop:af_market_herbalist" });
        var before = s.Lease.Document.GetRawText();
        foreach (var count in new[] { 0, -1, int.MaxValue })
            s.Send(0, "shopBuy", new { shop = "af_market_herbalist", item = "potion_of_healing", count, trade = "bad" + count });
        s.Send(0, "shopBuy", new { shop = "wrong", item = "potion_of_healing", count = 1, trade = "wrong" });
        s.Send(0, "shopSell", new { shop = "af_market_herbalist", item = "potion_of_healing", member = s.Profiles[1]!.Value.Value, count = 1, trade = "steal" });
        s.Harness.SetConnected(1, false);
        s.Send(1, "shopBuy", new { shop = "af_market_herbalist", item = "potion_of_healing", count = 1, trade = "offline" });
        Assert.Equal(before, s.Lease.Document.GetRawText());
    }

    [Fact]
    public void A_gold_pickup_is_paid_once_even_after_restart()
    {
        var rules = Vault.Load(["""{ "id": "coins", "kind": "pickup", "at": [3,1], "loot": [{ "gold": 25 }] }"""]);
        var s = Vault.At(new Tile(2, 1), rules);
        s.Send(0, "interact", new { target = "coins" });
        Assert.Equal(25, s.Game.Document.Gold);
        Assert.Contains("25 gold", s.Host.Notice);
        var again = new Session(document: s.Lease.Document, game: new TacticsGame(rules));
        again.Harness.Resume(again.Lease);
        for (var i = 0; i < 4; i++) again.Harness.SetConnected(i, true);
        again.Send(0, "interact", new { target = "coins" });
        Assert.Equal(25, again.Game.Document.Gold);
    }

    [Fact]
    public void Only_the_leader_leaves_and_the_last_disconnect_closes_the_shop()
    {
        var s = AtShop();
        s.Send(0, "interact", new { target = "shop:af_market_herbalist" });
        s.Send(1, "shopLeave", new { shop = "af_market_herbalist" });
        Assert.NotNull(s.Game.Document.World!.Shop);
        for (var i = 0; i < 4; i++) s.Harness.SetConnected(i, false);
        Assert.Null(s.Game.Document.World!.Shop);
        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
    }

    [Fact]
    public void Chapter_stock_restocks_and_never_stock_does_not()
    {
        var s = AtShop(1000);
        s.Send(0, "interact", new { target = "shop:af_market_herbalist" });
        s.Send(0, "shopBuy", new { shop = "af_market_herbalist", item = "potion_of_healing", count = 3, trade = "potions" });
        s.Send(0, "shopBuy", new { shop = "af_market_herbalist", item = "smoke_bomb", count = 2, trade = "bombs" });
        var doc = s.Game.Document;
        doc = doc with { Shops = doc.Shops! with { Chapter = 2 } };
        var again = new Session(document: doc.ToJson());
        again.Harness.Resume(again.Lease);
        for (var i = 0; i < 4; i++) again.Harness.SetConnected(i, true);
        Assert.Equal(3, again.Host.Shop!.Items.Single(x => x.Item == "potion_of_healing").Count);
        Assert.Equal(0, again.Host.Shop.Items.Single(x => x.Item == "smoke_bomb").Count);
        again.Send(0, "shopBuy", new { shop = "af_market_herbalist", item = "potion_of_healing", count = 1, trade = "chapter-two" });
        Assert.Equal(2, again.Host.Shop.Items.Single(x => x.Item == "potion_of_healing").Count);
    }

    [Fact]
    public void Ending_the_session_in_a_shop_keeps_the_visit_for_next_time()
    {
        var s = AtShop();
        s.Send(0, "interact", new { target = "shop:af_market_herbalist" });
        s.Send(0, "endSession");
        Assert.Equal("af_market_herbalist", TacticsDocument.Read(s.Lease.Document).World!.Shop!.ShopId);
        var again = new Session(document: s.Lease.Document);
        again.Harness.Resume(again.Lease);
        for (var i = 0; i < 4; i++) again.Harness.SetConnected(i, true);
        Assert.NotNull(again.Host.Shop);
        Assert.Equal(100, again.Host.Shop.Gold);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("1.5")]
    [InlineData("\"25\"")]
    [InlineData("null")]
    [InlineData("2147483648")]
    [InlineData("-2147483648")]
    public void LoreBook_rejects_the_same_invalid_gold_as_check_py(string json)
        => Assert.Throws<InvalidDataException>(() => StoryTests.EditedLore(scenes =>
            scenes.First(s => s!["beat"]!.GetValue<string>() == StoryTests.Mayor)!["nodes"]![0]!["gold"] = System.Text.Json.Nodes.JsonNode.Parse(json)));

    [Fact]
    public void Story_node_gold_is_saved_with_its_line_and_not_repeated_on_resume()
    {
        var lore = StoryTests.EditedLore(scenes => scenes.First(s => s!["beat"]!.GetValue<string>() == StoryTests.Mayor)!["nodes"]![0]!["gold"] = 25);
        var s = StoryTests.Near(StoryTests.Mayor, lore);
        s.Send(0, "interact");
        Assert.Equal(25, s.Game.Document.Gold);
        var again = new Session(document: s.Lease.Document, game: new TacticsGame(Rules, lore: lore));
        again.Harness.Resume(again.Lease);
        for (var i = 0; i < 4; i++) again.Harness.SetConnected(i, true);
        Assert.Equal(25, again.Game.Document.Gold);
        Assert.Equal(StoryTests.Cursor(s)!.Node, StoryTests.Cursor(again)!.Node);
    }

    [Fact]
    public void Unaffordable_story_choices_are_disabled_and_cannot_be_selected()
    {
        var lore = StoryTests.EditedLore(scenes =>
        {
            var scene = scenes.Single(s => s!["id"]!.GetValue<string>() == "ch1_ford")!;
            var node = scene["nodes"]!.AsArray().First(n => n!["options"] is not null)!;
            node["options"]!.AsArray().First(o => o!["id"]!.GetValue<string>() == "cases")!["gold"] = -20;
        });
        var s = StoryTests.Near(StoryTests.FordChoice, lore);
        s.Send(0, "interact");
        StoryVoteTests.Until(s, () => s.Host.Story?.State == TacticsGame.StoryStates.Asking, "the choice");
        var option = s.Screen(0)!.Data.GetProperty("options").EnumerateArray().Single(x => x.GetProperty("id").GetString() == "cases");
        Assert.False(option.GetProperty("can").GetBoolean());
        Assert.Equal("needs 20 gold", option.GetProperty("needs").GetString());
        s.Send(0, "vote", new { option = "cases" });
        Assert.Null(s.Screen(0)!.Data.GetProperty("mine").GetString());
        Assert.Equal(0, s.Game.Document.Gold);
        var fundedDoc = s.Game.Document with { Gold = 30 };
        var funded = new Session(document: fundedDoc.ToJson(), game: new TacticsGame(Rules, lore: lore));
        funded.Harness.Resume(funded.Lease);
        for (var i = 0; i < 4; i++) funded.Harness.SetConnected(i, true);
        funded.Send(0, "vote", new { option = "cases" });
        StoryVoteTests.Until(funded, () => funded.Game.Document.Gold == 10, "the paid choice");
        Assert.Equal(10, funded.Game.Document.Gold);
    }

    [Fact]
    public void Encounter_gold_is_awarded_with_the_winning_checkpoint()
    {
        var rules = ExplorationTests.EditedRules(folder =>
        {
            var file = Path.Combine(folder, "encounters", "river_crossing.json");
            var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!;
            json["gold"] = 37;
            File.WriteAllText(file, json.ToJsonString());
        });
        var s = new Session(game: new TacticsGame(rules, battleList: true));
        s.ToBattle(everyoneAway: true);
        s.PlayOut();
        Assert.Equal(TacticsPhase.Results, s.Game.Phase);
        Assert.Equal(37, s.Game.Document.Gold);
        Assert.Equal(37, TacticsDocument.Read(s.Lease.Checkpoints.Single().Doc).Gold);
    }
}
