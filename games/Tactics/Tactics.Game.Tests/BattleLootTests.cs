using Tactics.Logic;
using static Tactics.Game.Tests.TacticsGameTests;
using static Tactics.Game.Tests.StageBattleTests;
using static Tactics.Game.Tests.StageEndTests;
using static Tactics.Game.Tests.StoryVoteTests;

namespace Tactics.Game.Tests;

public sealed class BattleLootTests
{
    private static int Count(Session s, string item) => s.Game.Document.Members.Values.Sum(m => m.Character.Bag.Where(b => b.Item.Value == item).Sum(b => b.Count));
    private static string Member(Session s, int seat) => s.Profiles[seat]!.Value.Value;

    // Existing landing tests still check their original behavior, after completing the newly required reward votes.
    internal static void TakePending(Session s)
    {
        var pending = s.Game.Document.World?.Loot?.Items ?? [];
        var before = pending.Select(i => i.Item).Distinct().ToDictionary(i => i, i => Count(s, i));
        while (s.Game.Document.World?.Loot is { Items.IsEmpty: false })
        {
            Until(s, () => s.Host.Story is { State: TacticsGame.StoryStates.Asking }, "the reward question");
            var options = s.Host.Story!.Ask!.Options;
            Assert.NotEmpty(options);
            foreach (var seat in Enumerable.Range(0, 4)) Vote(s, seat, options[0].Id);
            s.Harness.Advance(TimeSpan.FromSeconds(16));
        }
        foreach (var (item, count) in before)
            Assert.Equal(count + pending.Where(i => i.Item == item).Sum(i => i.Count), Count(s, item));
    }

    [Fact]
    public void Every_shipped_table_reward_is_defined_and_awarded_on_a_win()
    {
        var encounters = Rules.Encounters.Values.Where(e => !e.Loot.IsEmpty).ToArray();
        Assert.Equal(9, encounters.Length);
        Assert.Equal(4, Rules.LootTables.Count);
        foreach (var encounter in encounters)
        {
            var s = new Session(); s.ToBattle(encounter.Id);
            var expected = encounter.Loot.SelectMany(l => Rules.LootTables[l.Value].Items)
                .Concat(encounter.ItemLoot).GroupBy(i => i.Item).ToDictionary(g => g.Key.Value, g => g.Sum(i => i.Count));
            var before = expected.Keys.ToDictionary(id => id, id => Count(s, id));
            Ended(s, BattleOutcome.Won);
            foreach (var (item, count) in expected)
                Assert.True(before[item] + count == Count(s, item) + (s.Game.Document.World?.Loot?.Items.Where(i => i.Item == item).Sum(i => i.Count) ?? 0), $"{encounter.Id}: {item} must be in a bag or the persisted vote queue");
            Assert.Equal(expected.OrderBy(x => x.Key), s.Host.Loot!.Value.GroupBy(i => i.Item).ToDictionary(g => g.Key.Value, g => g.Sum(i => i.Count)).OrderBy(x => x.Key));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Ford_with_fewer_than_two_present_players_waits_or_uses_the_only_present_bag(int present)
    {
        var s = Fighting(Force);
        for (var i = 0; i < 4; i++) s.Harness.SetConnected(i, present == 1 && i == 3);
        Ended(s, BattleOutcome.Won);
        Assert.Equal(present, Count(s, "oil_flask"));
        if (present == 0)
            Assert.Equal(1, s.Game.Document.World!.Loot!.Items.Single(i => i.Item == "oil_flask").Count);
        else
        {
            Assert.Equal(1, s.Game.Document.Members[Member(s, 3)].Character.Bag.Single(i => i.Item.Value == "oil_flask").Count);
            Assert.Null(s.Game.Document.World!.Loot);
        }
    }

    [Theory]
    [InlineData(BattleOutcome.Lost)]
    [InlineData(BattleOutcome.Fled)]
    public void Table_rewards_are_not_awarded_for_losses_or_flights(BattleOutcome outcome)
    {
        var s = Fighting(Force);
        var before = Count(s, "potion_of_healing");
        Ended(s, outcome);
        Assert.Equal(before, Count(s, "potion_of_healing"));
        Assert.Equal(0, Count(s, "oil_flask"));
        Assert.Null(s.Game.Document.World!.Loot);
        Assert.Null(s.Host.Loot);
    }

    [Fact]
    public void Ford_rewards_wait_for_present_players_and_survive_restarts_without_duplicating()
    {
        var s = Fighting(Force);
        for (var i = 0; i < 4; i++) s.Harness.SetConnected(i, true);
        var before = Count(s, "potion_of_healing");
        s.Harness.SetConnected(0, false); // first participant must not silently take the reward
        Ended(s, BattleOutcome.Won);
        Assert.Equal(0, Count(s, "oil_flask"));
        Assert.Equal(before, Count(s, "potion_of_healing"));
        Assert.Equal(2, s.Game.Document.World!.Loot!.Items.Length);
        var resumed = new Session(document: s.Lease.Document, game: WithLore(null));
        resumed.Harness.Resume(resumed.Lease);
        resumed.Harness.SetConnected(0, false);
        for (var i = 1; i < 4; i++) resumed.Harness.SetConnected(i, true);
        resumed.Send(1, "continue");
        Until(resumed, () => resumed.Host.Story is { State: TacticsGame.StoryStates.Asking }, "the battle reward vote");
        Assert.Equal("Who takes the Flask of Oil?", resumed.Host.Story!.Title);
        Assert.DoesNotContain(Member(resumed, 0), resumed.Host.Story.Ask!.Options.Select(o => o.Id));
        for (var i = 1; i < 4; i++) Vote(resumed, i, Member(resumed, 3));
        Until(resumed, () => resumed.Host.Story is { State: TacticsGame.StoryStates.Asking, Title: "Who takes the Potion of Healing?" }, "the table reward vote");
        Assert.Equal(1, Count(resumed, "oil_flask"));
        // Restart with the first item awarded and the second still pending.
        var again = new Session(document: resumed.Lease.Document, game: WithLore(null));
        again.Harness.Resume(again.Lease);
        for (var i = 0; i < 4; i++) again.Harness.SetConnected(i, true);
        Until(again, () => again.Host.Story is { State: TacticsGame.StoryStates.Asking }, "the remaining vote");
        for (var i = 0; i < 4; i++) Vote(again, i, Member(again, 2));
        Until(again, () => again.Game.Phase == TacticsPhase.Exploration, "walking after rewards");
        Assert.Equal(1, Count(again, "oil_flask"));
        Assert.Equal(before + 1, Count(again, "potion_of_healing"));
        Assert.Null(again.Game.Document.World!.Loot);
        var final = new Session(document: again.Lease.Document, game: WithLore(null));
        final.Harness.Resume(final.Lease);
        Assert.Equal(1, Count(final, "oil_flask"));
        Assert.Equal(before + 1, Count(final, "potion_of_healing"));
        Assert.Null(final.Game.Document.World!.Loot);
    }
}
