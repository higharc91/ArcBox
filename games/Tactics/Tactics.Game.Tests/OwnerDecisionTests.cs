using System.Text.Json;
using ArcBox.Sdk;
using Tactics.Logic;
using static Tactics.Game.Tests.StoryTests;
using static Tactics.Game.Tests.StoryVoteTests;
using static Tactics.Game.Tests.TacticsGameTests;

namespace Tactics.Game.Tests;

/// <summary>
/// The owner's rule decisions of 2026-11-03 (issue #83), in the game: a sprung trap's minor injury that a rest mends (decision 3), failed
/// ways that reopen after a long rest (decision 2), and the key ring (decision 8). The loot vote and haggling have their own tests.
/// <code>
///   0123456789
/// 0 ##########
/// 1 #.Lf.k...#     L the leader starts at (2, 1); f a campfire; k the brass key
/// 2 #.s..c...#     s a snare nobody sees; c a cache hidden past any search
/// 3 ####D#####     D a door that only its key opens: pick and force are DC 30
/// </code>
/// </summary>
public sealed class OwnerDecisionTests
{
    internal static readonly Lazy<RulesPack> Keep = new(() => Vault.Load([
        """{ "id": "fire", "kind": "campfire", "at": [3, 1] }""",
        """{ "id": "brass", "kind": "pickup", "at": [5, 1], "name": "brass key", "sets": ["brass_key"] }""",
        """{ "id": "snare", "kind": "trap", "at": [2, 2], "trap": { "spot": 30, "disarm": 10 } }""",
        """{ "id": "cache", "kind": "cache", "at": [5, 2], "hidden": 30 }""",
        """{ "id": "door", "kind": "door", "at": [4, 3], "lock": { "key": "brass_key", "keyName": "brass key", "pick": 30, "force": 30 } }""",
    ]));

    private static TacticsDocument Doc(Session s) => TacticsDocument.Read(s.Lease.Document);

    private static string Leader(Session s) => s.Profiles[0]!.Value.Value;

    private static InteractableSave? Saved(Session s, string id) => Doc(s).World!.Things?.GetValueOrDefault(id);

    private static Session At(Tile at, Func<WorldSave, WorldSave>? world = null)
    {
        var s = Vault.At(at, Keep.Value, w => (world?.Invoke(w) ?? w) with { RestReady = true });
        return s;
    }

    private static void Step(Session s, string dir)
    {
        var seq = s.Host.Explore!.Seq;
        s.Send(0, "walk", new { dir });
        s.Send(0, "walk", new { dir = (string?)null });
        s.Harness.Advance(TimeSpan.FromSeconds(1));
        Assert.True(s.Host.Explore!.Seq > seq, $"no step {dir}");
    }

    private static void Choose(Session s, string option)
    {
        Until(s, () => Story(s).State == TacticsGame.StoryStates.Asking, "the question");
        Vote(s, 0, option);
        Until(s, () => Story(s).State != TacticsGame.StoryStates.Asking, "the choice");
    }

    private static void Finish(Session s) => Until(s, () => s.Game.Phase == TacticsPhase.Exploration, "walking again", TimeSpan.FromSeconds(90));

    /// <summary>Camps at the fire: everyone votes to rest, rests easy, and the night ends.</summary>
    private static Session Camp(Session s)
    {
        s.Send(0, "interact", new { target = "fire" });
        RestTests.Vote(s);
        for (var i = 0; i < 4; i++) s.Send(i, "campJob", new { job = "rest" });
        RestTests.Finish(s);
        return s;
    }

    private static Session Again(Session s)
    {
        var again = new Session(document: s.Lease.Document, game: new TacticsGame(Keep.Value));
        again.Harness.Resume(again.Lease);
        for (var p = 0; p < 4; p++) again.Harness.SetConnected(p, true);
        return again;
    }

    // ---- Decision 3: a sprung trap's minor injury ----------------------------------------------------------------------------------

