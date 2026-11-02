using System.Collections.Immutable;

namespace Tactics.Logic.Tests;

/// <summary>What interacting does (interactables design §2.7): at once, or a scene in 5d's format whose checks the party rolls.</summary>
public sealed class InteractScenesTests
{
    private static readonly HashSet<string> NoFlags = [];

    private static string Name(ItemId id) => id.Value switch { "potion" => "Potion of Healing", _ => id.Value };

    private static InteractPlan? Plan(InteractableDef def, InteractableSave? save = null, IReadOnlySet<string>? flags = null, bool worked = false)
        => InteractScenes.Plan(def, save ?? Interactables.Initial(def), flags ?? NoFlags, worked, Name);

    private static ScenePlan Scene(InteractableDef def, InteractableSave? save = null, IReadOnlySet<string>? flags = null)
        => Assert.IsType<ScenePlan>(Plan(def, save, flags));

    private static ImmutableArray<string> Offered(ScenePlan plan, string node, IReadOnlySet<string>? flags = null)
        => [.. SceneRun.Offered(plan.Scene.Node(node)!, flags ?? NoFlags, PartyTags.None, new HashSet<string>()).Select(o => o.Id)];

    private static readonly InteractableDef Chest = Vault.Def("""{ "id": "c", "kind": "chest", "at": [1, 1], "loot": [{ "item": "potion", "count": 2 }], "sets": ["got_it"] }""");

    [Fact]
    public void An_unlocked_chest_opens_at_once_with_its_loot()
    {
        var plan = Assert.IsType<InstantPlan>(Plan(Chest));
        Assert.Equal("Inside: 2 × Potion of Healing.", plan.Line);
        Assert.Equal(new InteractOutcome(ThingEvents.Opened, "open", Loot: true, Sets: Chest.Sets), plan.Outcome);
        Assert.True(InteractScenes.LootDue(Interactables.Initial(Chest), plan.Outcome!));
        Assert.False(InteractScenes.LootDue(new InteractableSave("open"), plan.Outcome!));      // never twice
    }

    [Fact]
    public void An_empty_container_says_its_kinds_line()
        => Assert.Equal("Nothing but straw.", Assert.IsType<InstantPlan>(Plan(Vault.Def("""{ "id": "c", "kind": "crate", "at": [1, 1] }"""))).Line);

    [Fact]
    public void A_locked_chest_offers_the_key_held_each_check_and_leaving()
    {
        var chest = Chest with { Lock = new LockDef(Key: "iron", KeyName: "iron key", Pick: 12, Force: 15) };
        var plan = Scene(chest);
        Assert.Equal("locked", plan.Scene.Start);
        Assert.Equal(["pick", "force", "leave"], Offered(plan, "locked"));
        Assert.Equal(["key", "pick", "force", "leave"], Offered(plan, "locked", new HashSet<string> { "iron" }));
        var options = plan.Scene.Node("locked")!.Options;
        Assert.Equal("Use the iron key", options[0].Label);
        Assert.Equal(new SceneCheck("sleight_of_hand", 12, "unlocked", "pick_failed"), options[1].Check);
        Assert.Equal(new SceneCheck("athletics", 15, "unlocked", "force_failed"), options[2].Check);
        Assert.Equal("open", plan.Scene.Node("unlocked")!.Next);
        Assert.True(plan.Outcomes["unlocked"].Unlock);
        Assert.Equal("pick", plan.Outcomes["pick_failed"].Spend);
        Assert.True(plan.Outcomes["open"].Loot);
        Assert.False(plan.Scene.Node("locked")!.Vote);          // the leader's to choose
        Assert.Equal("leave", SceneRun.Unanswered([.. options.Where(o => o.Id != "key")], vote: false).Id);    // a timer never spends a roll
    }

    [Fact]
    public void A_failed_check_is_spent_and_a_lock_with_nothing_left_is_only_said()
    {
        var chest = Chest with { Lock = new LockDef(Pick: 12, Seal: 14) };
        var spent = new InteractableSave("closed", Spent: ["pick"]);
        Assert.Equal(["seal", "leave"], Offered(Scene(chest, spent), "locked"));
        var none = Assert.IsType<InstantPlan>(Plan(chest, spent with { Spent = ["pick", "seal"] }));
        Assert.Null(none.Outcome);
        Assert.Contains("nothing left to try", none.Line);
    }

