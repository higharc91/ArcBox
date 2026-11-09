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

    private static Session Again(Session s, RulesPack? rules = null)
    {
        var again = new Session(document: s.Lease.Document, game: new TacticsGame(rules ?? Vault.Rules.Value));
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
    public void An_unlocked_chest_opens_at_once_and_its_loot_is_the_openers_once_nobody_votes_otherwise()
    {
        var s = Vault.At(new Tile(6, 1));
        var before = Count(s, Leader(s), "potion_of_healing");

        Interact(s);

        // It opens at once; who takes what was inside is the party's vote (the owner's decision 1), and silence leaves it with the opener.
        Assert.Equal("open", Saved(s, "chest")!.State);
        Assert.Equal(TacticsPhase.Story, s.Game.Phase);
        Assert.Equal("Inside: 2 × Potion of Healing.", s.Host.Notice);
        Until(s, () => s.Game.Phase == TacticsPhase.Exploration, "the loot vote's close", TimeSpan.FromSeconds(30));
        Assert.Equal(before + 2, Count(s, Leader(s), "potion_of_healing"));
        Assert.Contains("chest_looted", Doc(s).World!.Flags);
        Assert.Equal(("open", "props.chest_open"), (Thing(s, "chest")!.State, Thing(s, "chest")!.StandIn));
        var e = s.Host.Explore!.ThingEvents!.Value[^1];
        Assert.Equal(("chest", ThingEvents.Opened), (e.Id, e.What));
        Assert.Equal(Doc(s).Members[Leader(s)].Character.Name, e.By);
        Assert.Equal($"{e.By} takes the 2 × Potion of Healing.", s.Host.Notice);
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

        Assert.Equal(new InteractableSave("open", Unlocked: true, Keyed: true) with { Spent = Saved(s, "door")!.Spent }, Saved(s, "door"));
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
            """{ "id": "lever", "kind": "lever", "at": [5, 5], "targets": ["gate"], "open": true }""",
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
    public void A_pit_nobody_saw_goes_off_underfoot_once_and_gives_the_leader_one_minor_injury()
    {
        var s = Vault.At(new Tile(3, 8), world: w => w with { Things = Ordinal.Map(("gate", new InteractableSave("open")), ("lever", new InteractableSave("on"))) });
        s.Game.TrapRoll = total => total;       // the last of the rest-kind table: twisted knee
        var injuries = Doc(s).Members[Leader(s)].Character.Injuries;
        Assert.Null(Thing(s, "pit"));
        Step(s, "nw");      // (2, 8)
        Assert.Equal(new Tile(2, 8), s.Host.Explore!.At);
        Assert.Equal(InteractableSave.Sprung, Saved(s, "pit")!.Trap);
        Assert.Contains("fell_in", Doc(s).World!.Flags);
        var name = Doc(s).Members[Leader(s)].Character.Name;
        Assert.Equal($"The floor gives way, but only a foot. {name} takes a minor injury: Twisted knee.", s.Host.Notice);
        Assert.Equal([.. injuries, new InjuryId("twisted_knee")], Doc(s).Members[Leader(s)].Character.Injuries);   // the owner's decision 3
        Assert.Equal("rest", TacticsGameTests.Rules.Injuries[new InjuryId("twisted_knee")].Mends);
        Assert.Equal("sprung", Thing(s, "pit")!.State);
        Step(s, "se");
        Step(s, "nw");
        Assert.Single(s.Host.Explore!.ThingEvents!.Value, e => e.Id == "pit" && e.What == ThingEvents.Sprung);
        Assert.Single(Doc(s).Members[Leader(s)].Character.Injuries, i => i.Value == "twisted_knee");     // once, however often it's crossed
        Assert.Single(Again(s).Game.Document.Members[Leader(s)].Character.Injuries, i => i.Value == "twisted_knee");  // and a restart doesn't hurt again
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
    public void Ashen_fords_visible_things_are_sent_but_hidden_secrets_are_not()
    {
        var s = new Session();
        s.ToWalking();
        var placed = Rules.InteractablesIn("ashen_ford");
        Assert.Equal(30, placed.Length);
        Assert.Equal(placed.Where(t => t.Hidden is null && Interactables.Present(t, s.Game.Document.World!.Flags.ToHashSet())).Select(t => t.Id), s.Host.Explore!.Things!.Value.Select(t => t.Id));    // the cache isn't found yet
        Assert.Empty(s.Host.Explore!.ThingEvents!.Value);                   // nothing has happened to one yet
        Assert.True(Walk(s).TryGetProperty("targets", out _));            // a region with things gives the leader's walk screen its list
        Assert.Null(Doc(s).World!.Things);
        Assert.DoesNotContain("things", Doc(s).ToJson().GetProperty("world").EnumerateObject().Select(p => p.Name));      // not written
    }

    /// <summary>
    /// Review: lose the ford fight, walk off and open a locked sack, come back to the stamp. The beat must still be waiting at its fight, so
    /// the step into the stamp starts the fight with no lead-in to replay (and no vote to choose the other branch's flags again).
    /// </summary>
    [Fact]
    public void A_battle_beat_waiting_at_its_fight_waits_on_through_an_interaction()
    {
        const string assault = "ch1_ford_assault";
        var (at, dir) = StageBattleTests.Entry();
        var ground = Rules.GroundFor(StageBattleTests.Ford)!.Terrain;
        var region = StageBattleTests.Region;
        var spots = region.Beats.Select(b => b.At).Append(region.Spawns.Start).Append(region.Spawns.Recovery).ToHashSet();
        var sack = new[] { (-1, 0), (0, -1), (0, 1), (-1, -1), (-1, 1) }.Select(d => new Tile(at.X + d.Item1, at.Y + d.Item2))
            .First(t => ground.Walkable(t) && !StageBattleTests.Stage.Holds(t) && !spots.Contains(t)
                && Interactables.InReach(ground, at, Vault0(t)));
        var rules = Vault.InAshenFord($$"""{ "id": "sack", "kind": "sack", "at": [{{sack.X}}, {{sack.Y}}], "lock": { "pick": 12 } }""");
        var fight = new StorySave(assault, StorySave.Fight, null, null, 0, 7);
        var doc = TacticsDocument.Read(ExplorationTests.DocumentAt(null)) with
        {
            World = new WorldSave(StageBattleTests.Ford, at, new Tile(at.X + 1, at.Y), null, StageBattleTests.Force.Flags, StageBattleTests.Force.Beats, fight),
        };
        var s = new Session(document: doc.ToJson(), game: new TacticsGame(rules, lore: StageBattleTests.Lore.Value));
        s.Harness.Resume(s.Lease);
        for (var p = 0; p < 4; p++)
            s.Harness.SetConnected(p, true);
        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
        var lead = StageBattleTests.Lead(s);

        s.Send(lead, "interact", new { target = "sack" });
        Assert.Equal(TacticsPhase.Story, s.Game.Phase);
        Assert.Equal((assault, StorySave.Fight), (Cursor(s)!.Thing!.Under!.Beat, Cursor(s)!.Thing!.Under!.Stage));
        Until(s, () => Story(s).State == TacticsGame.StoryStates.Asking, "the lock");
        s.Send(lead, "vote", new { option = "leave" });
        Finish(s);

        Assert.Equal((assault, StorySave.Fight, null, 7), (Cursor(s)!.Beat, Cursor(s)!.Stage, Cursor(s)!.Thing, Cursor(s)!.Seq));   // still waiting
        s.Send(lead, "walk", new { dir = StageBattleTests.Name(dir) });
        Assert.Equal(TacticsPhase.Battle, s.Game.Phase);                 // straight into it: no lead-in
    }

    private static InteractableDef Vault0(Tile at) => new("x", "sack", at, Text: [], Requires: [], Unless: [], Sets: [], Loot: [], Targets: []);

    [Fact]
    public void A_door_nobody_has_found_isnt_bumped_into_on_the_tv()
    {
        string[] things = ["""{ "id": "seam", "kind": "secret_door", "at": [4, 3], "hidden": 30 }"""];
        var s = Vault.At(new Tile(4, 2), Vault.Load(things));
        var bumps = s.Host.Explore!.Bumps;
        s.Send(0, "walk", new { dir = "sw" });
        Assert.Equal(new Tile(4, 2), s.Host.Explore!.At);
        Assert.Equal(bumps, s.Host.Explore!.Bumps);
        Assert.False(s.Host.Explore!.Blocked);
        Assert.False(s.Screen(0)!.Data.TryGetProperty("bumps", out _));    // and the leader's phone doesn't buzz: that would give it away too
        Assert.Null(s.Host.Explore!.Things!.Value.FirstOrDefault(t => t.Id == "seam"));
        s.Send(0, "walk", new { dir = (string?)null });

        var found = Vault.At(new Tile(4, 2), Vault.Load(things), w => w with { Things = Ordinal.Map(("seam", new InteractableSave("closed", Found: true))) });
        bumps = found.Host.Explore!.Bumps;
        found.Send(0, "walk", new { dir = "sw" });
        Assert.Equal(bumps + 1, found.Host.Explore!.Bumps);             // found, it's a door like any other
        Assert.True(found.Host.Explore!.Blocked);
        Assert.Equal(bumps + 1, found.Screen(0)!.Data.GetProperty("bumps").GetInt32());     // and the leader's phone feels it
    }

    [Fact]
    public void A_passage_thats_not_there_moves_unseen()
    {
        string[] things =
        [
            """{ "id": "lever", "kind": "lever", "at": [2, 2], "targets": ["gate"] }""",
            """{ "id": "gate", "kind": "gate", "at": [4, 3], "requires": ["gate_built"] }""",
        ];
        var s = Vault.At(new Tile(2, 1), Vault.Load(things));
        Interact(s, "lever");
        Assert.Equal("open", Saved(s, "gate")!.State);
        Assert.Equal(["lever"], s.Host.Explore!.ThingEvents!.Value.Select(e => e.Id));
        Assert.DoesNotContain(s.Host.Explore!.Things!.Value, t => t.Id == "gate");
    }

    [Fact]
    public void A_thing_whose_flag_is_set_under_the_leader_waits_for_them_to_step_off()
    {
        var rules = Vault.Load([.. Vault.Things, """{ "id": "reliquary", "kind": "chest", "at": [5, 8], "requires": ["blessed"] }"""]);
        var s = Vault.At(new Tile(5, 8), rules,
            w => w with { Things = Ordinal.Map(("gate", new InteractableSave("open")), ("lever", new InteractableSave("on"))) });
        Interact(s, "shrine");
        Finish(s);
        Assert.Contains("blessed", Doc(s).World!.Flags);
        Assert.Equal(new Tile(5, 8), s.Host.Explore!.At);
        Assert.Null(Thing(s, "reliquary"));                              // not on the leader: it waits
        Assert.DoesNotContain("reliquary", Targets(s));

        Step(s, "nw");                                                   // off it, to (4, 8)
        Assert.True(Thing(s, "reliquary")!.Blocks);
        s.Send(0, "walk", new { dir = "se" });
        s.Harness.Advance(TimeSpan.FromSeconds(1));
        Assert.NotEqual(new Tile(5, 8), s.Host.Explore!.At);           // and from then on it stands there

        s.Harness.Advance(TimeSpan.FromSeconds(5));                      // the walk is saved
        var again = Again(s, rules);                                     // a restart doesn't put it off again
        Assert.NotNull(Thing(again, "reliquary"));
    }

    [Fact]
    public void A_pickup_taken_and_a_cache_emptied_arent_drawn()
    {
        var s = Vault.At(new Tile(7, 2));
        Interact(s, "key");
        Assert.Null(Thing(s, "key"));
        s = Vault.At(new Tile(7, 8), world: w => w with
        {
            Things = Ordinal.Map(("gate", new InteractableSave("open")), ("lever", new InteractableSave("on")), ("cache", new InteractableSave("closed", Found: true))),
        });
        Assert.Equal("pickups.loot_sack", Thing(s, "cache")!.StandIn);
        Interact(s, "cache");
        Assert.Equal("open", Saved(s, "cache")!.State);
        Assert.Null(Thing(s, "cache"));
    }

    [Fact]
    public void An_unfound_secret_door_is_a_plug_of_wall_on_the_tv_that_says_nothing_else_and_goes_when_its_found()
    {
        string[] things = ["""{ "id": "seam", "kind": "secret_door", "at": [4, 3], "facing": "sw", "name": "seam in the stone", "hidden": 30 }"""];
        var s = Vault.At(new Tile(4, 1), Vault.Load(things));

        var plug = Assert.Single(s.Host.Explore!.Plugs!.Value);
        Assert.Equal(new WallPlug(new Tile(4, 3), "sw"), plug);
        var json = JsonSerializer.Serialize(s.Host.Explore, ArcJson.Options);
        Assert.Contains("\"plugs\":[{\"at\":", json);
        foreach (var leak in new[] { "seam", "secret_door", "hidden" })
            Assert.DoesNotContain(leak, json, StringComparison.OrdinalIgnoreCase);
        foreach (var seat in new[] { 0, 1 })
            Assert.DoesNotContain("seam", Walk(s, seat).GetRawText(), StringComparison.Ordinal);

        var found = Vault.At(new Tile(4, 1), Vault.Load(things), w => w with { Things = Ordinal.Map(("seam", new InteractableSave("closed", Found: true))) });
        Assert.Empty(found.Host.Explore!.Plugs!.Value);
        Assert.NotNull(Thing(found, "seam"));
        var open = Vault.At(new Tile(4, 1), Vault.Load(things), w => w with { Things = Ordinal.Map(("seam", new InteractableSave("open", Found: true))) });
        Assert.Empty(open.Host.Explore!.Plugs!.Value);
    }

    [Fact]
    public void Only_an_unfound_shut_passage_is_plugged()
    {
        string[] things =
        [
            """{ "id": "lever", "kind": "lever", "at": [2, 2], "targets": ["gate"] }""",
            """{ "id": "gate", "kind": "gate", "at": [4, 3] }""",
            """{ "id": "cache", "kind": "cache", "at": [8, 8], "hidden": 30 }""",
        ];
        Assert.Empty(Vault.At(new Tile(2, 1), Vault.Load(things)).Host.Explore!.Plugs!.Value);
        Assert.Empty(Vault.At(new Tile(2, 1)).Host.Explore!.Plugs!.Value);
    }

    [Fact]
    public void Thing_event_numbers_go_on_across_a_server_restart()
    {
        var s = Vault.At(new Tile(2, 2));
        Interact(s, "cart");                                           // a look at the cart: an event, at once
        var before = s.Host.Explore!.ThingEvents!.Value.Max(e => e.Seq);
        Assert.Equal(before, Doc(s).World!.ThingSeq);

        var again = Again(s);
        Assert.Empty(again.Host.Explore!.ThingEvents!.Value);            // not saved: a TV that stayed up has played them
        Interact(again, "cart");
        Assert.Equal(before + 1, Assert.Single(again.Host.Explore!.ThingEvents!.Value).Seq);
    }

    [Fact]
    public void The_walk_screen_says_when_the_button_is_a_beats()
    {
        var s = Vault.At(new Tile(6, 1));
        Assert.False(Walk(s).GetProperty("beat").GetBoolean());
        Assert.False(Walk(s, 1).GetProperty("beat").GetBoolean());
    }

    private static readonly string[] HiddenCrate =
    [
        """{ "id": "crate", "kind": "crate", "at": [3, 1], "hidden": 30, "loot": [{ "item": "smoke_bomb" }] }""",
        """{ "id": "watcher", "kind": "crate", "at": [7, 2], "hidden": 1 }""",
    ];

    [Fact]
    public void A_hidden_crate_nobody_has_found_is_walked_through_without_a_bump()
    {
        var s = Vault.At(new Tile(2, 1), Vault.Load(HiddenCrate));
        Assert.True(Saved(s, "crate") is null or { Found: false });
        var bumps = s.Host.Explore!.Bumps;
        Step(s, "e");                                                    // onto its tile
        Assert.Equal(new Tile(3, 1), s.Host.Explore!.At);
        Assert.Equal(bumps, s.Host.Explore!.Bumps);
        Assert.False(s.Host.Explore!.Blocked);
        Assert.Null(Thing(s, "crate"));
        Assert.Empty(s.Host.Explore!.Plugs!.Value);                       // and it's no wall either: it's nothing at all until it's found
        Step(s, "e");
        Assert.Equal(new Tile(4, 1), s.Host.Explore!.At);
    }

    [Fact]
    public void Once_found_a_hidden_crate_stands_in_the_way()
    {
        var s = Vault.At(new Tile(2, 1), Vault.Load(HiddenCrate), w => w with { Things = Ordinal.Map(("crate", new InteractableSave("closed", Found: true))) });
        var bumps = s.Host.Explore!.Bumps;
        s.Send(0, "walk", new { dir = "e" });
        Assert.Equal(new Tile(2, 1), s.Host.Explore!.At);
        Assert.Equal(bumps + 1, s.Host.Explore!.Bumps);
        Assert.NotNull(Thing(s, "crate"));
    }

    [Fact]
    public void A_hidden_crate_is_never_found_under_the_leader_but_as_they_step_off()
    {
        string[] things = ["""{ "id": "crate", "kind": "crate", "at": [3, 1], "hidden": 1 }"""];
        var s = Vault.At(new Tile(2, 1), Vault.Load(things));
        // A DC of 1: found from three tiles away, as soon as the party looks. Started beside it, it's found at once.
        Assert.True(Saved(s, "crate")!.Found);

        var under = Vault.At(new Tile(3, 1), Vault.Load(things));
        Assert.True(Saved(under, "crate") is null or { Found: false });    // standing on it: not found, so not in the way
        Assert.Equal(new Tile(3, 1), under.Host.Explore!.At);
        Step(under, "e");
        Assert.True(Saved(under, "crate")!.Found);                         // found as they step off, beside them
        Assert.NotEqual(new Tile(3, 1), under.Host.Explore!.At);
    }
}
