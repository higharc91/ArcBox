using System.Text.Json;
using ArcBox.Sdk;
using Tactics.Logic;
using static Tactics.Game.Tests.StoryTests;
using static Tactics.Game.Tests.StoryVoteTests;
using static Tactics.Game.Tests.TacticsGameTests;

namespace Tactics.Game.Tests;

/// <summary>
/// Things to interact with on the server (interactables design §2): reach and the leader's list, things done at once, scenes with checks
/// rolled by the story's own flow, what walking finds and springs, the doors in the way, what the TV is told, and restarts.
/// </summary>
public sealed class InteractTests
{
    private static string Leader(Session s) => s.Profiles[0]!.Value.Value;

    private static TacticsDocument Doc(Session s) => TacticsDocument.Read(s.Lease.Document);

    private static InteractableSave? Saved(Session s, string id) => Doc(s).World!.Things?.GetValueOrDefault(id);

    private static int Count(Session s, string member, string item)
        => Doc(s).Members[member].Character.Bag.Where(b => b.Item.Value == item).Sum(b => b.Count);

    private static ThingView? Thing(Session s, string id) => s.Host.Explore!.Things!.Value.FirstOrDefault(t => t.Id == id);

    private static void Interact(Session s, string? target = null, int seat = 0)
        => s.Send(seat, "interact", target is null ? null : new { target });

    private static JsonElement Walk(Session s, int seat = 0) => s.Screen(seat)!.Data;

    private static string[] Targets(Session s) => [.. Walk(s).GetProperty("targets").EnumerateArray().Select(t => t.GetProperty("id").GetString()!)];

    private static void Step(Session s, string dir)
    {
        var seq = s.Host.Explore!.Seq;
        s.Send(0, "walk", new { dir });
        s.Send(0, "walk", new { dir = (string?)null });
        s.Harness.Advance(TimeSpan.FromSeconds(1));
        Assert.True(s.Host.Explore!.Seq > seq, $"no step {dir}");
    }

    /// <summary>The leader picks <paramref name="option"/> of the interaction's question, and it closes.</summary>
    private static void Choose(Session s, string option)
    {
        Until(s, () => Story(s).State == TacticsGame.StoryStates.Asking, "the question");
        Vote(s, 0, option);
        Until(s, () => Story(s).State != TacticsGame.StoryStates.Asking, "the choice");
    }

    private static Session Again(Session s)
    {
        var again = new Session(document: s.Lease.Document, game: new TacticsGame(Vault.Rules.Value));
        again.Harness.Resume(again.Lease);
        for (var p = 0; p < 4; p++)
            again.Harness.SetConnected(p, true);
        return again;
    }

    private static void Finish(Session s) => Until(s, () => s.Game.Phase == TacticsPhase.Exploration, "walking again", TimeSpan.FromSeconds(60));

    [Fact]
    public void The_leader_sees_what_is_in_reach_and_nobody_else_does()
    {
        var s = Vault.At(new Tile(6, 1));
        Assert.Equal(["chest"], Targets(s));
        Assert.Equal("Open the chest", Walk(s).GetProperty("interact").GetString());
        Assert.Empty(Walk(s, 1).GetProperty("targets").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, Walk(s, 1).GetProperty("interact").ValueKind);
        s = Vault.At(new Tile(7, 2));
        Assert.Equal(["key", "chest"], Targets(s));           // both a step away: the one the leader faces first
    }

    [Fact]
    public void An_unlocked_chest_opens_at_once_and_its_loot_is_the_leaders_once()
    {
        var s = Vault.At(new Tile(6, 1));
        var before = Count(s, Leader(s), "potion_of_healing");

        Interact(s);

        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
        Assert.Equal("open", Saved(s, "chest")!.State);
        Assert.Equal(before + 2, Count(s, Leader(s), "potion_of_healing"));
        Assert.Contains("chest_looted", Doc(s).World!.Flags);
        Assert.Equal(("open", "props.chest_open"), (Thing(s, "chest")!.State, Thing(s, "chest")!.StandIn));
        var e = s.Host.Explore!.ThingEvents!.Value[^1];
        Assert.Equal(("chest", ThingEvents.Opened), (e.Id, e.What));
        Assert.Equal(Doc(s).Members[Leader(s)].Character.Name, e.By);
        Assert.Equal("Inside: 2 × Potion of Healing.", s.Host.Notice);
        Assert.Empty(Targets(s));

        Interact(s);
        Interact(s, "chest");
        Assert.Equal(before + 2, Count(s, Leader(s), "potion_of_healing"));
    }

