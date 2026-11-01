using ArcBox.Sdk;
using Tactics.Logic;
using static Tactics.Game.Tests.StoryTests;
using static Tactics.Game.Tests.StoryVoteTests;
using static Tactics.Game.Tests.TacticsGameTests;

namespace Tactics.Game.Tests;

/// <summary>
/// Coming back (overworld 5d, Task 7): a server restart in a scene waits for the party as a battle's does, then replays the saved line; a
/// question reopens, a roll isn't made again, an aftermath carries on, and nothing is lost or doubled.
/// </summary>
public sealed class StoryResumeTests
{
    /// <summary>The same campaign on a new server: nobody connected at first.</summary>
    internal static Session Restarted(Session s, int seed = 1)
    {
        var again = new Session(seed: seed, document: s.Lease.Document, game: new TacticsGame(Rules, lore: Lore));
        again.Harness.Resume(again.Lease);
        return again;
    }

    internal static void AllBack(Session s)
    {
        for (var p = 0; p < 4; p++)
            s.Harness.SetConnected(p, true);
    }

    [Fact]
    public void A_restart_mid_line_waits_for_the_party()
    {
        var s = Telling(Mayor);
        LineEnds(s);

        var again = Restarted(s);

        Assert.Equal(TacticsPhase.Resuming, again.Game.Phase);
        Assert.Equal("Resuming the story…", again.Host.Banner);
        Assert.Empty(again.Harness.ActiveTimers);        // no line runs to nobody
        again.Harness.Advance(TimeSpan.FromMinutes(5));
        again.Harness.SetConnected(1, true);
        again.Harness.SetConnected(2, true);
        Assert.Equal(TacticsPhase.Resuming, again.Game.Phase);
        Assert.Equal("Resuming… waiting for Player1, Player4", ((WaitView)again.Harness.PlayerView(2)).Message);
        Assert.Equal(1, Cursor(again)!.Line);
    }

    /// <summary>
    /// Review (adapter NOTE 3): a long scene, or a question held with every phone asleep, could only be left by closing the room. The VIP
    /// can end the session from inside a scene; the cursor is saved on every line, so the next session starts on the same line.
    /// </summary>
    /// <summary>
    /// What ending the session in a scene must leave (review A4): no clock of the scene's still running (the line's, the question's), and
    /// the document saved as it ends, the party's place with it.
    /// </summary>
    private static void EndedCleanly(Session s, int savesBefore)
    {
        Assert.True(s.Harness.IsOver);
        Assert.DoesNotContain(TacticsGame.Timers.Line, s.Harness.ActiveTimers);
        Assert.DoesNotContain(TacticsGame.Timers.Vote, s.Harness.ActiveTimers);
        Assert.True(s.Lease.Saves.Count > savesBefore, "ending the session saves");
        Assert.Equal(s.Game.Document.ToJson().GetRawText(), s.Lease.Document.GetRawText());
    }

    [Fact]
    public void The_vip_can_end_the_session_in_a_scene()
    {
        var s = Telling(Mayor);
        LineEnds(s);
        var line = Story(s).Line!.Id;
        Assert.Contains(TacticsGame.Timers.Line, s.Harness.ActiveTimers);     // the line's clock, which ending must stop

        s.Send(1, "endSession");
        Assert.False(s.Harness.IsOver);          // only the VIP's
        Assert.Equal(TacticsPhase.Story, s.Game.Phase);

        var saves = s.Lease.Saves.Count;
        s.Send(0, "endSession");

        EndedCleanly(s, saves);
        Assert.Equal(1, Cursor(s)!.Line);
        var again = Restarted(s);
        AllBack(again);
        Assert.Equal(TacticsPhase.Story, again.Game.Phase);
        Assert.Equal(line, Story(again).Line!.Id);
    }

    [Fact]
    public void The_vip_can_end_the_session_at_a_question()
    {
        var s = AtTheVote();
        Vote(s, 1, "pay");
        Assert.Contains(TacticsGame.Timers.Vote, s.Harness.ActiveTimers);     // the question's clock

        var saves = s.Lease.Saves.Count;
        s.Send(0, "endSession");

        EndedCleanly(s, saves);
        var again = Restarted(s);
        AllBack(again);
        Assert.Equal(TacticsGame.StoryStates.Asking, Story(again).State);     // votes aren't saved: the question is asked again
        Assert.Equal("vote", Cursor(again)!.Node);
    }

    [Fact]
    public void It_resumes_on_the_saved_line()
    {
        var s = Telling(Mayor);
        LineEnds(s);
        var (line, seq) = (Story(s).Line!.Id, Story(s).Seq);

        var again = Restarted(s);
        AllBack(again);

        Assert.Equal(TacticsPhase.Story, again.Game.Phase);
        Assert.Equal(line, Story(again).Line!.Id);
        Assert.Equal(seq + 1, Story(again).Seq);
    }

    [Fact]
    public void The_vip_can_continue_without_them()
    {
        var s = Telling(Mayor);
        LineEnds(s);
        var line = Story(s).Line!.Id;
        var again = Restarted(s);
        again.Harness.SetConnected(0, true);

        var buttons = Assert.IsType<ButtonsView>(again.Harness.PlayerView(0));
        Assert.Equal("continue", Assert.Single(buttons.Buttons).Id);
        again.Send(0, "continue");

        Assert.Equal(TacticsPhase.Story, again.Game.Phase);
        Assert.Equal(line, Story(again).Line!.Id);
    }

