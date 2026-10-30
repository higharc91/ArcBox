using System.Collections.Immutable;
using ArcBox.Sdk;

namespace Tactics.Logic.Tests;

public sealed class EncounterSetupTests
{
    private static RulesPack Rules => SheetBuilderTests.Rules;

    private static UnitId U(string id) => new(id);

    private static PartyMember Member(string id, string cls = "fighter", Controller? controller = null)
        => new(U(id), SheetBuilder.DefaultCharacter(id, "human", cls, null, 1, Rules),
            controller ?? new PlayerControlled(new ProfileId("pr_" + id)));

    private static ImmutableArray<PartyMember> Party(params string[] ids) => [.. ids.Select(id => Member(id))];

    private static ImmutableArray<ContentError> ErrorsOf(string encounter, params string[] party)
        => Assert.Throws<ContentException>(() => Encounters.Setup(encounter, Party(party), Difficulty.Normal, Rules)).Errors;

    [Fact]
    public void Encounter_places_party_on_spawn_tiles()
    {
        var setup = Encounters.Setup("field_skirmish", [Member("kara"), Member("dov", "rogue"), Member("sef")], Difficulty.Hard, Rules);

        // party_spawn is (0,3) (0,4) (1,3) (1,4): the party takes the first three, in the order given.
        var party = setup.Units.Where(u => u.Sheet.Side == Side.Party).ToArray();
        Assert.Equal([("kara", new Tile(0, 3)), ("dov", new Tile(0, 4)), ("sef", new Tile(1, 3))],
            party.Select(u => (u.Id.Value, u.At)));
        Assert.All(party, u => Assert.IsType<PlayerControlled>(u.Controller));
        Assert.All(party, u => Assert.False(u.Protected));
        Assert.Equal(["kara", "dov", "sef"], party.Select(u => u.Sheet.Name));   // the default character is named after its id
        Assert.Equal([new ItemStack(new ItemId("potion"), 1)], party[0].Bag);   // the fighter's kit
        Assert.Equal(("field", Difficulty.Hard), (setup.Map.Id, setup.Difficulty));
        Assert.Same(Rules.MapFor("field"), setup.Map);
    }

    [Fact]
    public void Setup_carries_the_encounters_objectives_triggers_xp_and_loot()
    {
        var setup = Encounters.Setup("named_units", Party("kara"), Difficulty.Normal, Rules);

        Assert.Equal(400, setup.Xp);
        Assert.Equal(["common", "rare"], setup.Loot.Select(l => l.Value));
        Assert.Equal(ObjectiveExpr.Of("defeat:leader"), setup.Objectives.Win);
        Assert.Equal(ObjectiveExpr.Of(Objectives.PartyOut), setup.Objectives.Lose);
        Assert.Equal("howl", Assert.Single(setup.Triggers).Id);
        Assert.Equal(Rules.Encounters["named_units"].Triggers, setup.Triggers);
    }

    [Fact]
    public void Enemies_are_numbered_by_monster_in_placement_order_unless_they_name_an_id()
    {
        var setup = Encounters.Setup("field_skirmish", Party("kara"), Difficulty.Normal, Rules);

        // Two wolves on the first free enemy_spawn tiles, then one on (4, 4).
        Assert.Equal([("wolf.1", new Tile(5, 0)), ("wolf.2", new Tile(5, 1)), ("wolf.3", new Tile(4, 4))],
            setup.Units.Where(u => u.Sheet.Side == Side.Enemy).Select(u => (u.Id.Value, u.At)));

        var named = Encounters.Setup("named_units", Party("kara"), Difficulty.Normal, Rules);
        Assert.Equal(["kara", "alpha", "wolf.1", "wolf.2", "wolf.3", "pet"], named.Units.Select(u => u.Id.Value));
    }

