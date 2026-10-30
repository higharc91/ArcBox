using System.Collections.Immutable;
using System.Text;

namespace Tactics.Logic.Tests;

public sealed class ValidationTests
{
    private const string Abilities = "chapter-01/abilities/chapter.json";

    private static string Base => ContentPaths.Fixture("minimal", "base");

    /// <summary>The errors from loading the minimal base pack plus the fixture's <c>chapter-01</c> pack.</summary>
    private static ImmutableArray<ContentError> ErrorsOf(string fixture, ArtManifest? art = null)
        => Assert.Throws<ContentException>(() => RulesPack.Load([Base, ContentPaths.Fixture(fixture, "chapter-01")], art)).Errors;

    [Fact]
    public void Unknown_block_key()
        => Assert.Equal([new ContentError(Abilities, "/abilities/0/effects/0/onHit/0", "Unknown effect block 'damgae'.")],
            ErrorsOf("unknown_block_key"));

    [Fact]
    public void Unresolved_condition_id()
        => Assert.Equal([new ContentError(Abilities, "/abilities/0/effects/0/onFail/0/apply", "Unknown condition 'stunnned'.")],
            ErrorsOf("unresolved_condition_id"));

    [Fact]
    public void Duplicate_without_replaces_names_both_files()
    {
        var error = Assert.Single(ErrorsOf("duplicate_without_replaces"));

        Assert.Equal(new ContentError(Abilities, "/abilities/1/id",
            "Duplicate ability 'test.strike': it is already defined at base/abilities/test.json#/abilities/0. "
            + "A later pack can change it only with \"replaces\": \"test.strike\"."), error);
        Assert.Equal("chapter-01/abilities/chapter.json#/abilities/1/id: Duplicate ability 'test.strike': it is already defined at "
            + "base/abilities/test.json#/abilities/0. A later pack can change it only with \"replaces\": \"test.strike\".",
            error.ToString());
    }

    [Fact]
    public void Class_missing_level_7_features()
        => Assert.Equal([new ContentError("chapter-01/classes/ranger.json", "/features", "Class 'ranger' has no features at level 7.")],
            ErrorsOf("class_missing_level_7"));

    [Fact]
    public void Subclass_missing_level_6()
        => Assert.Equal(
            [new ContentError("chapter-01/classes/rogue.json", "/subclasses/1/features", "Subclass 'rogue.shadow' has no features at level 6.")],
            ErrorsOf("subclass_missing_level_6"));

    [Fact]
    public void Missing_art_key()
    {
        var art = ArtManifests.Load(ContentPaths.ContentBase);

        Assert.Equal([new ContentError(Abilities, "/abilities/0/presentation/fx", "Unknown art key 'signature.no_such_flare'.")],
            ErrorsOf("missing_art_key", art));
        Assert.Contains("chapter.flare", RulesPack.Load([Base, ContentPaths.Fixture("missing_art_key", "chapter-01")])
            .Abilities.Keys.Select(k => k.Value));   // no art manifest: not checked
    }

    [Fact]
    public void Map_bytes_length_mismatch()
        => Assert.Equal(
            [new ContentError("chapter-01/maps/broken.json", "/tiles", "Expected 48 tile bytes for a 4x4 map (3 per tile), got 45.")],
            ErrorsOf("map_bytes_length_mismatch"));

    [Fact]
    public void New_block_values_are_checked()
        => Assert.Equal(
        [
            new ContentError(Abilities, "/abilities/0/effects/0/transform/hook",
                "Unknown named hook 'wild_shpe' (expected \"animate_dead\", \"counterspell\", \"polymorph\" or \"wild_shape\")."),
            new ContentError(Abilities, "/abilities/0/effects/1/transform/args", "'wild_shape' needs a \"monster\" id in its args."),
            new ContentError(Abilities, "/abilities/0/effects/2/extraAction/kind",
                "Unknown extra action 'reaction' (expected \"action\" or \"bonus\")."),
            new ContentError(Abilities, "/abilities/0/effects/3/turnOrder/op",
                "Unknown turn order op 'skip' (expected \"delay\", \"hasten\" or \"swap\")."),
            new ContentError(Abilities, "/abilities/0/effects/8/zone/concentration", Endless),
            new ContentError(Abilities, "/abilities/0/effects/9/statMod/concentration", Endless),
            new ContentError(Abilities, "/abilities/0/effects/10/summon/concentration", Endless),
            new ContentError(Abilities, "/abilities/1/effects/0/random/table/1/effects/0/damage/bonus/0",
                "Unknown addend 'mod.luck' (expected \"mod.<stat>\", \"mod.cast\", \"prof\" or \"level\")."),
            new ContentError(Abilities, "/abilities/1/effects/0/random/table/0/weight", "A weight must be at least 1."),
            new ContentError(Abilities, "/abilities/1/effects/1/zone/passives/0/hook", "Unknown hook 'onEnter'."),
            new ContentError(Abilities, "/abilities/1/effects/2/summon/count", "'count' must be at least 1."),
            new ContentError(Abilities, "/abilities/1/effects/3/chain/times", "'times' must be at least 1."),
            new ContentError(Abilities, "/abilities/0/effects/4/summon/monster", "Unknown monster 'wolff'."),
            new ContentError(Abilities, "/abilities/0/effects/5/transform/args/monster", "Unknown monster 'bear'."),
            new ContentError(Abilities, "/abilities/0/effects/6/transform/hook", "Unknown monster 'sheep'."),
            new ContentError(Abilities, "/abilities/0/effects/7/transform/hook", "Unknown monster 'skeleton'."),
        ], ErrorsOf("bad_effect_values"));

