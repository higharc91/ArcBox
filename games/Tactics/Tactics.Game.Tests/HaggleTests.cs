using System.Text.Json;
using ArcBox.Sdk;
using Tactics.Logic;
using static Tactics.Game.Tests.StoryTests;
using static Tactics.Game.Tests.StoryVoteTests;
using static Tactics.Game.Tests.TacticsGameTests;

namespace Tactics.Game.Tests;

/// <summary>
/// Haggling, once per shop visit (the owner's decision E, 2026-11-03, issue #83): the leader taps Haggle, the party picks who rolls
/// Persuasion against the shop's DC (15 by default), a pass takes a tenth off buys and adds a tenth to sales for the visit, a fail costs
/// nothing; the roll is saved and replayed like every 5d roll.
/// </summary>
public sealed class HaggleTests
{
    private const string Shop = "af_market_herbalist";

    private static TacticsDocument Doc(Session s) => TacticsDocument.Read(s.Lease.Document);

    /// <summary>The herbalist's shop, open, with the party's Persuasion check set to always pass (DC 1) or always fail (DC 30).</summary>
    private static Session Open(bool pass, int gold = 200)
    {
        var rules = pass ? Easy.Value : Hard.Value;
        var s = ShopTests.AtShop(gold, rules);
        s.Send(0, "interact", new { target = "shop:" + Shop });
        Assert.Equal("shop", s.Screen(0)!.Screen);
        return s;
    }

    private static readonly Lazy<RulesPack> Easy = new(() => WithDc(1));
    private static readonly Lazy<RulesPack> Hard = new(() => WithDc(30));

    private static RulesPack WithDc(int dc) => ExplorationTests.EditedRules(folder =>
    {
        var file = Path.Combine(folder, "shops", "ashen_ford.json");
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!;
        foreach (var shop in json["shops"]!.AsArray())
            if (shop!["id"]!.GetValue<string>() == Shop) shop["haggle"] = dc;
        File.WriteAllText(file, json.ToJsonString());
    });

    private static Session Again(Session s, RulesPack rules)
    {
        var again = new Session(document: s.Lease.Document, game: new TacticsGame(rules, battleList: true));
        again.Harness.Resume(again.Lease);
        for (var i = 0; i < 4; i++) again.Harness.SetConnected(i, true);
        return again;
    }

    private static JsonElement Screen(Session s, int seat = 0) => s.Screen(seat)!.Data;

    private static int Price(Session s, string item, int seat = 0)
        => Screen(s, seat).GetProperty("items").EnumerateArray().Single(i => i.GetProperty("item").GetString() == item).GetProperty("price").GetInt32();

    private static void Haggle(Session s)
    {
        s.Send(0, "shopHaggle", new { shop = Shop });
        Until(s, () => s.Host.Story?.State == TacticsGame.StoryStates.PickRoller, "the pick of who haggles");
    }

    private static void BackInTheShop(Session s)
        => Until(s, () => s.Screen(0)!.Screen == "shop" && s.Host.Story is null, "the shop again", TimeSpan.FromSeconds(60));

    [Fact]
    public void A_pass_takes_a_tenth_off_buys_and_adds_a_tenth_to_sales_for_the_visit_and_only_once()
    {
        var s = Open(pass: true);
        var rules = Easy.Value;
        var potion = rules.Items[new ItemId("potion_of_healing")];
        var shop = rules.ShopsIn("ashen_ford").Single(x => x.Id == Shop);
        Assert.Equal(ShopRules.Buy(potion, shop), Price(s, "potion_of_healing"));
        var haggle = Screen(s).GetProperty("haggle");
        Assert.True(haggle.GetProperty("can").GetBoolean());
        Assert.Equal(1, haggle.GetProperty("dc").GetInt32());
        Assert.False(Screen(s, 1).GetProperty("haggle").GetProperty("can").GetBoolean());     // the leader starts it

        Haggle(s);
        Assert.Equal(TacticsGame.Screens.Check, s.Screen(1)!.Screen);
        Assert.Equal("persuasion", s.Host.Story!.Check!.Skill);
        var roller = s.Profiles[2]!.Value.Value;
        for (var i = 0; i < 4; i++) s.Send(i, "pickRoller", new { member = roller });
        Until(s, () => Doc(s).World!.Shop!.Haggle is not null, "the roll");
        Assert.Equal(roller, Doc(s).World!.Shop!.Haggle!.Member);
        Assert.True(Doc(s).World!.Shop!.Haggle!.Passed);
        BackInTheShop(s);

        Assert.Equal(ShopRules.Buy(potion, shop, haggled: true), Price(s, "potion_of_healing"));
        Assert.Equal((int)decimal.Ceiling(potion.Price!.Value * shop.PriceMod * 0.9m), Price(s, "potion_of_healing"));
        Assert.True(Screen(s).GetProperty("haggle").GetProperty("result").GetProperty("passed").GetBoolean());
        Assert.False(Screen(s).GetProperty("haggle").GetProperty("can").GetBoolean());          // once a visit
        Assert.True(s.Host.Shop!.Haggle!.Passed);
        Assert.Equal(ShopRules.Buy(potion, shop, true), s.Host.Shop.Items.Single(i => i.Item == "potion_of_healing").Price);
        var sells = Screen(s, 1).GetProperty("sell").EnumerateArray().FirstOrDefault(x => x.GetProperty("item").GetString() == "potion_of_healing");
        if (sells.ValueKind == JsonValueKind.Object)
            Assert.Equal(ShopRules.Sell(potion, shop, true), sells.GetProperty("price").GetInt32());

        var gold = Doc(s).Members[s.Profiles[0]!.Value.Value].Gold;
        s.Send(0, "shopBuy", new { shop = Shop, item = "potion_of_healing", count = 1, trade = "haggled-buy" });
        Assert.Equal(gold - ShopRules.Buy(potion, shop, true), Doc(s).Members[s.Profiles[0]!.Value.Value].Gold);

        s.Send(0, "shopHaggle", new { shop = Shop });
        Assert.Null(s.Host.Story);                       // not twice

        // A new visit starts fresh.
        s.Send(0, "shopLeave", new { shop = Shop });
        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
        s.Send(0, "interact", new { target = "shop:" + Shop });
        Assert.Equal(ShopRules.Buy(potion, shop), Price(s, "potion_of_healing"));
        Assert.True(Screen(s).GetProperty("haggle").GetProperty("can").GetBoolean());
    }

