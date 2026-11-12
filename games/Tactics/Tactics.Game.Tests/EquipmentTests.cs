using System.Text.Json;
using ArcBox.Sdk;
using Tactics.Logic;

namespace Tactics.Game.Tests;

public sealed class EquipmentTests
{
    private static RulesPack Rules => TacticsGameTests.Rules;
    private static CharacterRecord Character(string cls = "fighter") => SheetBuilder.DefaultCharacter("Kara", "human", cls, null, 3, Rules);

    [Fact]
    public void Equipping_requires_owned_proficient_gear_and_keeps_every_item()
    {
        var c = Character() with { Bag = [new(new("gear_longsword_plus_one"), 1), new(new("gear_leather_plus_one"), 1)] };
        Assert.NotNull(EquipmentRules.Equip(c, "weapon", "gear_rapier", Rules).Problem);
        var weapon = EquipmentRules.Equip(c, "weapon", "gear_longsword_plus_one", Rules);
        Assert.Null(weapon.Problem); Assert.Equal("longsword_plus_one", weapon.Character!.Weapon);
        Assert.Contains(weapon.Character.Bag, s => s.Item.Value == "gear_longsword_plus_one" && s.Count == 1);
        var armor = EquipmentRules.Equip(weapon.Character, "armor", "gear_leather_plus_one", Rules);
        Assert.Null(armor.Problem); Assert.Equal("leather_plus_one", armor.Character!.Armor);
        Assert.Equal(SheetBuilder.ForCharacter(armor.Character with { Armor = "leather" }, Rules).Ac + 1,
            SheetBuilder.ForCharacter(armor.Character, Rules).Ac);
        Assert.NotNull(EquipmentRules.Equip(Character("wizard") with { Bag = c.Bag }, "weapon", "gear_longsword_plus_one", Rules).Problem);
        Assert.NotNull(EquipmentRules.Equip(Character("wizard") with { Bag = c.Bag }, "armor", "gear_leather_plus_one", Rules).Problem);
    }

    [Fact]
    public void Starting_gear_can_be_claimed_once_even_after_a_save_or_remake()
    {
        var c = Character(); var claimed = EquipmentRules.ClaimStarting(c, Rules);
        Assert.True(claimed.EquipmentClaimed); Assert.True(claimed.Bag.Length > c.Bag.Length);
        var restored = JsonSerializer.Deserialize<CharacterRecord>(JsonSerializer.Serialize(claimed, ArcJson.Options), ArcJson.Options)!;
        Assert.Equal(claimed.Bag.Length, EquipmentRules.ClaimStarting(restored, Rules).Bag.Length);
        var remade = SheetBuilder.DefaultCharacter("Kara", "human", "wizard", null, 3, Rules) with { Bag = claimed.Bag, EquipmentClaimed = claimed.EquipmentClaimed };
        Assert.Equal(claimed.Bag.Length, EquipmentRules.ClaimStarting(remade, Rules).Bag.Length);
        Assert.False(EquipmentRules.ClaimStarting(remade, Rules).Bag.Any(s => Rules.Items[s.Item].Equipment?.Weapon == remade.Weapon && !claimed.Bag.Contains(s)));
    }

    [Fact]
    public void Two_handed_weapons_and_shields_cannot_be_combined_and_hexblades_can_use_their_sword()
    {
        var c = Character() with { Shield = true, Bag = [new(new("gear_shortbow_plus_one"), 1)] };
        Assert.NotNull(EquipmentRules.Equip(c, "weapon", "gear_shortbow_plus_one", Rules).Problem);
        c = EquipmentRules.Equip(c, "shield", "", Rules).Character!;
        Assert.Null(EquipmentRules.Equip(c, "weapon", "gear_shortbow_plus_one", Rules).Problem);
        var hex = Character("warlock") with { Subclass = "warlock.hexblade", Bag = [new(new("gear_longsword_plus_one"), 1)] };
        Assert.Null(EquipmentRules.Equip(hex, "weapon", "gear_longsword_plus_one", Rules).Problem);
    }

