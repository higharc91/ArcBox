using System.Text.Json.Nodes;
using ArcBox.Sdk;
using Tactics.Logic;
using static Tactics.Game.Tests.StoryTests;
using static Tactics.Game.Tests.StoryVoteTests;
using static Tactics.Game.Tests.TacticsGameTests;

namespace Tactics.Game.Tests;

/// <summary>The review of issue #83's build: loot that waited with no vote, recovery mid-vote, a deferred service, who a roll's trap hurts, old saves' failed ways, and voters who left.</summary>
public sealed class OwnerDecisionReviewTests
{
    private static TacticsDocument Doc(Session s) => TacticsDocument.Read(s.Lease.Document);

    private static string Member(Session s, int seat) => s.Profiles[seat]!.Value.Value;

    private static int Count(TacticsDocument doc, string member, string item)
        => doc.Members[member].Character.Bag.Where(b => b.Item.Value == item).Sum(b => b.Count);

    private static int Everyone(TacticsDocument doc, string item)
        => doc.Members.Values.Sum(m => m.Character.Bag.Where(b => b.Item.Value == item).Sum(b => b.Count));

    private static readonly Lazy<LoreBook> Book = new(() => LoreBook.Load(ExplorationTests.ContentRules()));

    /// <summary>A session on <paramref name="doc"/> with these rules and lore, everyone here.</summary>
    private static Session Open(TacticsDocument doc, RulesPack rules, LoreBook? lore = null)
    {
        var s = new Session(document: doc.ToJson(), game: new TacticsGame(rules, lore: lore ?? Book.Value));
        s.Harness.Resume(s.Lease);
        for (var i = 0; i < 4; i++) s.Harness.SetConnected(i, true);
        return s;
    }

    private static void Step(Session s, string dir)
    {
        s.Send(0, "walk", new { dir });
        s.Send(0, "walk", new { dir = (string?)null });
        s.Harness.Advance(TimeSpan.FromSeconds(1));
    }

    // ---- 2a: a quest paid while walking asks at once --------------------------------------------------------------------------

    [Fact]
    public void A_quests_item_paid_by_a_step_is_voted_on_at_once_not_after_the_next_scene()
    {
        var rules = Vault.Load(["""{ "id": "snare", "kind": "trap", "at": [2, 2], "trap": { "spot": 30, "disarm": 10, "sets": ["sq_forge_asked", "sq_forge_fuel", "sq_forge_done"] } }"""]);
        var s = Open(Vault.At(new Tile(2, 1), rules).Game.Document, rules);
        var oil = Everyone(Doc(s), "oil_flask");
        Step(s, "sw");
        Assert.Contains("sq_forge_done", Doc(s).World!.Flags);
        Until(s, () => s.Host.Story is { State: TacticsGame.StoryStates.Asking, Title: "Who takes the Flask of Oil?" }, "the reward's vote", TimeSpan.FromSeconds(10));
        Until(s, () => s.Game.Phase == TacticsPhase.Exploration, "the vote's close", TimeSpan.FromSeconds(30));
        Assert.Equal(oil + 1, Everyone(Doc(s), "oil_flask"));
        Assert.Null(Doc(s).World!.Loot);
    }

    // ---- 2b: a recovery mid-vote keeps the items and asks again ---------------------------------------------------------------

    [Fact]
    public void A_recovery_mid_vote_keeps_the_items_drops_the_vote_and_the_next_walk_asks_again()
    {
        var s = Vault.At(new Tile(2, 1), LootVoteTests.Hoard.Value);
        var bombs = Everyone(Doc(s), "smoke_bomb");
        s.Send(0, "interact", new { target = "bomb" });
        Until(s, () => s.Host.Story?.State == TacticsGame.StoryStates.Asking, "the loot vote");
        s.Game.WorldActionCompleted = path => { if (path == "input:vote") throw new InvalidOperationException("injected loot vote"); };
        s.Send(1, "vote", new { option = Member(s, 1) });
        s.Game.WorldActionCompleted = null;
        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
        var loot = Doc(s).World!.Loot!;
        Assert.Equal((false, (StorySave?)null), (loot.Voting, loot.Under));
        Assert.Empty(loot.Choices);
        Assert.Equal("smoke_bomb", loot.Items.Single().Item);
        Assert.Null(Doc(s).World!.Story);
        // A restart can't bring back a cursor the recovery dropped.
        var again = Open(Doc(s), LootVoteTests.Hoard.Value);
        Until(again, () => again.Host.Story is { State: TacticsGame.StoryStates.Asking, Title: "Who takes the Smoke Bomb?" }, "the vote asked again");
        // And in the session that recovered, the next thing anyone does asks again too.
        s.Send(0, "walk", new { dir = (string?)null });
        Until(s, () => s.Host.Story is { State: TacticsGame.StoryStates.Asking, Title: "Who takes the Smoke Bomb?" }, "the vote asked again");
        Until(s, () => s.Game.Phase == TacticsPhase.Exploration, "the vote's close", TimeSpan.FromSeconds(30));
        Assert.Equal(bombs + 1, Everyone(Doc(s), "smoke_bomb"));
    }

