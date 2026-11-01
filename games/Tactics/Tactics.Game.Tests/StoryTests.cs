using System.Text.Json;
using ArcBox.Sdk;
using Tactics.Logic;
using static Tactics.Game.Tests.TacticsGameTests;

namespace Tactics.Game.Tests;

/// <summary>
/// Beats start, and lines run (overworld 5d, Task 4): the marker, Interact, the Story phase, lines on the server's clock, the leader's
/// skip, node flags, the end of a beat, and the lead in a scene. Questions are <see cref="StoryVoteTests"/>.
/// </summary>
public sealed class StoryTests
{
    // ---- Fixtures, shared with the other story tests --------------------------------------------------------------------

    internal const string Mayor = "ch1_mayors_request";
    internal const string FordChoice = "ch1_the_ford_choice";
    internal const string OldMaud = "ch1_old_maud";

    internal static LoreBook Lore => StageBattleTests.Lore.Value;

    internal static Arc Arc => Lore.ArcFor(StageBattleTests.Ford)!;

    internal static Terrain Ground => StageBattleTests.Ground;

    /// <summary>The story so far for each beat these tests play: everything before it done. Maud's comes after the assault was won.</summary>
    internal static TacticsSeed SeedFor(string beat) => beat switch
    {
        Mayor => new TacticsSeed([], []),
        FordChoice => new TacticsSeed([], [Mayor]),
        OldMaud => new TacticsSeed(["ch1_skarrow_slain"], [Mayor, FordChoice, "ch1_ford_assault"]),
        _ => throw new ArgumentException(beat),
    };

    internal static Tile SpotOf(string beat) => StageBattleTests.Region.Beats.Single(b => b.Beat == beat).At;

    private static readonly Lazy<HashSet<Tile>> ReachedOnce = new(() => StageBattleTests.Reached);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Tile> NearTiles = new();

    /// <summary>A tile the party can reach, in reach of <paramref name="beat"/>'s spot.</summary>
    internal static Tile NearSpot(string beat) => NearTiles.GetOrAdd(beat, b =>
    {
        var spot = SpotOf(b);
        return ReachedOnce.Value.Where(t => t != spot && Math.Abs(t.X - spot.X) <= 3 && Math.Abs(t.Y - spot.Y) <= 3 && StoryRules.InReach(Ground, t, spot))
            .OrderBy(t => Math.Abs(t.X - spot.X) + Math.Abs(t.Y - spot.Y)).ThenBy(t => t).First();
    });

    /// <summary>A step from a tile out of reach of <paramref name="beat"/>'s spot that lands in reach of it.</summary>
    internal static (Tile From, WalkDir Dir) StepIntoReach(string beat)
    {
        var spot = SpotOf(beat);
        foreach (var from in StageBattleTests.Reached.Order())
            if (!StoryRules.InReach(Ground, from, spot))
                foreach (var dir in Enum.GetValues<WalkDir>())
                    if (WorldWalk.Next(Ground, from, dir, ClassDef.DefaultJump, null) is { } move && StoryRules.InReach(Ground, move.To, spot))
                        return (from, dir);
        throw new InvalidOperationException($"No step into reach of {beat}.");
    }

    /// <summary>A session walking in reach of <paramref name="beat"/>, which is on offer.</summary>
    internal static Session Near(string beat, LoreBook? lore = null, int harnessSeed = 1, TacticsTiming? timing = null)
    {
        var at = NearSpot(beat);
        var s = new Session(seed: harnessSeed, game: new TacticsGame(Rules, timing, lore ?? Lore, SeedFor(beat) with { At = at }));
        s.ToWalking();
        Assert.Equal(at, s.Host.Explore!.At);
        return s;
    }

    /// <summary>A session that has just begun <paramref name="beat"/>: its leader tapped Interact in reach of it.</summary>
    internal static Session Telling(string beat, LoreBook? lore = null, int harnessSeed = 1, TacticsTiming? timing = null)
    {
        var s = Near(beat, lore, harnessSeed, timing);
        s.Send(StageBattleTests.Lead(s), "interact");
        Assert.Equal(TacticsPhase.Story, s.Game.Phase);
        return s;
    }

    internal static StoryView Story(Session s) => s.Host.Story!;

