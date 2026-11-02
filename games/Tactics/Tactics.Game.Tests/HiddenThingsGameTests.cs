using System.Text.Json;
using ArcBox.Sdk;
using Tactics.Logic;
using static Tactics.Game.Tests.StoryTests;
using static Tactics.Game.Tests.StoryVoteTests;
using static Tactics.Game.Tests.TacticsGameTests;

namespace Tactics.Game.Tests;

/// <summary>
/// Hidden things and passive checks on the server (interactables design §7): passive skills other than Perception, notices and what they
/// reveal, hidden paths in the way until found, the leader's Search with the party's pick of who rolls and its hidden DC, restarts, and
/// that nothing hidden reaches the TV or a phone.
/// </summary>
public sealed class HiddenThingsGameTests
{
    private static TacticsDocument Doc(Session s) => TacticsDocument.Read(s.Lease.Document);

    private static InteractableSave? Saved(Session s, string id) => Doc(s).World!.Things?.GetValueOrDefault(id);

    private static ThingView? Thing(Session s, string id) => s.Host.Explore!.Things!.Value.FirstOrDefault(t => t.Id == id);

    private static JsonElement Walk(Session s, int seat = 0) => s.Screen(seat)!.Data;

    private static void Step(Session s, string dir)
    {
        s.Send(0, "walk", new { dir });
        s.Send(0, "walk", new { dir = (string?)null });
        s.Harness.Advance(TimeSpan.FromSeconds(1));
    }

    private static void Finish(Session s) => Until(s, () => s.Game.Phase == TacticsPhase.Exploration, "walking again", TimeSpan.FromSeconds(90));

    private static Session Again(Session s, RulesPack rules)
    {
        var again = new Session(document: s.Lease.Document, game: new TacticsGame(rules));
        again.Harness.Resume(again.Lease);
        for (var p = 0; p < 4; p++)
            again.Harness.SetConnected(p, true);
        return again;
    }

    /// <summary>Whose passive <paramref name="skill"/> is the party's best, by name: the first in member order on a tie.</summary>
    private static string Best(Session s, RulesPack rules, string skill)
        => Doc(s).Members.Values.Select(m => m.Character)
            .Aggregate((a, b) => Interactables.Passive(SheetBuilder.ForCharacter(b, rules), skill) > Interactables.Passive(SheetBuilder.ForCharacter(a, rules), skill) ? b : a)
            .Name;

    /// <summary>Everything the TV and every phone are sent, as JSON, for "is it in there" checks.</summary>
    private static string Sent(Session s)
        => JsonSerializer.Serialize(s.Host, ArcJson.Options) + string.Concat(Enumerable.Range(0, 4).Select(p => JsonSerializer.Serialize(s.Harness.PlayerView(p), ArcJson.Options)));

    // ---- Passive skills and notices -------------------------------------------------------------------------------------------

    private static readonly Lazy<RulesPack> Clues = new(() => Vault.Load(
    [
        """{ "id": "rune", "kind": "rune", "at": [3, 2], "name": "ward-rune", "hidden": { "dc": 1, "skill": "arcana" }, "text": ["A ward-rune, still faintly warm."] }""",
        """{ "id": "tracks", "kind": "tracks", "at": [6, 1], "hidden": 1, "range": 1, "text": ["Fresh wolf tracks lead south,", "under the door."], "reveals": ["cache", "blood"], "sets": ["saw_tracks"] }""",
        """{ "id": "blood", "kind": "bloodstain", "at": [7, 5], "hidden": 30, "text": ["Old blood."] }""",
        """{ "id": "cache", "kind": "cache", "at": [8, 8], "hidden": 30, "loot": [{ "item": "smoke_bomb" }] }""",
        """{ "id": "deep", "kind": "carving", "at": [2, 2], "hidden": { "dc": 30, "skill": "history" }, "text": ["Old words."] }""",
        """{ "id": "door", "kind": "door", "at": [4, 3] }""",
        """{ "id": "gate", "kind": "gate", "at": [5, 6], "open": true }""",
    ]));

    [Fact]
    public void A_thing_hidden_by_arcana_is_noticed_by_the_partys_best_arcana_and_says_what_was_noticed()
    {
        var s = Vault.At(new Tile(2, 1), Clues.Value);
        Assert.True(Saved(s, "rune")!.Found);
        var e = s.Host.Explore!.ThingEvents!.Value.Single(e => e.Id == "rune");
        Assert.Equal(ThingEvents.Noticed, e.What);
        var who = Best(s, Clues.Value, "arcana");
        Assert.Equal(who, e.By);
        Assert.Equal($"{who} notices: A ward-rune, still faintly warm.", e.Line);
        Assert.Contains(e.Line!, s.Host.Notice);
        Assert.Equal(("rune", InteractableKinds.Noticed, false), (Thing(s, "rune")!.Kind, Thing(s, "rune")!.State, Thing(s, "rune")!.Blocks));
        // A DC 30 nobody's passive History meets: not found, and not sent anywhere.
        Assert.True(Saved(s, "deep") is null or { Found: false });
        Assert.DoesNotContain("\"deep\"", Sent(s));
    }