    [Fact]
    public void A_seal_only_lock_says_its_magic()
        => Assert.Equal("A seal of old magic holds the door shut.",
            Scene(Vault.Def("""{ "id": "d", "kind": "door", "at": [1, 1], "lock": { "seal": 13 } }""")).Scene.Node("locked")!.Lines[0].Text);

    [Fact]
    public void A_key_held_after_every_check_failed_still_opens_it()
    {
        var chest = Chest with { Lock = new LockDef(Key: "iron", Pick: 12) };
        var plan = Scene(chest, new InteractableSave("closed", Spent: ["pick"]), new HashSet<string> { "iron" });
        Assert.Equal(["key", "leave"], Offered(plan, "locked", new HashSet<string> { "iron" }));
    }

    [Fact]
    public void An_unseen_trap_goes_off_first_and_the_scene_goes_on_to_the_lock()
    {
        var chest = Chest with { Lock = new LockDef(Pick: 12), Trap = new TrapDef(14, 12, [], ["stung"]) };
        var plan = Scene(chest);
        Assert.Equal("sprung", plan.Scene.Start);
        Assert.Equal("locked", plan.Scene.Node("sprung")!.Next);
        Assert.Equal(InteractableSave.Sprung, plan.Outcomes["sprung"].Trap);
        Assert.Equal(["stung"], plan.Outcomes["sprung"].Sets);
        Assert.True(InteractScenes.SpringDue(Interactables.Initial(chest), plan.Outcomes["sprung"]));
        Assert.False(InteractScenes.SpringDue(new InteractableSave("closed", Trap: "sprung"), plan.Outcomes["sprung"]));
    }

    [Fact]
    public void A_seen_trap_can_be_disarmed_left_or_braved()
    {
        var chest = Chest with { Trap = new TrapDef(14, 12) };
        var plan = Scene(chest, new InteractableSave("closed", TrapSpotted: true));
        Assert.Equal("trap", plan.Scene.Start);
        Assert.Equal(["disarm", "leave", "anyway"], Offered(plan, "trap"));
        Assert.Equal(new SceneCheck("sleight_of_hand", 12, "disarmed", "disarm_failed"), plan.Scene.Node("trap")!.Options[0].Check);
        Assert.Equal("open", plan.Scene.Node("disarmed")!.Next);
        Assert.Equal(InteractableSave.Disarmed, plan.Outcomes["disarmed"].Trap);
        Assert.Equal((InteractableSave.Sprung, "disarm"), (plan.Outcomes["disarm_failed"].Trap, plan.Outcomes["disarm_failed"].Spend));
        Assert.Equal("leave", SceneRun.Unanswered([.. plan.Scene.Node("trap")!.Options], vote: false).Id);   // never braved by a timer
    }

    [Fact]
    public void A_floor_trap_goes_off_when_stepped_on_unseen_and_is_disarmed_once_seen()
    {
        var trap = Vault.Def("""{ "id": "t", "kind": "trap", "at": [1, 1], "trap": { "spot": 20, "disarm": 10, "text": ["Spikes!"] } }""");
        var stepped = InteractScenes.SteppedOn(trap, Interactables.Initial(trap), NoFlags)!;
        Assert.Equal("Spikes!", stepped.Line);
        Assert.Equal(InteractableSave.Sprung, stepped.Outcome!.Trap);
        Assert.Null(InteractScenes.SteppedOn(trap, new InteractableSave("idle", TrapSpotted: true), NoFlags));
        Assert.Null(InteractScenes.SteppedOn(trap, new InteractableSave("idle", Trap: "sprung"), NoFlags));
        Assert.Null(Plan(trap));                                                                 // unseen: nothing to use
        var seen = Scene(trap, new InteractableSave("idle", TrapSpotted: true));
        Assert.Equal(["disarm", "leave"], Offered(seen, "trap"));
        Assert.True(seen.Scene.Node("disarmed")!.End);
    }