    [Fact]
    public void A_trap_sprung_out_of_battle_gives_the_leader_a_minor_injury_that_a_night_at_camp_mends()
    {
        var s = At(new Tile(2, 1));
        s.Game.TrapRoll = _ => 4;           // the rest-kind table in id order: black eye 1-3, scraped hands 4-6
        var before = Doc(s).Members[Leader(s)].Character.Injuries;
        Step(s, "sw");                      // onto the snare
        Assert.Equal(InteractableSave.Sprung, Saved(s, "snare")!.Trap);
        var leader = Doc(s).Members[Leader(s)].Character;
        Assert.Equal([.. before, new InjuryId("scraped_hands")], leader.Injuries);
        Assert.Equal($"{InteractScenes.DefaultSprung} {leader.Name} takes a minor injury: Scraped hands.", s.Host.Notice);
        Assert.All(Doc(s).Members.Where(m => m.Key != Leader(s)), m => Assert.DoesNotContain(new InjuryId("scraped_hands"), m.Value.Character.Injuries));

        Camp(s);
        Assert.DoesNotContain(new InjuryId("scraped_hands"), Doc(s).Members[Leader(s)].Character.Injuries);
        Assert.Equal(1, Doc(s).World!.RestSeq);
    }

    [Fact]
    public void A_trap_that_names_its_injury_gives_that_one()
    {
        var rules = Vault.Load(["""{ "id": "snare", "kind": "trap", "at": [2, 2], "trap": { "spot": 30, "disarm": 10, "injury": "singed", "text": ["Fire spits from a crack."] } }"""]);
        var s = Vault.At(new Tile(2, 1), rules);
        s.Game.TrapRoll = _ => throw new InvalidOperationException("a trap with its own injury rolls nothing");
        Step(s, "sw");
        Assert.Contains(new InjuryId("singed"), Doc(s).Members[Leader(s)].Character.Injuries);
        Assert.StartsWith("Fire spits from a crack. ", s.Host.Notice);
    }

    // ---- Decision 2: failed ways reopen after a long rest ------------------------------------------------------------------------

    [Fact]
    public void A_lock_whose_pick_and_force_failed_can_be_tried_again_after_the_partys_next_long_rest()
    {
        var s = At(new Tile(4, 2));
        s.Send(0, "interact", new { target = "door" });
        Choose(s, "pick");
        Until(s, () => Cursor(s)?.Roll is not null, "the pick's roll");
        Finish(s);
        Assert.Equal(["pick"], Saved(s, "door")!.Spent);
        s.Send(0, "interact", new { target = "door" });
        Until(s, () => Story(s).State == TacticsGame.StoryStates.Asking, "the question");
        Assert.Equal(["force", "leave"], Story(s).Ask!.Options.Select(o => o.Id));       // the key isn't held, and the pick is spent
        Vote(s, 0, "force");
        Until(s, () => Cursor(s)?.Roll is not null, "the force's roll");
        Finish(s);
        Assert.Equal(["pick", "force"], Saved(s, "door")!.Spent);
        s.Send(0, "interact", new { target = "door" });
        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
        Assert.Equal("The door is locked, and there's nothing left to try until the party has rested.", s.Host.Notice);

        // A restart before the rest changes nothing: still spent.
        s = Again(s);
        s.Send(0, "interact", new { target = "door" });
        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);

