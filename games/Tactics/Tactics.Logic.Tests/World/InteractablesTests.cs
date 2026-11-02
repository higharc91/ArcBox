using System.Collections.Immutable;

namespace Tactics.Logic.Tests;

/// <summary>The rules of things to interact with (interactables design §2): what's there, seen, in the way and in reach, and what's found.</summary>
public sealed class InteractablesTests
{
    private static readonly HashSet<string> NoFlags = [];

    private static InteractableSave S(InteractableDef d) => Interactables.Initial(d);

    [Fact]
    public void Every_kind_has_states_an_art_key_and_a_name()
    {
        Assert.All(InteractableKinds.All.Values, k =>
        {
            Assert.NotEmpty(k.States);
            Assert.StartsWith("interactables.", k.Art);
            Assert.Equal($"interactables.{k.Id}", k.Art);
            Assert.False(string.IsNullOrWhiteSpace(k.Name));
            Assert.All(k.StandIn.Keys, state => Assert.Contains(state, k.States));
        });
    }

    [Fact]
    public void Every_stand_in_is_art_the_pack_has()
    {
        var art = ArtManifests.Load(ContentPaths.ContentBase);
        Assert.All(InteractableKinds.All.Values.SelectMany(k => k.StandIn.Values), key => Assert.Contains(key, art.Keys));
    }

    [Theory]
    [InlineData("""{ "id": "c", "kind": "chest", "at": [1, 1] }""", "closed")]
    [InlineData("""{ "id": "d", "kind": "door", "at": [1, 1], "open": true }""", "open")]
    [InlineData("""{ "id": "l", "kind": "lever", "at": [1, 1], "sets": ["x"] }""", "off")]
    [InlineData("""{ "id": "l", "kind": "lever", "at": [1, 1], "open": true, "sets": ["x"] }""", "on")]
    [InlineData("""{ "id": "n", "kind": "note", "at": [1, 1], "text": ["Hi."] }""", "unread")]
    [InlineData("""{ "id": "p", "kind": "pickup", "at": [1, 1], "sets": ["k"] }""", "here")]
    [InlineData("""{ "id": "s", "kind": "shrine", "at": [1, 1], "text": ["Hum."] }""", "ready")]
    public void A_thing_starts_in_its_kinds_first_state_or_open(string json, string state)
        => Assert.Equal(state, S(Vault.Def(json)).State);

    [Fact]
    public void A_state_the_kind_doesnt_have_reads_as_the_start()
    {
        var chest = Vault.Def("""{ "id": "c", "kind": "chest", "at": [1, 1] }""");
        var saves = new Dictionary<string, InteractableSave> { ["c"] = new("on") };
        Assert.Equal("closed", Interactables.StateOf(chest, saves).State);
    }

    [Fact]
    public void Flags_put_a_thing_there_and_take_it_away()
    {
        var note = Vault.Def("""{ "id": "n", "kind": "note", "at": [1, 1], "text": ["Hi."], "requires": ["a"], "unless": ["b"] }""");
        Assert.False(Interactables.Present(note, NoFlags));
        Assert.True(Interactables.Present(note, new HashSet<string> { "a" }));
        Assert.False(Interactables.Present(note, new HashSet<string> { "a", "b" }));
        Assert.Null(Interactables.Label(note, S(note), NoFlags, false));
    }

    [Fact]
    public void A_hidden_thing_isnt_seen_or_used_until_its_found()
    {
        var cache = Vault.Def("""{ "id": "h", "kind": "cache", "at": [1, 1], "hidden": 12 }""");
        Assert.False(Interactables.Visible(cache, S(cache), NoFlags));
        Assert.Null(Interactables.Label(cache, S(cache), NoFlags, false));
        Assert.True(Interactables.Visible(cache, S(cache) with { Found = true }, NoFlags));
        Assert.Equal("Open the hidden cache", Interactables.Label(cache, S(cache) with { Found = true }, NoFlags, false));
    }

    [Fact]
    public void A_floor_trap_is_seen_once_spotted_or_sprung_and_blocks_only_while_seen_and_set()
    {
        var trap = Vault.Def("""{ "id": "t", "kind": "trap", "at": [1, 1], "trap": { "spot": 12, "disarm": 10 } }""");
        var set = S(trap);
        Assert.False(Interactables.Visible(trap, set, NoFlags));
        Assert.False(Interactables.Blocks(trap, set, NoFlags));          // nobody knows: they walk onto it
        Assert.True(Interactables.Blocks(trap, set with { TrapSpotted = true }, NoFlags));
        Assert.Equal("Disarm the trap", Interactables.Label(trap, set with { TrapSpotted = true }, NoFlags, false));
        var sprung = set with { TrapSpotted = true, Trap = InteractableSave.Sprung };
        Assert.True(Interactables.Visible(trap, sprung, NoFlags));
        Assert.False(Interactables.Blocks(trap, sprung, NoFlags));
        Assert.Null(Interactables.Label(trap, sprung, NoFlags, false));
        Assert.Equal("sprung", Interactables.ArtState(trap, sprung));
        Assert.Equal("armed", Interactables.ArtState(trap, set));
    }

