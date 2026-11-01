using System.Text.Json;
using ArcBox.Sdk;
using Tactics.Logic;
using static Tactics.Game.Tests.StoryTests;
using static Tactics.Game.Tests.StoryVoteTests;
using static Tactics.Game.Tests.TacticsGameTests;

namespace Tactics.Game.Tests;

/// <summary>
/// Battles, travel and the chapter's end (overworld 5d, Task 6): a battle beat's lead-in, its fight and its aftermath; a battle that follows
/// at once in the same place; a scene set somewhere else; and the card at the end of Chapter 1.
/// </summary>
public sealed class StoryBattleTests
{
    private const string Assault = "ch1_ford_assault";
    private const string Crossing = "ch1_river_crossing";
    private const string Ember = "ch1_the_ember";

    /// <summary>A session walking near <paramref name="beat"/>'s spot with <paramref name="seed"/>'s story, playing the base lore.</summary>
    private static Session NearBeat(string beat, TacticsSeed seed, bool battleList = false)
    {
        var at = NearSpot(beat);
        var s = new Session(game: new TacticsGame(Rules, lore: Lore, seed: seed with { At = at }, battleList: battleList));
        s.ToWalking();
        return s;
    }

    /// <summary>Walks into the ford's stamp from the west, with <paramref name="seed"/>'s story.</summary>
    private static Session SteppedIn(TacticsSeed seed)
    {
        var (from, dir) = StageBattleTests.Entry();
        var s = new Session(game: new TacticsGame(Rules, lore: Lore, seed: seed with { At = from }));
        s.ToWalking();
        s.Send(StageBattleTests.Lead(s), "walk", new { dir = StageBattleTests.Name(dir) });
        return s;
    }

    /// <summary>Plays the lead-in through to the fight, the TV having shown its walk-in.</summary>
    private static void ToTheFight(Session s)
    {
        PlayOut(s);
        Assert.Equal(TacticsPhase.Battle, s.Game.Phase);
        s.Harness.HostSignal("shown", new { seq = s.Host.Battle!.Beats[^1].Seq });
    }

    private static WorldSave World(Session s) => TacticsDocument.Read(s.Lease.Document).World!;

    // ---- The lead-in, the fight, the aftermath ------------------------------------------------------------------------------------

    [Fact]
    public void Walking_into_the_stamp_starts_the_lead_in()
    {
        var s = SteppedIn(StageBattleTests.Force);

        Assert.Equal(TacticsPhase.Story, s.Game.Phase);
        Assert.Equal("ch1_dawn", Story(s).Scene);
        Assert.Equal((Assault, StorySave.Lead), (Cursor(s)!.Beat, Cursor(s)!.Stage));
        Assert.Null(s.Game.Document.Battle);
    }

    [Fact]
    public void A_battle_beat_plays_its_lead_in_first()
    {
        var s = NearBeat(Crossing, StageBattleTests.Parley);
        var lead = StageBattleTests.Lead(s);
        Assert.Equal("Begin", Data(s, lead).GetProperty("interact").GetString());

        s.Send(lead, "interact");

        Assert.Equal("ch1_young_ones", Story(s).Scene);
    }

    [Fact]
    public void Tapping_the_marker_starts_the_battle_beat()
    {
        var s = NearBeat(Crossing, StageBattleTests.Parley);
        Assert.Equal((Crossing, "alert"), (Assert.Single(s.Host.Explore!.Markers!.Value).Beat, s.Host.Explore.Markers.Value[0].Emote));

        s.Send(StageBattleTests.Lead(s), "interact");

        Assert.Equal(TacticsPhase.Story, s.Game.Phase);
        Assert.Null(s.Host.Explore!.Markers!.Value[0].Emote);       // the figure stands, the "!" is down while it plays
    }

    [Fact]
    public void The_battle_starts_when_the_lead_in_ends()
    {
        var s = SteppedIn(StageBattleTests.Force);

        PlayOut(s);

        Assert.Equal(TacticsPhase.Battle, s.Game.Phase);
        Assert.Equal((Assault, "ford_assault"), (s.Game.Document.Battle!.Beat, s.Game.Document.Battle.Encounter));
        Assert.Equal(new StorySave(Assault, StorySave.Fight, null, null, 0, World(s).StorySeq) with { Played = [] }, World(s).Story! with { Played = [] });
        Assert.NotNull(s.Host.Battle!.Stage);
    }