        Camp(s);
        s.Send(0, "interact", new { target = "door" });
        Until(s, () => Story(s).State == TacticsGame.StoryStates.Asking, "the question after the rest");
        Assert.Equal(["pick", "force", "leave"], Story(s).Ask!.Options.Select(o => o.Id));
        Vote(s, 0, "pick");
        Until(s, () => Cursor(s)?.Roll is not null, "the second pick's roll");
        Finish(s);
        // Spent again, until the rest after this one; the save says which rest it was spent in.
        Assert.Equal(["pick"], Saved(s, "door")!.Spent);
        Assert.Equal(1, Saved(s, "door")!.Rest);
    }

    [Fact]
    public void A_search_that_missed_looks_again_after_a_long_rest()
    {
        var s = At(new Tile(4, 2));
        s.Send(0, "search");
        Assert.Equal(["cache"], Cursor(s)!.Thing!.Search);
        Finish(s);
        Assert.True(Saved(s, "cache")!.Searched);
        s.Send(0, "search");
        Assert.Empty(Cursor(s)!.Thing!.Search);         // missed: not looked for again
        Finish(s);
        Camp(s);
        s.Send(0, "search");
        Assert.Equal(["cache"], Cursor(s)!.Thing!.Search);
        Finish(s);
        Assert.Equal((true, 1), (Saved(s, "cache")!.Searched, Saved(s, "cache")!.Rest));
    }

    [Fact]
    public void A_failed_disarm_still_springs_the_trap_and_a_rest_never_resets_it()
    {
        var rules = Vault.Load([
            """{ "id": "fire", "kind": "campfire", "at": [3, 1] }""",
            """{ "id": "plate", "kind": "trap", "at": [4, 2], "trap": { "spot": 1, "disarm": 30 } }"""]);
        var s = Vault.At(new Tile(5, 2), rules, w => w with { RestReady = true });
        s.Send(0, "interact", new { target = "plate" });
        Choose(s, "disarm");
        Until(s, () => Cursor(s)?.Roll is not null, "the disarm's roll");
        Finish(s);
        Assert.Equal(InteractableSave.Sprung, Saved(s, "plate")!.Trap);
        Assert.Equal(["disarm"], Saved(s, "plate")!.Spent);
        var save = Saved(s, "plate")!;
        Assert.Equal(save, Interactables.Rested(save, 5));
    }

    // ---- Decision 8: the key ring ----------------------------------------------------------------------------------------------

    [Fact]
    public void A_key_picked_up_goes_on_the_key_ring_in_the_journal_and_the_portal_and_says_when_its_used()
    {
        var s = At(new Tile(4, 2));
        var walk = s.Screen(0)!.Data;
        Assert.False(walk.TryGetProperty("journal", out _));            // no quests here and no key yet: the walk screen is as it was
        s.Send(0, "interact", new { target = "brass" });
        Assert.Contains("brass_key", Doc(s).World!.Flags);
        foreach (var seat in new[] { 0, 1 })
        {
            var keys = s.Screen(seat)!.Data.GetProperty("journal").GetProperty("keys");
            Assert.Equal("""[{"id":"brass_key","name":"brass key","found":"The Vault","used":false}]""", keys.GetRawText());
        }
        Assert.Equal(JsonValueKind.True, s.Screen(0)!.Data.GetProperty("canOpenLog").ValueKind);
        var portal = new TacticsCampaignRules(Keep.Value).PortalView(s.Lease.Info, s.Lease.Document, s.Profiles[1]!.Value);
        Assert.Equal("brass key", portal.GetProperty("keys")[0].GetProperty("name").GetString());

        s.Send(0, "interact", new { target = "door" });
        Choose(s, "key");
        Finish(s);
        Assert.True(Saved(s, "door")!.Keyed);
        Assert.True(s.Screen(1)!.Data.GetProperty("journal").GetProperty("keys")[0].GetProperty("used").GetBoolean());
        s.Send(0, "questLog", new { action = "open" });
        Assert.True(s.Host.Explore!.QuestLog!.Keys!.Value.Single().Used);
        portal = new TacticsCampaignRules(Keep.Value).PortalView(s.Lease.Info, s.Lease.Document, s.Profiles[1]!.Value);
        Assert.True(portal.GetProperty("keys")[0].GetProperty("used").GetBoolean());
    }

    [Fact]
    public void A_portal_with_no_key_has_no_key_ring()
    {
        var s = At(new Tile(4, 2));
        var portal = new TacticsCampaignRules(Keep.Value).PortalView(s.Lease.Info, s.Lease.Document, s.Profiles[0]!.Value);
        Assert.False(portal.TryGetProperty("keys", out _));
    }
}