    [Fact]
    public void Noticing_tracks_finds_what_they_lead_to_at_once_wherever_it_is_and_sets_their_flags()
    {
        var s = Vault.At(new Tile(4, 1), Clues.Value);
        Assert.True(Saved(s, "tracks") is null or { Found: false });          // two tiles off, and its range is one
        Assert.DoesNotContain("\"cache\"", Sent(s));
        Assert.DoesNotContain("\"blood\"", Sent(s));
        Step(s, "e");                                                         // (5, 1): beside the tracks
        Assert.True(Saved(s, "tracks")!.Found);
        Assert.True(Saved(s, "cache")!.Found);                                // DC 30, two rooms away: the tracks led there
        Assert.True(Saved(s, "blood")!.Found);
        Assert.Contains("saw_tracks", Doc(s).World!.Flags);
        var events = s.Host.Explore!.ThingEvents!.Value;
        Assert.Equal([("tracks", ThingEvents.Noticed), ("cache", ThingEvents.Revealed), ("blood", ThingEvents.Noticed)],
            events.Where(e => e.Id is "tracks" or "cache" or "blood").Select(e => (e.Id, e.What)));
        Assert.StartsWith(events.Single(e => e.Id == "tracks").Line!, s.Host.Notice);           // all on the notice, the tracks' first
        Assert.Contains("Fresh wolf tracks lead south, under the door.", s.Host.Notice);
        Assert.NotNull(Thing(s, "cache"));

        // Saved at once: a restart keeps it all, and "Look" says the tracks again, changing nothing.
        var again = Again(s, Clues.Value);
        Assert.True(Saved(again, "cache")!.Found);
        again.Send(0, "interact", new { target = "tracks" });
        Assert.Equal(TacticsPhase.Exploration, again.Game.Phase);
        Assert.Equal("Fresh wolf tracks lead south, under the door.", again.Host.Notice);
        Assert.Equal(ThingEvents.Looked, again.Host.Explore!.ThingEvents!.Value[^1].What);
    }

    // ---- Hidden paths -----------------------------------------------------------------------------------------------------------

    private static readonly Lazy<RulesPack> Ford = new(() => Vault.Load(
    [
        """{ "id": "ford", "kind": "hidden_path", "at": [4, 3], "name": "goat track", "hidden": 30, "sets": ["found_ford"] }""",
        """{ "id": "gate", "kind": "gate", "at": [5, 6], "open": true }""",
    ]));

    [Fact]
    public void A_hidden_path_is_a_wall_without_a_bump_drawn_as_its_surroundings_until_found_and_then_ground_the_TV_draws()
    {
        var s = Vault.At(new Tile(4, 2), Ford.Value);
        var bumps = s.Host.Explore!.Bumps;
        Step(s, "sw");                                                              // south, along y: onto the path
        Assert.Equal(new Tile(4, 2), s.Host.Explore!.At);
        Assert.Equal(bumps, s.Host.Explore!.Bumps);                                 // no bump: that would give it away
        Assert.False(s.Host.Explore!.Blocked);
        Assert.DoesNotContain("\"ford\"", Sent(s));
        Assert.Equal([new WallPlug(new Tile(4, 3), "sw")], s.Host.Explore!.Plugs!.Value);    // drawn as the wall either side, as a secret door is

        var found = Vault.At(new Tile(4, 2), Ford.Value, w => w with { Things = Ordinal.Map(("ford", new InteractableSave(InteractableKinds.FoundPath, Found: true))) });
        Assert.Equal(("hidden_path", "found", false), (Thing(found, "ford")!.Kind, Thing(found, "ford")!.State, Thing(found, "ford")!.Blocks));
        Assert.Empty(found.Host.Explore!.Plugs!.Value);
        Step(found, "sw");
        Assert.Equal(new Tile(4, 3), found.Host.Explore!.At);
        Step(found, "sw");
        Assert.Equal(new Tile(4, 4), found.Host.Explore!.At);
        Assert.DoesNotContain(found.Screen(0)!.Data.GetProperty("targets").EnumerateArray(), t => t.GetProperty("id").GetString() == "ford");
    }

    // ---- Search -----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Two caches two tiles from (5, 1), too far for the party's passive look (range 1) and within a search's: one any roll finds, one none
    /// does. Searched for, the one is found and the other is searched for good.
    /// </summary>
    private static readonly Lazy<RulesPack> Stash = new(() => Vault.Load(
    [
        """{ "id": "easy", "kind": "cache", "at": [7, 2], "name": "loose flagstone", "hidden": { "dc": 1, "skill": "investigation" }, "range": 1, "loot": [{ "item": "smoke_bomb" }] }""",
        """{ "id": "hard", "kind": "cache", "at": [3, 2], "hidden": 30, "range": 1 }""",
        """{ "id": "door", "kind": "door", "at": [4, 3] }""",
        """{ "id": "gate", "kind": "gate", "at": [5, 6], "open": true }""",
    ]));