    [Fact]
    public void A_win_plays_the_aftermath_after_results()
    {
        var s = NearBeat(Crossing, StageBattleTests.Parley);
        s.Send(StageBattleTests.Lead(s), "interact");
        ToTheFight(s);

        StageEndTests.Ended(s, BattleOutcome.Won);

        Assert.Equal((Crossing, StorySave.After), (World(s).Story!.Beat, World(s).Story!.Stage));
        Assert.DoesNotContain(Crossing, World(s).Beats);
        s.Send(0, "next");
        Assert.Equal(TacticsPhase.Story, s.Game.Phase);
        Assert.Equal("ch1_road_east", Story(s).Scene);
    }

    [Fact]
    public void The_beat_is_done_when_the_aftermath_ends()
    {
        var s = NearBeat(Crossing, StageBattleTests.Parley);
        s.Send(StageBattleTests.Lead(s), "interact");
        ToTheFight(s);
        StageEndTests.Ended(s, BattleOutcome.Won);
        s.Send(0, "next");

        PlayOut(s);

        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
        Assert.Contains(Crossing, World(s).Beats);
        Assert.Null(World(s).Story);
        Assert.Equal("After The Young Ones", s.Lease.Checkpoints[^1].Label);
    }

    [Fact]
    public void A_loss_keeps_the_beat_at_the_fight()
    {
        var s = SteppedIn(StageBattleTests.Force);
        ToTheFight(s);

        StageEndTests.Ended(s, BattleOutcome.Lost);
        Assert.Equal((Assault, StorySave.Fight), (World(s).Story!.Beat, World(s).Story!.Stage));
        s.Send(0, "next");

        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
        Assert.DoesNotContain(Assault, World(s).Beats);
        Assert.Equal("alert", Assert.Single(s.Host.Explore!.Markers!.Value).Emote);      // the "!" and the stamp are back
    }

    [Fact]
    public void A_retry_skips_the_lead_in()
    {
        var s = SteppedIn(StageBattleTests.Force);
        ToTheFight(s);
        StageEndTests.Ended(s, BattleOutcome.Lost);
        s.Send(0, "next");

        // Back to the stamp's edge (the party woke at the recovery spawn) and in again.
        var doc = TacticsDocument.Read(s.Lease.Document);
        var (from, dir) = StageBattleTests.Entry();
        var again = new Session(document: (doc with { World = doc.World! with { At = from } }).ToJson(), game: new TacticsGame(Rules, lore: Lore));
        again.Harness.StartCampaign(again.Lease);
        again.Send(StageBattleTests.Lead(again), "walk", new { dir = StageBattleTests.Name(dir) });

        Assert.Equal(TacticsPhase.Battle, again.Game.Phase);
        Assert.Equal(Assault, again.Game.Document.Battle!.Beat);
    }

    [Fact]
    public void A_list_battle_leaves_a_pending_beat_alone()
    {
        var s = SteppedIn(StageBattleTests.Force);
        ToTheFight(s);
        StageEndTests.Ended(s, BattleOutcome.Lost);
        var doc = TacticsDocument.Read(s.Lease.Document);
        var list = new Session(document: doc.ToJson(), game: new TacticsGame(Rules, lore: Lore, battleList: true));
        list.Harness.StartCampaign(list.Lease);
        list.Send(0, "battles");
        list.Send(0, "pickEncounter", new { encounter = "river_crossing" });
        if (list.Host.Battle!.Stage is not null)
            list.Harness.HostSignal("shown", new { seq = list.Host.Battle.Beats[^1].Seq });

        StageEndTests.Ended(list, BattleOutcome.Won);

        Assert.Equal((Assault, StorySave.Fight), (World(list).Story!.Beat, World(list).Story!.Stage));
        Assert.DoesNotContain(Assault, World(list).Beats);
    }