    [Fact]
    public void Only_the_leader_can_interact()
    {
        var s = Vault.At(new Tile(6, 1));
        Interact(s, seat: 1);
        Interact(s, "chest", seat: 1);
        Assert.Null(Saved(s, "chest"));
    }

    [Fact]
    public void A_thing_out_of_reach_cant_be_used_whatever_the_phone_says()
    {
        var s = Vault.At(new Tile(4, 1));
        Interact(s, "chest");
        Assert.Null(Saved(s, "chest"));
    }

    [Fact]
    public void An_ambient_thing_says_its_line_and_changes_nothing()
    {
        var s = Vault.At(new Tile(1, 1));
        Interact(s, "cart");
        Assert.Equal("A cart with a broken wheel.", s.Host.Notice);
        Assert.Null(Saved(s, "cart"));
        Assert.Equal(ThingEvents.Looked, s.Host.Explore!.ThingEvents!.Value[^1].What);
    }

    [Fact]
    public void The_closed_door_is_in_the_way_until_it_opens()
    {
        var s = Vault.At(new Tile(4, 2));
        s.Send(0, "walk", new { dir = "sw" });      // (4, 3), the door
        s.Harness.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(new Tile(4, 2), s.Host.Explore!.At);
        Assert.True(Thing(s, "door")!.Blocks);
    }

    [Fact]
    public void A_locked_door_opens_with_the_key_the_party_picked_up()
    {
        var s = Vault.At(new Tile(7, 2));
        Interact(s, "key");
        Assert.Contains("iron_key", Doc(s).World!.Flags);
        Assert.Equal("You take the iron key.", s.Host.Notice);

        s = Vault.At(new Tile(4, 2), world: w => w with { Flags = ["iron_key"] });
        Interact(s, "door");
        Assert.Equal(TacticsPhase.Story, s.Game.Phase);
        Assert.Equal("Door", Story(s).Title);
        Until(s, () => Story(s).State == TacticsGame.StoryStates.Asking, "the question");
        Assert.Equal(["key", "pick", "force", "leave"], Ask(s).Options.Select(o => o.Id));
        Assert.False(Ask(s).Party);
        Assert.Equal(TacticsGame.Screens.Vote, s.Screen(0)!.Screen);
        Choose(s, "key");
        Finish(s);

        Assert.Equal(new InteractableSave("open", Unlocked: true) with { Spent = Saved(s, "door")!.Spent }, Saved(s, "door"));
        Assert.Null(Doc(s).World!.Story);
        Assert.False(Thing(s, "door")!.Blocks);
        Step(s, "sw");
        Assert.Equal(new Tile(4, 3), s.Host.Explore!.At);
    }

    [Fact]
    public void Picking_the_lock_goes_to_the_party_to_pick_who_rolls_and_the_roll_decides()
    {
        var passed = 0;
        for (var seed = 1; seed <= 12; seed++)
        {
            var s = Vault.At(new Tile(4, 2), seed: seed);
            Interact(s, "door");
            Choose(s, "pick");
            Assert.Equal(TacticsGame.StoryStates.PickRoller, Story(s).State);
            Assert.Equal(("sleight_of_hand", 12), (Story(s).Check!.Skill, Story(s).Check!.Dc));
            Assert.Equal(TacticsGame.Screens.Check, s.Screen(1)!.Screen);
            Until(s, () => Cursor(s)?.Roll is not null, "the roll");
            var roll = Cursor(s)!.Roll!;
            Assert.Equal(("pick", "sleight_of_hand", 12), (roll.Option, roll.Skill, roll.Dc));
            Finish(s);
            var door = Saved(s, "door")!;
            if (roll.Passed)
            {
                passed++;
                Assert.Equal(("open", true), (door.State, door.Unlocked));
                Assert.DoesNotContain("pick", door.Spent);
            }
            else
            {
                Assert.Equal(("closed", false), (door.State, door.Unlocked));
                Assert.Equal(["pick"], door.Spent);
                // The pick is spent: the next try offers what's left.
                Interact(s, "door");
                Until(s, () => Story(s).State == TacticsGame.StoryStates.Asking, "the question");
                Assert.Equal(["force", "leave"], Ask(s).Options.Select(o => o.Id));
            }
        }
        Assert.InRange(passed, 1, 11);
    }

    [Fact]
    public void A_restart_during_the_reveal_never_rolls_again_and_the_door_opens_once()
    {
        var s = Vault.At(new Tile(4, 2));
        Interact(s, "door");
        Choose(s, "force");
        Until(s, () => Cursor(s)?.Roll is not null, "the roll");
        var roll = Cursor(s)!.Roll!;

        var again = Again(s);
        Assert.Equal(TacticsGame.StoryStates.Rolled, Story(again).State);
        Assert.Equal(roll, Cursor(again)!.Roll);
        Finish(again);
        Assert.Equal(roll.Passed ? "open" : "closed", Saved(again, "door")!.State);
        Assert.Equal(roll.Passed ? [] : ["force"], Saved(again, "door")!.Spent);
    }

