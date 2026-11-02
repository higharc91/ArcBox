using System.Text.Json;
using ArcBox.Sdk;

namespace Tactics.Logic.Tests;

/// <summary>
/// Hidden things and passive checks (interactables design §7): hidden by any passive skill, notices and what they reveal, hidden paths, and
/// what an active Search looks for; and the loader's checks of each.
/// </summary>
public sealed class HiddenThingsTests
{
    private static readonly HashSet<string> NoFlags = [];

    private static InteractableSave S(InteractableDef d) => Interactables.Initial(d);

    private static Dictionary<string, InteractableSave> Saves(params (string Id, InteractableSave Save)[] saves)
        => saves.ToDictionary(s => s.Id, s => s.Save, StringComparer.Ordinal);

    // ---- Hidden by a passive skill ---------------------------------------------------------------------------------------------

    [Fact]
    public void Hidden_is_a_perception_dc_as_a_number_or_any_passive_skill_as_an_object()
    {
        var plain = Vault.Def("""{ "id": "c", "kind": "cache", "at": [1, 1], "hidden": 12 }""");
        Assert.Equal(new HiddenDef(12, "perception"), plain.Hidden);
        var arcane = Vault.Def("""{ "id": "c", "kind": "cache", "at": [1, 1], "hidden": { "dc": 14, "skill": "arcana" } }""");
        Assert.Equal(new HiddenDef(14, "arcana"), arcane.Hidden);
        Assert.Equal(new HiddenDef(9), Vault.Def("""{ "id": "c", "kind": "cache", "at": [1, 1], "hidden": { "dc": 9 } }""").Hidden);
        // A Perception one is written back as the plain number content has always used; another skill as the object.
        Assert.Equal("12", JsonSerializer.Serialize(plain.Hidden, ArcJson.Options));
        Assert.Equal("""{"dc":14,"skill":"arcana"}""", JsonSerializer.Serialize(arcane.Hidden, ArcJson.Options));
        HiddenDef implicitDc = 7;
        Assert.Equal("perception", implicitDc.Skill);
    }

    [Fact]
    public void Each_hidden_thing_is_found_by_its_own_skills_passive_within_its_own_range()
    {
        var rune = Vault.Def("""{ "id": "rune", "kind": "rune", "at": [4, 4], "hidden": { "dc": 14, "skill": "arcana" }, "text": ["A rune."] }""");
        var cache = Vault.Def("""{ "id": "cache", "kind": "cache", "at": [4, 5], "hidden": 14 }""");
        var far = Vault.Def("""{ "id": "far", "kind": "tracks", "at": [9, 4], "hidden": 5, "range": 6, "text": ["Tracks."] }""");
        var near = Vault.Def("""{ "id": "near", "kind": "tracks", "at": [6, 4], "hidden": 5, "range": 1, "text": ["Tracks."] }""");
        int Passive(string skill) => skill == "arcana" ? 15 : 12;
        var finds = Interactables.Finds([rune, cache, far, near], null, NoFlags, new Tile(4, 3), Passive);
        Assert.Equal([("far", Interactables.Finding.Noticed, "perception"), ("rune", Interactables.Finding.Noticed, "arcana")],
            finds.Select(f => (f.Def.Id, f.Found, f.Skill)));
        // The plain overload is every skill at one score, as passive Perception alone was.
        Assert.Equal(["cache", "far", "rune"], Interactables.Finds([rune, cache, far, near], null, NoFlags, new Tile(4, 3), 14).Select(f => f.Def.Id));
    }

    [Fact]
    public void A_passive_score_is_ten_plus_that_skills_bonus()
    {
        var sheet = TestSheets.Fighter.WithScore(Stat.Int, 16) with { Skills = ["arcana"] };
        Assert.Equal(13 + Stats.Proficiency(sheet.Level), Interactables.Passive(sheet, "arcana"));
        Assert.Equal(13, Interactables.Passive(sheet, "investigation"));
    }

