using Tactics.Logic;
using static Tactics.Game.Tests.TacticsGameTests;
using static Tactics.Game.Tests.StageEndTests;

namespace Tactics.Game.Tests;

public sealed class OilLootTests
{
    private static int Oil(Session session) => session.Game.Document.Members.Values.Sum(m => m.Character.Bag.Where(s => s.Item.Value == "oil_flask").Sum(s => s.Count));

    [Theory]
    [InlineData(BattleOutcome.Won, 1)]
    [InlineData(BattleOutcome.Lost, 0)]
    [InlineData(BattleOutcome.Fled, 0)]
    public void Ford_awards_one_flask_to_the_party_on_a_win_and_restoring_does_not_duplicate_it(BattleOutcome outcome, int count)
    {
        var session = new Session(); session.ToBattle("ford_assault");
        Assert.Equal(0, Oil(session));
        Ended(session, outcome);
        Assert.Equal(count, Oil(session));
        var resumed = new Session(document: session.Lease.Document);
        resumed.Harness.Resume(resumed.Lease);
        Assert.Equal(count, Oil(resumed));
    }

    [Fact]
    public void Only_the_authored_chest_and_Hollow_cache_add_flasks_and_starting_kits_have_none()
    {
        var rules = Rules;
        Assert.Equal(10, rules.Items[new ItemId("oil_flask")].Price);
        var encounters = rules.Encounters.Values.SelectMany(e => e.ItemLoot.IsDefault ? [] : e.ItemLoot).Where(s => s.Item.Value == "oil_flask").ToArray();
        Assert.Equal(1, Assert.Single(encounters).Count);
        Assert.Equal("ford_assault", Assert.Single(rules.Encounters.Values, e => !e.ItemLoot.IsDefaultOrEmpty).Id);
        var chests = rules.Interactables.Values.SelectMany(t => t).Where(t => t.Loot.Any(s => s.Item.Value == "oil_flask")).ToArray();
        Assert.Equal(new[] { "af_reeves_chest", "hollow_hidden_cache" }, chests.Select(c => c.Id).Order());
        Assert.Equal(1, chests[0].Loot.Single(s => s.Item.Value == "oil_flask").Count);
        foreach (var cls in rules.Classes.Keys)
            Assert.DoesNotContain(SheetBuilder.DefaultCharacter("kit", "human", cls, null, 1, rules).Bag, s => s.Item.Value == "oil_flask");
    }
}