    /// <summary>The herbalist as a temple (only cures, DC 1), or as a shop that also cures.</summary>
    private static RulesPack Cures(bool onlyCures) => ExplorationTests.EditedRules(folder =>
    {
        var file = Path.Combine(folder, "shops", "ashen_ford.json");
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!;
        foreach (var shop in json["shops"]!.AsArray())
        {
            if (shop!["id"]!.GetValue<string>() != Shop) continue;
            shop["haggle"] = 1;
            shop["services"] = System.Text.Json.Nodes.JsonNode.Parse("""[{"id":"cure","name":"Healing","kind":"cure","price":25}]""");
            if (onlyCures)
            {
                shop["stock"] = new System.Text.Json.Nodes.JsonArray();
                shop["buys"] = new System.Text.Json.Nodes.JsonArray();
            }
        }
        File.WriteAllText(file, json.ToJsonString());
    });

    // The owner, 2026-11-04: rooms and cures haggle too.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_pass_takes_a_tenth_off_rooms_and_cures_too_even_where_nothing_else_is_sold(bool onlyCures)
    {
        var rules = Cures(onlyCures);
        var s = ShopTests.AtShop(200, rules);
        var doc = s.Game.Document;
        doc = doc with { Members = Ordinal.Map(doc.Members.Select(m => (m.Key, m.Value with { Character = m.Value.Character with { Injuries = [new("cracked_ribs")] } }))) };
        s = RestTests.Again(doc, rules: rules);
        s.Send(0, "interact", new { target = "shop:" + Shop });
        int Cure() => Screen(s).GetProperty("services").EnumerateArray().Single().GetProperty("price").GetInt32();
        Assert.Equal(25, Cure());
        Assert.True(Screen(s).GetProperty("haggle").GetProperty("can").GetBoolean());

        Haggle(s);
        var roller = s.Profiles[0]!.Value.Value;
        for (var i = 0; i < 4; i++) s.Send(i, "pickRoller", new { member = roller });
        Until(s, () => Doc(s).World!.Shop!.Haggle is not null, "the roll");
        BackInTheShop(s);

        Assert.Equal(23, Cure());                                                   // 25 × 0.9 = 22.5, rounded up as buys are
        Assert.Equal(23, s.Host.Shop!.Services!.Value.Single().Price);
        var payer = s.Profiles[0]!.Value.Value;
        s.Send(0, "shopService", new { shop = Shop, service = "cure", member = payer, injury = "cracked_ribs", trade = "haggled-cure" });
        Assert.Equal(200 - 23, Doc(s).Members[payer].Gold);
        Assert.DoesNotContain(new InjuryId("cracked_ribs"), Doc(s).Members[payer].Character.Injuries);
    }

    [Fact]
    public void A_fail_changes_no_price_and_costs_nothing()
    {
        var s = Open(pass: false);
        var before = Price(s, "potion_of_healing");
        var gold = Doc(s).Members.Values.Sum(m => m.Gold);
        Haggle(s);
        Until(s, () => Doc(s).World!.Shop!.Haggle is not null, "the roll", TimeSpan.FromSeconds(60));
        Assert.False(Doc(s).World!.Shop!.Haggle!.Passed);
        BackInTheShop(s);
        Assert.Equal(before, Price(s, "potion_of_healing"));
        Assert.Equal(gold, Doc(s).Members.Values.Sum(m => m.Gold));
        Assert.False(s.Host.Shop!.Haggle!.Passed);
        Assert.False(Screen(s).GetProperty("haggle").GetProperty("can").GetBoolean());
    }