    [Fact]
    public void A_passage_blocks_while_closed_found_or_not_and_a_chest_always()
    {
        var door = Vault.Def("""{ "id": "d", "kind": "secret_door", "at": [1, 1], "hidden": 15 }""");
        Assert.True(Interactables.Blocks(door, S(door), NoFlags));                      // a hidden door is a wall
        Assert.False(Interactables.Blocks(door, S(door) with { State = "open" }, NoFlags));
        var chest = Vault.Def("""{ "id": "c", "kind": "chest", "at": [2, 2] }""");
        Assert.True(Interactables.Blocks(chest, S(chest) with { State = "open" }, NoFlags));
        var sack = Vault.Def("""{ "id": "s", "kind": "sack", "at": [2, 2] }""");
        Assert.False(Interactables.Blocks(sack, S(sack), NoFlags));
        Assert.True(Interactables.Blocks(sack with { Blocks = true }, S(sack), NoFlags));
        var gone = Vault.Def("""{ "id": "g", "kind": "gate", "at": [3, 3], "requires": ["never"] }""");
        Assert.False(Interactables.Blocks(gone, S(gone), NoFlags));
        Assert.Equal([new Tile(1, 1), new Tile(2, 2)], Interactables.Blocking([door, chest, sack, gone], null, NoFlags));
    }

    [Fact]
    public void Reach_is_the_eight_tiles_round_within_a_climb()
    {
        var ground = Vault.Ground(Vault.Region(rows: ["#####", "#...#", "#.5.#", "#...#", "#####"])).Terrain;
        var chest = Vault.Def("""{ "id": "c", "kind": "chest", "at": [1, 1] }""");
        Assert.True(Interactables.InReach(ground, new Tile(1, 1), chest));
        Assert.True(Interactables.InReach(ground, new Tile(2, 1), chest));
        Assert.False(Interactables.InReach(ground, new Tile(3, 1), chest));
        Assert.False(Interactables.InReach(ground, new Tile(2, 2), chest));      // 5 up: a cliff, not a step
        var high = chest with { At = new Tile(2, 2) };
        Assert.False(Interactables.InReach(ground, new Tile(1, 1), high));
    }

    [Fact]
    public void Targets_are_the_nearest_first_then_the_faced_then_by_id()
    {
        var ground = Vault.Ground().Terrain;
        var a = Vault.Def("""{ "id": "a_sack", "kind": "sack", "at": [1, 2] }""");
        var b = Vault.Def("""{ "id": "b_sack", "kind": "sack", "at": [2, 2] }""");
        var z = Vault.Def("""{ "id": "z_sack", "kind": "sack", "at": [3, 2] }""");
        var c = Vault.Def("""{ "id": "c_note", "kind": "note", "at": [2, 1], "text": ["Hi."] }""");
        var far = Vault.Def("""{ "id": "far", "kind": "sack", "at": [5, 2] }""");
        var targets = Interactables.Targets([a, b, z, c, far], null, NoFlags, ground, new Tile(2, 1), new Tile(3, 1));
        Assert.Equal(["c_note", "b_sack", "a_sack", "z_sack"], targets.Select(t => t.Def.Id));
        Assert.Equal("Read the note", targets[0].Label);
        targets = Interactables.Targets([a, z, far], null, NoFlags, ground, new Tile(2, 1), new Tile(3, 2));
        Assert.Equal(["z_sack", "a_sack"], targets.Select(t => t.Def.Id));          // both as near: the one in front first
    }

    [Fact]
    public void Nothing_is_offered_once_its_done()
    {
        var chest = Vault.Def("""{ "id": "c", "kind": "chest", "at": [1, 1] }""");
        Assert.Null(Interactables.Label(chest, S(chest) with { State = "open" }, NoFlags, false));
        var door = Vault.Def("""{ "id": "d", "kind": "door", "at": [1, 1] }""");
        Assert.Null(Interactables.Label(door, S(door) with { State = "open" }, NoFlags, false));
        Assert.Equal("Look at the gate", Interactables.Label(door with { Kind = "gate", Name = "gate" }, S(door), NoFlags, worked: true));
        var shrine = Vault.Def("""{ "id": "s", "kind": "well", "at": [1, 1], "text": ["Cold."] }""");
        Assert.Equal("Draw from the well", Interactables.Label(shrine, S(shrine), NoFlags, false));
        Assert.Null(Interactables.Label(shrine, S(shrine) with { State = "used" }, NoFlags, false));
        var lever = Vault.Def("""{ "id": "l", "kind": "lever", "at": [1, 1], "sets": ["x"] }""");
        Assert.Equal("Pull the lever", Interactables.Label(lever, S(lever) with { State = "on" }, NoFlags, false));
    }