    /// <summary>
    /// Review finding 1: a battle beat waiting at its fight, after a restart, is never marked done without its fight. It waits in the
    /// world, with its stamp armed.
    /// </summary>
    [Fact]
    public void A_restart_at_the_fight_doesnt_mark_the_beat_done()
    {
        var doc = TacticsDocument.Read(ExplorationTests.DocumentAt(null));
        var at = StageBattleTests.Entry().From;
        doc = doc with
        {
            World = new WorldSave(StageBattleTests.Ford, at, new Tile(at.X + 1, at.Y), null, StageBattleTests.Force.Flags, StageBattleTests.Force.Beats,
                new StorySave(Assault, StorySave.Fight, null, null, 0, 7)),
        };
        var s = new Session(document: doc.ToJson(), game: new TacticsGame(Rules, lore: Lore));

        s.Harness.Resume(s.Lease);
        for (var p = 0; p < 4; p++)
            s.Harness.SetConnected(p, true);

        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
        Assert.DoesNotContain(Assault, World(s).Beats);
        Assert.Equal((Assault, StorySave.Fight), (World(s).Story!.Beat, World(s).Story!.Stage));
        Assert.Null(s.Host.Notice);
        s.Send(StageBattleTests.Lead(s), "walk", new { dir = StageBattleTests.Name(StageBattleTests.Entry().Dir) });
        Assert.Equal(TacticsPhase.Battle, s.Game.Phase);        // straight into the fight
    }

    // ---- What follows a beat ----------------------------------------------------------------------------------------------------

    [Fact]
    public void Force_runs_straight_into_the_assault()
    {
        var s = AtTheVote();
        for (var p = 0; p < 4; p++)
            Vote(s, p, "force");
        var phases = new HashSet<TacticsPhase>();
        var scenes = new List<string>();
        for (var i = 0; i < 2000 && s.Game.Phase != TacticsPhase.Battle; i++)
        {
            s.Harness.Advance(TimeSpan.FromMilliseconds(200));
            phases.Add(s.Game.Phase);
            if (s.Host.Story is { } story && (scenes.Count == 0 || scenes[^1] != story.Scene))
                scenes.Add(story.Scene);
        }

        Assert.Equal(TacticsPhase.Battle, s.Game.Phase);
        Assert.DoesNotContain(TacticsPhase.Exploration, phases);       // no walk between
        Assert.Equal(["ch1_ford", "ch1_dawn"], scenes);
        Assert.Contains(FordChoice, World(s).Beats);
        Assert.Equal(Assault, s.Game.Document.Battle!.Beat);
    }

    [Fact]
    public void Parley_travels_with_a_new_epoch_and_a_save()
    {
        var s = AtTheVote();
        var (epoch, before) = (s.Host.Explore!.Epoch, s.Host.Explore.At);
        for (var p = 0; p < 4; p++)
            Vote(s, p, "parley");
        Until(s, () => Story(s).Scene == "ch1_parley", "the parley");

        var party = s.Host.Explore!;
        Assert.Equal(TacticsGame.StoryStates.Gap, Story(s).State);       // the fade is the gap's
        Assert.Equal(epoch + 1, party.Epoch);
        Assert.Equal(TacticsGame.Travelled, party.Arrived);
        Assert.NotEqual(before, party.At);
        var spot = SpotOf("ch1_skarrow_song");
        Assert.True(Math.Abs(party.At.X - spot.X) <= 2 && Math.Abs(party.At.Y - spot.Y) <= 2, $"{party.At} isn't by the clan's fire at {spot}");
        Assert.Equal(party.At, World(s).At);
        Assert.Equal(("ch1_parley", null), (Cursor(s)!.Scene, Cursor(s)!.Node));
    }

    [Fact]
    public void After_the_parley_the_crossing_waits_at_its_marker()
    {
        var s = AtTheVote();
        for (var p = 0; p < 4; p++)
            Vote(s, p, "parley");

        PlayOut(s);

        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
        var marker = Assert.Single(s.Host.Explore!.Markers!.Value);
        Assert.Equal((Crossing, SpotOf(Crossing), "alert"), (marker.Beat, marker.At, marker.Emote));
        Assert.Contains("ch1_skarrow_spared", World(s).Flags);
        Assert.Equal("Go to The Ford Bridge", Data(s, 0).GetProperty("objective").GetString());
    }

    // ---- The end of the chapter --------------------------------------------------------------------------------------------------

    private static readonly TacticsSeed AllButTheEmber = new(["ch1_skarrow_spared", "ch1_met_maud", "ch1_halden_confessed"],
        ["ch1_mayors_request", FordChoice, Crossing, OldMaud, "ch1_granary", "ch1_skarrow_song"]);

    private static Session ToTheCard()
    {
        var s = NearBeat(Ember, AllButTheEmber);
        s.Send(StageBattleTests.Lead(s), "interact");
        for (var i = 0; i < 5000 && s.Game.Phase == TacticsPhase.Story && s.Host.Chapter is null; i++)
            s.Harness.Advance(TimeSpan.FromMilliseconds(200));
        Assert.NotNull(s.Host.Chapter);
        return s;
    }