    internal static StorySave? Cursor(Session s) => TacticsDocument.Read(s.Lease.Document).World!.Story;

    /// <summary>The scene's lines in order, by id: the scene a test is in.</summary>
    internal static SceneDef SceneNamed(string id) => Lore.Scenes.Single(sc => sc.Id == id);

    internal static string PhoneJson(Session s, int player) => JsonSerializer.Serialize(s.Harness.PlayerView(player), ArcJson.Options);

    internal static JsonElement Data(Session s, int player) => s.Screen(player)!.Data;

    /// <summary>How long a line with no clip is read for (5d plan §4).</summary>
    internal static TimeSpan Reading(string text)
        => TimeSpan.FromSeconds(text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length * 0.36 + 1.2);

    /// <summary>Lets the line on screen run out, and no more.</summary>
    internal static void LineEnds(Session s) => s.Harness.Advance(Story(s).Until!.Value - s.Harness.Now);

    /// <summary>Plays on, a second at a time, until the story is over or <paramref name="limit"/> runs out.</summary>
    internal static void PlayOut(Session s, TimeSpan? limit = null)
    {
        var end = s.Harness.Now + (limit ?? TimeSpan.FromMinutes(20));
        while (s.Game.Phase == TacticsPhase.Story && s.Harness.Now < end)
            s.Harness.Advance(TimeSpan.FromSeconds(1));
    }

    /// <summary>The lore with every clip gone: each line is read on a timer.</summary>
    internal static readonly Lazy<LoreBook> NoClips = new(() =>
    {
        var copy = Directory.CreateTempSubdirectory("tactics-lore-no-clips-");
        try
        {
            var from = Path.Combine(ExplorationTests.ContentRules(), "lore");
            foreach (var file in Directory.EnumerateFiles(from, "*.json", SearchOption.AllDirectories))
            {
                var to = Path.Combine(copy.FullName, "lore", Path.GetRelativePath(from, file));
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Copy(file, to);
            }
            var book = LoreBook.Load(copy.FullName);
            foreach (var line in book.Scenes.SelectMany(sc => sc.Lines))
                _ = book.ClipSeconds(line.Id);      // read now, while the folder is there: every one is missing
            return book;
        }
        finally
        {
            copy.Delete(recursive: true);
        }
    });

    // ---- The marker and Interact ----------------------------------------------------------------------------------------

    [Fact]
    public void Story_is_at_the_end_of_the_phases_so_old_values_keep_their_order()
    {
        Assert.Equal(TacticsPhase.Story, Enum.GetValues<TacticsPhase>()[^1]);
        Assert.Equal(6, (int)TacticsPhase.Story);
    }

    [Fact]
    public void A_new_campaign_shows_the_mayors_marker()
    {
        var s = new Session(game: new TacticsGame(Rules, lore: Lore));
        s.ToWalking();

        var marker = Assert.Single(s.Host.Explore!.Markers!.Value);
        Assert.Equal(new StoryMarker(Mayor, SpotOf(Mayor), "characters.child", "alert"), marker);     // Pip runs up first (5d default 12)
        Assert.Equal("Go to Emberwick", Data(s, 0).GetProperty("objective").GetString());
        Assert.Equal("Go to Emberwick", Data(s, 1).GetProperty("objective").GetString());
    }

    [Fact]
    public void Without_lore_there_is_no_marker_and_nothing_to_go_to()
    {
        var s = new Session(game: new TacticsGame(Rules));
        s.ToWalking();

        Assert.Empty(s.Host.Explore!.Markers!.Value);
        Assert.Equal("Explore the region", Data(s, 0).GetProperty("objective").GetString());
        Assert.Equal(JsonValueKind.Null, Data(s, 0).GetProperty("interact").ValueKind);
    }

    [Fact]
    public void Interact_appears_only_in_reach()
    {
        var (from, dir) = StepIntoReach(Mayor);
        var s = new Session(game: new TacticsGame(Rules, lore: Lore, seed: SeedFor(Mayor) with { At = from }));
        s.ToWalking();
        var lead = StageBattleTests.Lead(s);
        Assert.Equal(JsonValueKind.Null, Data(s, lead).GetProperty("interact").ValueKind);

        s.Send(lead, "walk", new { dir = StageBattleTests.Name(dir) });
        s.Send(lead, "walk", new { dir = (string?)null });

        Assert.Equal("Talk", Data(s, lead).GetProperty("interact").GetString());
        for (var p = 0; p < 4; p++)
            if (p != lead)
                Assert.Equal(JsonValueKind.Null, Data(s, p).GetProperty("interact").ValueKind);     // the leader's alone
    }

