using System.Text.Json;
using ArcBox.Sdk;
using Tactics.Logic;
using static Tactics.Game.Tests.StoryTests;
using static Tactics.Game.Tests.StoryVoteTests;
using static Tactics.Game.Tests.TacticsGameTests;

namespace Tactics.Game.Tests;

/// <summary>
/// Found items: the party picks whose bag (the owner's decision 1, 2026-11-03, issue #83). A short party vote on the 5d vote screen, between
/// the players here with a character; 15 seconds, and silence gives it to whoever found it; one vote per item, in order; a party of one
/// takes it at once. Restarts, reconnects and End session never give an item twice or lose one.
/// </summary>
public sealed class LootVoteTests
{
    internal static readonly Lazy<RulesPack> Hoard = new(() => Vault.Load([
        """{ "id": "chest", "kind": "chest", "at": [3, 1], "loot": [{ "item": "potion_of_healing", "count": 2 }, { "gold": 40 }, { "item": "smoke_bomb" }] }""",
        """{ "id": "bomb", "kind": "pickup", "at": [1, 2], "name": "smoke bomb", "loot": [{ "item": "smoke_bomb" }] }""",
        """{ "id": "purse", "kind": "pickup", "at": [2, 2], "name": "purse", "loot": [{ "gold": 12 }] }""",
    ]));

    private static TacticsDocument Doc(Session s) => TacticsDocument.Read(s.Lease.Document);

    private static string Member(Session s, int seat) => s.Profiles[seat]!.Value.Value;

    private static int Count(TacticsDocument doc, string member, string item)
        => doc.Members[member].Character.Bag.Where(b => b.Item.Value == item).Sum(b => b.Count);

    private static int Everyone(TacticsDocument doc, string item)
        => doc.Members.Values.Sum(m => m.Character.Bag.Where(b => b.Item.Value == item).Sum(b => b.Count));

    private static int Waiting(TacticsDocument doc, string item)
        => doc.World!.Loot?.Items.Where(i => i.Item == item).Sum(i => i.Count) ?? 0;

    private static Session At(Tile? at = null, int connected = 4)
    {
        var s = Vault.At(at ?? new Tile(2, 1), Hoard.Value);
        for (var i = connected; i < 4; i++) s.Harness.SetConnected(i, false);
        return s;
    }

    private static Session Again(Session s, int connected = 4)
    {
        var again = new Session(document: s.Lease.Document, game: new TacticsGame(Hoard.Value));
        again.Harness.Resume(again.Lease);
        for (var p = 0; p < connected; p++) again.Harness.SetConnected(p, true);
        return again;
    }

    private static void Asked(Session s, string question)
    {
        Until(s, () => s.Host.Story is { State: TacticsGame.StoryStates.Asking } story && story.Title == question, question);
    }

    [Fact]
    public void Opening_a_chest_asks_the_party_who_takes_each_item_in_order_and_splits_the_gold_at_once()
    {
        var s = At();
        var gold = Doc(s).Members.Values.Sum(m => m.Gold);
        var before = (Count(Doc(s), Member(s, 2), "potion_of_healing"), Count(Doc(s), Member(s, 3), "smoke_bomb"));
        s.Send(0, "interact", new { target = "chest" });
        Assert.Equal(gold + 40, Doc(s).Members.Values.Sum(m => m.Gold));            // the gold is split across the purses, as ever
        Assert.Equal(TacticsPhase.Story, s.Game.Phase);
        Asked(s, "Who takes the 2 × Potion of Healing?");
        var ask = Story(s).Ask!;
        Assert.True(ask.Party);
        Assert.Equal([.. Enumerable.Range(0, 4).Select(i => Member(s, i)).Order(StringComparer.Ordinal)], ask.Options.Select(o => o.Id));
        var phone = s.Screen(1)!;
        Assert.Equal(TacticsGame.Screens.Vote, phone.Screen);
        Assert.Equal("Who takes the 2 × Potion of Healing?", phone.Data.GetProperty("question").GetString());
        Assert.True(phone.Data.GetProperty("party").GetBoolean());
        Assert.Equal(TacticsGame.LootVoteLength.TotalSeconds, (phone.DeadlineUtc!.Value - s.Harness.Now).TotalSeconds, 1);

        for (var i = 0; i < 4; i++) Vote(s, i, Member(s, 2));
        Asked(s, "Who takes the Smoke Bomb?");
        Assert.Equal(before.Item1 + 2, Count(Doc(s), Member(s, 2), "potion_of_healing"));
        Assert.Equal(0, Waiting(Doc(s), "potion_of_healing"));
        Assert.Equal(1, Waiting(Doc(s), "smoke_bomb"));
        Assert.Contains("takes the 2 × Potion of Healing", s.Host.Notice);
        Assert.Equal(1, s.Screen(0)!.Data.GetProperty("vote").GetInt32());        // a new question on every phone

        for (var i = 0; i < 4; i++) Vote(s, i, Member(s, 3));
        Until(s, () => s.Game.Phase == TacticsPhase.Exploration, "walking again");
        Assert.Equal(before.Item2 + 1, Count(Doc(s), Member(s, 3), "smoke_bomb"));
        Assert.Null(Doc(s).World!.Loot);
        Assert.Null(Doc(s).World!.Story);
    }

