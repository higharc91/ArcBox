namespace Tactics.Logic.Tests;

/// <summary>How <see cref="LoreBook"/> reads scenes, characters and clips, on small packs written for each test.</summary>
public sealed class SceneLoadTests
{
    private const string Arcs = """
        { "arcs": [{ "id": "a", "chapter": 1, "region": "r", "beats": [
            { "id": "talk", "kind": "story", "place": "inn" },
            { "id": "fight", "kind": "battle", "place": "yard", "encounter": "e" } ] }] }
        """;

    private const string Characters = """
        { "characters": [{ "id": "mara", "name": "Mara Venn", "preset": "mayor", "role": "ally", "bio": "ignored" }] }
        """;

    /// <summary>A scene with one of everything: a tagged option, a check, a gated vote with a choice, sets, and an aftermath.</summary>
    private const string Scenes = """
        { "scenes": [
          { "id": "s1", "beat": "talk", "title": "Talk", "place": "yard", "requires": ["x"], "unless": ["y"], "cast": ["narrator", "mara"], "start": "open",
            "nodes": [
              { "id": "open", "lines": [{ "id": "l1", "speaker": "narrator", "text": "Hello." }, { "id": "l2", "speaker": "mara", "text": "Well?", "mood": "angry", "direction": "never shown" }],
                "sets": ["met"],
                "options": [
                  { "id": "ask", "label": "Ask.", "to": "vote", "tags": { "background": ["sage"], "class": ["wizard"] } },
                  { "id": "look", "label": "Look.", "check": { "skill": "perception", "dc": 12, "pass": "vote", "fail": "done" } } ] },
              { "id": "vote", "lines": [{ "id": "l3", "speaker": "mara", "text": "Choose." }], "vote": true,
                "options": [{ "id": "go", "label": "Go.", "to": "done", "choice": "c1", "sets": ["went"], "requires": ["met"] }] },
              { "id": "done", "lines": [{ "id": "l4", "speaker": "narrator", "text": "Done." }], "end": true } ] },
          { "id": "s2", "beat": "fight", "after": true, "cast": ["narrator"], "start": "n", "nodes": [{ "id": "n", "lines": [{ "id": "l5", "speaker": "narrator", "text": "After." }], "next": "m" },
            { "id": "m", "lines": [{ "id": "l6", "speaker": "narrator", "text": "Then." }], "end": true }] } ] }
        """;

    /// <summary>Writes a lore pack listing <paramref name="files"/> (path, contents) and loads it.</summary>
    private static LoreBook Load(params (string Path, string Json)[] files) => WithPack(files, LoreBook.Load);

    private static T WithPack<T>((string Path, string Json)[] files, Func<string, T> load)
    {
        var pack = Directory.CreateTempSubdirectory("tactics-scenes-test-");
        try
        {
            var lore = Path.Combine(pack.FullName, "lore");
            Directory.CreateDirectory(Path.Combine(lore, "scenes"));
            Directory.CreateDirectory(Path.Combine(lore, "audio"));
            var listed = string.Join(", ", files.Where(f => !f.Path.StartsWith("audio/", StringComparison.Ordinal)).Select(f => $"\"{f.Path}\""));
            File.WriteAllText(Path.Combine(lore, "pack.json"), $$"""{ "id": "base", "loreVersion": 1, "files": [{{listed}}] }""");
            foreach (var (path, json) in files)
                File.WriteAllText(Path.Combine(lore, path), json);
            return load(pack.FullName);
        }
        finally
        {
            pack.Delete(recursive: true);
        }
    }