    [Fact]
    public void A_step_with_no_change_of_reach_changes_no_view()
    {
        var spot = SpotOf(Mayor);
        // A step that stays in reach, and one that stays out of it.
        foreach (var inReach in new[] { true, false })
        {
            var (from, dir) = StageBattleTests.Reached.Order()
                .SelectMany(t => Enum.GetValues<WalkDir>().Select(d => (From: t, Dir: d)))
                .First(p => StoryRules.InReach(Ground, p.From, spot) == inReach
                    && WorldWalk.Next(Ground, p.From, p.Dir, ClassDef.DefaultJump, null) is { } m && m.To != spot
                    && StoryRules.InReach(Ground, m.To, spot) == inReach);
            var s = new Session(game: new TacticsGame(Rules, lore: Lore, seed: SeedFor(Mayor) with { At = from }));
            s.ToWalking();
            var lead = StageBattleTests.Lead(s);
            s.Send(lead, "walk", new { dir = (string?)null });
            var before = Enumerable.Range(0, 4).Select(p => PhoneJson(s, p)).ToArray();
            var seq = s.Host.Explore!.Seq;

            s.Send(lead, "walk", new { dir = StageBattleTests.Name(dir) });
            s.Send(lead, "walk", new { dir = (string?)null });

            Assert.Equal(seq + 1, s.Host.Explore!.Seq);
            Assert.Equal(before, Enumerable.Range(0, 4).Select(p => PhoneJson(s, p)));
        }
    }

    [Fact]
    public void Only_the_leader_can_interact()
    {
        var s = Near(Mayor);
        var other = (StageBattleTests.Lead(s) + 1) % 4;

        s.Send(other, "interact");

        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
        Assert.Null(s.Host.Story);
    }

    [Fact]
    public void Interact_out_of_reach_is_ignored()
    {
        var (from, _) = StepIntoReach(Mayor);
        var s = new Session(game: new TacticsGame(Rules, lore: Lore, seed: SeedFor(Mayor) with { At = from }));
        s.ToWalking();

        s.Send(StageBattleTests.Lead(s), "interact");

        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
    }

    [Fact]
    public void Interact_starts_the_beats_first_scene()
    {
        var s = Telling(Mayor);

        var story = Story(s);
        var first = SceneNamed("ch1_mayor");
        Assert.Equal(("ch1_mayor", "Emberwick at Dusk", "lines"), (story.Scene, story.Title, story.State));
        Assert.Equal(first.Nodes[0].Lines[0].Id, story.Line!.Id);
        Assert.Equal("narrator", story.Line.Speaker);
        Assert.Null(story.Line.Name);
        Assert.Null(story.Line.Portrait);
        Assert.Equal(new StorySave(Mayor, StorySave.Lead, "ch1_mayor", "open", 0, story.Seq) with { Played = [] }, Cursor(s)! with { Played = [] });
        Assert.Equal(["open"], Cursor(s)!.Played);
        Assert.Null(Assert.Single(s.Host.Explore!.Markers!.Value).Emote);       // the figure stays, the "!" goes while its scene plays
        Assert.Equal("Emberwick at Dusk", s.Host.Banner);
    }

    [Fact]
    public void Interact_stops_the_party()
    {
        var spot = SpotOf(Mayor);
        var (from, dir) = StageBattleTests.Reached.Order()
            .SelectMany(t => Enum.GetValues<WalkDir>().Select(d => (From: t, Dir: d)))
            .First(p => p.From != spot && StoryRules.InReach(Ground, p.From, spot)
                && WorldWalk.Next(Ground, p.From, p.Dir, ClassDef.DefaultJump, null) is { } m && StoryRules.InReach(Ground, m.To, spot));
        var s = new Session(game: new TacticsGame(Rules, lore: Lore, seed: SeedFor(Mayor) with { At = from }));
        s.ToWalking();
        var lead = StageBattleTests.Lead(s);
        s.Send(lead, "walk", new { dir = StageBattleTests.Name(dir) });
        Assert.True(s.Host.Explore!.Held);

        s.Send(lead, "interact");

        Assert.Equal(TacticsPhase.Story, s.Game.Phase);
        Assert.False(s.Host.Explore!.Held);
        Assert.Empty(s.Harness.ActiveTimers.Intersect(StageBattleTests.WalkingTimers));
        Assert.Equal(s.Host.Explore.At, TacticsDocument.Read(s.Lease.Document).World!.At);     // the place is saved on the way in
    }