    [Fact]
    public void Leaving_a_lock_changes_nothing_and_a_question_nobody_answers_leaves_it()
    {
        var s = Vault.At(new Tile(4, 2));
        Interact(s, "door");
        Until(s, () => Story(s).State == TacticsGame.StoryStates.Asking, "the question");
        Finish(s);                  // nobody chooses: the choice takes the first way with no roll, which is to leave
        Assert.Equal("closed", Saved(s, "door")?.State ?? "closed");
        Assert.Empty(Saved(s, "door")?.Spent ?? []);
    }

    [Fact]
    public void Restarted_mid_line_the_interaction_goes_on_from_its_line()
    {
        var s = Vault.At(new Tile(3, 2));
        Interact(s, "sign");
        Assert.Equal(TacticsPhase.Story, s.Game.Phase);
        Assert.Equal("The vault.", Story(s).Line!.Text);
        Assert.Equal(TacticsGame.Screens.Dialogue, s.Screen(1)!.Screen);
        Assert.Equal("The vault.", Data(s, 1).GetProperty("text").GetString());
        LineEnds(s);
        Assert.Equal("Keep out.", Story(s).Line!.Text);

        var again = Again(s);
        Assert.Equal("Keep out.", Story(again).Line!.Text);
        Finish(again);
        Assert.Equal("read", Saved(again, "sign")!.State);
        Assert.Contains("read_sign", Doc(again).World!.Flags);
    }

    [Fact]
    public void The_lever_works_the_gate_which_wont_budge_by_hand()
    {
        var s = Vault.At(new Tile(5, 5));
        Interact(s, "gate");
        Assert.Contains("won't budge", s.Host.Notice);
        Assert.Null(Saved(s, "gate"));

        s = Vault.At(new Tile(2, 4));
        Interact(s, "lever");
        Assert.Equal(("on", "open"), (Saved(s, "lever")!.State, Saved(s, "gate")!.State));
        Assert.Contains("gate_opened", Doc(s).World!.Flags);
        Assert.Equal(["lever", "gate"], s.Host.Explore!.ThingEvents!.Value.TakeLast(2).Select(e => e.Id));
        Interact(s, "lever");
        Assert.Equal(("off", "closed"), (Saved(s, "lever")!.State, Saved(s, "gate")!.State));
    }

    [Fact]
    public void The_lever_wont_shut_the_gate_on_the_leader()
    {
        string[] things = [.. Vault.Things.Where(t => !t.Contains("\"lever\", \"kind\"") && !t.Contains("\"gate\", \"kind\"")),
            """{ "id": "lever", "kind": "lever", "at": [4, 5], "targets": ["gate"], "open": true }""",
            """{ "id": "gate", "kind": "gate", "at": [5, 6], "open": true }"""];
        var s = Vault.At(new Tile(5, 6), Vault.Load(things));      // in the gateway, beside the lever
        Interact(s, "lever");
        Assert.Null(Saved(s, "lever"));
        Assert.Equal("Someone's standing in the way.", s.Host.Notice);
        Step(s, "ne");      // out of it, to (5, 5)
        Interact(s, "lever");
        Assert.Equal(("off", "closed"), (Saved(s, "lever")!.State, Saved(s, "gate")!.State));
    }

    [Fact]
    public void Walking_near_a_hidden_cache_finds_it_and_only_then_is_it_sent()
    {
        var s = Vault.At(new Tile(3, 8), world: w => w with { Things = Ordinal.Map(("gate", new InteractableSave("open")), ("lever", new InteractableSave("on"))) });
        Assert.Null(Thing(s, "cache"));
        Step(s, "se");      // (4, 8)
        Step(s, "se");      // (5, 8): the cache is 3 along
        Assert.True(Saved(s, "cache")!.Found);
        Assert.NotNull(Thing(s, "cache"));
        var e = s.Host.Explore!.ThingEvents!.Value.Last(e => e.Id == "cache");
        Assert.Equal(ThingEvents.Revealed, e.What);
        Assert.EndsWith("spots a hidden cache.", e.Line);
        Assert.Equal(e.Line, s.Host.Notice);
    }