    [Fact]
    public void A_lever_toggles_and_sets_its_flags_only_going_on()
    {
        var lever = Vault.Def("""{ "id": "l", "kind": "lever", "at": [1, 1], "sets": ["pulled"], "targets": ["g"] }""");
        var on = Assert.IsType<InstantPlan>(Plan(lever)).Outcome!;
        Assert.Equal("on", on.State);
        Assert.Equal(["pulled"], on.Sets);
        var off = Assert.IsType<InstantPlan>(Plan(lever, new InteractableSave("on"))).Outcome!;
        Assert.Equal("off", off.State);
        Assert.Empty(off.Sets);
    }

    [Fact]
    public void A_passage_a_lever_works_wont_budge()
    {
        var gate = Vault.Def("""{ "id": "g", "kind": "gate", "at": [1, 1] }""");
        var plan = Assert.IsType<InstantPlan>(Plan(gate, worked: true));
        Assert.Null(plan.Outcome);
        Assert.Contains("won't budge", plan.Line);
    }

    [Fact]
    public void A_readable_is_a_scene_of_its_words_that_marks_it_read()
    {
        var note = Vault.Def("""{ "id": "n", "kind": "note", "at": [1, 1], "text": ["Dear Maud,", "the grain is in the cellar."], "sets": ["know_cellar"] }""");
        var plan = Scene(note);
        var words = plan.Scene.Node("words")!;
        Assert.Equal(["Dear Maud,", "the grain is in the cellar."], words.Lines.Select(l => l.Text));
        Assert.All(words.Lines, l => Assert.Equal(SceneDef.Narrator, l.Speaker));
        Assert.Equal(["thing.n.words.0", "thing.n.words.1"], words.Lines.Select(l => l.Id));
        Assert.True(words.End);
        Assert.Equal((ThingEvents.Read, "read"), (plan.Outcomes["words"].Event, plan.Outcomes["words"].State));
        Assert.Equal(["know_cellar"], plan.Outcomes["words"].Sets);
        Assert.Equal("Note", plan.Scene.Title);
        Assert.IsType<ScenePlan>(Plan(note, new InteractableSave("read")));      // it can be read again
    }

    [Fact]
    public void Ambient_and_pickups_are_at_once()
    {
        var cart = Vault.Def("""{ "id": "a", "kind": "ambient", "at": [1, 1], "art": "props.cart", "text": ["A cart with a broken wheel."] }""");
        Assert.Equal(new InstantPlan(null, "A cart with a broken wheel."), Plan(cart));
        var key = Vault.Def("""{ "id": "k", "kind": "pickup", "at": [1, 1], "name": "iron key", "sets": ["iron"] }""");
        var taken = Assert.IsType<InstantPlan>(Plan(key));
        Assert.Equal("You take the iron key.", taken.Line);
        Assert.Equal(("taken", true), (taken.Outcome!.State, taken.Outcome.Loot));
        Assert.Null(Plan(key, new InteractableSave("taken")));
    }

    [Fact]
    public void An_outcome_applied_twice_is_the_same_as_once()
    {
        var outcome = new InteractOutcome(ThingEvents.Failed, Spend: "pick", Unlock: true, Trap: "sprung");
        var once = outcome.Apply(new InteractableSave("closed"));
        Assert.Equal(once with { Spent = once.Spent }, outcome.Apply(once) with { Spent = once.Spent });
        Assert.Equal(["pick"], outcome.Apply(once).Spent);
        Assert.True(once.TrapSpotted);
        Assert.Equal("disarmed", new InteractOutcome(ThingEvents.Disarmed, Trap: "sprung").Apply(new("closed", Trap: "disarmed")).Trap);   // a trap never resets
    }

    [Fact]
    public void A_scene_built_again_from_its_start_is_the_same_whatever_the_flags_became()
    {
        var chest = Chest with { Lock = new LockDef(Key: "iron", Pick: 12), Unless = ["got_it"] };
        var first = Scene(chest, flags: new HashSet<string> { "iron" });
        var again = Assert.IsType<ScenePlan>(InteractScenes.Plan(chest, Interactables.Initial(chest), new HashSet<string> { "got_it" }, false, Name, again: true));
        Assert.Equal(first.Scene.Nodes.Select(n => (n.Id, n.Next, n.End, string.Join("|", n.Lines.Select(l => l.Text)))),
            again.Scene.Nodes.Select(n => (n.Id, n.Next, n.End, string.Join("|", n.Lines.Select(l => l.Text)))));
        Assert.Equal(first.Outcomes.Keys.Order(), again.Outcomes.Keys.Order());
    }

