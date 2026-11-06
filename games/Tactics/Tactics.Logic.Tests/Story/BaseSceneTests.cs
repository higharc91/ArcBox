using System.Collections.Immutable;
using System.Text.Json;

namespace Tactics.Logic.Tests;

/// <summary>Every scene of the base pack, all five chapters, as <see cref="LoreBook"/> reads them: whole, voiced, drawn and playable.</summary>
public sealed class BaseSceneTests
{
    private static LoreBook Book => Campaign.Book.Value;

    private static string Lore(params string[] parts) => Path.Combine([ContentPaths.ContentBase, "lore", .. parts]);

    private static ArcBeat BeatOf(SceneDef scene) => Book.Arcs.SelectMany(a => a.Beats).Single(b => b.Id == scene.Beat);

    private static int ChapterOf(SceneDef scene) => Book.Arcs.Single(a => a.Beats.Any(b => b.Id == scene.Beat)).Chapter;

    [Fact]
    public void Chapter_1_has_8_beats_and_13_scenes()
    {
        var arc = Book.Arcs.Single(a => a.Chapter == 1 && !a.SideAdventure);

        Assert.Equal(8, arc.Beats.Length);
        Assert.Equal(13, arc.Beats.Sum(b => Book.ScenesOf(b.Id).Length));
        Assert.Equal(["ch1_ford", "ch1_parley"], Book.ScenesOf("ch1_the_ford_choice").Select(s => s.Id));
        Assert.Equal([1, 2, 3, 4, 5], Book.Scenes.Where(s => s.Beat is not null).Select(ChapterOf).Distinct());
        Assert.Equal(75, Book.Scenes.Count(s => !s.Camp && s.Beat is not null));
        Assert.Equal(5, Book.Scenes.Count(s => s.Camp));
    }

    [Fact]
    public void Every_scene_line_loads_with_its_speaker()
    {
        var lines = Book.Scenes.SelectMany(s => s.Lines.Select(l => (Scene: s, Line: l))).ToList();

        Assert.Equal(945, lines.Count);
        foreach (var (scene, line) in lines)
        {
            Assert.Contains(line.Speaker, scene.Cast);
            Assert.True(line.Speaker == SceneDef.Narrator || Book.Character(line.Speaker) is not null, $"'{line.Id}' is spoken by '{line.Speaker}', who isn't a character.");
            Assert.False(string.IsNullOrWhiteSpace(line.Text), $"'{line.Id}' says nothing.");
        }

        var vote = Book.ScenesOf("ch1_the_ford_choice")[0].Node("vote")!.Lines[1];
        Assert.Equal(new SceneLine("ch1_ford_vote_2", "halden_varrow", "Will you clear them out, or will you talk to their chief?", "sad"), vote);
        Assert.Equal("Halden Varrow", Book.Character("halden_varrow")!.Name);
    }