    [Fact]
    public void Enemies_are_ai_controlled_with_the_overrides_the_placement_gives()
    {
        var setup = Encounters.Setup("named_units", Party("kara"), Difficulty.Normal, Rules);
        var alpha = setup.Units.Single(u => u.Id == U("alpha"));
        var zone = setup.Units.Single(u => u.Id == U("wolf.1"));

        Assert.Equal(new AiControlled("brute"), alpha.Controller);
        Assert.Equal(Side.Enemy, alpha.Sheet.Side);
        Assert.False(alpha.Protected);
        Assert.Equal(3, zone.Sheet.Level);                                  // "level": 3
        Assert.Equal("beasts.wolf.dire", zone.Sheet.Sprite);                // "variant": "dire"
        Assert.Equal(1, alpha.Sheet.Level);
        Assert.Equal("beasts.wolf.grey", alpha.Sheet.Sprite);
        Assert.Contains("leader", alpha.Sheet.Tags);                        // the tag objectives refer to
        Assert.Contains("wolf", setup.Units.Single(u => u.Id == U("wolf.3")).Sheet.Tags);    // by default the monster id
        Assert.Empty(alpha.Bag);
    }

    [Fact]
    public void Npcs_are_protected_allies()
    {
        var setup = Encounters.Setup("named_units", Party("kara"), Difficulty.Normal, Rules);
        var pet = setup.Units.Single(u => u.Id == U("pet"));

        Assert.True(pet.Protected);
        Assert.Equal(Side.Party, pet.Sheet.Side);
        Assert.Equal(new Tile(1, 1), pet.At);
        Assert.IsType<AiControlled>(pet.Controller);
    }

    [Fact]
    public void The_setup_starts_a_battle()
    {
        var setup = Encounters.Setup("field_skirmish", [Member("kara"), Member("dov", "rogue")], Difficulty.Normal, Rules);

        var state = Battle.Start(setup, Rules, 7).State;

        Assert.Equal(5, state.Units.Count);
        Assert.Equal(250, state.Xp);
        Assert.Equal(3, state.Units.Values.Count(u => u.Sheet.Side == Side.Enemy));
        Assert.Null(state.Outcome);
    }

    [Fact]
    public void Party_size_outside_the_encounters_limits_is_refused()
    {
        Assert.Throws<ArgumentException>(() => Encounters.Setup("field_skirmish", [], Difficulty.Normal, Rules));
        Assert.Throws<ArgumentException>(() => Encounters.Setup("field_skirmish", Party("a", "b", "c", "d", "e"), Difficulty.Normal, Rules));
        Assert.Throws<ArgumentException>(() => Encounters.Setup("field_skirmish", Party("a", "a"), Difficulty.Normal, Rules));
    }

    [Fact]
    public void Unknown_encounter_is_a_content_error()
        => Assert.Equal([new ContentError("encounter 'no_such'", "", "Unknown encounter 'no_such'.")], ErrorsOf("no_such", "kara"));

    [Fact]
    public void A_placement_onto_an_unwalkable_tile_is_a_content_error()
        => Assert.Equal([new ContentError("encounter 'on_crate'", "/enemies/1/at", "Tile (3, 2) can't be stood on.")],
            ErrorsOf("on_crate", "kara"));

    [Fact]
    public void A_placement_onto_an_occupied_tile_is_a_content_error()
        => Assert.Equal([new ContentError("encounter 'on_party'", "/enemies/0/at", "Tile (0, 3) is already taken by 'kara'.")],
            ErrorsOf("on_party", "kara"));

    [Fact]
    public void A_zone_without_enough_free_tiles_is_a_content_error()
        => Assert.Equal([new ContentError("encounter 'zone_too_small'", "/enemies/0/zone",
            "The zone 'enemy_spawn' has 3 free tiles for 4 units.")], ErrorsOf("zone_too_small", "kara"));

    [Fact]
    public void Fixed_tile_units_are_placed_before_zones_fill()
    {
        // The zone's first tile (5, 0) belongs to the wolf placed there, though it is listed after the zone's two wolves.
        var setup = Encounters.Setup("fixed_in_zone", Party("kara"), Difficulty.Normal, Rules);

        Assert.Equal([("wolf.1", new Tile(5, 1)), ("wolf.2", new Tile(5, 2)), ("wolf.3", new Tile(5, 0))],
            setup.Units.Where(u => u.Sheet.Side == Side.Enemy).Select(u => (u.Id.Value, u.At)));
    }

    [Fact]
    public void A_unit_id_that_collides_is_a_content_error()
    {
        // The party member's id is the one the encounter's enemy names.
        Assert.Equal([new ContentError("encounter 'named_units'", "/enemies/0/id", "Unit id 'alpha' is already in use.")],
            ErrorsOf("named_units", "alpha"));
    }
}