    /// <summary>
    /// Every scene any thing can make, in any state it can be in, holds together as 5d plays it: it starts at a node it has, every node has a
    /// line and ends, goes on or asks, every option leads to a node it has, every check uses a real skill, and every outcome is of a node.
    /// </summary>
    [Fact]
    public void Every_scene_any_thing_makes_holds_together()
    {
        var count = 0;
        foreach (var def in Defs())
            foreach (var save in States(def))
            {
                if (Plan(def, save, new HashSet<string> { "iron" }) is not ScenePlan plan)
                    continue;
                count++;
                var ids = plan.Scene.Nodes.Select(n => n.Id).ToHashSet();
                Assert.Contains(plan.Scene.Start, ids);
                Assert.Equal(ids.Count, plan.Scene.Nodes.Length);
                Assert.All(plan.Scene.Nodes, n =>
                {
                    Assert.NotEmpty(n.Lines);
                    Assert.Equal(1, (n.End ? 1 : 0) + (n.Next is not null ? 1 : 0) + (n.Options.IsEmpty ? 0 : 1));
                    Assert.All(n.Targets, t => Assert.Contains(t, ids));
                    Assert.All(n.Options.Where(o => o.Check is not null), o => Assert.NotNull(SkillCheck.StatOf(o.Check!.Skill)));
                    Assert.All(n.Options, o => Assert.True(o.Check is null || o.To is null));
                });
                Assert.All(plan.Outcomes.Keys, k => Assert.Contains(k, ids));
                Assert.Equal(plan.Scene.Nodes.SelectMany(n => n.Lines).Count(), plan.Scene.Nodes.SelectMany(n => n.Lines).Select(l => l.Id).Distinct().Count());
                // Every node can be reached from the start, so nothing written is dead.
                var seen = new HashSet<string> { plan.Scene.Start };
                var open = new Queue<string>([plan.Scene.Start]);
                while (open.TryDequeue(out var at))
                    foreach (var next in plan.Scene.Node(at)!.Targets)
                        if (seen.Add(next))
                            open.Enqueue(next);
                Assert.Equal(ids, seen);
            }
        Assert.True(count > 40, $"only {count} scenes");
    }

    private static IEnumerable<InteractableDef> Defs()
    {
        LockDef?[] locks = [null, new(Key: "iron"), new(Pick: 10), new(Key: "iron", Pick: 10, Force: 12, Seal: 14), new(Seal: 9)];
        TrapDef?[] traps = [null, new(10, 12), new(10)];
        foreach (var kind in new[] { "chest", "sack", "door", "secret_door" })
            foreach (var lk in locks)
                foreach (var trap in traps)
                    yield return Chest with { Kind = kind, Lock = lk, Trap = trap, Hidden = kind == "secret_door" ? 10 : null };
        yield return Vault.Def("""{ "id": "t", "kind": "trap", "at": [1, 1], "trap": { "spot": 20, "disarm": 10 } }""");
        yield return Vault.Def("""{ "id": "n", "kind": "book", "at": [1, 1], "text": ["One.", "Two."] }""");
        yield return Vault.Def("""{ "id": "s", "kind": "altar", "at": [1, 1], "text": ["Light."] }""");
    }

    private static IEnumerable<InteractableSave> States(InteractableDef def)
    {
        var start = Interactables.Initial(def);
        foreach (var found in new[] { false, true })
            foreach (var spotted in new[] { false, true })
                foreach (var trap in new string?[] { null, "disarmed", "sprung" })
                    foreach (var spent in new[] { ImmutableArray<string>.Empty, ["pick"], ["pick", "force", "seal", "disarm"] })
                        yield return start with { Found = found, TrapSpotted = spotted || trap is not null, Trap = trap, Spent = spent };
    }
}