    // ---- Notices ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_notice_is_always_hidden_never_in_the_way_drawn_once_noticed_and_looked_at_again()
    {
        var tracks = Vault.Def("""{ "id": "t", "kind": "tracks", "at": [2, 2], "hidden": 10, "text": ["Fresh wolf tracks lead north,", "into the trees."] }""");
        Assert.All(InteractableKinds.All.Values.Where(k => k.Family == InteractableFamily.Notice), k => Assert.True(k.MustHide, k.Id));
        Assert.Equal(["bloodstain", "carving", "disturbed_earth", "draft", "remains", "rune", "scratches", "tracks"],
            InteractableKinds.All.Values.Where(k => k.Family == InteractableFamily.Notice).Select(k => k.Id));
        var noticed = S(tracks) with { Found = true };
        Assert.Equal(InteractableKinds.Noticed, noticed.State);
        Assert.False(Interactables.Visible(tracks, S(tracks), NoFlags));
        Assert.True(Interactables.Visible(tracks, noticed, NoFlags));
        Assert.False(Interactables.Blocks(tracks, noticed, NoFlags));
        Assert.Equal("Look at the tracks", Interactables.Label(tracks, noticed, NoFlags, false));
        var plan = Assert.IsType<InstantPlan>(InteractScenes.Plan(tracks, noticed, NoFlags, false, i => i.Value));
        Assert.Null(plan.Outcome);
        Assert.Equal("Fresh wolf tracks lead north, into the trees.", plan.Line);
        var draft = Vault.Def("""{ "id": "d", "kind": "draft", "at": [2, 2], "hidden": 10, "text": ["Cold air."] }""");
        Assert.Equal("Feel the draft", Interactables.Label(draft, S(draft) with { Found = true }, NoFlags, false));
        Assert.True(Interactables.FoundIsItsOutcome(tracks));
    }

    [Fact]
    public void A_notice_reveals_its_hidden_things_and_theirs_in_turn_each_once_and_only_whats_there()
    {
        var tracks = Vault.Def("""{ "id": "tracks", "kind": "tracks", "at": [2, 2], "hidden": 10, "text": ["Tracks."], "reveals": ["blood", "cache"] }""");
        var blood = Vault.Def("""{ "id": "blood", "kind": "bloodstain", "at": [8, 8], "hidden": 25, "text": ["Blood."], "reveals": ["door", "tracks"] }""");
        var cache = Vault.Def("""{ "id": "cache", "kind": "cache", "at": [8, 7], "hidden": 30 }""");
        var door = Vault.Def("""{ "id": "door", "kind": "secret_door", "at": [4, 3], "hidden": 30, "requires": ["night"] }""");
        InteractableDef[] all = [tracks, blood, cache, door];
        Assert.Equal([("blood", Interactables.Finding.Noticed), ("cache", Interactables.Finding.Revealed)],
            Interactables.Revealing(tracks, all, null, NoFlags).Select(f => (f.Def.Id, f.Found)));
        // The door is there only at night; and what's found already isn't found again.
        Assert.Equal(["blood", "cache", "door"], Interactables.Revealing(tracks, all, null, new HashSet<string> { "night" }).Select(f => f.Def.Id));
        Assert.Equal(["blood", "door"], Interactables.Revealing(tracks, all, Saves(("cache", new("closed", Found: true))), new HashSet<string> { "night" })
            .Select(f => f.Def.Id));
        Assert.Empty(Interactables.Revealing(cache, all, null, NoFlags));
    }

    // ---- Hidden paths -----------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_hidden_path_is_in_the_way_unseen_until_its_found_and_then_its_ground_with_nothing_to_do()
    {
        var path = Vault.Def("""{ "id": "p", "kind": "hidden_path", "at": [4, 3], "facing": "sw", "hidden": { "dc": 12, "skill": "survival" } }""");
        var found = S(path) with { Found = true };
        Assert.Equal(InteractableKinds.FoundPath, found.State);
        Assert.True(Interactables.Blocks(path, S(path), NoFlags));
        Assert.False(Interactables.Visible(path, S(path), NoFlags));
        Assert.True(Interactables.Plugged(path, S(path), NoFlags));         // the TV draws it as what it hides in, as a secret door's wall
        Assert.False(Interactables.Plugged(path, found, NoFlags));
        Assert.False(Interactables.Blocks(path, found, NoFlags));
        Assert.True(Interactables.Visible(path, found, NoFlags));
        Assert.Null(Interactables.Label(path, found, NoFlags, false));
        Assert.Null(InteractScenes.Plan(path, found, NoFlags, false, i => i.Value, again: true));
        Assert.False(Interactables.Solid(path));
        Assert.Equal([path.At], Interactables.Blocking([path], null, NoFlags));
        Assert.Empty(Interactables.Blocking([path], Saves(("p", found)), NoFlags));
    }