    private const string Endless =
        "'concentration' needs an ability that costs concentration (\"cost\": {\"concentration\": true}); otherwise it would never end.";

    [Fact]
    public void Collects_all_errors_not_just_first()
    {
        const string map = "chapter-01/maps/bad.json", monk = "chapter-01/classes/monk.json", encounter = "chapter-01/encounters/bad.json";
        ContentError[] expected =
        [
            new(Abilities, "/abilities/0/target/range", "Range 13 is over the limit of 12."),
            new(Abilities, "/abilities/0/target/area/size", "Area size 7 is over the limit of 6."),
            new(Abilities, "/abilities/0/effects/0/attack/kind", "Unknown attack kind 'melee' (expected \"weapon\" or \"spell\")."),
            new(Abilities, "/abilities/0/effects/0/onHit/0/damage/bonus/0",
                "Unknown addend 'mod.luck' (expected \"mod.<stat>\", \"mod.cast\", \"prof\" or \"level\")."),
            new(Abilities, "/abilities/1/effects/0/untilSave", "'untilSave' needs an enclosing \"save\" block."),
            new(Abilities, "/abilities/1/effects/1/damage/dice", "The by-level table doesn't cover level 1 (it starts at level 3)."),
            new(Abilities, "/abilities/2/id", "The id is empty."),
            new(Abilities, "/passives/0/hook", "Unknown hook 'onSunrise'."),
            new(map, "/tiles", "Tile (1, 0) uses material 4, but the map has 2 materials."),
            new(map, "/tiles", "Tile (2, 1) has undefined flag bits 0x40."),
            new(map, "/props/0/at", "Prop 'crate' at (5, 0) is outside the 3x2 map."),
            new(map, "/props/0/cover", "Cover 'total' isn't allowed in content (use \"none\", \"half\" or \"three_quarters\")."),
            new(map, "/zones/spawn/1", "Tile (3, 1) is outside the 3x2 map."),
            new(monk, "/features", "Class 'monk' has no features at level 7."),
            new(monk, "/features/1/0/passive", "Unknown passive 'monk.ki'."),
            new(encounter, "/enemies/0/unit", "Unknown monster 'goblin'."),
            new(encounter, "/enemies/0/zone", "The map 'field' has no zone 'nowhere'."),
            new(encounter, "/objectives/win", "No unit in this encounter has the tag 'mayor'."),
            new(encounter, "/triggers/0/when/unitEntersZone/tag", "No unit in this encounter has the tag 'ghosts'."),
            new(encounter, "/triggers/0/do/0/spawn/at", "The map 'field' has no zone 'cellar'."),
        ];

        Assert.Equal(Sorted(expected), Sorted(ErrorsOf("collects_all_errors")));
    }

    [Fact]
    public void Unknown_property()
        => Assert.Equal([new ContentError(Abilities, "/abilities/0/efects", "Unknown property 'efects'.")],
            ErrorsOf("unknown_property"));

    [Fact]
    public void Unknown_properties_are_errors_everywhere_in_content()
    {
        var errors = ErrorsOfChapter(
            ("races.json", """[ { "id": "orc", "name": "Orc", "size": "medium", "move": 4, "resist": [], "passives": [], "speed": 5 } ]"""),
            ("maps/m.json", """{ "id": "m", "size": [1, 1], "tiles": "AAAA", "materials": ["grass/lush"], "props": [ { "id": "crate", "at": [0, 0], "cver": "half" } ], "zones": {} }"""),
            ("encounters/e.json", """
                { "id": "e", "name": "E", "map": "field", "party": { "min": 1, "max": 4 }, "enemies": [ { "unit": "wolf", "at": [5, 4] } ],
                  "triggers": [ { "id": "t", "when": { "round": 2 }, "do": [ { "setObjective": { "win": { "anyof": ["defeat_all"] } } } ] } ] }
                """));

        Assert.Equal(
            [
                new ContentError("chapter-01/encounters/e.json", "/triggers/0/do/0/setObjective/win",
                    "Unknown objective key 'anyof' (expected \"any\" or \"all\")."),
                new ContentError("chapter-01/maps/m.json", "/props/0/cver", "Unknown property 'cver'."),
                new ContentError("chapter-01/races.json", "/0/speed", "Unknown property 'speed'."),
            ],
            Sorted(errors));
    }