    [Fact]
    public void The_last_beat_ends_the_chapter()
    {
        var s = ToTheCard();

        Assert.Equal(TacticsPhase.Story, s.Game.Phase);
        Assert.Equal("Chapter I · The Bridge at the Ford", s.Host.Chapter);
        Assert.Null(s.Host.Story);
        Assert.Contains(Ember, World(s).Beats);
        Assert.Equal("End of Chapter I", s.Lease.Checkpoints[^1].Label);
        Assert.Equal("Chapter I · The Bridge at the Ford", Data(s, 0).GetProperty("title").GetString());

        s.Harness.Advance(StoryTiming.Default.Card);

        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
        Assert.Null(s.Host.Chapter);
    }

    [Fact]
    public void After_the_chapter_there_are_no_markers_and_walking_works()
    {
        var s = ToTheCard();
        s.Harness.Advance(StoryTiming.Default.Card);

        Assert.Empty(s.Host.Explore!.Markers!.Value);
        Assert.Equal("Chapter II isn't here yet", Data(s, 0).GetProperty("objective").GetString());
        var lead = StageBattleTests.Lead(s);
        Assert.Equal(JsonValueKind.Null, Data(s, lead).GetProperty("interact").ValueKind);
        var seq = s.Host.Explore.Seq;
        foreach (var dir in new[] { "n", "e", "s", "w", "ne", "se", "sw", "nw" })
        {
            s.Send(lead, "walk", new { dir });
            s.Send(lead, "walk", new { dir = (string?)null });
            if (s.Host.Explore.Seq > seq)
                break;
        }
        Assert.True(s.Host.Explore.Seq > seq);
    }

    [Fact]
    public void The_chapter_card_only_shows_once()
    {
        var s = ToTheCard();

        var again = new Session(document: s.Lease.Document, game: new TacticsGame(Rules, lore: Lore));
        again.Harness.Resume(again.Lease);
        for (var p = 0; p < 4; p++)
            again.Harness.SetConnected(p, true);

        Assert.Equal(TacticsPhase.Exploration, again.Game.Phase);
        Assert.Null(again.Host.Chapter);
    }

    // ---- A saved story the lore no longer fits (review finding 5) ----------------------------------------------------------------

    private static Session ResumedOn(StorySave cursor, TacticsSeed seed)
    {
        var doc = TacticsDocument.Read(ExplorationTests.DocumentAt(null));
        var at = NearSpot(FordChoice);
        doc = doc with { World = new WorldSave(StageBattleTests.Ford, at, new Tile(at.X + 1, at.Y), null, seed.Flags, seed.Beats, cursor) };
        var s = new Session(document: doc.ToJson(), game: new TacticsGame(Rules, lore: Lore));
        s.Harness.Resume(s.Lease);
        for (var p = 0; p < 4; p++)
            s.Harness.SetConnected(p, true);
        return s;
    }

    [Fact]
    public void A_cursor_on_a_beat_thats_gone_is_dropped_with_a_notice()
    {
        var s = ResumedOn(new StorySave("ch1_no_such_beat", StorySave.Lead, "nowhere", "open", 2, 9), SeedFor(FordChoice));

        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
        Assert.Null(World(s).Story);
        Assert.Equal(TacticsGame.StoryChanged, s.Host.Notice);
    }

    [Fact]
    public void A_scene_that_no_longer_plays_starts_its_beat_again_with_a_notice()
    {
        // Between the ford's scenes, about to play the parley, with the flag that chose it gone.
        var s = ResumedOn(new StorySave(FordChoice, StorySave.Lead, "ch1_parley", null, 0, 9), SeedFor(FordChoice));

        Assert.Equal(TacticsPhase.Story, s.Game.Phase);
        Assert.Equal("ch1_ford", Story(s).Scene);
        Assert.Equal(TacticsGame.StoryChanged, s.Host.Notice);
    }

    [Fact]
    public void A_story_stage_on_a_story_beat_is_dropped_with_a_notice()
    {
        var s = ResumedOn(new StorySave(FordChoice, StorySave.Fight, null, null, 0, 9), SeedFor(FordChoice));

        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
        Assert.DoesNotContain(FordChoice, World(s).Beats);
        Assert.Equal(TacticsGame.StoryChanged, s.Host.Notice);
    }
}