    // ---- Search -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_search_looks_for_what_is_hidden_within_two_tiles_not_found_not_searched_and_not_underfoot()
    {
        InteractableDef Cache(string id, int x, int y) => Vault.Def($$"""{ "id": "{{id}}", "kind": "cache", "at": [{{x}}, {{y}}], "hidden": 20 }""");
        var (a, b, c, d, e) = (Cache("a", 6, 6), Cache("b", 2, 2), Cache("c", 7, 4), Cache("d", 4, 6), Cache("e", 5, 5));
        var chest = Vault.Def("""{ "id": "chest", "kind": "chest", "at": [5, 4] }""");
        var saves = Saves(("c", new("closed", Searched: true)), ("d", new("closed", Found: true)));
        Assert.Equal(["a"], Interactables.Searchable([a, b, c, d, e, chest], saves, NoFlags, new Tile(5, 5)).Select(t => t.Id));
    }

    [Fact]
    public void Searched_never_goes_back()
    {
        var cache = Vault.Def("""{ "id": "c", "kind": "cache", "at": [1, 1], "hidden": 9 }""");
        Assert.True(Interactables.Regressed(cache, new("closed", Searched: true), new("closed"), false));
        Assert.False(Interactables.Regressed(cache, new("closed"), new("closed", Searched: true), false));
    }

    [Fact]
    public void The_search_scene_routes_by_the_lowest_dc_it_looks_for_and_plays_the_same_with_nothing()
    {
        var scene = InteractScenes.SearchScene([15, 11, 20]);
        var check = scene.Node(scene.Start)!.Options.Single().Check!;
        Assert.Equal(("investigation", 11, "found", "nothing"), (check.Skill, check.Dc, check.Pass, check.Fail));
        var empty = InteractScenes.SearchScene([]);
        Assert.Equal(InteractScenes.NothingToFind, empty.Node(empty.Start)!.Options.Single().Check!.Dc);
        Assert.Equal(scene.Nodes.SelectMany(n => n.Lines).Select(l => l.Text), empty.Nodes.SelectMany(n => n.Lines).Select(l => l.Text));
        Assert.All(scene.Nodes.Where(n => n.Id != scene.Start), n => Assert.True(n.End));
    }

    // ---- The loader's checks -------------------------------------------------------------------------------------------------

    private const string File = "chapter-01/interactables/vault.json";

    private static IReadOnlyList<ContentError> ErrorsOf(params string[] things)
        => ValidationTests.ErrorsOfChapter(Vault.World(Vault.Region()), Vault.Things("vault", things));

    [Theory]
    [InlineData("""{ "id": "x", "kind": "cache", "at": [2, 2], "hidden": { "dc": 12, "skill": "stealth" } }""", "/interactables/0/hidden/skill", "isn't one of perception")]
    [InlineData("""{ "id": "x", "kind": "cache", "at": [2, 2], "hidden": { "dc": 31, "skill": "arcana" } }""", "/interactables/0/hidden", "must be 1 to 30")]
    [InlineData("""{ "id": "x", "kind": "cache", "at": [2, 2], "hidden": { "skill": "arcana" } }""", "/interactables/0/hidden", "needs its \"dc\"")]
    [InlineData("""{ "id": "x", "kind": "cache", "at": [2, 2], "hidden": { "dc": 12, "skil": "arcana" } }""", "/interactables/0/hidden", "Unknown key 'skil'")]
    [InlineData("""{ "id": "x", "kind": "cache", "at": [2, 2], "hidden": 12, "range": 7 }""", "/interactables/0/range", "must be 1 to 6")]
    [InlineData("""{ "id": "x", "kind": "chest", "at": [2, 2], "range": 2 }""", "/interactables/0/range", "neither hidden nor trapped")]
    [InlineData("""{ "id": "x", "kind": "tracks", "at": [2, 2], "text": ["Tracks."] }""", "/interactables/0/hidden", "always hidden")]
    [InlineData("""{ "id": "x", "kind": "tracks", "at": [2, 2], "hidden": 10 }""", "/interactables/0/text", "needs its words")]
    [InlineData("""{ "id": "x", "kind": "tracks", "at": [2, 2], "hidden": 10, "text": ["T."], "blocks": true }""", "/interactables/0/blocks", "never stands in the way")]
    [InlineData("""{ "id": "x", "kind": "tracks", "at": [2, 2], "hidden": 10, "text": ["T."], "reveals": ["x"] }""", "/interactables/0/reveals", "reveals itself")]
    [InlineData("""{ "id": "x", "kind": "chest", "at": [2, 2], "reveals": ["y"] }""", "/interactables/0/reveals", "Only a notice reveals")]
    [InlineData("""{ "id": "x", "kind": "hidden_path", "at": [4, 3], "facing": "sw" }""", "/interactables/0/hidden", "always hidden")]
    [InlineData("""{ "id": "x", "kind": "hidden_path", "at": [4, 3], "hidden": 10 }""", "/interactables/0/facing", "needs its facing")]
    [InlineData("""{ "id": "x", "kind": "hidden_path", "at": [4, 3], "facing": "sw", "hidden": 10, "requires": ["a"] }""", "/interactables/0/requires", "always there")]
    [InlineData("""{ "id": "x", "kind": "hidden_path", "at": [4, 3], "facing": "sw", "hidden": 10, "blocks": false }""", "/interactables/0/blocks", "depends on its state")]
    [InlineData("""{ "id": "x", "kind": "hidden_path", "at": [4, 3], "facing": "sw", "hidden": 10, "lock": { "pick": 10 } }""", "/interactables/0/lock", "can't be locked")]
    public void A_hidden_thing_the_rules_dont_allow_is_an_error(string thing, string pointer, string part)
    {
        var error = Assert.Single(ErrorsOf(thing));
        Assert.Equal((File, pointer), (error.File, error.Pointer));
        Assert.Contains(part, error.Message);
    }