    // ---- 3: a conversation that gives an item and opens a service -------------------------------------------------------------

    private static readonly Lazy<LoreBook> GivingInn = new(() =>
    {
        var source = Path.Combine(ExplorationTests.ContentRules(), "lore");
        var dir = Directory.CreateTempSubdirectory("tactics-lore-");
        try
        {
            var lore = Directory.CreateDirectory(Path.Combine(dir.FullName, "lore")).FullName;
            foreach (var entry in Directory.EnumerateFileSystemEntries(source))
                if (Path.GetFileName(entry) != "conversations")
                    File.CreateSymbolicLink(Path.Combine(lore, Path.GetFileName(entry)), entry);
            var conversations = Directory.CreateDirectory(Path.Combine(lore, "conversations")).FullName;
            foreach (var file in Directory.EnumerateFiles(Path.Combine(source, "conversations")))
                if (Path.GetFileName(file) != "bree_tallow.json")
                    File.CreateSymbolicLink(Path.Combine(conversations, Path.GetFileName(file)), file);
            var bree = JsonNode.Parse(File.ReadAllText(Path.Combine(source, "conversations", "bree_tallow.json")))!;
            static IEnumerable<JsonObject> All(JsonNode? node) => node switch
            {
                JsonObject o => [o, .. o.SelectMany(p => All(p.Value))],
                JsonArray a => a.SelectMany(All),
                _ => [],
            };
            var room = All(bree).Single(o => o["id"] is JsonValue id && id.GetValueKind() == System.Text.Json.JsonValueKind.String
                && id.GetValue<string>() == "room" && o["service"] is not null);
            room["give"] = "smoke_bomb";
            File.WriteAllText(Path.Combine(conversations, "bree_tallow.json"), bree.ToJsonString());
            return LoreBook.Load(dir.FullName);
        }
        finally { dir.Delete(recursive: true); }
    });

    [Fact]
    public void A_conversation_that_gives_an_item_and_opens_the_inn_opens_it_after_the_vote_even_across_a_restart()
    {
        var at = Rules.RegionFor("ashen_ford")!.Def.People.Single(p => p.Id == "bree_tallow").At;
        var doc = TacticsDocument.Read(ExplorationTests.DocumentAt(ExplorationTests.Saved(at)));
        var s = new Session(document: doc.ToJson(), game: new TacticsGame(Rules, lore: GivingInn.Value));
        s.Harness.StartCampaign(s.Lease);
        var bombs = Everyone(Doc(s), "smoke_bomb");
        ConversationQuestTests.Talk(s, "bree_tallow");
        ConversationQuestTests.Choose(s, "room");
        Until(s, () => s.Host.Story is { State: TacticsGame.StoryStates.Asking, Title: "Who takes the Smoke Bomb?" }, "the gift's vote", TimeSpan.FromMinutes(2));
        Assert.Equal("af_inn", Doc(s).World!.Loot!.Service);
        var again = new Session(document: s.Lease.Document, game: new TacticsGame(Rules, lore: GivingInn.Value));
        again.Harness.Resume(again.Lease);
        for (var i = 0; i < 4; i++) again.Harness.SetConnected(i, true);
        Until(again, () => again.Host.Story?.State == TacticsGame.StoryStates.Asking, "the vote after the restart");
        for (var i = 0; i < 4; i++) Vote(again, i, Member(again, 2));
        Until(again, () => again.Screen(0)?.Screen == "shop", "the inn after the vote");
        Assert.Equal("af_inn", Doc(again).World!.Shop!.ShopId);
        Assert.Equal(bombs + 1, Everyone(Doc(again), "smoke_bomb"));
        Assert.Null(Doc(again).World!.Loot);
    }

    // ---- 4: a failed disarm hurts whoever rolled it ---------------------------------------------------------------------------

    [Fact]
    public void A_failed_disarm_hurts_the_roller_not_the_one_who_began_it()
    {
        var rules = Vault.Load(["""{ "id": "plate", "kind": "trap", "at": [4, 2], "trap": { "spot": 1, "disarm": 30, "injury": "singed" } }"""]);
        var s = Vault.At(new Tile(5, 2), rules);
        var roller = Member(s, 2);
        s.Send(0, "interact", new { target = "plate" });
        Until(s, () => s.Host.Story?.State == TacticsGame.StoryStates.Asking, "the question");
        Vote(s, 0, "disarm");
        Until(s, () => s.Host.Story?.State == TacticsGame.StoryStates.PickRoller, "who rolls");
        for (var i = 0; i < 4; i++) s.Send(i, "pickRoller", new { member = roller });
        Until(s, () => s.Game.Phase == TacticsPhase.Exploration, "walking again", TimeSpan.FromSeconds(60));
        Assert.Contains(new InjuryId("singed"), Doc(s).Members[roller].Character.Injuries);
        Assert.DoesNotContain(new InjuryId("singed"), Doc(s).Members[Member(s, 0)].Character.Injuries);
    }

