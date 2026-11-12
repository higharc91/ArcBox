using System.Text.Json.Nodes;
using Tactics.Logic;

namespace Tactics.Game.Tests;

public sealed class RecoveryServiceTests
{
    internal static TacticsGameTests.Session AtService(string kind, int gold = 100, bool ready = true)
    {
        var rules = RulesFor(kind);
        var s = ShopTests.AtShop(gold, rules);
        var doc = s.Game.Document;
        doc = doc with { World = doc.World! with { RestReady = ready }, Members = Ordinal.Map(doc.Members.Select(m =>
            (m.Key, m.Value with { Character = m.Value.Character with { Injuries = [new("black_eye"), new("cracked_ribs"), new("shaken")] } }))) };
        return RestTests.Again(doc, rules: rules);
    }

    internal static RulesPack RulesFor(string kind)
        => ExplorationTests.EditedRules(folder =>
        {
            var file = Path.Combine(folder, "shops", "ashen_ford.json");
            var root = JsonNode.Parse(File.ReadAllText(file))!;
            var shop = root["shops"]![0]!;
            shop["services"] = JsonNode.Parse($$"""[{"id":"{{kind}}","name":"{{(kind == "room" ? "A room for the night" : "Healing")}}","kind":"{{kind}}","price":{{(kind == "room" ? 10 : 25)}}}]""");
            shop["stock"]![0]!["restock"] = "rest";
            File.WriteAllText(file, root.ToJsonString());
        });

    [Fact]
    public void A_temple_cure_uses_the_buyers_purse_can_target_an_away_friend_and_retries_are_free()
    {
        var s = AtService("cure", ready: false);
        s.Send(0, "interact", new { target = "shop:af_market_herbalist" });
        var guest = s.Profiles[3]!.Value.Value;
        var payer = s.Profiles[1]!.Value.Value;
        s.Harness.SetConnected(3, false);
        var beforeCard = s.Host.Shop!.Members!.Value.Single(m => m.Member == guest);
        Assert.False(beforeCard.Here);
        Assert.Equal(beforeCard.MaxHp, beforeCard.Hp);
        Assert.All(beforeCard.Resources, r => Assert.Equal(r.Max, r.Current));
        Assert.Contains(beforeCard.Injuries, i => i.Id == "cracked_ribs");
        var trade = new { shop = "af_market_herbalist", service = "cure", member = guest, injury = "cracked_ribs", trade = "heal-one" };
        s.Send(1, "shopService", trade);
        Assert.Equal(75, s.Game.Document.Members[payer].Gold);
        var healedCard = s.Host.Shop!.Members!.Value.Single(m => m.Member == guest);
        Assert.Equal(Math.Max(1, healedCard.MaxHp * 15 / 100), healedCard.MaxHp - beforeCard.MaxHp);   // Cracked ribs: −15% of max HP
        Assert.DoesNotContain(healedCard.Injuries, i => i.Id == "cracked_ribs");
        Assert.Equal(100, s.Game.Document.Members[guest].Gold);
        Assert.DoesNotContain(new InjuryId("cracked_ribs"), s.Game.Document.Members[guest].Character.Injuries);
        Assert.Contains(new InjuryId("shaken"), s.Game.Document.Members[guest].Character.Injuries);
        var before = s.Lease.Document.GetRawText();
        s.Send(1, "shopService", trade);
        Assert.Equal(before, s.Lease.Document.GetRawText());
        s = RestTests.Again(s.Game.Document, rules: RulesFor("cure"));
        s.Send(1, "shopService", trade);
        Assert.Equal(75, s.Game.Document.Members[payer].Gold);
    }

    [Fact]
    public void A_poor_buyer_cannot_charge_another_players_purse()
    {
        var s = AtService("cure", gold: 10);
        s.Send(0, "interact", new { target = "shop:af_market_herbalist" });
        var before = s.Lease.Document.GetRawText();
        s.Send(1, "shopService", new { shop = "af_market_herbalist", service = "cure", member = s.Profiles[0]!.Value.Value, injury = "shaken", trade = "poor" });
        Assert.Equal(before, s.Lease.Document.GetRawText());
    }

