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