    [Fact]
    public void A_notice_reveals_only_hidden_things_of_its_own_region()
    {
        var errors = ErrorsOf("""{ "id": "t", "kind": "tracks", "at": [2, 2], "hidden": 10, "text": ["T."], "reveals": ["nope", "chest", "cache"] }""",
            """{ "id": "chest", "kind": "chest", "at": [8, 1] }""", """{ "id": "cache", "kind": "cache", "at": [8, 8], "hidden": 25 }""");
        Assert.Equal(["'t' reveals 'chest', which isn't hidden: there's nothing to find.", "'t' reveals 'nope', which isn't in this region."],
            errors.Select(e => e.Message).Order());
        Assert.Equal(["/interactables/0/reveals/0", "/interactables/0/reveals/1"], errors.Select(e => e.Pointer).Order());
    }

    [Fact]
    public void A_region_with_notices_paths_and_skills_loads()
    {
        var pack = ValidationTests.LoadChapter(Vault.World(Vault.Region()), Vault.Things("vault",
            """{ "id": "tracks", "kind": "tracks", "at": [2, 2], "hidden": 10, "range": 5, "text": ["Fresh wolf tracks lead south."], "reveals": ["cache"], "sets": ["saw_tracks"] }""",
            """{ "id": "cache", "kind": "cache", "at": [8, 8], "hidden": { "dc": 18, "skill": "investigation" } }""",
            """{ "id": "rune", "kind": "rune", "at": [6, 2], "hidden": { "dc": 13, "skill": "arcana" }, "text": ["A rune."] }"""));
        var things = pack.InteractablesIn("vault");
        Assert.Equal(["cache"], things[0].Reveals);
        Assert.Equal(new HiddenDef(18, "investigation"), things[1].Hidden);
        Assert.Empty(things[1].Reveals);       // absent lists are empty, never default
    }

    [Fact]
    public void What_the_story_needs_cant_lie_beyond_a_hidden_path_that_may_never_be_found()
    {
        var region = Vault.Region() with { Spawns = new RegionSpawns(Vault.Start, new Tile(4, 4)) };
        var errors = ValidationTests.ErrorsOfChapter(Vault.World(region), Vault.Things("vault", """{ "id": "ford", "kind": "hidden_path", "at": [4, 3], "facing": "sw", "hidden": 12 }"""));
        Assert.StartsWith("The recovery spawn at (4, 4) is behind a passage that may never open ('ford')", Assert.Single(errors).Message);
        Assert.Contains("hidden door or path never found", errors[0].Message);
        // Behind it, things are still reachable as far as placement goes: with every way open, the path is open too.
        Assert.Single(ValidationTests.LoadChapter(Vault.World(Vault.Region()), Vault.Things("vault",
            """{ "id": "ford", "kind": "hidden_path", "at": [4, 3], "facing": "sw", "hidden": 12 }""", """{ "id": "chest", "kind": "chest", "at": [8, 5] }"""))
            .InteractablesIn("vault").Where(t => t.Id == "chest"));
    }
}