    [Fact]
    public void A_scene_reads_as_it_is_written()
    {
        var book = Load(("arcs.json", Arcs), ("characters.json", Characters), ("scenes/one.json", Scenes));

        var scene = Assert.Single(book.ScenesOf("talk"));
        Assert.Equal(("s1", "Talk", "yard", "open", false), (scene.Id, scene.Title, scene.Place, scene.Start, scene.After));
        Assert.Equal(["x"], scene.Requires);
        Assert.Equal(["y"], scene.Unless);
        Assert.Equal(["narrator", "mara"], scene.Cast);

        var open = scene.Node("open")!;
        Assert.Equal(new SceneLine("l1", "narrator", "Hello.", "idle"), open.Lines[0]);
        Assert.Equal(new SceneLine("l2", "mara", "Well?", "angry"), open.Lines[1]);
        Assert.Equal(["met"], open.Sets);
        Assert.False(open.Vote);
        Assert.Equal(["sage"], open.Options[0].Tags.Background);
        Assert.Equal(["wizard"], open.Options[0].Tags.Class);
        Assert.Empty(open.Options[0].Tags.Race);
        Assert.Equal(new SceneCheck("perception", 12, "vote", "done"), open.Options[1].Check);
        Assert.Null(open.Options[1].To);
        Assert.Equal(["vote", "vote", "done"], open.Targets);

        var go = Assert.Single(scene.Node("vote")!.Options);
        Assert.True(scene.Node("vote")!.Vote);
        Assert.Equal(("done", "c1", false), (go.To, go.Choice, go.Tags.Any));
        Assert.Equal(["went"], go.Sets);
        Assert.Equal(["met"], go.Requires);
        Assert.True(scene.Node("done")!.End);
        Assert.Null(scene.Node("nowhere"));

        var after = Assert.Single(book.ScenesOf("fight"));
        Assert.True(after.After);
        Assert.Null(after.Place);
        Assert.Equal("m", after.Node("n")!.Next);
        Assert.Equal(["s1", "s2"], book.Scenes.Select(s => s.Id));
        Assert.Empty(book.ScenesOf("nothing"));

        Assert.Equal(new LoreCharacter("mara", "Mara Venn", "mayor", "ally"), book.Character("mara"));
        Assert.Null(book.Character("narrator"));
    }

    /// <summary>What 5c plays from today: a pack whose lore lists no scenes has its arcs, and nothing else changes.</summary>
    [Fact]
    public void A_pack_with_no_scenes_keeps_its_arcs_and_has_none()
    {
        var book = Load(("arcs.json", Arcs));

        Assert.Equal(["talk", "fight"], Assert.Single(book.Arcs).Beats.Select(b => b.Id));
        Assert.Empty(book.Scenes);
        Assert.Empty(book.ScenesOf("talk"));
        Assert.Null(book.Character("mara"));
        Assert.Null(book.ClipSeconds("l1"));
        Assert.Empty(LoreBook.Empty.Scenes);
        Assert.Null(LoreBook.Empty.ClipSeconds("l1"));
    }

    /// <summary>
    /// Review (rules NOTE 2 and 3): what a game can play but would never show, which <c>tools/tactics-lore/check.py</c> refuses as well. An
    /// option back to its own node is never offered (<see cref="SceneRun.Offered"/> counts the node as played), and a node whose every option
    /// waits on flags can be left with nothing, unless one vote's choices each set a flag one of them waits on.
    /// </summary>
    [Fact]
    public void A_scene_that_can_never_show_an_option_is_warned_of()
    {
        const string arcs = """
            { "arcs": [{ "id": "a", "chapter": 1, "region": "r", "beats": [
                { "id": "pick", "kind": "choice", "choices": [{ "id": "c1", "sets": ["left"] }, { "id": "c2", "sets": ["right"] }] },
                { "id": "talk", "kind": "story" } ] }] }
            """;
        const string scenes = """
            { "scenes": [{ "id": "s", "beat": "talk", "cast": ["narrator"], "start": "hub", "nodes": [
                { "id": "hub", "lines": [{ "id": "l1", "speaker": "narrator", "text": "Hub." }], "options": [
                  { "id": "again", "label": "Again.", "to": "hub" },
                  { "id": "try", "label": "Try.", "check": { "skill": "athletics", "dc": 10, "pass": "sides", "fail": "hub" } },
                  { "id": "both", "label": "Both.", "to": "hub", "check": { "skill": "athletics", "dc": 10, "pass": "sides", "fail": "done" } },
                  { "id": "on", "label": "On.", "to": "sides" } ] },
                { "id": "sides", "lines": [{ "id": "l2", "speaker": "narrator", "text": "Which?" }], "options": [
                  { "id": "l", "label": "Left.", "to": "gated", "requires": ["left"] },
                  { "id": "r", "label": "Right.", "to": "gated", "requires": ["right"] } ] },
                { "id": "gated", "lines": [{ "id": "l3", "speaker": "narrator", "text": "Maybe." }], "options": [
                  { "id": "m", "label": "Maybe.", "to": "done", "requires": ["left", "maybe"] } ] },
                { "id": "done", "lines": [{ "id": "l4", "speaker": "narrator", "text": "Done." }], "end": true } ] }] }
            """;

        var book = Load(("arcs.json", arcs), ("scenes/one.json", scenes));

        Assert.Equal(
        [
            "the option 'again' of the node 'hub' of the scene 's' leads back to its own node, so it's never offered.",
            "the option 'try' of the node 'hub' of the scene 's' leads back to its own node, so it's never offered.",
            "the option 'both' of the node 'hub' of the scene 's' leads back to its own node, so it's never offered.",     // its 'to', beside a check
            "the node 'gated' of the scene 's' has every option gated on flags, and no vote guarantees one of them is set: the party could be left with nothing to pick.",
        ], book.Warnings.Select(w => w[(w.IndexOf(": ", StringComparison.Ordinal) + 2)..]));
        Assert.All(book.Warnings, w => Assert.Contains("one.json", w, StringComparison.Ordinal));      // each names its file
        Assert.Empty(LoreBook.Empty.Warnings);
    }