    [Fact]
    public void Nobody_answering_for_fifteen_seconds_gives_it_to_whoever_opened_it()
    {
        var s = At(new Tile(2, 1));
        var leader = Member(s, 0);
        var before = Count(Doc(s), leader, "smoke_bomb");
        s.Send(0, "interact", new { target = "bomb" });
        Asked(s, "Who takes the Smoke Bomb?");
        s.Harness.Advance(TacticsGame.LootVoteLength - TimeSpan.FromSeconds(2));
        Assert.Equal(TacticsPhase.Story, s.Game.Phase);
        Until(s, () => s.Game.Phase == TacticsPhase.Exploration, "the vote's close", TimeSpan.FromSeconds(30));
        Assert.Equal(before + 1, Count(Doc(s), leader, "smoke_bomb"));
    }

    [Fact]
    public void A_tied_vote_goes_to_the_leaders_pick_as_any_vote_does()
    {
        var s = At(new Tile(2, 1));
        s.Send(0, "interact", new { target = "bomb" });
        Asked(s, "Who takes the Smoke Bomb?");
        var before = Count(Doc(s), Member(s, 1), "smoke_bomb");
        Vote(s, 0, Member(s, 1));
        Vote(s, 1, Member(s, 1));
        Vote(s, 2, Member(s, 2));
        Vote(s, 3, Member(s, 2));
        Until(s, () => s.Game.Phase == TacticsPhase.Exploration, "the vote's close");
        Assert.Equal(before + 1, Count(Doc(s), Member(s, 1), "smoke_bomb"));
    }

    [Fact]
    public void A_party_of_one_takes_it_at_once_with_no_vote()
    {
        var s = At(new Tile(2, 1), connected: 1);
        var before = Count(Doc(s), Member(s, 0), "smoke_bomb");
        s.Send(0, "interact", new { target = "bomb" });
        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
        Assert.Equal(before + 1, Count(Doc(s), Member(s, 0), "smoke_bomb"));
        Assert.Null(Doc(s).World!.Loot);
    }

    [Fact]
    public void Gold_alone_has_no_vote()
    {
        var s = At(new Tile(2, 1));
        var gold = Doc(s).Members.Values.Sum(m => m.Gold);
        s.Send(0, "interact", new { target = "purse" });
        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
        Assert.Equal(gold + 12, Doc(s).Members.Values.Sum(m => m.Gold));
    }

    [Fact]
    public void A_restart_mid_vote_asks_the_same_item_again_and_never_gives_one_twice()
    {
        var s = At();
        var start = (Everyone(Doc(s), "potion_of_healing"), Everyone(Doc(s), "smoke_bomb"));
        s.Send(0, "interact", new { target = "chest" });
        Asked(s, "Who takes the 2 × Potion of Healing?");
        for (var i = 0; i < 4; i++) Vote(s, i, Member(s, 1));
        Asked(s, "Who takes the Smoke Bomb?");
        var saved = Doc(s);
        s = Again(s);
        Asked(s, "Who takes the Smoke Bomb?");
        Assert.Equal(start.Item1 + 2, Everyone(Doc(s), "potion_of_healing"));       // the potions went once, before the restart
        Assert.Equal(saved.World!.Loot!.Items, Doc(s).World!.Loot!.Items);
        Assert.Equal(saved.World!.Loot!.Seq, Doc(s).World!.Loot!.Seq);
        s = Again(s);
        Asked(s, "Who takes the Smoke Bomb?");
        Until(s, () => s.Game.Phase == TacticsPhase.Exploration, "the vote's close", TimeSpan.FromSeconds(30));
        Assert.Equal((start.Item1 + 2, start.Item2 + 1), (Everyone(Doc(s), "potion_of_healing"), Everyone(Doc(s), "smoke_bomb")));
        Assert.Equal(1, Count(Doc(s), Member(s, 0), "smoke_bomb") - Count(saved, Member(s, 0), "smoke_bomb"));   // silence: the opener's
        s = Again(s);
        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
        Assert.Equal((start.Item1 + 2, start.Item2 + 1), (Everyone(Doc(s), "potion_of_healing"), Everyone(Doc(s), "smoke_bomb")));
    }