    // ---- 5: an old save's failed ways reopen at the next rest, not at once ----------------------------------------------------

    [Fact]
    public void An_old_saves_failed_way_is_spent_until_the_next_rest_not_retryable_at_once()
    {
        var s = Vault.At(new Tile(4, 2), OwnerDecisionTests.Keep.Value, w => w with
        {
            RestReady = true, RestSeq = 2,
            Things = Ordinal.Map(("door", new InteractableSave("closed", Spent: ["pick"])), ("cache", new InteractableSave("closed", Searched: true))),
        });
        Assert.Equal(3, Doc(s).World!.Things!["door"].Rest);      // stamped as read: spent until rest 3
        Assert.Equal(3, Doc(s).World!.Things!["cache"].Rest);
        s.Send(0, "interact", new { target = "door" });
        Until(s, () => s.Host.Story?.State == TacticsGame.StoryStates.Asking, "the door's question");
        Assert.Equal(["force", "leave"], s.Host.Story!.Ask!.Options.Select(o => o.Id));
        Vote(s, 0, "leave");
        Until(s, () => s.Game.Phase == TacticsPhase.Exploration, "walking again");
        s.Send(0, "search");
        Assert.Empty(Cursor(s)!.Thing!.Search);
        Until(s, () => s.Game.Phase == TacticsPhase.Exploration, "walking again", TimeSpan.FromSeconds(60));
        s.Send(0, "interact", new { target = "fire" });
        RestTests.Vote(s);
        for (var i = 0; i < 4; i++) s.Send(i, "campJob", new { job = "rest" });
        RestTests.Finish(s);
        s.Send(0, "interact", new { target = "door" });
        Until(s, () => s.Host.Story?.State == TacticsGame.StoryStates.Asking, "the door's question after the rest");
        Assert.Equal(["pick", "force", "leave"], s.Host.Story!.Ask!.Options.Select(o => o.Id));
    }

    // ---- 6: only the players here count, and nobody here keeps it waiting -----------------------------------------------------

    [Fact]
    public void A_vote_from_someone_who_left_counts_for_nothing_and_the_item_goes_to_someone_here()
    {
        var s = Vault.At(new Tile(2, 1), LootVoteTests.Hoard.Value);
        var gone = Member(s, 3);
        var before = Count(Doc(s), gone, "smoke_bomb");
        var opener = Count(Doc(s), Member(s, 0), "smoke_bomb");
        s.Send(0, "interact", new { target = "bomb" });
        Until(s, () => s.Host.Story?.State == TacticsGame.StoryStates.Asking, "the loot vote");
        s.Send(3, "vote", new { option = gone });
        s.Harness.SetConnected(3, false);
        Until(s, () => s.Game.Phase == TacticsPhase.Exploration, "the vote's close", TimeSpan.FromSeconds(30));
        Assert.Equal(before, Count(Doc(s), gone, "smoke_bomb"));
        Assert.Equal(opener + 1, Count(Doc(s), Member(s, 0), "smoke_bomb"));
    }

    [Fact]
    public void An_opener_who_left_gets_nothing_by_silence_and_with_nobody_here_the_item_waits()
    {
        var s = Vault.At(new Tile(2, 1), LootVoteTests.Hoard.Value);
        var opener = Member(s, 0);
        var mine = Count(Doc(s), opener, "smoke_bomb");
        var bombs = Everyone(Doc(s), "smoke_bomb");
        s.Send(0, "interact", new { target = "bomb" });
        Until(s, () => s.Host.Story?.State == TacticsGame.StoryStates.Asking, "the loot vote");
        s.Harness.SetConnected(0, false);
        Until(s, () => Doc(s).World!.Loot is null, "the vote's close", TimeSpan.FromSeconds(30));
        Assert.Equal(mine, Count(Doc(s), opener, "smoke_bomb"));
        Assert.Equal(bombs + 1, Everyone(Doc(s), "smoke_bomb"));

        // Nobody here at all: the vote holds, the item waits, and never throws.
        s = Vault.At(new Tile(2, 1), LootVoteTests.Hoard.Value);
        s.Send(0, "interact", new { target = "bomb" });
        Until(s, () => s.Host.Story?.State == TacticsGame.StoryStates.Asking, "the loot vote");
        for (var i = 0; i < 4; i++) s.Harness.SetConnected(i, false);
        s.Harness.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal("smoke_bomb", Doc(s).World!.Loot!.Items.Single().Item);
    }

    // ---- the key ring names only a place the party found the key -------------------------------------------------------------

    [Fact]
    public void A_key_the_story_gave_names_no_place_and_never_an_unfound_one()
    {
        var keys = KeyRing.Of(OwnerDecisionTests.Keep.Value, ["brass_key"], null);
        Assert.Null(keys.Single().Found);
        keys = KeyRing.Of(OwnerDecisionTests.Keep.Value, ["brass_key"], Ordinal.Map(("brass", new InteractableSave("taken"))));
        Assert.Equal("The Vault", keys.Single().Found);
    }
}
