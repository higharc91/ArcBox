namespace Tactics.Logic.Tests;

/// <summary>The loader's checks of things to interact with (interactables design §5): each rule has a case that breaks it.</summary>
public sealed class InteractableCheckTests
{
    private const string File = "chapter-01/interactables/vault.json";

    private const string Key = """{ "id": "key", "kind": "pickup", "at": [8, 2], "name": "iron key", "sets": ["iron"] }""";
    private const string Door = """{ "id": "door", "kind": "door", "at": [4, 3], "lock": { "key": "iron", "keyName": "iron key", "pick": 12 } }""";
    private const string Lever = """{ "id": "lever", "kind": "lever", "at": [2, 5], "targets": ["gate"] }""";
    private const string Gate = """{ "id": "gate", "kind": "gate", "at": [5, 6] }""";

    private static readonly string[] Whole =
    [
        """{ "id": "sign", "kind": "signpost", "at": [3, 1], "text": ["The vault. Keep out."] }""",
        """{ "id": "chest", "kind": "chest", "at": [8, 1], "loot": [{ "item": "potion", "count": 2 }] }""",
        Key, Door, Lever, Gate,
        """{ "id": "crate", "kind": "crate", "at": [8, 4], "lock": { "pick": 15 }, "trap": { "spot": 1, "disarm": 10, "injury": "sprained_ankle" }, "loot": [{ "item": "potion" }] }""",
        """{ "id": "cache", "kind": "cache", "at": [8, 8], "hidden": 5, "loot": [{ "item": "potion" }] }""",
        """{ "id": "pit", "kind": "trap", "at": [2, 8], "trap": { "spot": 30, "disarm": 10 } }""",
        """{ "id": "shrine", "kind": "shrine", "at": [6, 8], "text": ["You kneel."], "sets": ["blessed"] }""",
        """{ "id": "cart", "kind": "ambient", "at": [1, 2], "art": "props.cart", "text": ["A cart with a broken wheel."] }""",
    ];

    private static IReadOnlyList<ContentError> ErrorsOf(params string[] things)
        => ValidationTests.ErrorsOfChapter(Vault.World(Vault.Region()), Vault.Things("vault", things));

    private static string Message(params string[] things) => Assert.Single(ErrorsOf(things)).Message;

    [Fact]
    public void The_whole_vault_loads_and_its_things_are_kept_by_region()
    {
        var pack = ValidationTests.LoadChapter(Vault.World(Vault.Region()), Vault.Things("vault", Whole));
        Assert.Equal(Whole.Length, pack.InteractablesIn("vault").Length);
        Assert.Equal("sign", pack.InteractablesIn("vault")[0].Id);
        Assert.Empty(pack.InteractablesIn("nowhere"));
        Assert.Empty(pack.InteractablesIn("vault")[0].Requires);          // absent lists are empty, never default
    }

    [Fact]
    public void Interactables_dont_change_the_battle_hash()
    {
        var with = ValidationTests.LoadChapter(Vault.World(Vault.Region()), Vault.Things("vault", Whole));
        var without = ValidationTests.LoadChapter(Vault.World(Vault.Region()), Vault.Things("vault", Key));
        Assert.Equal(without.Hash, with.Hash);
    }