    [Fact]
    public void Voters_who_go_hold_nothing_up_and_with_nobody_here_the_vote_waits()
    {
        var s = At(new Tile(2, 1));
        var before = Count(Doc(s), Member(s, 2), "smoke_bomb");
        s.Send(0, "interact", new { target = "bomb" });
        Asked(s, "Who takes the Smoke Bomb?");
        s.Harness.SetConnected(1, false);
        Vote(s, 0, Member(s, 2));
        Vote(s, 2, Member(s, 2));
        s.Harness.Advance(TimeSpan.FromSeconds(3));
        Assert.NotNull(Doc(s).World!.Loot);                                          // seat 3 hasn't answered: it's still open
        for (var i = 0; i < 4; i++) s.Harness.SetConnected(i, false);
        s.Harness.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(TacticsPhase.Story, s.Game.Phase);                             // held: nobody here to answer
        Assert.NotNull(Doc(s).World!.Loot);
        s.Harness.SetConnected(2, true);
        Assert.Equal(TacticsGame.Screens.Vote, s.Screen(2)!.Screen);
        Assert.Equal(Member(s, 2), s.Screen(2)!.Data.GetProperty("mine").GetString());   // a vote stands while its phone is away
        Until(s, () => s.Game.Phase == TacticsPhase.Exploration, "the vote's close");
        Assert.Equal(before + 1, Count(Doc(s), Member(s, 2), "smoke_bomb"));
    }

    [Fact]
    public void End_session_mid_vote_keeps_the_item_for_the_next_night()
    {
        var s = At(new Tile(2, 1));
        var before = Everyone(Doc(s), "smoke_bomb");
        s.Send(0, "interact", new { target = "bomb" });
        Asked(s, "Who takes the Smoke Bomb?");
        s.Send(0, "endSession");
        Assert.Equal(1, Waiting(Doc(s), "smoke_bomb"));
        Assert.Equal(before, Everyone(Doc(s), "smoke_bomb"));
        var next = Again(s);
        Asked(next, "Who takes the Smoke Bomb?");
        for (var i = 0; i < 4; i++) Vote(next, i, Member(next, 3));
        Until(next, () => next.Game.Phase == TacticsPhase.Exploration, "the vote's close");
        Assert.Equal(before + 1, Everyone(Doc(next), "smoke_bomb"));
        Assert.Null(Doc(next).World!.Loot);
    }

    [Fact]
    public void A_quest_rewards_item_goes_to_the_partys_pick()
    {
        var s = ConversationQuestTests.At("tobin_fell", "sq_forge_asked", "sq_forge_fuel");
        var before = Everyone(s.Game.Document, "oil_flask");
        var taker = s.Profiles[3]!.Value.Value;
        var mine = s.Game.Document.Members[taker].Character.Bag.Where(b => b.Item.Value == "oil_flask").Sum(b => b.Count);
        ConversationQuestTests.Talk(s, "tobin_fell");
        ConversationQuestTests.Choose(s, "give");
        Until(s, () => s.Host.Story is { State: TacticsGame.StoryStates.Asking, Title: "Who takes the Flask of Oil?" }, "the reward's vote", TimeSpan.FromMinutes(3));
        for (var i = 0; i < 4; i++) Vote(s, i, taker);
        Until(s, () => s.Game.Phase == TacticsPhase.Exploration, "walking again");
        Assert.Equal(before + 1, Everyone(s.Game.Document, "oil_flask"));
        Assert.Equal(mine + 1, s.Game.Document.Members[taker].Character.Bag.Where(b => b.Item.Value == "oil_flask").Sum(b => b.Count));
    }
}