    [Fact]
    public void The_party_cant_walk_during_a_scene()
    {
        var s = Telling(Mayor);
        var party = JsonSerializer.Serialize(s.Host.Explore, ArcJson.Options);

        foreach (var dir in new[] { "n", "e", "s", "w" })
        {
            s.Send(StageBattleTests.Lead(s), "walk", new { dir });
            s.Harness.Advance(TimeSpan.FromMilliseconds(400));
        }

        Assert.Equal(TacticsPhase.Story, s.Game.Phase);
        Assert.Equal(party, JsonSerializer.Serialize(s.Host.Explore, ArcJson.Options));
        Assert.Empty(s.Harness.ActiveTimers.Intersect(StageBattleTests.WalkingTimers));
    }

    // ---- Lines ----------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_line_runs_for_its_clip_and_a_gap()
    {
        var s = Telling(Mayor);
        var line = Story(s).Line!.Id;
        var clip = Lore.ClipSeconds(line)!.Value;
        var length = TimeSpan.FromSeconds(clip) + StoryTiming.Default.LineGap;

        Assert.Equal(s.Harness.Now + length, Story(s).Until);
        s.Harness.Advance(length - TimeSpan.FromMilliseconds(1));
        Assert.Equal(line, Story(s).Line!.Id);
        s.Harness.Advance(TimeSpan.FromMilliseconds(1));
        Assert.NotEqual(line, Story(s).Line!.Id);
    }

    [Fact]
    public void A_line_with_no_clip_runs_on_a_reading_timer()
    {
        var s = Telling(Mayor, NoClips.Value);
        var text = SceneNamed("ch1_mayor").Nodes[0].Lines[0].Text;

        Assert.Null(NoClips.Value.ClipSeconds(Story(s).Line!.Id));
        Assert.Equal(s.Harness.Now + Reading(text), Story(s).Until);
        Assert.Equal(Reading(text), StoryTiming.Default.Line(null, text));
    }

    [Fact]
    public void Lines_advance_with_nobody_responding()
    {
        var s = Telling(Mayor);
        for (var p = 0; p < 4; p++)
            s.Harness.SetConnected(p, false);
        var lines = SceneNamed("ch1_mayor").Nodes[0].Lines;

        foreach (var line in lines)
        {
            Assert.Equal(line.Id, Story(s).Line!.Id);
            LineEnds(s);
        }
        Assert.Equal("halden", Cursor(s)!.Node);
    }

    [Fact]
    public void Lines_advance_with_no_tv()
    {
        var s = Telling(Mayor);
        var seq = Story(s).Seq;

        LineEnds(s);
        LineEnds(s);

        Assert.Equal(seq + 2, Story(s).Seq);
        Assert.Equal(Story(s).Seq, Cursor(s)!.Seq);
        Assert.Equal(2, Cursor(s)!.Line);
    }

    [Fact]
    public void Every_line_saves_its_cursor()
    {
        var s = Telling(Mayor);
        for (var i = 0; i < 6; i++)
        {
            var (story, cursor) = (Story(s), Cursor(s)!);
            var node = SceneNamed(cursor.Scene!).Node(cursor.Node!)!;
            Assert.Equal(story.Line!.Id, node.Lines[cursor.Line].Id);
            Assert.Equal(story.Seq, cursor.Seq);
            LineEnds(s);
        }
    }

    [Fact]
    public void Only_the_leader_can_skip()
    {
        var s = Telling(Mayor);
        var lead = StageBattleTests.Lead(s);
        var line = Story(s).Line!.Id;

        for (var p = 0; p < 4; p++)
            if (p != lead)
            {
                Assert.False(Data(s, p).GetProperty("canSkip").GetBoolean());
                s.Send(p, "skip", new { line });
            }
        Assert.Equal(line, Story(s).Line!.Id);

        Assert.True(Data(s, lead).GetProperty("canSkip").GetBoolean());
        s.Send(lead, "skip", new { line });
        Assert.NotEqual(line, Story(s).Line!.Id);
    }