    [Theory]
    [InlineData("""{ "win": null }""", "/triggers/0/do/0/setObjective/win", "The value is null.")]
    [InlineData("""{ "win": { "any": [null] } }""", "/triggers/0/do/0/setObjective/win/any/0", "A value is missing.")]
    public void A_null_in_a_trigger_objective_is_a_content_error(string setObjective, string pointer, string message)
    {
        var errors = ErrorsOfChapter(("encounters/e.json", $$"""
            { "id": "e", "name": "E", "map": "field", "party": { "min": 1, "max": 4 }, "enemies": [ { "unit": "wolf", "at": [5, 4] } ],
              "triggers": [ { "id": "t", "when": { "round": 2 }, "do": [ { "setObjective": {{setObjective}} } ] } ] }
            """));

        Assert.Equal([new ContentError("chapter-01/encounters/e.json", pointer, message)], errors);
    }

    [Fact]
    public void A_value_that_does_not_deserialise_is_a_content_error()
    {
        var errors = ErrorsOfChapter(("abilities/chapter.json", """
            { "abilities": [
                { "id": "chapter.far", "name": "Far", "tier": 0, "cost": { "action": 1 },
                  "target": { "kind": "enemy", "range": "far", "los": true, "area": { "shape": "single" } },
                  "effects": [ { "damage": { "dice": "2d" } } ] },
                { "id": "chapter.ok", "name": "Ok", "tier": 0, "cost": { "action": 1 },
                  "target": { "kind": "everyone", "range": 1, "los": true, "area": { "shape": "single" } },
                  "effects": [ { "heal": { "dice": "1d4" } } ] }
            ] }
            """));

        Assert.Equal(["/abilities/0/effects/0", "/abilities/1/target/kind"], errors.Select(e => e.Pointer));
        Assert.All(errors, e => Assert.Equal(Abilities, e.File));
        Assert.Contains("2d", errors[0].Message, StringComparison.Ordinal);
        Assert.Equal("The value has the wrong type, or isn't one of the allowed values.", errors[1].Message);
    }

    [Fact]
    public void A_file_that_is_not_json_is_a_content_error()
    {
        var error = Assert.Single(ErrorsOfChapter(("abilities/chapter.json", "{ \"abilities\": [ ")));

        Assert.Equal((Abilities, ""), (error.File, error.Pointer));
    }

    [Fact]
    public void Replaces_needs_an_earlier_definition_with_the_same_id()
    {
        var errors = ErrorsOfChapter(("abilities/chapter.json", """
            { "abilities": [
                { "id": "chapter.new", "replaces": "chapter.new", "name": "New", "tier": 0, "cost": { "action": 1 },
                  "target": { "kind": "self", "range": 0, "los": false, "area": { "shape": "self" } }, "effects": [] },
                { "id": "test.mend", "replaces": "test.strike", "name": "Mend", "tier": 0, "cost": { "action": 1 },
                  "target": { "kind": "self", "range": 0, "los": false, "area": { "shape": "self" } }, "effects": [] }
            ] }
            """));

        Assert.Equal(
            [
                new ContentError(Abilities, "/abilities/0/replaces", "'replaces' names 'chapter.new', but no earlier pack defines it."),
                new ContentError(Abilities, "/abilities/1/replaces", "'replaces' must be this definition's own id 'test.mend'."),
            ],
            Sorted(errors));
    }

    [Fact]
    public void Subclasses_pending_skips_the_subclass_checks()
    {
        var pack = LoadChapter(("classes/druid.json", ClassJson("druid", subclasses: "\"subclassesPending\": true")));
        Assert.Contains("druid", pack.Classes.Keys);

        var error = Assert.Single(ErrorsOfChapter(("classes/druid.json", ClassJson("druid", subclasses: "\"subclasses\": []"))));
        Assert.Equal(new ContentError("chapter-01/classes/druid.json", "/subclasses", "Class 'druid' has no subclasses."), error);
    }