    [Fact]
    public void Passive_perception_finds_what_its_dc_allows_within_range()
    {
        var near = Vault.Def("""{ "id": "near", "kind": "cache", "at": [4, 4], "hidden": 12 }""");
        var hard = Vault.Def("""{ "id": "hard", "kind": "cache", "at": [4, 5], "hidden": 15 }""");
        var far = Vault.Def("""{ "id": "far", "kind": "cache", "at": [8, 4], "hidden": 5 }""");
        var trapped = Vault.Def("""{ "id": "trapped", "kind": "chest", "at": [3, 3], "trap": { "spot": 12 } }""");
        var finds = Interactables.Finds([near, hard, far, trapped], null, NoFlags, new Tile(4, 2), 12);
        Assert.Equal([("near", Interactables.Finding.Revealed), ("trapped", Interactables.Finding.Spotted)],
            finds.Select(f => (f.Def.Id, f.Found)));
        Assert.Contains(Interactables.Finds([hard], null, NoFlags, new Tile(4, 2), 15), f => f.Def.Id == "hard");
        Assert.Empty(Interactables.Finds([near], new Dictionary<string, InteractableSave> { ["near"] = new("closed", Found: true) }, NoFlags,
            new Tile(4, 2), 20));
    }

    [Fact]
    public void A_trap_on_a_hidden_thing_is_seen_with_it_or_not_at_all()
    {
        var cache = Vault.Def("""{ "id": "h", "kind": "cache", "at": [1, 1], "hidden": 10, "trap": { "spot": 14 } }""");
        Assert.Equal([Interactables.Finding.Revealed, Interactables.Finding.Spotted],
            Interactables.Finds([cache], null, NoFlags, new Tile(1, 1), 14).Select(f => f.Found));
        Assert.Empty(Interactables.Finds([cache], null, NoFlags, new Tile(1, 1), 9));      // not found: its trap can't be seen either
    }

    [Fact]
    public void Passive_perception_is_ten_plus_the_perception_bonus()
    {
        var sheet = TestSheets.Fighter.WithScore(Stat.Wis, 14) with { Skills = [] };
        Assert.Equal(12, Interactables.Passive(sheet));
        Assert.Equal(12 + Stats.Proficiency(sheet.Level), Interactables.Passive(sheet with { Skills = sheet.Skills.Add("perception") }));
    }

    [Fact]
    public void Going_back_is_a_regression_except_for_a_lever_and_the_passages_it_works()
    {
        var chest = Vault.Def("""{ "id": "c", "kind": "chest", "at": [1, 1] }""");
        var open = new InteractableSave("open", Unlocked: true, Spent: ["pick"]);
        Assert.True(Interactables.Regressed(chest, open, open with { State = "closed" }, false));
        Assert.True(Interactables.Regressed(chest, open, open with { Unlocked = false }, false));
        Assert.True(Interactables.Regressed(chest, open, open with { Spent = [] }, false));
        Assert.False(Interactables.Regressed(chest, open, open, false));
        var gate = Vault.Def("""{ "id": "g", "kind": "gate", "at": [1, 1] }""");
        Assert.True(Interactables.Regressed(gate, new("open"), new("closed"), worked: false));
        Assert.False(Interactables.Regressed(gate, new("open"), new("closed"), worked: true));
        var lever = Vault.Def("""{ "id": "l", "kind": "lever", "at": [1, 1], "sets": ["x"] }""");
        Assert.False(Interactables.Regressed(lever, new("on"), new("off"), false));
        var trap = Vault.Def("""{ "id": "t", "kind": "trap", "at": [1, 1], "trap": { "spot": 1, "disarm": 1 } }""");
        Assert.True(Interactables.Regressed(trap, new("idle", Trap: "sprung"), new("idle"), false));
    }

    [Fact]
    public void The_ground_with_things_blocks_their_tiles_and_nothing_else()
    {
        var world = Vault.Ground();
        Assert.Same(world.Terrain, world.With([]));
        var with = world.With([new Tile(4, 3)]);
        Assert.False(with.Walkable(new Tile(4, 3)));
        Assert.True(world.Terrain.Walkable(new Tile(4, 3)));
        Assert.True(with.Walkable(new Tile(4, 2)));
        Assert.DoesNotContain(new Tile(4, 4), WorldWalk.Reach(with, Vault.Start, ClassDef.DefaultJump));       // the door shuts the next room off
        Assert.Contains(new Tile(4, 4), WorldWalk.Reach(world.Terrain, Vault.Start, ClassDef.DefaultJump));
    }
}