    [Fact]
    public void Every_line_id_is_used_once()
    {
        var twice = Book.Scenes.SelectMany(s => s.Lines).GroupBy(l => l.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.Empty(twice);
    }

    /// <summary>Every main-story line has its own clip; camp reflections use captions until voiced. Each clip in the audio manifest, and the clip reads: no line falls back to a timer.</summary>
    [Fact]
    public void Every_line_has_a_clip_length()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Lore("audio", "manifest.json")));
        var listed = manifest.RootElement.GetProperty("lines");
        var missing = new List<string>();
        foreach (var line in Book.Scenes.Where(s => !s.Camp).SelectMany(s => s.Lines))
        {
            if (!listed.TryGetProperty(line.Id, out var clip) || !clip.TryGetProperty("main", out var file) || !File.Exists(Lore("audio", file.GetString()!)))
                missing.Add($"{line.Id}: no clip in the manifest");
            else if (Book.ClipSeconds(line.Id) is not (> 0.3 and < 30))
                missing.Add($"{line.Id}: a clip of {Book.ClipSeconds(line.Id)} s");
        }
        Assert.Empty(missing);

        // Recasts can change the longest line; enforce the playback budget, not a historical duration.
        var chapter1 = Book.Arcs.Single(a => a.Chapter == 1 && !a.SideAdventure).Beats.SelectMany(b => Book.ScenesOf(b.Id)).SelectMany(s => s.Lines);
        Assert.InRange(chapter1.Max(l => Book.ClipSeconds(l.Id)!.Value), 0.3, 30);
        Assert.Equal(74401 / 24000.0, Book.ClipSeconds("ch1_ford_vote_2")!.Value, 6);
    }

    [Fact]
    public void Every_speaker_has_a_portrait_in_every_mood()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(ContentPaths.ContentBase, "portraits", "manifest.json")));
        var moods = manifest.RootElement.GetProperty("moods").EnumerateObject().Select(m => m.Name).ToHashSet();
        var presets = manifest.RootElement.GetProperty("presets");

        Assert.Equal(["idle", "hurt", "happy", "angry", "sad", "surprised"], moods);
        var speakers = Book.Scenes.SelectMany(s => s.Lines).Select(l => l.Speaker).Where(s => s != SceneDef.Narrator).Distinct().Order().ToList();
        Assert.Equal(19, speakers.Count);
        foreach (var speaker in speakers)
        {
            var preset = Book.Character(speaker)!.Preset;
            Assert.True(presets.TryGetProperty(preset, out var art), $"'{speaker}' has the preset '{preset}', which has no portraits.");
            foreach (var mood in moods)
                Assert.True(art.TryGetProperty(mood, out var file) && File.Exists(Path.Combine(ContentPaths.ContentBase, "portraits", file.GetString()!)),
                    $"'{speaker}' ({preset}) has no '{mood}' portrait.");
        }
        foreach (var line in Book.Scenes.SelectMany(s => s.Lines))
            Assert.Contains(line.Mood, moods);
    }

    /// <summary>
    /// The scenes a battle beat plays after its fight (5d plan, flag 3): marked, only on battle beats, and after every lead-in. The two
    /// <c>ch3_the_bridge</c> scenes are both lead-ins, one per branch, and <c>ch4_rime_tower</c> and <c>ch5_gate_duel</c> have only an
    /// aftermath.
    /// </summary>
    [Fact]
    public void Every_battle_beat_marks_its_aftermath()
    {
        Assert.Equal(["ch1_bridge_cost", "ch1_road_east", "ch2_journal", "ch3_crypt_records", "ch4_caves_after", "ch4_rime_tower_after", "ch5_gate_duel_after", "ch5_ritual_after", "ashmaw_lair_after"],
            Book.Scenes.Where(s => s.After).Select(s => s.Id));
        foreach (var scene in Book.Scenes.Where(s => s.After))
            Assert.Equal(ArcBeat.Battle, BeatOf(scene).Kind);
        foreach (var beat in Book.Arcs.SelectMany(a => a.Beats))
        {
            var scenes = Book.ScenesOf(beat.Id);
            Assert.True(scenes.SkipWhile(s => !s.After).All(s => s.After), $"A lead-in of '{beat.Id}' comes after its aftermath.");
        }
        Assert.Equal(["ch3_bridge_spared", "ch3_bridge_slain"], Book.ScenesOf("ch3_the_bridge").Where(s => !s.After).Select(s => s.Id));
    }

    /// <summary>
    /// On every branch of the whole campaign, every beat plays a scene: a story beat has one for its flags as it starts, and a battle
    /// beat one before or after its fight. A beat with nothing to play would end with nothing said.
    /// </summary>
    [Fact]
    public void Every_beat_has_a_scene_on_each_branch()
    {
        var branches = Campaign.Branches(Book).ToList();
        Assert.Equal(2 * 3 * 2 * 2 * 2 * 3, branches.Count);
        var silent = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var branch in branches)
            foreach (var step in branch)
                if (!Book.ScenesOf(step.Beat.Id).Any(s => s.Requires.All(step.During.Contains) && !s.Unless.Any(step.During.Contains)))
                    silent.Add($"{step.Beat.Id} with [{string.Join(", ", step.During.Order())}]");
        Assert.Empty(silent);
    }

    /// <summary>The checker's graph rules, here for every chapter: every node is reached from the start, and some path ends.</summary>
    [Fact]
    public void Every_node_is_reachable_and_every_target_exists()
    {
        var problems = new List<string>();
        foreach (var scene in Book.Scenes)
        {
            var seen = new HashSet<string>();
            var open = new Stack<string>(new[] { scene.Start }.Concat(Book.Conversations.Where(c => c.Scene.Id == scene.Id).SelectMany(c => c.Entries.Select(e => e.Node))));
            while (open.TryPop(out var id))
            {
                var node = scene.Node(id);
                if (node is null)
                    problems.Add($"{scene.Id}: '{id}' isn't a node");
                else if (seen.Add(id))
                    foreach (var target in node.Targets)
                        open.Push(target);
            }
            problems.AddRange(scene.Nodes.Where(n => !seen.Contains(n.Id)).Select(n => $"{scene.Id}: '{n.Id}' can't be reached"));
            if (!scene.Nodes.Any(n => n.End && seen.Contains(n.Id)))
                problems.Add($"{scene.Id}: no path ends");
        }
        Assert.Empty(problems);
    }

    /// <summary>
    /// Dialogue can't invent story state (lore §7): every flag a node or option sets is one a beat or choice of <c>arcs.json</c> sets.
    /// </summary>
    [Fact]
    public void Scenes_set_only_flags_the_arcs_set()
    {
        var arcs = Book.Arcs.SelectMany(a => a.Beats).SelectMany(b => b.Sets.Concat(b.Choices.SelectMany(c => c.Sets))).ToHashSet();
        var invented = Book.Scenes.Where(s => !s.Camp && s.Beat is not null).SelectMany(s => s.Nodes.SelectMany(n => n.Sets.Concat(n.Options.SelectMany(o => o.Sets)))).Where(f => !arcs.Contains(f)).Distinct();
        Assert.Empty(invented);
    }

    /// <summary>
    /// Every flag the story waits on is set before it's read, in the order the campaign plays: a beat's <c>requires</c> by an earlier beat,
    /// and a scene's <c>requires</c> and <c>unless</c>, and an option's <c>requires</c>, by an earlier beat or by the beat's own choices
    /// and scenes. A beat's own <c>sets</c> come once it's done, so they don't count for its scenes. A flag read too early leaves a branch
    /// that can never play, or an option that never shows.
    /// </summary>
    [Fact]
    public void Every_flag_is_set_before_it_is_read()
    {
        // Side stories begin after their tested entrance has supplied its prerequisites.
        var before = Book.Arcs.Where(a => a.SideAdventure).SelectMany(a => a.Beats.Take(1)).SelectMany(b => b.Requires).ToHashSet();
        var problems = new List<string>();
        foreach (var beat in Book.Arcs.OrderBy(a => a.Chapter).SelectMany(a => a.Beats))
        {
            var scenes = Book.ScenesOf(beat.Id);
            var during = before.Union(beat.Choices.SelectMany(c => c.Sets))
                .Union(scenes.SelectMany(s => s.Nodes.SelectMany(n => n.Sets.Concat(n.Options.SelectMany(o => o.Sets))))).ToHashSet();
            problems.AddRange(beat.Requires.Where(f => !before.Contains(f)).Select(f => $"the beat '{beat.Id}' waits on '{f}'"));
            foreach (var scene in scenes)
            {
                problems.AddRange(scene.Requires.Concat(scene.Unless).Where(f => !during.Contains(f)).Select(f => $"the scene '{scene.Id}' reads '{f}'"));
                problems.AddRange(scene.Nodes.SelectMany(n => n.Options.SelectMany(o => o.Requires.Where(f => !during.Contains(f))
                    .Select(f => $"the option '{scene.Id}/{n.Id}/{o.Id}' reads '{f}'"))));
            }
            before.UnionWith(during);
            before.UnionWith(beat.Sets);
        }
        Assert.Empty(problems);
    }

    /// <summary>Only a vote's options stand for a choice, each a choice of its own beat, and a choice beat's votes offer all of them.</summary>
    [Fact]
    public void Every_vote_offers_every_choice_of_its_beat()
    {
        foreach (var beat in Book.Arcs.SelectMany(a => a.Beats))
        {
            var options = Book.ScenesOf(beat.Id).SelectMany(s => s.Nodes).SelectMany(n => n.Options.Select(o => (Node: n, Option: o))).Where(x => x.Option.Choice is not null).ToList();
            Assert.All(options, x => Assert.True(x.Node.Vote, $"'{x.Option.Id}' of '{beat.Id}' is a choice outside a vote."));
            Assert.All(options, x => Assert.Contains(x.Option.Choice, beat.Choices.Select(c => c.Id)));
            Assert.Equal(beat.Choices.Select(c => c.Id).Order(), options.Select(x => x.Option.Choice!).Distinct().Order());
        }
    }
}