    [Fact]
    public void Missing_required_values_are_reported_and_absent_lists_are_empty()
    {
        var errors = ErrorsOfChapter(("items.json", """[ { "id": "rope", "use": "test.mend" } ]"""));
        Assert.Equal([new ContentError("chapter-01/items.json", "/0/name", "'name' is missing.")], errors);
        Assert.Equal([new ContentError("chapter-01/items.json", "/0/tags", "A value is missing.")],
            ErrorsOfChapter(("items.json", """[ { "id": "rope", "name": "Rope", "use": "test.mend", "tags": [null] } ]""")));

        var pack = LoadChapter(("items.json", """[ { "id": "rope", "name": "Rope", "use": "test.mend" } ]"""));
        Assert.Empty(pack.Items[new ItemId("rope")].Tags);
        Assert.False(pack.Passives["test.tough"].Effects.IsDefault);
    }

    [Fact]
    public void Packs_must_resolve_their_requirements()
    {
        var error = Assert.Single(ErrorsOfChapter([], requires: "\"base\", \"chapter-00\""));

        Assert.Equal(new ContentError("chapter-01/manifest.json", "/requires/1",
            "Pack 'chapter-01' requires 'chapter-00', which isn't loaded."), error);
    }

    [Fact]
    public void Listed_files_must_exist_and_be_a_known_kind()
    {
        var errors = ErrorsOfChapter([("notes.json", "{}")], extraFiles: ["notes.json", "abilities/missing.json"]);

        Assert.Equal(
            [
                new ContentError("chapter-01/manifest.json", "/files/0",
                    "'notes.json' isn't a known content file (see rules/schema for the file kinds)."),
                new ContentError("chapter-01/manifest.json", "/files/1", "The file 'abilities/missing.json' doesn't exist."),
            ],
            errors);
    }

    [Fact]
    public void Content_error_to_string_is_file_hash_pointer_colon_message()
        => Assert.Equal("base/x.json#/a/0: Bad.", new ContentError("base/x.json", "/a/0", "Bad.").ToString());

    private static ContentError[] Sorted(IEnumerable<ContentError> errors)
        => [.. errors.OrderBy(e => e.ToString(), StringComparer.Ordinal)];

    private static string ClassJson(string id, string subclasses) => $$"""
        { "id": "{{id}}", "name": "X", "role": "control", "hitDie": 8, "saves": ["int", "wis"], "armorProf": [], "weaponProf": [],
          "skillChoices": { "count": 2, "from": ["arcana"] }, "startingKit": { "weapon": "staff", "armor": "none" },
          "resources": {},
          "features": { "1": [ { "ability": "test.mend" } ], "2": [ { "attacks": 1 } ], "3": [ { "attacks": 1 } ],
                        "4": [ { "attacks": 1 } ], "5": [ { "attacks": 1 } ], "6": [ { "attacks": 1 } ], "7": [ { "attacks": 1 } ],
                        "8": [ { "attacks": 1 } ], "9": [ { "attacks": 1 } ], "10": [ { "attacks": 1 } ] },
          {{subclasses}},
          "aiTags": { "tags": [], "priority": ["int", "wis", "con", "dex", "str", "cha"] } }
        """;

    private static RulesPack LoadChapter(params (string Path, string Json)[] files)
        => WithChapter(files, null, [], dir => RulesPack.Load([Base, dir]));

    private static ImmutableArray<ContentError> ErrorsOfChapter(params (string Path, string Json)[] files)
        => ErrorsOfChapter(files, requires: null);

    private static ImmutableArray<ContentError> ErrorsOfChapter((string Path, string Json)[] files, string? requires = null,
        string[]? extraFiles = null)
        => WithChapter(files, requires, extraFiles ?? [],
            dir => Assert.Throws<ContentException>(() => RulesPack.Load([Base, dir])).Errors);

    /// <summary>Writes a temporary <c>chapter-01</c> pack holding <paramref name="files"/> and runs <paramref name="load"/> on it.</summary>
    private static T WithChapter<T>((string Path, string Json)[] files, string? requires, string[] extraFiles, Func<string, T> load)
    {
        var root = Path.Combine(Path.GetTempPath(), "tactics-validation-" + Guid.NewGuid().ToString("N"));
        var dir = Path.Combine(root, "chapter-01");
        try
        {
            Directory.CreateDirectory(dir);
            var listed = extraFiles.Length > 0 ? extraFiles : files.Select(f => f.Path).ToArray();
            File.WriteAllText(Path.Combine(dir, "manifest.json"),
                $$"""{ "id": "chapter-01", "rulesVersion": 1, "requires": [{{requires ?? "\"base\""}}], "files": [{{string.Join(", ", listed.Select(f => $"\"{f}\""))}}] }""",
                new UTF8Encoding(false));
            foreach (var (path, json) in files)
            {
                var full = Path.Combine(dir, path);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, json, new UTF8Encoding(false));
            }
            return load(dir);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