    /// <summary>
    /// Review A6: an option with both a <c>to</c> and a check names all three nodes, as <c>check.py</c> counts them, so the two agree on what
    /// it leads to; where it goes is still the check's to decide.
    /// </summary>
    [Fact]
    public void An_options_to_beside_a_check_is_one_of_its_targets()
    {
        var option = new SceneOption("o", "O.", "side", null, [], [], OptionTags.None, new SceneCheck("athletics", 10, "pass", "fail"));

        Assert.Equal(["side", "pass", "fail"], option.Targets);
        Assert.Equal(("pass", "fail"), (SceneRun.Target(option, passed: true), SceneRun.Target(option, passed: false)));
        Assert.Equal(["pass", "fail"], (option with { To = null }).Targets);
        Assert.Equal(["side"], (option with { Check = null }).Targets);
    }

    /// <summary>Scenes with no arcs to play them aren't read: as before, a pack with no <c>arcs.json</c> is the empty book.</summary>
    [Fact]
    public void Scenes_without_arcs_are_the_empty_book()
        => Assert.Same(LoreBook.Empty, Load(("scenes/one.json", "not even json")));

    [Fact]
    public void A_clip_length_comes_from_the_manifest_and_the_file()
    {
        const string manifest = """
            { "lines": { "l1": { "main": "l1.ogg" }, "l2": { "main": "missing.ogg" }, "l3": { "main": "bad.ogg" }, "l4": { "low": "l4.ogg" },
                         "bark": { "main": "l1.ogg" } } }
            """;
        var lengths = WithPack([("arcs.json", Arcs), ("scenes/one.json", Scenes), ("audio/manifest.json", manifest)], dir =>
        {
            var audio = Path.Combine(dir, "lore", "audio");
            File.WriteAllBytes(Path.Combine(audio, "l1.ogg"), ClipLengthTests.Clip(24000, 36000));
            File.WriteAllBytes(Path.Combine(audio, "bad.ogg"), "nope"u8.ToArray());
            File.WriteAllBytes(Path.Combine(audio, "l4.ogg"), ClipLengthTests.Clip(24000, 36000));
            var book = LoreBook.Load(dir);
            return new[] { "l1", "l2", "l3", "l4", "l5", "bark" }.Select(book.ClipSeconds).ToArray();
        });

        // Only a scene line's own voice has a length: a missing or bad file, a player voice set's clip and a bark have none.
        Assert.Equal([1.5, null, null, null, null, null], lengths);
    }

