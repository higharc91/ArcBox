using System.Text.Json;
using ArcBox.Sdk;

namespace Tactics.Logic.Tests;

public sealed class ShopRulesTests
{
    [Fact]
    public void The_base_pack_prices_its_consumables_and_has_a_trade_stall()
    {
        var rules = RulesPack.Load([Path.Combine(ContentPaths.ContentBase, "rules")], ArtManifests.Load(ContentPaths.ContentBase));
        var item = JsonSerializer.SerializeToElement(rules.Items[new ItemId("potion_of_healing")], ArcJson.Options);
        Assert.True(item.TryGetProperty("price", out var price));
        Assert.Equal(50, price.GetInt32());
        Assert.Contains("stall", InteractableKinds.All.Keys);
    }

    [Theory]
    [InlineData(0, -1)]
    [InlineData(2147483647, 1)]
    public void Gold_cannot_underflow_or_overflow(int before, int change)
        => Assert.Throws<RulesInvariantException>(() => ShopRules.Gold(before, change));

    [Fact]
    public void Prices_round_and_the_sale_ratio_is_content()
    {
        var item = new ItemDef(new("potion"), "Potion", new("use"), Ordinal.Set("potion"), Price: 25);
        var shop = new ShopDef("test", "Test", new(Person: 0), [], ["potion"], PriceMod: 1.25m, SellRatio: 0.5m);
        Assert.Equal(32, ShopRules.Buy(item, shop));
        Assert.Equal(16, ShopRules.Sell(item, shop));
        Assert.True(ShopRules.Accepts(item, shop));
        Assert.False(ShopRules.Accepts(item with { Price = null }, shop));
    }

    /// <summary>The owner's decision E (2026-11-03): a haggle that passed takes a tenth off buys and adds a tenth to the sale ratio.</summary>
    [Fact]
    public void A_haggle_takes_a_tenth_off_buys_and_adds_a_tenth_to_sales_and_still_rounds_the_shops_way()
    {
        var item = new ItemDef(new("potion"), "Potion", new("use"), Ordinal.Set("potion"), Price: 50);
        var shop = new ShopDef("test", "Test", new(Person: 0), [], ["potion"], SellRatio: 0.5m);
        Assert.Equal((50, 25), (ShopRules.Buy(item, shop), ShopRules.Sell(item, shop)));
        Assert.Equal((45, 27), (ShopRules.Buy(item, shop, haggled: true), ShopRules.Sell(item, shop, haggled: true)));
        var odd = item with { Price = 25 };
        Assert.Equal((23, 13), (ShopRules.Buy(odd, shop, true), ShopRules.Sell(odd, shop, true)));     // 22.5 up, 13.75 down
        Assert.Equal(15, ShopRules.DefaultHaggleDc);
        Assert.Null(shop.Haggle);
    }

    /// <summary>Nothing can be bought and sold back at a profit, haggled or not, at any sale ratio the loader takes (0 to 1) and any mark-up.</summary>
    [Fact]
    public void A_sale_never_pays_more_than_the_same_visit_asks_for_the_item()
    {
        foreach (var ratio in Enumerable.Range(0, 21).Select(i => i * 0.05m))
            foreach (var mod in new[] { 0.5m, 0.75m, 1m, 1.25m, 2m })
                for (var price = 1; price <= 300; price++)
                {
                    var item = new ItemDef(new("potion"), "Potion", new("use"), Ordinal.Set("potion"), Price: price);
                    var shop = new ShopDef("test", "Test", new(Person: 0), [], ["potion"], PriceMod: mod, SellRatio: ratio);
                    foreach (var haggled in new[] { false, true })
                    {
                        var (buy, sell) = (ShopRules.Buy(item, shop, haggled), ShopRules.Sell(item, shop, haggled));
                        Assert.True(sell <= buy, $"price {price}, mod {mod}, ratio {ratio}, haggled {haggled}: sells for {sell}, buys for {buy}");
                        Assert.True(sell >= 0);
                    }
                }
        // The review's case: a ratio of 1 and a price of 10 would have sold for 11 against a buy of 9.
        var ten = new ItemDef(new("potion"), "Potion", new("use"), Ordinal.Set("potion"), Price: 10);
        var even = new ShopDef("test", "Test", new(Person: 0), [], ["potion"], SellRatio: 1m);
        Assert.Equal((9, 9), (ShopRules.Buy(ten, even, true), ShopRules.Sell(ten, even, true)));
    }

    [Theory]
    [InlineData("\"priceMod\": 0", "/priceMod")]
    [InlineData("\"priceMod\": 5", "/priceMod")]
    [InlineData("\"buys\": [\"unknown-tag\"]", "/buys/0")]
    [InlineData("\"keeper\": {\"person\": 99}", "/keeper/person")]
    public void Invalid_shops_report_the_source_file_and_pointer(string change, string pointer)
    {
        var shop = "{\"id\":\"test\",\"name\":\"Test\",\"keeper\":{\"person\":0},\"stock\":[],\"buys\":[],\"priceMod\":1}";
        var node = System.Text.Json.Nodes.JsonNode.Parse(shop)!.AsObject();
        var part = System.Text.Json.Nodes.JsonNode.Parse("{" + change + "}")!.AsObject();
        foreach (var kv in part) node[kv.Key] = kv.Value?.DeepClone();
        var region = Vault.Region() with { People = [new RegionPerson("merchant", new Tile(3, 1), "sw")] };
        var errors = ValidationTests.ErrorsOfChapter(Vault.World(region), ("shops/vault.json", "{\"region\":\"vault\",\"shops\":[" + node.ToJsonString() + "]}"));
        Assert.Contains(errors, e => e.File.EndsWith("shops/vault.json", StringComparison.Ordinal) && e.Pointer == "/shops/0" + pointer);
    }
}