    [Fact]
    public void A_skip_doesnt_skip_two_lines()
    {
        var s = Telling(Mayor);
        var lead = StageBattleTests.Lead(s);
        var lines = SceneNamed("ch1_mayor").Nodes[0].Lines;
        var before = PhoneJson(s, lead);

        s.Send(lead, "skip", new { line = lines[0].Id });
        s.Send(lead, "skip", new { line = lines[0].Id });      // a double tap: it names the line it meant

        Assert.Equal(lines[1].Id, Story(s).Line!.Id);
        Assert.NotEqual(before, PhoneJson(s, lead));                // and the room drops a tap on the old view anyway
        Assert.Equal(lines[1].Id, Data(s, lead).GetProperty("line").GetString());
    }

    [Fact]
    public void A_skip_cancels_the_line_timer()
    {
        var s = Telling(Mayor);
        var lead = StageBattleTests.Lead(s);
        s.Harness.Advance(TimeSpan.FromSeconds(1));

        s.Send(lead, "skip", new { line = Story(s).Line!.Id });

        var second = Story(s).Line!.Id;
        var length = TimeSpan.FromSeconds(Lore.ClipSeconds(second)!.Value) + StoryTiming.Default.LineGap;
        Assert.Equal(s.Harness.Now + length, Story(s).Until);
        s.Harness.Advance(length - TimeSpan.FromMilliseconds(1));
        Assert.Equal(second, Story(s).Line!.Id);                   // the first line's timer didn't cut the second short
    }

    [Fact]
    public void The_phones_carry_the_line()
    {
        var s = Telling(Mayor);
        LineEnds(s);
        LineEnds(s);        // Pip's line

        var data = Data(s, 1);
        Assert.Equal(TacticsGame.Screens.Dialogue, s.Screen(1)!.Screen);
        Assert.Equal("Pip Ashdown", data.GetProperty("speaker").GetString());
        Assert.Equal("child", data.GetProperty("portrait").GetString());
        Assert.Equal("happy", data.GetProperty("mood").GetString());
        Assert.Equal(SceneNamed("ch1_mayor").Nodes[0].Lines[2].Text, data.GetProperty("text").GetString());
        Assert.Equal("Emberwick at Dusk", data.GetProperty("title").GetString());
        var line = Story(s).Line!;
        Assert.Equal(("pip_ashdown", "Pip Ashdown", "child", "happy", "npc"), (line.Speaker, line.Name, line.Portrait, line.Mood, line.Role));
    }

    // ---- Flags and the end of a beat --------------------------------------------------------------------------------------

    [Fact]
    public void Node_sets_apply_when_it_plays()
    {
        var s = Telling(OldMaud);

        Assert.Equal("ch1_maud", Story(s).Scene);
        Assert.Contains("ch1_met_maud", TacticsDocument.Read(s.Lease.Document).World!.Flags);     // saved with the node's first line
    }

    [Fact]
    public void The_beat_ends_and_the_next_marker_shows()
    {
        var s = Telling(Mayor);

        PlayOut(s);

        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
        var world = TacticsDocument.Read(s.Lease.Document).World!;
        Assert.Contains(Mayor, world.Beats);
        Assert.Null(world.Story);
        Assert.Equal("After Emberwick at Dusk", s.Lease.Checkpoints[^1].Label);
        var marker = Assert.Single(s.Host.Explore!.Markers!.Value);
        Assert.Equal((FordChoice, SpotOf(FordChoice), "alert"), (marker.Beat, marker.At, marker.Emote));
        Assert.Equal("Go to The Ford Bridge", Data(s, 0).GetProperty("objective").GetString());
        Assert.Null(s.Host.Story);
        Assert.Contains(TacticsGame.Timers.Lead, s.Harness.ActiveTimers);       // walking again, with its timers back
    }

