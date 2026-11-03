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

    [Fact]
    public void The_TV_hides_an_unseen_empty_purse_while_the_shop_regions_phone_shows_it()
    {
        var s = AtShop(0);
        Assert.Null(s.Host.Explore!.Gold);
        Assert.Equal(0, s.Screen(0)!.Data.GetProperty("gold").GetInt32());
        s.Send(0, "interact", new { target = "shop:af_market_herbalist" });
        Assert.Equal(0, s.Host.Explore.Gold);
        s.Send(0, "shopLeave", new { shop = "af_market_herbalist" });
        Assert.Equal(0, s.Host.Explore.Gold);
    }

    internal static Session AtShop(int gold = 100, RulesPack? rules = null)
    {
        var s = new Session(game: new TacticsGame(rules ?? Rules, battleList: true));
        s.ToWalking();
        var doc = s.Game.Document;
        var target = new InteractableDef("shop", "stall", new Tile(26, 64));
        var at = Rules.ReachFor("ashen_ford")!.Where(t => t != target.At && Interactables.InReach(Rules.GroundFor("ashen_ford")!.Terrain, t, target)).Order().First();
        doc = doc with { Members = Ordinal.Map(doc.Members.Select(m => (m.Key, m.Value with { Gold = gold }))), World = doc.World! with { At = at, Facing = target.At } };
        var next = new Session(document: doc.ToJson(), game: new TacticsGame(rules ?? Rules, battleList: true));
        next.Harness.Resume(next.Lease);
        for (var i = 0; i < 4; i++) next.Harness.SetConnected(i, true);
        return next;
    }

    [Fact]
    public void Traders_and_things_share_the_stable_target_order_and_default_interact()
    {
        var rules = ExplorationTests.EditedRules(folder =>
        {
            var file = Path.Combine(folder, "interactables", "ashen_ford.json");
            var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!;
            json["interactables"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse("""{ "id": "test_coin", "kind": "pickup", "at": [26,64], "loot": [{"gold":1}] }"""));
            File.WriteAllText(file, json.ToJsonString());
        });
        var s = AtShop(0, rules);
        Assert.Equal("shop:af_market_herbalist", s.Screen(0)!.Data.GetProperty("targets")[0].GetProperty("id").GetString());
        s.Send(0, "interact");
        Assert.NotNull(s.Host.Shop);
        Assert.Equal(0, s.Game.Document.Gold);
    }

    [Fact]
    public void Buy_and_sell_are_atomic_and_a_repeated_trade_after_restart_is_a_noop()
    {
        var s = AtShop();
        s.Send(0, "interact", new { target = "shop:af_market_herbalist" });
        Assert.Equal(TacticsPhase.Story, s.Game.Phase);
        var member = s.Profiles[1]!.Value.Value;
        var others = s.Game.Document.Members.Remove(member);
        var before = s.Game.Document.Members[member].Character.Bag.Sum(x => x.Item.Value == "potion_of_healing" ? x.Count : 0);
        var trade = new { shop = "af_market_herbalist", item = "potion_of_healing", count = 1, trade = "buy-one" };
        s.Send(1, "shopBuy", trade);
        Assert.Equal(50, s.Game.Document.Members[member].Gold);
        Assert.Equal(before + 1, s.Game.Document.Members[member].Character.Bag.Sum(x => x.Item.Value == "potion_of_healing" ? x.Count : 0));
        Assert.Equal(others, s.Game.Document.Members.Remove(member));
        Assert.Equal(100, s.Screen(0)!.Data.GetProperty("gold").GetInt32());
        Assert.Equal(50, s.Screen(1)!.Data.GetProperty("gold").GetInt32());
        var again = new Session(document: s.Lease.Document);
        again.Harness.Resume(again.Lease);
        for (var i = 0; i < 4; i++) again.Harness.SetConnected(i, true);
        again.Send(1, "shopBuy", trade);
        Assert.Equal(50, again.Game.Document.Members[member].Gold);
        again.Send(1, "shopSell", new { trade.shop, trade.item, count = 1, member, trade = "sell-one" });
        Assert.Equal(75, again.Game.Document.Members[member].Gold);
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
        Assert.Equal([400, 450, 500, 500], s.Game.Document.Members.Values.Select(m => m.Gold));
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
        Assert.Equal([7, 6, 6, 6], s.Game.Document.Members.Values.Select(m => m.Gold));
        Assert.Contains("25 gold", s.Host.Notice);
        var again = new Session(document: s.Lease.Document, game: new TacticsGame(rules));
        again.Harness.Resume(again.Lease);
        for (var i = 0; i < 4; i++) again.Harness.SetConnected(i, true);
        again.Send(0, "interact", new { target = "coins" });
        Assert.Equal([7, 6, 6, 6], again.Game.Document.Members.Values.Select(m => m.Gold));
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
        Assert.Equal(400, again.Host.Shop.Gold);
        Assert.All(again.Host.Shop.Purses, p => Assert.Equal(100, p.Gold));
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
        Assert.Equal([7, 6, 6, 6], s.Game.Document.Members.Values.Select(m => m.Gold));
        var again = new Session(document: s.Lease.Document, game: new TacticsGame(Rules, lore: lore));
        again.Harness.Resume(again.Lease);
        for (var i = 0; i < 4; i++) again.Harness.SetConnected(i, true);
        Assert.Equal([7, 6, 6, 6], again.Game.Document.Members.Values.Select(m => m.Gold));
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
        StoryVoteTests.Until(funded, () => funded.Game.Document.Members.Values.Sum(m => m.Gold) == 10, "the paid choice");
        Assert.Equal([3, 3, 2, 2], funded.Game.Document.Members.Values.Select(m => m.Gold));
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
        Assert.Equal([10, 9, 9, 9], s.Game.Document.Members.Values.Select(m => m.Gold));
        Assert.Equal([10, 9, 9, 9], TacticsDocument.Read(s.Lease.Checkpoints.Single().Doc).Members.Values.Select(m => m.Gold));
    }

    [Fact]
    public void A_poor_player_cannot_spend_a_rich_players_gold_or_spoof_the_buyer()
    {
        var start = AtShop(0);
        var rich = start.Profiles[0]!.Value.Value;
        var poor = start.Profiles[1]!.Value.Value;
        var doc = start.Game.Document;
        doc = doc with { Members = doc.Members.SetItem(rich, doc.Members[rich] with { Gold = 200 }) };
        var s = new Session(document: doc.ToJson());
        s.Harness.Resume(s.Lease);
        for (var i = 0; i < 4; i++) s.Harness.SetConnected(i, true);
        s.Send(0, "interact", new { target = "shop:af_market_herbalist" });
        Assert.True(s.Screen(0)!.Data.GetProperty("items")[0].GetProperty("can").GetBoolean());
        Assert.False(s.Screen(1)!.Data.GetProperty("items")[0].GetProperty("can").GetBoolean());
        var before = s.Lease.Document.GetRawText();
        s.Send(1, "shopBuy", new { shop = "af_market_herbalist", item = "potion_of_healing", count = 1, member = rich, trade = "spoof" });
        Assert.Equal(before, s.Lease.Document.GetRawText());
        // A non-leader sells and buys using only their own purse and bag.
        s.Send(1, "shopSell", new { shop = "af_market_herbalist", item = "potion_of_healing", count = 1, member = poor, trade = "fund" });
        Assert.Equal(25, s.Game.Document.Members[poor].Gold);
        s.Send(1, "shopBuy", new { shop = "af_market_herbalist", item = "smoke_bomb", count = 1, member = rich, trade = "own-buy" });
        Assert.Equal(0, s.Game.Document.Members[poor].Gold);
        Assert.Equal(doc.ToJson().GetProperty("members").GetProperty(rich).GetRawText(), s.Game.Document.ToJson().GetProperty("members").GetProperty(rich).GetRawText());
    }

    [Fact]
    public void Selling_then_repeatedly_remaking_cannot_refill_a_starting_kit_or_mint_gold()
    {
        var s = AtShop(0);
        var profile = s.Profiles[0]!.Value;
        s.Send(0, "interact", new { target = "shop:af_market_herbalist" });
        s.Send(0, "shopSell", new { shop = "af_market_herbalist", item = "potion_of_healing", count = 1, member = profile.Value, trade = "original-kit" });
        Assert.Equal(25, s.Game.Document.For(profile)!.Gold);
        Assert.Empty(s.Game.Document.For(profile)!.Character.Bag);
        s.Send(0, "endSession");

        var portal = new TacticsCampaignRules(Rules);
        foreach (var cls in new[] { "wizard", "cleric", "fighter" })
        {
            var change = portal.Handle(s.Lease.Info, s.Lease.Document, profile, "createCharacter",
                System.Text.Json.JsonSerializer.SerializeToElement(new { race = "human", @class = cls, background = "soldier",
                    scores = new { str = 15, dex = 15, con = 15, @int = 8, wis = 8, cha = 8 } }, ArcJson.Options));
            Assert.Null(change.Error);
            var member = TacticsDocument.Read(change.Document!.Value).For(profile)!;
            Assert.Equal(cls, member.Character.Class);
            Assert.False(member.Remade); // Pre-battle remakes remain unlimited.
            Assert.Empty(member.Character.Bag);
            Assert.Equal(25, member.Gold);

            s = new Session(document: change.Document.Value);
            s.Harness.Resume(s.Lease);
            for (var i = 0; i < 4; i++) s.Harness.SetConnected(i, true);
            var before = s.Lease.Document.GetRawText();
            s.Send(0, "shopSell", new { shop = "af_market_herbalist", item = "potion_of_healing", count = 1, member = profile.Value, trade = "refilled-" + cls });
            Assert.Equal(before, s.Lease.Document.GetRawText());
            Assert.Equal(25, s.Game.Document.For(profile)!.Gold);
            s.Send(0, "endSession");
        }
    }

    [Fact]
    public void Legacy_shared_gold_is_split_once_preserving_existing_purses_and_the_total()
    {
        var doc = AtShop(10).Game.Document with { Gold = 25 };
        var migrated = TacticsDocument.Read(doc.ToJson());
        Assert.Equal([17, 16, 16, 16], migrated.Members.Values.Select(m => m.Gold));
        Assert.Equal(0, migrated.Gold);
        Assert.False(migrated.ToJson().TryGetProperty("gold", out _));
        Assert.Equal(migrated.ToJson().GetRawText(), TacticsDocument.Read(migrated.ToJson()).ToJson().GetRawText());
    }

    [Fact]
    public void Loot_is_split_among_present_characters_only()
    {
        var rules = Vault.Load(["""{ "id": "coins", "kind": "pickup", "at": [3,1], "loot": [{ "gold": 25 }] }"""]);
        var s = Vault.At(new Tile(2, 1), rules);
        s.Harness.SetConnected(3, false);
        s.Send(0, "interact", new { target = "coins" });
        Assert.Equal([9, 8, 8, 0], s.Game.Document.Members.Values.Select(m => m.Gold));
    }

    [Fact]
    public void Shared_payments_use_available_individual_balances_without_negative_purses()
    {
        var doc = AtShop(0).Game.Document;
        var rich = doc.Members.Keys.Last();
        doc = doc with { Members = doc.Members.SetItem(rich, doc.Members[rich] with { Gold = 30 }) };
        var paid = doc.ChangeGold(-20, doc.Members.Keys);
        Assert.Equal([0, 0, 0, 10], paid.Members.Values.Select(m => m.Gold));
        Assert.Throws<RulesInvariantException>(() => doc.ChangeGold(-31, doc.Members.Keys));
        var full = doc with { Members = Ordinal.Map(doc.Members.Select(m => (m.Key, m.Value with { Gold = int.MaxValue }))) };
        full = full with { Members = full.Members.SetItem(rich, full.Members[rich] with { Gold = 0 }) };
        Assert.Equal(25, full.ChangeGold(25, full.Members.Keys).Members[rich].Gold);
    }
}
