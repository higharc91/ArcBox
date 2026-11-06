using System.Text.Json;
using Tactics.Logic;
using static Tactics.Game.Tests.TacticsGameTests;

namespace Tactics.Game.Tests;

public sealed class GiveTests
{
    private const string Potion = "potion_of_healing";
    private static int Count(TacticsDocument doc, string who) => doc.Members[who].Character.Bag.Where(i => i.Item.Value == Potion).Sum(i => i.Count);
    private static Session Ready()
    {
        var s = ShopTests.AtShop(100);
        var doc = s.Game.Document;
        var member = doc.Members[s.Profiles[0]!.Value.Value];
        doc = doc with { Members = doc.Members.SetItem(s.Profiles[0]!.Value.Value, member with { Character = member.Character with { Bag = [new(new(Potion), 3)] } }) };
        return RestTests.Again(doc, rules: Rules);
    }

    [Fact]
    public void An_item_and_gold_reach_an_absent_member_in_one_save_and_a_retry_after_restart_is_a_noop()
    {
        var s = Ready();
        var from = s.Profiles[0]!.Value.Value;
        var to = s.Profiles[3]!.Value.Value;
        s.Harness.SetConnected(3, false);
        var before = s.Game.Document;
        var saves = s.Lease.Saves.Count;
        var gift = new { member = to, item = Potion, count = 1, gold = 20, trade = "gift-one" };
        s.Send(0, "give", gift);
        var after = s.Game.Document;
        Assert.Equal(80, after.Members[from].Gold);
        Assert.Equal(120, after.Members[to].Gold);
        Assert.Equal(2, Count(after, from));
        Assert.Equal(Count(before, to) + 1, Count(after, to));
        Assert.Equal(saves + 1, s.Lease.Saves.Count);
        Assert.Contains("gave", s.Host.Notice);
        Assert.Contains("20 gold", s.Host.Notice);
        s.Send(0, "giveOpen");
        var option = s.Screen(0)!.Data.GetProperty("give").GetProperty("members").EnumerateArray().Single(m => m.GetProperty("id").GetString() == to);
        Assert.True(option.GetProperty("away").GetBoolean());
        s.Send(0, "give", gift);
        Assert.Equal(after.ToJson().GetRawText(), s.Game.Document.ToJson().GetRawText());
        s = RestTests.Again(after, rules: Rules);
        var restarted = s.Game.Document.ToJson().GetRawText();
        s.Send(0, "give", gift);
        Assert.Equal(restarted, s.Game.Document.ToJson().GetRawText());
    }

    [Fact]
    public void Recovery_during_a_gift_never_leaves_half_a_transfer_and_the_same_id_can_finish_after_restart()
    {
        var s = Ready();
        var from = s.Profiles[0]!.Value.Value;
        var to = s.Profiles[3]!.Value.Value;
        var before = s.Game.Document;
        s.Game.WorldActionCompleted = path => { if (path == "input:give") throw new InvalidOperationException("interrupted gift"); };
        s.Send(0, "give", new { member = to, item = Potion, count = 1, gold = 20, trade = "interrupted" });
        foreach (var saved in s.Lease.Saves)
        {
            var doc = TacticsDocument.Read(saved);
            var moved = before.Members[from].Gold - doc.Members[from].Gold;
            Assert.True(moved is 0 or 20);
            Assert.Equal(moved, doc.Members[to].Gold - before.Members[to].Gold);
            Assert.Equal(moved / 20, Count(before, from) - Count(doc, from));
            Assert.Equal(moved / 20, Count(doc, to) - Count(before, to));
        }
        s = RestTests.Again(TacticsDocument.Read(s.Lease.Document), rules: Rules);
        var gift = new { member = to, item = Potion, count = 1, gold = 20, trade = "interrupted" };
        s.Send(0, "give", gift);
        s.Send(0, "give", gift);
        Assert.Equal(80, s.Game.Document.Members[from].Gold);
        Assert.Equal(120, s.Game.Document.Members[to].Gold);
    }

    [Fact]
    public void Gold_only_and_item_only_gifts_work_and_old_receipts_remain_idempotent_per_sender()
    {
        var s = Ready();
        var to = s.Profiles[3]!.Value.Value;
        var before = s.Game.Document;
        for (var i = 0; i < 40; i++) s.Send(0, "give", new { member = to, gold = 1, trade = "gold-" + i });
        s = RestTests.Again(s.Game.Document, rules: Rules);
        s.Send(0, "give", new { member = to, gold = 1, trade = "gold-0" });
        Assert.Equal(60, s.Game.Document.Members[s.Profiles[0]!.Value.Value].Gold);
        s.Send(1, "give", new { member = to, gold = 1, trade = "gold-0" }); // ids belong to the sender
        Assert.Equal(141, s.Game.Document.Members[to].Gold);
        s.Send(0, "give", new { member = to, item = Potion, count = 1, trade = "item-only" });
        Assert.Equal(Count(before, to) + 1, Count(s.Game.Document, to));
        Assert.Equal(141, s.Game.Document.Members[to].Gold);
    }

    [Theory]
    [InlineData("{\"member\":\"pr_member4\",\"gold\":101,\"trade\":\"bad\"}")]
    [InlineData("{\"member\":\"pr_member4\",\"gold\":-1,\"trade\":\"bad\"}")]
    [InlineData("{\"member\":\"pr_member4\",\"item\":\"potion_of_healing\",\"count\":4,\"gold\":20,\"trade\":\"bad\"}")]
    [InlineData("{\"member\":\"pr_member4\",\"item\":\"missing\",\"count\":1,\"gold\":20,\"trade\":\"bad\"}")]
    [InlineData("{\"member\":\"pr_member1\",\"gold\":20,\"trade\":\"bad\"}")]
    [InlineData("{\"member\":\"missing\",\"gold\":20,\"trade\":\"bad\"}")]
    [InlineData("{\"member\":\"pr_member4\",\"gold\":20}")]
    public void Invalid_gifts_move_nothing(string json)
    {
        var s = Ready();
        var before = s.Game.Document;
        s.Send(0, "give", JsonSerializer.Deserialize<JsonElement>(json));
        Assert.Equal(before.Members, s.Game.Document.Members);
    }

    [Fact]
    public void Give_is_ignored_during_battle()
    {
        var s = new Session(); s.ToBattle();
        var before = s.Game.Document.Members;
        s.Send(0, "give", new { member = "pr_member4", gold = 1, trade = "battle" });
        Assert.Equal(before, s.Game.Document.Members);
    }
}