    [Fact]
    public void A_beat_with_no_scene_to_play_ends_at_once()
    {
        // Maud's scene needs nothing, so take it away: with no lore scene for the beat, it's simply done.
        var at = NearSpot(OldMaud);
        var lore = StoryLoreWithout("ch1_maud");
        var s = new Session(game: new TacticsGame(Rules, lore: lore, seed: SeedFor(OldMaud) with { At = at }));
        s.ToWalking();

        s.Send(StageBattleTests.Lead(s), "interact");

        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
        var world = TacticsDocument.Read(s.Lease.Document).World!;
        Assert.Contains(OldMaud, world.Beats);
        Assert.Contains("ch1_met_maud", world.Flags);       // the beat's own sets still apply
    }

    /// <summary>The base lore with the scene <paramref name="scene"/> taken out of its file.</summary>
    internal static LoreBook StoryLoreWithout(string scene)
        => EditedLore(scenes => scenes.Remove(scenes.Single(x => x!["id"]!.GetValue<string>() == scene)));

    /// <summary>The base lore's JSON with Chapter 1's scenes edited by <paramref name="edit"/>, and no clips: every line is read on a timer.</summary>
    internal static LoreBook EditedLore(Action<System.Text.Json.Nodes.JsonArray> edit)
    {
        var copy = Directory.CreateTempSubdirectory("tactics-lore-edited-");
        try
        {
            var from = Path.Combine(ExplorationTests.ContentRules(), "lore");
            foreach (var file in Directory.EnumerateFiles(from, "*.json", SearchOption.AllDirectories))
            {
                var to = Path.Combine(copy.FullName, "lore", Path.GetRelativePath(from, file));
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Copy(file, to);
            }
            var path = Path.Combine(copy.FullName, "lore", "scenes", "ch1.json");
            var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
            edit(node["scenes"]!.AsArray());
            File.WriteAllText(path, node.ToJsonString());
            var book = LoreBook.Load(copy.FullName);
            foreach (var line in book.Scenes.SelectMany(sc => sc.Lines))
                _ = book.ClipSeconds(line.Id);
            return book;
        }
        finally
        {
            copy.Delete(recursive: true);
        }
    }

    // ---- Who's here, and who leads ------------------------------------------------------------------------------------------

    [Fact]
    public void A_member_with_no_character_can_pick_during_a_scene()
    {
        var at = NearSpot(Mayor);
        var s = new Session(game: new TacticsGame(Rules, lore: Lore, seed: SeedFor(Mayor) with { At = at }));
        s.Harness.StartCampaign(s.Lease);
        s.Harness.SetConnected(3, false);
        for (var i = 0; i < 3; i++)
            s.Send(i, "pickClass", new { @class = "fighter" });
        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
        s.Send(StageBattleTests.Lead(s), "interact");
        Assert.Equal(TacticsPhase.Story, s.Game.Phase);

        s.Harness.SetConnected(3, true);
        Assert.Equal(TacticsGame.Screens.PickClass, s.Screen(3)!.Screen);
        s.Send(3, "pickClass", new { @class = "wizard" });

        Assert.Equal(TacticsPhase.Story, s.Game.Phase);
        Assert.Equal("wizard", s.Game.Document.For(s.Profiles[3]!.Value)!.Character.Class);
        Assert.Equal(TacticsGame.Screens.Dialogue, s.Screen(3)!.Screen);
    }

    [Fact]
    public void Leader_silence_doesnt_change_the_lead_in_a_scene()
    {
        var s = Telling(Mayor);
        var leader = s.Game.Leader!.Id;

        Assert.DoesNotContain(TacticsGame.Timers.Lead, s.Harness.ActiveTimers);
        s.Harness.Advance(TimeSpan.FromSeconds(25));        // three times the silence rule, with nobody saying a word

        Assert.Equal(TacticsPhase.Story, s.Game.Phase);
        Assert.Equal(leader, s.Game.Leader!.Id);
    }

    [Fact]
    public void The_session_leader_waits_for_the_scene_to_end()
    {
        var at = NearSpot(Mayor);
        var s = new Session(game: new TacticsGame(Rules, lore: Lore, seed: SeedFor(Mayor) with { At = at }));
        s.ToWalking();
        var own = StageBattleTests.Lead(s);
        s.Harness.SetConnected(own, false);
        var standIn = StageBattleTests.Lead(s);
        Assert.NotEqual(own, standIn);
        s.Send(standIn, "interact");
        Assert.Equal(TacticsPhase.Story, s.Game.Phase);

        s.Harness.SetConnected(own, true);
        s.Send(own, "skip", new { line = Story(s).Line!.Id });     // heard from, and can lead: still not mid-scene

        Assert.Equal(s.Harness.Players[standIn].Id, s.Game.Leader!.Id);
        PlayOut(s);
        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
        Assert.Equal(s.Harness.Players[own].Id, s.Game.Leader!.Id);
    }