    [Fact]
    public void A_failed_haggle_stays_spent_after_reopen_and_restart_until_the_next_long_rest()
    {
        var s = Open(pass: false);
        Haggle(s);
        Until(s, () => Doc(s).World!.Shop!.Haggle is not null, "the failed roll", TimeSpan.FromSeconds(60));
        // Restart on the result too: its failed attempt must be saved atomically with that roll.
        s = Again(s, Hard.Value);
        BackInTheShop(s);
        s.Send(0, "shopLeave", new { shop = Shop });
        s = Again(s, Hard.Value);
        s.Send(0, "interact", new { target = "shop:" + Shop });
        Assert.False(Screen(s).GetProperty("haggle").GetProperty("can").GetBoolean());
        s.Send(0, "shopHaggle", new { shop = Shop });
        Assert.Null(s.Host.Story);
        // A normal buy must not erase the spent-at-rest stamp.
        s.Send(0, "shopBuy", new { shop = Shop, item = "potion_of_healing", count = 1, trade = "after-refusal" });
        s.Send(0, "shopLeave", new { shop = Shop });
        s = Again(s, Hard.Value);
        s.Send(0, "interact", new { target = "shop:" + Shop });
        Assert.False(Screen(s).GetProperty("haggle").GetProperty("can").GetBoolean());
        s.Send(0, "shopLeave", new { shop = Shop });
        var doc = Doc(s);
        // Dawn increments RestSeq; this test isolates the shop's consumption of that counter.
        s = RestTests.Again(doc with { World = doc.World! with { RestSeq = doc.World.RestSeq + 1 } }, rules: Hard.Value);
        s.Send(0, "interact", new { target = "shop:" + Shop });
        Assert.True(Screen(s).GetProperty("haggle").GetProperty("can").GetBoolean());
    }

    [Fact]
    public void A_restart_during_the_haggle_shows_the_saved_roll_and_never_rolls_again()
    {
        var s = Open(pass: true);
        Haggle(s);
        var roller = s.Profiles[1]!.Value.Value;
        for (var i = 0; i < 4; i++) s.Send(i, "pickRoller", new { member = roller });
        Until(s, () => Cursor(s)?.Roll is not null, "the roll");
        var roll = Cursor(s)!.Roll;
        s = Again(s, Easy.Value);
        Assert.Equal(TacticsGame.StoryStates.Rolled, s.Host.Story!.State);
        Assert.Equal(roll, Cursor(s)!.Roll);
        BackInTheShop(s);
        Assert.Equal(roll!.Die, Doc(s).World!.Shop!.Haggle!.Die);
        s = Again(s, Easy.Value);
        Assert.Equal("shop", s.Screen(0)!.Screen);
        Assert.True(Doc(s).World!.Shop!.Haggle!.Passed);
        Assert.False(Screen(s).GetProperty("haggle").GetProperty("can").GetBoolean());
    }

    [Fact]
    public void A_restart_while_the_party_picks_who_haggles_goes_back_to_the_pick()
    {
        var s = Open(pass: true);
        Haggle(s);
        s = Again(s, Easy.Value);
        Until(s, () => s.Host.Story?.State == TacticsGame.StoryStates.PickRoller, "the pick again");
        Assert.Null(Cursor(s)!.Roll);
    }

    [Fact]
    public void Nobody_here_holds_the_haggle_and_a_phone_back_goes_on_with_it()
    {
        var s = Open(pass: true);
        Haggle(s);
        for (var i = 0; i < 4; i++) s.Harness.SetConnected(i, false);
        s.Harness.Advance(TimeSpan.FromMinutes(2));
        Assert.NotNull(Doc(s).World!.Shop);                // the shop stays open behind the check
        Assert.Null(Cursor(s)!.Roll);
        s.Harness.SetConnected(0, true);
        Assert.Equal(TacticsGame.Screens.Check, s.Screen(0)!.Screen);
        Until(s, () => Doc(s).World!.Shop?.Haggle is not null, "the roll after the phone came back", TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void End_session_mid_haggle_keeps_the_visit_and_its_check_for_the_next_night()
    {
        var s = Open(pass: true);
        Haggle(s);
        s.Send(0, "endSession");
        Assert.True(Doc(s).World!.Shop!.Haggling);
        var next = Again(s, Easy.Value);
        Until(next, () => next.Host.Story?.State == TacticsGame.StoryStates.PickRoller, "the pick on the next night");
        for (var i = 0; i < 4; i++) next.Send(i, "pickRoller", new { member = next.Profiles[0]!.Value.Value });
        BackInTheShop(next);
        Assert.True(Doc(next).World!.Shop!.Haggle!.Passed);
    }
}