    [Theory]
    [InlineData("""{ "id": "x", "kind": "wardrobe", "at": [2, 2] }""", "/interactables/0/kind", "Unknown kind 'wardrobe'")]
    [InlineData("""{ "id": "x", "kind": "chest", "at": [2, 2], "facing": "north" }""", "/interactables/0/facing", "must be ne, se, sw or nw")]
    [InlineData("""{ "id": "x", "kind": "chest", "at": [2, 2], "lock": { } }""", "/interactables/0/lock", "needs a way through it")]
    [InlineData("""{ "id": "x", "kind": "chest", "at": [2, 2], "lock": { "pick": 31 } }""", "/interactables/0/lock/pick", "must be 1 to 30")]
    [InlineData("""{ "id": "x", "kind": "note", "at": [2, 2], "lock": { "pick": 10 }, "text": ["Hi."] }""", "/interactables/0/lock", "can't be locked")]
    [InlineData("""{ "id": "x", "kind": "trap", "at": [2, 2], "trap": { "spot": 10 } }""", "/interactables/0/trap/disarm", "needs a disarm DC")]
    [InlineData("""{ "id": "x", "kind": "trap", "at": [2, 2] }""", "/interactables/0/trap", "A trap needs its trap")]
    [InlineData("""{ "id": "x", "kind": "note", "at": [2, 2] }""", "/interactables/0/text", "needs words")]
    [InlineData("""{ "id": "x", "kind": "ambient", "at": [2, 2], "text": ["Hm."] }""", "/interactables/0/art", "needs an art key")]
    [InlineData("""{ "id": "x", "kind": "cache", "at": [2, 2] }""", "/interactables/0/hidden", "always hidden")]
    [InlineData("""{ "id": "x", "kind": "pickup", "at": [2, 2] }""", "/interactables/0/loot", "needs something to pick up")]
    [InlineData("""{ "id": "x", "kind": "door", "at": [4, 3], "loot": [{ "item": "potion" }] }""", "/interactables/0/loot", "holds nothing")]
    [InlineData("""{ "id": "x", "kind": "chest", "at": [2, 2], "loot": [{ "item": "potion", "count": 0 }] }""", "/interactables/0/loot/0/count", "must be 1 to 99")]
    [InlineData("""{ "id": "x", "kind": "lever", "at": [2, 2] }""", "/interactables/0/targets", "does nothing")]
    [InlineData("""{ "id": "x", "kind": "chest", "at": [2, 2], "targets": ["y"] }""", "/interactables/0/targets", "Only a lever")]
    [InlineData("""{ "id": "x", "kind": "chest", "at": [2, 2], "open": true }""", "/interactables/0/open", "Only a passage")]
    [InlineData("""{ "id": "x", "kind": "door", "at": [4, 3], "blocks": false }""", "/interactables/0/blocks", "depends on its state")]
    [InlineData("""{ "id": "x", "kind": "ambient", "at": [2, 2], "art": "props.cart", "text": ["Hm."], "sets": ["y"] }""", "/interactables/0/sets", "sets nothing")]
    [InlineData("""{ "id": "x", "kind": "chest", "at": [2, 2], "hidden": 0 }""", "/interactables/0/hidden", "must be 1 to 30")]
    public void A_thing_its_kind_doesnt_allow_is_an_error(string thing, string pointer, string part)
    {
        var error = Assert.Single(ErrorsOf(thing));
        Assert.Equal((File, pointer), (error.File, error.Pointer));
        Assert.Contains(part, error.Message);
    }

    [Fact]
    public void Unknown_items_and_injuries_are_errors()
    {
        var errors = ErrorsOf("""{ "id": "x", "kind": "chest", "at": [2, 2], "loot": [{ "item": "gold" }], "trap": { "spot": 9, "injury": "broken_heart" } }""");
        Assert.Equal(["Unknown injury 'broken_heart'.", "Unknown item 'gold'."], errors.Select(e => e.Message).Order());
    }

    [Fact]
    public void An_unknown_field_is_an_error()
        => Assert.Contains("lok", Assert.Single(ErrorsOf("""{ "id": "x", "kind": "chest", "at": [2, 2], "lok": { "pick": 3 } }""")).Message);

    [Fact]
    public void Ids_are_unique_across_the_pack()
        => Assert.Contains("Duplicate interactable 'x'",
            Message("""{ "id": "x", "kind": "sack", "at": [2, 2] }""", """{ "id": "x", "kind": "sack", "at": [3, 2] }"""));

    [Fact]
    public void A_file_for_a_region_that_isnt_loaded_is_an_error()
    {
        var errors = ValidationTests.ErrorsOfChapter(Vault.World(Vault.Region()), Vault.Things("elsewhere", """{ "id": "x", "kind": "sack", "at": [2, 2] }"""));
        Assert.Equal("The interactable 'x' is in the region 'elsewhere', which isn't loaded.", Assert.Single(errors).Message);
    }

    [Theory]
    [InlineData(40, 2, "outside the region")]
    [InlineData(0, 2, "isn't on ground that can be walked")]
    [InlineData(1, 1, "is on a spawn")]
    public void A_thing_stands_on_ground_in_the_region_off_the_spawns(int x, int y, string part)
        => Assert.Contains(part, Assert.Single(ErrorsOf($$"""{ "id": "x", "kind": "sack", "at": [{{x}}, {{y}}] }""")).Message);

    [Fact]
    public void Two_things_on_one_tile_are_an_error()
        => Assert.Equal("'y' at (2, 2) shares its tile with 'x'.",
            Message("""{ "id": "x", "kind": "sack", "at": [2, 2] }""", """{ "id": "y", "kind": "sack", "at": [2, 2] }"""));

    [Fact]
    public void A_thing_inside_a_stamp_or_on_a_beat_spot_is_an_error()
    {
        const string map = """{ "id": "ford", "size": [2, 2], "tiles": "AAAAAAAAAAAAAAAA", "materials": ["grass/lush"], "props": [], "zones": {} }""";
        var region = Vault.Region() with { Stamps = [new RegionStamp("ford", new Tile(6, 4), 0)], Beats = [new BeatSpot("b1", new Tile(2, 7))] };
        var errors = ValidationTests.ErrorsOfChapter(Vault.World(region), ("maps/ford.json", map),
            Vault.Things("vault", """{ "id": "x", "kind": "sack", "at": [7, 5] }""", """{ "id": "y", "kind": "sack", "at": [2, 7] }"""));
        Assert.Equal(["'x' at (7, 5) is inside the stamp 'ford': a fight there would play over it.",
            "'y' at (2, 7) is on the spot of the beat 'b1', where its figure stands."], errors.Select(e => e.Message));
    }