    [Fact]
    public void A_leader_who_leaves_mid_scene_is_replaced()
    {
        var s = Telling(Mayor);
        var leader = StageBattleTests.Lead(s);

        s.Harness.SetConnected(leader, false);

        Assert.NotNull(s.Game.Leader);
        Assert.NotEqual(s.Harness.Players[leader].Id, s.Game.Leader!.Id);
        Assert.True(Data(s, StageBattleTests.Lead(s)).GetProperty("canSkip").GetBoolean());
    }

    [Fact]
    public void The_banner_works_in_a_scene()
    {
        var s = Telling(Mayor);

        Assert.Equal("Emberwick at Dusk", s.Host.Banner);
        Assert.Equal(nameof(TacticsPhase.Story), s.Host.Phase);
        Assert.Null(s.Host.Battle);
    }

    // ---- Coming back to a scene ---------------------------------------------------------------------------------------------

    [Fact]
    public void A_restart_replays_the_saved_line()
    {
        var s = Telling(Mayor);
        LineEnds(s);
        LineEnds(s);
        var (line, seq) = (Story(s).Line!.Id, Story(s).Seq);

        var again = new Session(document: s.Lease.Document, game: new TacticsGame(Rules, lore: Lore));
        again.Harness.Resume(again.Lease);
        StoryResumeTests.AllBack(again);

        Assert.Equal(TacticsPhase.Story, again.Game.Phase);
        Assert.Equal(line, Story(again).Line!.Id);
        Assert.Equal(seq + 1, Story(again).Seq);        // said again: a TV that stayed up hears it as new
        Assert.Equal(2, Cursor(again)!.Line);
    }

    /// <summary>A TV can stay up while the server restarts between beats: the next beat's first line must still be a new one to it.</summary>
    [Fact]
    public void The_line_number_goes_on_across_beats_and_restarts()
    {
        var s = Telling(Mayor);
        PlayOut(s);
        var last = TacticsDocument.Read(s.Lease.Document).World!.StorySeq;
        Assert.True(last > 10);

        var doc = TacticsDocument.Read(s.Lease.Document);
        var moved = doc with { World = doc.World! with { At = NearSpot(FordChoice) } };
        var again = new Session(document: moved.ToJson(), game: new TacticsGame(Rules, lore: Lore));
        again.ToWalking();
        again.Send(StageBattleTests.Lead(again), "interact");

        Assert.Equal(FordChoice, Cursor(again)!.Beat);
        Assert.Equal(last + 1, Story(again).Seq);
    }

    [Fact]
    public void A_new_session_carries_on_the_scene_after_muster()
    {
        var s = Telling(Mayor);
        LineEnds(s);
        var line = Story(s).Line!.Id;

        var again = new Session(document: s.Lease.Document, game: new TacticsGame(Rules, lore: Lore));
        again.Harness.StartCampaign(again.Lease);

        Assert.Equal(TacticsPhase.Story, again.Game.Phase);
        Assert.Equal(line, Story(again).Line!.Id);
    }

    [Fact]
    public void A_cursor_on_a_scene_thats_gone_restarts_the_beat_with_a_notice()
    {
        var s = Telling(OldMaud);
        var world = TacticsDocument.Read(s.Lease.Document).World!;
        var moved = TacticsDocument.Read(s.Lease.Document) with { World = world with { Story = world.Story! with { Node = "nowhere" } } };

        var again = new Session(document: moved.ToJson(), game: new TacticsGame(Rules, lore: Lore));
        again.Harness.Resume(again.Lease);
        StoryResumeTests.AllBack(again);

        Assert.Equal(TacticsPhase.Story, again.Game.Phase);
        Assert.Equal(SceneNamed("ch1_maud").Nodes[0].Lines[0].Id, Story(again).Line!.Id);
        Assert.Equal(TacticsGame.StoryChanged, again.Host.Notice);
    }
}