    /// <summary>Loading reads only the manifest: a clip is read the first time its line asks, and kept.</summary>
    [Fact]
    public void Load_doesnt_open_a_clip_until_its_line_asks()
    {
        const string manifest = """{ "lines": { "l1": { "main": "l1.ogg" }, "l2": { "main": "l2.ogg" } } }""";
        var lengths = WithPack([("arcs.json", Arcs), ("scenes/one.json", Scenes), ("audio/manifest.json", manifest)], dir =>
        {
            var audio = Path.Combine(dir, "lore", "audio");
            var l1 = Path.Combine(audio, "l1.ogg");
            File.WriteAllBytes(l1, "not a clip yet"u8.ToArray());
            var book = LoreBook.Load(dir);

            // Written after Load: had Load read the clips, l1 would be the bad file and l2 missing.
            File.WriteAllBytes(l1, ClipLengthTests.Clip(24000, 36000));
            File.WriteAllBytes(Path.Combine(audio, "l2.ogg"), ClipLengthTests.Clip(24000, 48000));
            var first = (book.ClipSeconds("l1"), book.ClipSeconds("l2"));
            File.Delete(l1);
            return (first, Again: book.ClipSeconds("l1"));
        });

        Assert.Equal(((double?)1.5, (double?)2.0), lengths.first);
        Assert.Equal(1.5, lengths.Again);
    }

    [Fact]
    public void A_pack_with_no_audio_manifest_has_no_clips()
        => Assert.Null(Load(("arcs.json", Arcs), ("scenes/one.json", Scenes)).ClipSeconds("l1"));