    [Fact]
    public void A_thing_walled_in_is_unreachable()
        => Assert.Equal("'x' at (8, 8) can't be reached from the start, even with every passage open.",
            Message("""{ "id": "x", "kind": "sack", "at": [8, 8] }""", """{ "id": "c1", "kind": "chest", "at": [5, 6] }"""));

    [Fact]
    public void A_hidden_chest_in_the_only_way_is_judged_as_found_and_refused()
    {
        // Unfound it's walked through, but once found it stands there for good: the check counts it as found.
        var region = Vault.Region() with { Spawns = new RegionSpawns(Vault.Start, new Tile(4, 4)) };
        var errors = ValidationTests.ErrorsOfChapter(Vault.World(region), Vault.Things("vault", """{ "id": "c1", "kind": "chest", "at": [4, 3], "hidden": 12 }"""));
        Assert.Equal("The recovery spawn at (4, 4) is walled off by things that stand in the way ('c1').", Assert.Single(errors).Message);
    }

    [Fact]
    public void A_chest_in_the_only_way_to_the_recovery_spawn_is_an_error()
    {
        var region = Vault.Region() with { Spawns = new RegionSpawns(Vault.Start, new Tile(4, 4)) };
        var errors = ValidationTests.ErrorsOfChapter(Vault.World(region), Vault.Things("vault", """{ "id": "c1", "kind": "chest", "at": [4, 3] }"""));
        Assert.Equal("The recovery spawn at (4, 4) is walled off by things that stand in the way ('c1').", Assert.Single(errors).Message);
    }

    [Fact]
    public void A_lock_that_can_be_failed_for_good_cant_stand_before_what_the_story_needs()
    {
        var region = Vault.Region() with { Spawns = new RegionSpawns(Vault.Start, new Tile(4, 4)) };
        var errors = ValidationTests.ErrorsOfChapter(Vault.World(region),
            Vault.Things("vault", """{ "id": "door", "kind": "door", "at": [4, 3], "lock": { "pick": 12 } }"""));
        Assert.StartsWith("The recovery spawn at (4, 4) is behind a passage that may never open ('door')", Assert.Single(errors).Message);
        // A door with no lock always opens. (A key door would not do: a party that wakes beyond it, before finding the key, is shut in.)
        Assert.Single(ValidationTests.LoadChapter(Vault.World(region), Vault.Things("vault", """{ "id": "door", "kind": "door", "at": [4, 3] }""")).InteractablesIn("vault"));
        Assert.Contains("can't be sure of a way back", Assert.Single(ValidationTests.ErrorsOfChapter(Vault.World(region), Vault.Things("vault", Key, Door))).Message);
        // A hidden door may never be found.
        errors = ValidationTests.ErrorsOfChapter(Vault.World(region),
            Vault.Things("vault", """{ "id": "door", "kind": "secret_door", "at": [4, 3], "hidden": 12 }"""));
        Assert.Contains("may never open", Assert.Single(errors).Message);
    }

    [Fact]
    public void A_key_behind_its_own_door_is_an_error()
        => Assert.Equal("The key 'iron' of 'door' can't be had before it: what gives it ('key') is behind it, hidden, or behind a lock that can be failed.",
            Message(Door, """{ "id": "key", "kind": "pickup", "at": [8, 5], "name": "iron key", "sets": ["iron"] }"""));

    [Fact]
    public void A_key_in_a_chest_whose_lock_can_be_failed_isnt_sure()
        => Assert.Contains("can't be had before it",
            Message(Door, """{ "id": "box", "kind": "chest", "at": [8, 2], "lock": { "pick": 14 }, "sets": ["iron"] }"""));

    [Fact]
    public void A_key_the_region_never_gives_is_left_to_the_story_check()
        => Assert.Single(ValidationTests.LoadChapter(Vault.World(Vault.Region()), Vault.Things("vault", Door)).InteractablesIn("vault"));

    [Theory]
    [InlineData("""{ "id": "lever", "kind": "lever", "at": [2, 2], "targets": ["nope"] }""", "which isn't in this region")]
    [InlineData("""{ "id": "lever", "kind": "lever", "at": [2, 2], "targets": ["box"] }""", "which isn't a passage")]
    [InlineData("""{ "id": "lever", "kind": "lever", "at": [2, 2], "targets": ["gate"], "open": true }""", "a lever's passages are open while it's on")]
    public void A_lever_works_passages_of_its_region_that_start_as_it_does(string lever, string part)
        => Assert.Contains(part, Assert.Single(ErrorsOf(lever, """{ "id": "box", "kind": "sack", "at": [3, 2] }""",
            """{ "id": "gate", "kind": "gate", "at": [4, 3] }""")).Message);