    [Fact]
    public void Each_post_chapter_one_story_battle_awards_non_consumable_equipment()
    {
        foreach (var beat in StoryTests.Lore.Arcs.Where(a => !a.SideAdventure && a.Chapter > 1).SelectMany(a => a.Beats).Where(b => b.Encounter is not null))
        {
            var items = Rules.Encounters[beat.Encounter!].Loot.SelectMany(id => Rules.LootTables[id.Value].Items).Select(l => Rules.Items[l.Item]);
            Assert.Contains(items, i => i.Equipment is not null && i.Use is null);
        }
        var shop = Rules.ShopsIn("ashen_ford").Single(s => s.Id == "af_market_smith");
        Assert.Contains("sq_forge_done", shop.Requires);
        Assert.Contains(shop.Stock, s => Rules.Items[s.Item].Equipment?.Weapon?.EndsWith("_plus_one", StringComparison.Ordinal) == true);
        Assert.Contains(shop.Stock, s => Rules.Items[s.Item].Equipment?.Armor?.EndsWith("_plus_one", StringComparison.Ordinal) == true);
    }
    [Fact]
    public void Equipped_gear_cannot_be_sold_or_given_away_and_rejected_trades_change_nothing()
    {
        var rules = ExplorationTests.EditedRules(folder =>
        {
            var file = Path.Combine(folder, "shops", "ashen_ford.json");
            var data = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!;
            data["shops"]![0]!["buys"] = System.Text.Json.Nodes.JsonNode.Parse("[\"equipment\"]");
            File.WriteAllText(file, data.ToJsonString());
        });
        var s = ShopTests.AtShop(100, rules); var who = s.Profiles[0]!.Value.Value;
        var member = s.Game.Document.Members[who];
        var doc = s.Game.Document with { Members = s.Game.Document.Members.SetItem(who, member with
        { Character = member.Character with { Weapon = "longsword_plus_one", EquipmentClaimed = true, Bag = [new(new("gear_longsword_plus_one"), 1)] } }) };
        s = RestTests.Again(doc, rules: rules);
        var before = s.Game.Document.ToJson().GetRawText();
        s.Send(0, "give", new { member = s.Profiles[1]!.Value.Value, item = "gear_longsword_plus_one", count = 1, trade = "equipped-gift" });
        Assert.Equal(before, s.Game.Document.ToJson().GetRawText());
        s.Send(0, "interact", new { target = "shop:af_market_herbalist" });
        before = s.Game.Document.ToJson().GetRawText();
        s.Send(0, "shopSell", new { shop = "af_market_herbalist", member = who, item = "gear_longsword_plus_one", count = 1, trade = "equipped-sale" });
        Assert.Equal(before, s.Game.Document.ToJson().GetRawText());
        Assert.Equal(100, s.Game.Document.Members[who].Gold);
    }

    [Fact]
    public void Portal_equips_atomically_and_refuses_changes_during_a_saved_battle()
    {
        var session = ShopTests.AtShop(); var owner = session.Profiles[0]!.Value;
        var member = session.Game.Document.For(owner)!;
        var doc = session.Game.Document with { Members = session.Game.Document.Members.SetItem(owner.Value,
            member with { Character = Character() with { Bag = [new(new("gear_longsword_plus_one"), 1)] } }) };
        var portal = new TacticsCampaignRules(Rules);
        var request = JsonSerializer.SerializeToElement(new { slot = "weapon", item = "gear_longsword_plus_one" }, ArcJson.Options);
        var result = portal.Handle(session.Lease.Info, doc.ToJson(), owner, "equip", request);
        Assert.Null(result.Error); Assert.Equal("longsword_plus_one", TacticsDocument.Read(result.Document!.Value).For(owner)!.Character.Weapon);
        PhoneFixtureTests.Check(new SortedDictionary<string, System.Text.Json.Nodes.JsonNode>(StringComparer.Ordinal)
        {
            ["portal-equipment"] = System.Text.Json.Nodes.JsonNode.Parse(portal.PortalView(session.Lease.Info, result.Document.Value, owner).GetRawText())!,
        });
        var battle = new TacticsGameTests.Session(); battle.ToBattle();
        doc = doc with { Battle = battle.Game.Document.Battle };
        Assert.NotNull(portal.Handle(session.Lease.Info, doc.ToJson(), owner, "equip", request).Error);
    }
}