    [Fact]
    public void A_pit_nobody_saw_goes_off_underfoot_harmlessly_and_once()
    {
        var s = Vault.At(new Tile(3, 8), world: w => w with { Things = Ordinal.Map(("gate", new InteractableSave("open")), ("lever", new InteractableSave("on"))) });
        var injuries = Doc(s).Members[Leader(s)].Character.Injuries;
        Assert.Null(Thing(s, "pit"));
        Step(s, "nw");      // (2, 8)
        Assert.Equal(new Tile(2, 8), s.Host.Explore!.At);
        Assert.Equal(InteractableSave.Sprung, Saved(s, "pit")!.Trap);
        Assert.Contains("fell_in", Doc(s).World!.Flags);
        Assert.Equal("The floor gives way, but only a foot.", s.Host.Notice);
        Assert.Equal(injuries, Doc(s).Members[Leader(s)].Character.Injuries);       // harmless: the user's decision 3
        Assert.Equal("sprung", Thing(s, "pit")!.State);
        Step(s, "se");
        Step(s, "nw");
        Assert.Single(s.Host.Explore!.ThingEvents!.Value, e => e.Id == "pit" && e.What == ThingEvents.Sprung);
    }

    [Fact]
    public void A_trap_seen_from_afar_stands_in_the_way_until_its_disarmed()
    {
        var s = Vault.At(new Tile(4, 2), world: w => w with { Flags = ["iron_key"], Things = Ordinal.Map(("door", new InteractableSave("open", Unlocked: true))) });
        Assert.True(Saved(s, "plate")!.TrapSpotted);         // 2 tiles off, with a DC of 1: seen as the party comes into the world
        Step(s, "sw");      // the doorway, (4, 3)
        Assert.True(Thing(s, "plate")!.Blocks);
        s.Send(0, "walk", new { dir = "sw" });
        s.Harness.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(new Tile(4, 3), s.Host.Explore!.At);            // it won't step onto a trap it has seen
        Assert.Contains("plate", Targets(s));
        Interact(s, "plate");
        Choose(s, "disarm");
        Until(s, () => Cursor(s)?.Roll is not null, "the roll");
        Finish(s);
        Assert.NotNull(Saved(s, "plate")!.Trap);
        Assert.False(Thing(s, "plate")!.Blocks);                     // disarmed or sprung, it's out of the way
    }

    [Fact]
    public void A_trapped_crate_shows_its_trap_and_offers_to_disarm_it_first()
    {
        var s = Vault.At(new Tile(7, 4), world: w => w with { Things = Ordinal.Map(("door", new InteractableSave("open", Unlocked: true))) });
        Assert.True(Saved(s, "crate")!.TrapSpotted);
        Assert.True(Thing(s, "crate")!.Trapped);
        Interact(s, "crate");
        Until(s, () => Story(s).State == TacticsGame.StoryStates.Asking, "the question");
        Assert.Equal(["disarm", "leave", "anyway"], Ask(s).Options.Select(o => o.Id));
        Choose(s, "anyway");
        Until(s, () => Story(s).State == TacticsGame.StoryStates.Asking, "the lock");
        Assert.Equal(["pick", "leave"], Ask(s).Options.Select(o => o.Id));
        Assert.Equal(InteractableSave.Sprung, Saved(s, "crate")!.Trap);
        Assert.Contains("crate_trap_went_off", Doc(s).World!.Flags);
    }

    [Fact]
    public void A_shrine_is_used_once()
    {
        var s = Vault.At(new Tile(5, 7), world: w => w with { Things = Ordinal.Map(("gate", new InteractableSave("open")), ("lever", new InteractableSave("on"))) });
        Interact(s, "shrine");
        Finish(s);
        Assert.Equal("used", Saved(s, "shrine")!.State);
        Assert.Contains("blessed", Doc(s).World!.Flags);
        Assert.DoesNotContain("shrine", Targets(s));
    }

    [Fact]
    public void The_things_save_reads_back()
    {
        var s = Vault.At(new Tile(6, 1));
        Interact(s);
        var doc = Doc(s);
        Assert.Equal(doc.ToJson().GetRawText(), TacticsDocument.Read(doc.ToJson()).ToJson().GetRawText());
    }

    [Fact]
    public void Ashen_ford_has_no_things_and_its_views_say_so()
    {
        var s = new Session();
        s.ToWalking();
        Assert.Null(s.Host.Explore!.Things);
        Assert.Null(s.Host.Explore!.ThingEvents);
        Assert.Empty(Walk(s).GetProperty("targets").EnumerateArray());
        Assert.Null(Doc(s).World!.Things);
        Assert.DoesNotContain("things", Doc(s).ToJson().GetProperty("world").EnumerateObject().Select(p => p.Name));      // not written
    }
}