    [Fact]
    public void A_passage_with_two_levers_or_a_lock_as_well_is_an_error()
    {
        var errors = ErrorsOf("""{ "id": "l1", "kind": "lever", "at": [2, 2], "targets": ["gate"] }""",
            """{ "id": "l2", "kind": "lever", "at": [3, 2], "targets": ["gate"] }""",
            """{ "id": "gate", "kind": "gate", "at": [4, 3], "lock": { "key": "k" } }""");
        Assert.Equal(["'gate' is worked by 'l1' already: two levers would fight over it.",
            "'gate' is worked by the lever 'l1', so it can't have a lock as well.",
            "'gate' is worked by the lever 'l2', so it can't have a lock as well."], errors.Select(e => e.Message).Order());
    }

    [Fact]
    public void A_lever_behind_the_gate_it_works_shuts_what_lies_beyond_for_good()
    {
        var region = Vault.Region() with { Spawns = new RegionSpawns(Vault.Start, new Tile(4, 7)) };
        var errors = ValidationTests.ErrorsOfChapter(Vault.World(region), Vault.Things("vault", Key, Door,
            """{ "id": "lever", "kind": "lever", "at": [2, 8], "targets": ["gate"] }""", Gate));
        Assert.StartsWith("The recovery spawn at (4, 7) is behind a passage that may never open ('gate')", Assert.Single(errors).Message);
    }

    /// <summary>
    /// Review: a party that wakes beyond a gate whose lever is on the far side may find the gate shut, whatever it was when the region began.
    /// From where the party is put without walking, it must be sure of a way back.
    /// </summary>
    [Fact]
    public void A_recovery_spawn_behind_a_gate_whose_lever_is_the_other_side_is_an_error()
    {
        var region = Vault.Region() with { Spawns = new RegionSpawns(Vault.Start, new Tile(4, 7)) };
        Assert.Equal("The recovery spawn at (4, 7) can't be sure of a way back to the start ('gate' may be shut): the party is put there without "
            + "walking, so give it a lever, a key or a way round on its own side.",
            Assert.Single(ValidationTests.ErrorsOfChapter(Vault.World(region), Vault.Things("vault", Key, Door, Lever, Gate))).Message);
        // A gate that starts open, with its lever on: still shut by the time the party wakes, if someone pulled it.
        Assert.Contains("can't be sure of a way back to the start ('gate' may be shut)", Assert.Single(ValidationTests.ErrorsOfChapter(Vault.World(region), Vault.Things("vault",
            """{ "id": "door", "kind": "door", "at": [4, 3] }""",
            """{ "id": "lever", "kind": "lever", "at": [2, 5], "targets": ["gate"], "open": true }""",
            """{ "id": "gate", "kind": "gate", "at": [5, 6], "open": true }"""))).Message);
    }

    private const string StampMap = """{ "id": "ford", "size": [2, 2], "tiles": "AAAAAAAAAAAAAAAA", "materials": ["grass/lush"], "props": [], "zones": { "party_spawn": [[0, 0]] } }""";

    private static IReadOnlyList<ContentError> StampErrors(Tile at, params string[] things)
        => ValidationTests.ErrorsOfChapter(Vault.World(Vault.Region() with { Stamps = [new RegionStamp("ford", at, 0)] }), ("maps/ford.json", StampMap),
            Vault.Things("vault", things));

    [Fact]
    public void A_landing_by_a_stamp_beyond_a_lever_gate_is_an_error()
        => Assert.StartsWith("The landing by the stamp 'ford' at (6, 7) can't be sure of a way back to the start ('gate' may be shut)",
            Assert.Single(StampErrors(new Tile(6, 7), Key, Door, Lever, Gate)).Message);

    [Theory]
    [InlineData("""{ "id": "door", "kind": "door", "at": [4, 3], "lock": { "pick": 12 } }""")]
    [InlineData("""{ "id": "door", "kind": "secret_door", "at": [4, 3], "hidden": 12 }""")]
    public void A_stamp_behind_a_lock_that_can_be_spent_or_a_door_never_found_is_an_error(string door)
        => Assert.StartsWith("The party spawn of the stamp 'ford' at (6, 4) is behind a passage that may never open ('door')",
            Assert.Single(StampErrors(new Tile(6, 4), door)).Message);

    [Fact]
    public void A_key_that_can_be_taken_away_isnt_sure()
        => Assert.Contains("can't be had before it",
            Message(Door, """{ "id": "key", "kind": "pickup", "at": [8, 2], "name": "iron key", "sets": ["iron"], "unless": ["robbed"] }"""));
}