    [Fact]
    public void Inn_rooms_need_an_agreed_rest_are_sponsored_once_and_leave_unpaid_guests_at_camp_recovery()
    {
        var s = AtService("room");
        s.Send(0, "interact", new { target = "shop:af_market_herbalist" });
        var payer = s.Profiles[1]!.Value.Value;
        var guest = s.Profiles[3]!.Value.Value;
        var trade = new { shop = "af_market_herbalist", service = "room", member = guest, trade = "room-one" };
        s.Send(1, "shopService", trade);
        Assert.Equal(100, s.Game.Document.Members[payer].Gold);
        s.Send(0, "restPropose", new { shop = "af_market_herbalist" });
        RestTests.Vote(s);
        s.Harness.SetConnected(3, false);
        s.Send(1, "shopService", trade);
        Assert.Equal(90, s.Game.Document.Members[payer].Gold);
        s.Send(2, "shopService", new { trade.shop, trade.service, trade.member, trade = "already-paid" });
        Assert.Equal(100, s.Game.Document.Members[s.Profiles[2]!.Value.Value].Gold);
        var doc = s.Game.Document;
        s = RestTests.Again(doc, rules: RulesFor("room"));
        s.Send(1, "shopService", trade);
        Assert.Equal(90, s.Game.Document.Members[payer].Gold);
        RestTests.Finish(s);
        Assert.Equal([new InjuryId("shaken")], s.Game.Document.Members[guest].Character.Injuries);
        Assert.True(s.Game.Document.Members[guest].WellFed);
        Assert.Equal([new InjuryId("cracked_ribs"), new InjuryId("shaken")], s.Game.Document.Members[payer].Character.Injuries);
        Assert.False(s.Game.Document.World!.RestReady);
    }

    [Fact]
    public void New_save_fields_default_to_absent_and_nondefault_fields_round_trip()
    {
        var s = ShopTests.AtShop(0);
        var json = s.Game.Document.ToJson();
        Assert.False(json.GetProperty("world").TryGetProperty("restReady", out _));
        Assert.False(json.GetProperty("world").TryGetProperty("camp", out _));
        Assert.All(json.GetProperty("members").EnumerateObject(), m => Assert.False(m.Value.TryGetProperty("wellFed", out _)));
        s = RestTests.AtCamp();
        s.Send(0, "interact", new { target = "fire" }); RestTests.Vote(s);
        s.Send(0, "campJob", new { job = "cook" });
        var doc = TacticsDocument.Read(s.Game.Document.ToJson());
        Assert.True(doc.World!.RestReady);
        Assert.Equal("cook", doc.World.Camp!.Jobs[s.Profiles[0]!.Value.Value].Job);
        Assert.Equal(s.Game.Document.ToJson().GetRawText(), doc.ToJson().GetRawText());
    }
    [Fact]
    public void A_rest_refills_rest_stock_without_touching_never_stock()
    {
        var s = AtService("room"); s.Send(0, "interact", new { target = "shop:af_market_herbalist" });
        s.Send(0, "shopBuy", new { shop = "af_market_herbalist", item = "potion_of_healing", count = 1, trade = "use-rest-stock" });
        s.Send(0, "shopBuy", new { shop = "af_market_herbalist", item = "smoke_bomb", count = 1, trade = "use-never-stock" });
        s.Send(0, "restPropose", new { shop = "af_market_herbalist" }); RestTests.Vote(s); RestTests.Finish(s);
        s.Send(0, "interact", new { target = "shop:af_market_herbalist" });
        Assert.Equal(3, s.Host.Shop!.Items.Single(x => x.Item == "potion_of_healing").Count);
        Assert.Equal(1, s.Host.Shop.Items.Single(x => x.Item == "smoke_bomb").Count);
    }
    [Theory]
    [InlineData("black_eye")]
    [InlineData("cracked_ribs")]
    [InlineData("shaken")]
    public void Temple_matrix_heals_one_selected_injury_for_25_gold_without_a_rest(string injury)
    {
        var s = AtService("cure", ready: false);
        s.Send(0, "interact", new { target = "shop:af_market_herbalist" });
        var member = s.Profiles[0]!.Value.Value;
        s.Send(0, "shopService", new { shop = "af_market_herbalist", service = "cure", member, injury, trade = "matrix" });
        Assert.DoesNotContain(new InjuryId(injury), s.Game.Document.Members[member].Character.Injuries);
        Assert.Equal(2, s.Game.Document.Members[member].Character.Injuries.Length);
        Assert.Equal(100 - RecoveryRules.Price("temple"), s.Game.Document.Members[member].Gold);
        Assert.False(s.Game.Document.World!.RestReady);
    }
}