    [Fact]
    public void A_restart_mid_vote_reopens_the_question()
    {
        var s = AtTheVote();
        Vote(s, 1, "parley");
        Vote(s, 2, "parley");

        var again = Restarted(s);
        AllBack(again);

        Assert.Equal(TacticsGame.StoryStates.Asking, Story(again).State);
        Assert.Equal("vote", Cursor(again)!.Node);
        Assert.Equal(0, Ask(again).Voted);                      // votes aren't kept
        Assert.Equal(again.Harness.Now + StoryTiming.Default.Vote, Ask(again).Deadline);
    }

    [Fact]
    public void A_restart_on_a_tie_break_reopens_the_vote()
    {
        var s = AtTheVote();
        var lead = StageBattleTests.Lead(s);
        var others = Enumerable.Range(0, 4).Where(p => p != lead).ToArray();
        s.Harness.SetConnected(others[2], false);
        Vote(s, others[0], "force");
        Vote(s, others[1], "parley");
        s.Harness.Advance(Ask(s).Deadline!.Value - s.Harness.Now);
        Assert.Equal(TacticsGame.StoryStates.TieBreak, Story(s).State);

        var again = Restarted(s);
        AllBack(again);

        Assert.Equal(TacticsGame.StoryStates.Asking, Story(again).State);
        Assert.False(Ask(again).Tie);
        Assert.Equal(["force", "parley", "pay"], Ask(again).Options.Select(o => o.Id));
    }

    [Fact]
    public void A_restart_after_a_roll_doesnt_roll_again()
    {
        var s = AtTheVote();
        for (var p = 0; p < 4; p++)
            Vote(s, p, "pay");
        s.Harness.Advance(StoryTiming.Default.Settle + StoryTiming.Default.Roller);
        var roll = Cursor(s)!.Roll!;

        for (var seed = 1; seed <= 3; seed++)
        {
            var again = Restarted(s, seed);
            AllBack(again);
            Assert.Equal(TacticsGame.StoryStates.Rolled, Story(again).State);
            Assert.Equal(roll, Cursor(again)!.Roll);
            again.Harness.Advance(StoryTiming.Default.Reveal);
            Assert.Equal(roll.Passed ? "pay_pass" : "pay_fail", Cursor(again)!.Node);
        }
    }

    private static Session InTheAftermath()
    {
        var s = NearSpot("ch1_river_crossing") is var at
            ? new Session(game: new TacticsGame(Rules, lore: Lore, seed: StageBattleTests.Parley with { At = at }))
            : null!;
        s.ToWalking();
        s.Send(StageBattleTests.Lead(s), "interact");
        PlayOut(s);
        s.Harness.HostSignal("shown", new { seq = s.Host.Battle!.Beats[^1].Seq });
        StageEndTests.Ended(s, BattleOutcome.Won);
        return s;
    }

    [Fact]
    public void A_restart_at_results_goes_to_the_aftermath()
    {
        var s = InTheAftermath();
        Assert.Equal(TacticsPhase.Results, s.Game.Phase);

        var again = Restarted(s);
        Assert.Equal(TacticsPhase.Resuming, again.Game.Phase);
        AllBack(again);

        Assert.Equal(TacticsPhase.Story, again.Game.Phase);
        Assert.Equal("ch1_road_east", Story(again).Scene);
    }

    [Fact]
    public void A_restart_in_an_aftermath_carries_on()
    {
        var s = InTheAftermath();
        s.Send(0, "next");
        LineEnds(s);
        LineEnds(s);
        var line = Story(s).Line!.Id;

        var again = Restarted(s);
        AllBack(again);

        Assert.Equal(("ch1_road_east", line), (Story(again).Scene, Story(again).Line!.Id));
        PlayOut(again);
        Assert.Contains("ch1_river_crossing", TacticsDocument.Read(again.Lease.Document).World!.Beats);
    }

    [Fact]
    public void Flags_arent_lost_or_doubled_by_a_replay()
    {
        var s = Telling(OldMaud);       // its first node sets ch1_met_maud
        var flags = TacticsDocument.Read(s.Lease.Document).World!.Flags;

        var again = Restarted(s);
        AllBack(again);
        var again2 = Restarted(again);
        AllBack(again2);

        Assert.Equal(flags, TacticsDocument.Read(again2.Lease.Document).World!.Flags);
        Assert.Single(flags, f => f == "ch1_met_maud");
    }

    [Fact]
    public void A_restart_with_no_scene_under_way_doesnt_wait()
    {
        var s = Near(Mayor);

        var again = Restarted(s);

        Assert.Equal(TacticsPhase.Exploration, again.Game.Phase);
    }

    [Fact]
    public void Someone_with_no_character_picks_one_before_the_scene_goes_on()
    {
        var s = Telling(Mayor);
        var doc = TacticsDocument.Read(s.Lease.Document);
        var without = doc with { Members = doc.Members.Remove(s.Profiles[3]!.Value.Value) };
        var again = new Session(document: without.ToJson(), game: new TacticsGame(Rules, lore: Lore));
        again.Harness.Resume(again.Lease);

        for (var p = 0; p < 3; p++)
            again.Harness.SetConnected(p, true);       // the three with characters: all that's awaited
        Assert.Equal(TacticsPhase.Story, again.Game.Phase);

        again.Harness.SetConnected(3, true);
        Assert.Equal(TacticsGame.Screens.PickClass, again.Screen(3)!.Screen);
    }
}