    private static Session Searching()
    {
        var s = Vault.At(new Tile(5, 1), Stash.Value);
        Assert.True(Saved(s, "easy") is null or { Found: false });
        Assert.True(Walk(s).GetProperty("search").GetBoolean());
        Assert.False(Walk(s, 1).GetProperty("search").GetBoolean());
        s.Send(0, "search");
        Assert.Equal(TacticsPhase.Story, s.Game.Phase);
        return s;
    }

    [Fact]
    public void A_search_is_the_partys_investigation_check_with_no_dc_shown_and_finds_what_the_roll_meets()
    {
        var s = Searching();
        Assert.Equal(("search", "Search"), (Story(s).Scene, Story(s).Title));
        Until(s, () => Story(s).State == TacticsGame.StoryStates.PickRoller, "the pick of who rolls");     // straight there: not asked twice
        Assert.Equal(("investigation", (int?)null, "Search the area"), (Story(s).Check!.Skill, Story(s).Check!.Dc, Story(s).Check!.Title));
        var phone = s.Screen(1)!;
        Assert.Equal(TacticsGame.Screens.Check, phone.Screen);
        Assert.Equal(JsonValueKind.Null, phone.Data.GetProperty("dc").ValueKind);
        Until(s, () => Cursor(s)?.Roll is not null, "the roll");
        Assert.Equal(TacticsGame.StoryStates.Rolled, Story(s).State);
        Assert.Equal(0, Story(s).Check!.Roll!.Vs);                                                 // not even in the roll the TV gets
        Assert.True(Story(s).Check!.Roll!.Success);
        Assert.Equal(JsonValueKind.Null, s.Screen(1)!.Data.GetProperty("roll").GetProperty("dc").ValueKind);
        var roller = Doc(s).Members[Cursor(s)!.Roll!.Member].Character.Name;
        Finish(s);

        Assert.True(Saved(s, "easy")!.Found);
        Assert.Equal((false, true), (Saved(s, "hard")!.Found, Saved(s, "hard")!.Searched));
        Assert.NotNull(Thing(s, "easy"));
        Assert.Null(Thing(s, "hard"));
        Assert.Equal($"{roller} finds a loose flagstone.", s.Host.Explore!.ThingEvents!.Value.Single(e => e.Id == "easy").Line);
        Assert.DoesNotContain("\"hard\"", Sent(s));
        Assert.Null(Cursor(s));
    }

    [Fact]
    public void A_thing_a_search_missed_is_never_searched_for_again_and_a_search_with_nothing_plays_the_same()
    {
        var s = Searching();
        Finish(s);
        var hard = Saved(s, "hard");
        s.Send(0, "search");
        Assert.Empty(Cursor(s)!.Thing!.Search);                       // nothing left to look for
        Until(s, () => Story(s).State == TacticsGame.StoryStates.PickRoller, "the pick of who rolls");
        Assert.Null(Story(s).Check!.Dc);
        Until(s, () => Cursor(s)?.Roll is not null, "the roll");
        Assert.False(Cursor(s)!.Roll!.Passed);
        Finish(s);
        Assert.Equal(hard, Saved(s, "hard"));
    }

    [Fact]
    public void A_restart_during_a_search_keeps_its_roll_and_what_it_looked_for()
    {
        var s = Searching();
        Assert.Equal(["easy", "hard"], Cursor(s)!.Thing!.Search);
        Until(s, () => Cursor(s)?.Roll is not null, "the roll");
        var roll = Cursor(s)!.Roll!;

        var again = Again(s, Stash.Value);
        Assert.Equal(TacticsGame.StoryStates.Rolled, Story(again).State);
        Assert.Equal(roll, Cursor(again)!.Roll);
        Assert.Null(Story(again).Check!.Dc);
        Finish(again);
        Assert.True(Saved(again, "easy")!.Found);
        Assert.True(Saved(again, "hard")!.Searched);

        // Restarted on the pick of who rolls, before anyone rolled: the pick again, and the same things looked for.
        var picking = Searching();
        Until(picking, () => Story(picking).State == TacticsGame.StoryStates.PickRoller, "the pick of who rolls");
        var back = Again(picking, Stash.Value);
        Until(back, () => Story(back).State == TacticsGame.StoryStates.PickRoller, "the pick again");
        Assert.Equal(["easy", "hard"], Cursor(back)!.Thing!.Search);
    }

    [Fact]
    public void Only_the_leader_searches_and_only_while_walking_in_a_region_with_things()
    {
        var s = Vault.At(new Tile(5, 1), Stash.Value);
        s.Send(1, "search");
        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
        Assert.Null(Cursor(s));
    }
}