    [Theory]
    [InlineData("""[]""", "the manifest isn't a JSON object")]
    [InlineData("""{ "lines": [] }""", "the manifest has no 'lines' object")]
    [InlineData("""{ "lines": { "l1": { "main": 3 } } }""", "the line 'l1' has a 'main' that isn't a string")]
    [InlineData("""{ "lines": """, "can't be read as lore")]
    [InlineData("""{ "lines": { "l1": { "main": "../secret.ogg" } } }""", "the line 'l1' names the clip '../secret.ogg', which isn't in the audio folder")]
    [InlineData("""{ "lines": { "l1": { "main": "clips/../../secret.ogg" } } }""", "isn't in the audio folder")]
    [InlineData("""{ "lines": { "l1": { "main": "..\\secret.ogg" } } }""", "isn't in the audio folder")]
    [InlineData("""{ "lines": { "l1": { "main": "/etc/secret.ogg" } } }""", "isn't in the audio folder")]
    public void An_audio_manifest_that_cant_be_read_is_refused_naming_it(string manifest, string problem)
    {
        var error = Assert.Throws<InvalidDataException>(() => Load(("arcs.json", Arcs), ("scenes/one.json", Scenes), ("audio/manifest.json", manifest)));

        Assert.Contains("manifest.json", error.Message, StringComparison.Ordinal);
        Assert.Contains(problem, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "characters": [{ "id": "mara", "name": "Mara", "role": "ally" }] }""", "the character 'mara' has no 'preset'")]
    [InlineData("""{ "characters": [{ "id": "mara", "name": "M", "preset": "p", "role": "ally" }, { "id": "mara", "name": "M", "preset": "p", "role": "ally" }] }""", "the character 'mara' is listed twice")]
    [InlineData("""{ "characters": {} }""", "the file has a 'characters' that isn't a list")]
    public void Characters_that_cant_be_read_are_refused_naming_the_file(string characters, string problem)
    {
        var error = Assert.Throws<InvalidDataException>(() => Load(("arcs.json", Arcs), ("characters.json", characters)));

        Assert.Contains("characters.json", error.Message, StringComparison.Ordinal);
        Assert.Contains(problem, error.Message, StringComparison.Ordinal);
    }

    private const string Line = """{ "id": "l", "speaker": "narrator", "text": "t" }""";

    private static string SceneOf(string nodes, string start = "n", string beat = "talk", string id = "s")
        => $$"""{ "scenes": [{ "id": "{{id}}", "beat": "{{beat}}", "cast": ["narrator"], "start": "{{start}}", "nodes": [{{nodes}}] }] }""";

    public static TheoryData<string, string> BadScenes => new()
    {
        { SceneOf($$"""{ "id": "n", "lines": [{{Line}}], "end": true }""", beat: "nope"), "the scene 's' plays the beat 'nope', which no arc has" },
        { SceneOf($$"""{ "id": "n", "lines": [{{Line}}], "end": true }""", start: "gone"), "the scene 's' starts at 'gone', which isn't one of its nodes" },
        { SceneOf($$"""{ "id": "n", "lines": [{{Line}}], "next": "n" }, { "id": "n", "lines": [{{Line}}], "end": true }"""), "the scene 's' has the node 'n' twice" },
        { SceneOf($$"""{ "id": "n", "lines": [{{Line}}], "next": "gone" }"""), "the node 'n' leads to 'gone', which isn't one of its nodes" },
        { SceneOf($$"""{ "id": "n", "lines": [{{Line}}], "options": [{ "id": "o", "label": "O", "to": "gone" }] }"""), "the node 'n' leads to 'gone'" },
        { SceneOf($$"""{ "id": "n", "lines": [{{Line}}], "options": [{ "id": "o", "label": "O", "check": { "skill": "arcana", "dc": 10, "pass": "n", "fail": "gone" } }] }"""), "the node 'n' leads to 'gone'" },
        { SceneOf($$"""{ "id": "n", "lines": [{{Line}}] }"""), "the node 'n' of the scene 's' needs exactly one of 'options', 'next' or 'end'" },
        { SceneOf($$"""{ "id": "n", "lines": [{{Line}}], "next": "n", "end": true }"""), "needs exactly one of 'options', 'next' or 'end'" },
        { SceneOf($$"""{ "id": "n", "lines": [{{Line}}], "end": false }"""), "needs exactly one of 'options', 'next' or 'end'" },
        { SceneOf($$"""{ "id": "n", "lines": [{{Line}}], "options": [] }"""), "the node 'n' of the scene 's' has an empty 'options' list" },
        { SceneOf("""{ "id": "n", "lines": [], "end": true }"""), "the node 'n' of the scene 's' has no lines" },
        { SceneOf("""{ "id": "n", "end": true }"""), "the node 'n' of the scene 's' has no 'lines' list" },
        { SceneOf("""{ "id": "n", "lines": [{ "id": "l", "text": "t" }], "end": true }"""), "the line 'l' has no 'speaker'" },
        { SceneOf("""{ "id": "n", "lines": [{ "id": "l", "speaker": "narrator", "text": "t", "mood": 4 }], "end": true }"""), "the line 'l' has a 'mood' that isn't a string" },
        { SceneOf($$"""{ "id": "n", "lines": [{{Line}}], "options": [{ "id": "o", "label": "O" }] }"""), "the option 'o' of the node 'n' of the scene 's' needs 'to' or a 'check'" },
        { SceneOf($$"""{ "id": "n", "lines": [{{Line}}], "options": [{ "id": "o", "to": "n" }] }"""), "the option 'o' of the node 'n' of the scene 's' has no 'label'" },
        { SceneOf($$"""{ "id": "n", "lines": [{{Line}}], "options": [{ "id": "o", "label": "O", "check": { "skill": "arcana", "dc": "hard", "pass": "n", "fail": "n" } }] }"""), "needs a whole number for 'dc'" },
        { SceneOf($$"""{ "id": "n", "lines": [{{Line}}], "options": [{ "id": "o", "label": "O", "to": "n", "tags": { "race": "elf" } }] }"""), "has a 'race' that isn't a list" },
        { SceneOf($$"""{ "id": "n", "lines": [{{Line}}], "vote": "yes", "options": [{ "id": "o", "label": "O", "to": "n" }] }"""), "has a 'vote' that isn't true or false" },
        { SceneOf($$"""{ "id": "n", "lines": [{{Line}}], "end": true }""").Replace("\"start\"", "\"after\": 1, \"start\"", StringComparison.Ordinal), "the scene 's' has a 'after' that isn't true or false" },
        { """{ "scenes": [{ "beat": "talk" }] }""", "a scene has no 'id'" },
        { SceneOf($$"""{ "id": "n", "lines": [{{Line}}], "end": true }""").Replace("\"start\"", "\"after\": true, \"start\"", StringComparison.Ordinal),
            "the scene 's' is an aftermath, and only a battle beat has one: 'talk' is a story beat" },
        { SceneOf($$"""{ "id": "n", "lines": [{{Line}}], "options": [{ "id": "o", "label": "O", "check": { "skill": "juggling", "dc": 10, "pass": "n", "fail": "n" } }] }"""),
            "the check of the option 'o' of the node 'n' of the scene 's' is of 'juggling', which isn't a skill" },
        { """{ "scenes": {} }""", "the file has a 'scenes' that isn't a list" },
        { """{ "scenes": [ """, "can't be read as lore" },
    };

    [Theory]
    [MemberData(nameof(BadScenes))]
    public void A_scene_that_cant_be_played_is_refused_naming_the_file(string scenes, string problem)
    {
        var error = Assert.Throws<InvalidDataException>(() => Load(("arcs.json", Arcs), ("scenes/bad.json", scenes)));

        Assert.Contains("bad.json", error.Message, StringComparison.Ordinal);
        Assert.Contains(problem, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_aftermath_before_a_lead_in_is_refused_across_files()
    {
        var after = SceneOf($$"""{ "id": "n", "lines": [{{Line}}], "end": true }""", beat: "fight", id: "won").Replace("\"start\"", "\"after\": true, \"start\"", StringComparison.Ordinal);
        var lead = SceneOf($$"""{ "id": "n", "lines": [{ "id": "l2", "speaker": "narrator", "text": "t" }], "end": true }""", beat: "fight", id: "late");

        var error = Assert.Throws<InvalidDataException>(() => Load(("arcs.json", Arcs), ("scenes/one.json", after), ("scenes/two.json", lead)));

        Assert.Contains("two.json", error.Message, StringComparison.Ordinal);
        Assert.Contains("the scene 'late' is a lead-in of 'fight', and comes after one of its aftermath scenes", error.Message, StringComparison.Ordinal);
        Assert.Equal(["late", "won"], Load(("arcs.json", Arcs), ("scenes/one.json", lead), ("scenes/two.json", after)).ScenesOf("fight").Select(s => s.Id));
    }

    [Fact]
    public void Two_scenes_sharing_an_id_are_refused_across_files()
    {
        var scene = SceneOf($$"""{ "id": "n", "lines": [{{Line}}], "end": true }""");
        var error = Assert.Throws<InvalidDataException>(() => Load(("arcs.json", Arcs), ("scenes/one.json", scene), ("scenes/two.json", scene)));

        Assert.Contains("two.json", error.Message, StringComparison.Ordinal);
        Assert.Contains("the scene 's' is already in the pack", error.Message, StringComparison.Ordinal);
    }
    [Fact]
    public void A_place_is_named_by_the_regions_file()
    {
        const string regions = """
            { "regions": [{ "id": "r", "name": "R", "places": [{ "id": "inn", "name": "The Last Coal", "kind": "town" }, { "id": "yard", "name": "The Yard" }] }] }
            """;
        var book = Load(("arcs.json", Arcs), ("regions.json", regions));

        Assert.Equal("The Last Coal", book.PlaceName("inn"));
        Assert.Equal("The Yard", book.PlaceName("yard"));
        Assert.Null(book.PlaceName("moon"));
        Assert.Null(Load(("arcs.json", Arcs)).PlaceName("inn"));       // no regions file, no names
    }

    [Fact]
    public void A_place_with_no_name_is_refused()
    {
        const string regions = """{ "regions": [{ "id": "r", "places": [{ "id": "inn" }] }] }""";

        var error = Assert.Throws<InvalidDataException>(() => Load(("arcs.json", Arcs), ("regions.json", regions)));

        Assert.Contains("regions.json", error.Message, StringComparison.Ordinal);
        Assert.Contains("the place 'inn' has no 'name'", error.Message, StringComparison.Ordinal);
    }
}
