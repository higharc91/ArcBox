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

    [Fact]
    public void A_pack_with_no_audio_manifest_has_no_clips()
        => Assert.Null(Load(("arcs.json", Arcs), ("scenes/one.json", Scenes)).ClipSeconds("l1"));

    [Theory]
    [InlineData("""[]""", "the manifest isn't a JSON object")]
    [InlineData("""{ "lines": [] }""", "the manifest has no 'lines' object")]
    [InlineData("""{ "lines": { "l1": { "main": 3 } } }""", "the line 'l1' has a 'main' that isn't a string")]
    [InlineData("""{ "lines": """, "can't be read as lore")]
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
    public void Two_scenes_sharing_an_id_are_refused_across_files()
    {
        var scene = SceneOf($$"""{ "id": "n", "lines": [{{Line}}], "end": true }""");
        var error = Assert.Throws<InvalidDataException>(() => Load(("arcs.json", Arcs), ("scenes/one.json", scene), ("scenes/two.json", scene)));

        Assert.Contains("two.json", error.Message, StringComparison.Ordinal);
        Assert.Contains("the scene 's' is already in the pack", error.Message, StringComparison.Ordinal);
    }
}
