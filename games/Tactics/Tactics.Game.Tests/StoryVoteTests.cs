using System.Text.Json;
using ArcBox.Sdk;
using Tactics.Logic;
using static Tactics.Game.Tests.StoryTests;
using static Tactics.Game.Tests.TacticsGameTests;

namespace Tactics.Game.Tests;

/// <summary>
/// Asking and voting (overworld 5d, Task 5): the question opens with the line that asks it, present players vote, the leader breaks ties and
/// makes the choices that aren't votes, and a question nobody answers takes its first option. Skill checks are <see cref="StoryCheckTests"/>.
/// </summary>
public sealed class StoryVoteTests
{
    private static readonly TimeSpan Ms1 = TimeSpan.FromMilliseconds(1);

    // ---- Getting to a question ------------------------------------------------------------------------------------------------

    /// <summary>Plays on, a tenth of a second at a time, until <paramref name="done"/> holds.</summary>
    internal static void Until(Session s, Func<bool> done, string what, TimeSpan? limit = null)
    {
        var end = s.Harness.Now + (limit ?? TimeSpan.FromMinutes(3));
        while (!done())
        {
            Assert.True(s.Harness.Now < end, $"never got to {what}");
            s.Harness.Advance(TimeSpan.FromMilliseconds(100));
        }
    }

    internal static StoryAskView Ask(Session s) => Story(s).Ask!;

    internal static void Vote(Session s, int seat, string option) => s.Send(seat, "vote", new { option });

    /// <summary>The ford choice's opening question, the leader's: what to ask the mayor first.</summary>
    internal static Session AtTheMayorsQuestion(int harnessSeed = 1)
    {
        var s = Telling(FordChoice, harnessSeed: harnessSeed);
        Until(s, () => Story(s).State == TacticsGame.StoryStates.Asking, "the mayor's question");
        Assert.Equal("open", Cursor(s)!.Node);
        return s;
    }

    /// <summary>The ford choice's party vote (mockup 02), its question heard: the leader asked about the cases.</summary>
    internal static Session AtTheVote(bool heard = true, int harnessSeed = 1)
    {
        var s = AtTheMayorsQuestion(harnessSeed);
        Vote(s, StageBattleTests.Lead(s), "cases");
        Until(s, () => Story(s).State == TacticsGame.StoryStates.Asking && Cursor(s)!.Node == "vote", "the vote");
        if (heard)
            LineEnds(s);
        return s;
    }

    /// <summary>When the open question was asked: as its line started.</summary>
    internal static DateTimeOffset OpenedAt(Session s)
    {
        var line = Story(s).Line!;
        return Story(s).Until!.Value - StoryTiming.Default.Line(Lore.ClipSeconds(line.Id), line.Text);
    }

    private static string[] Phones(Session s) => [.. Enumerable.Range(0, 4).Select(p => PhoneJson(s, p))];

    private static string[] Options(Session s) => [.. Ask(s).Options.Select(o => o.Id)];

    // ---- Opening ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Options_open_as_the_last_line_starts()
    {
        var s = Telling(FordChoice);
        var open = SceneNamed("ch1_ford").Node("open")!;
        Until(s, () => Cursor(s)!.Line == open.Lines.Length - 2, "the line before the question");
        Assert.Null(Story(s).Ask);
        Assert.Equal(TacticsGame.Screens.Dialogue, s.Screen(0)!.Screen);

        LineEnds(s);

        Assert.Equal(TacticsGame.StoryStates.Asking, Story(s).State);
        Assert.Equal(open.Lines[^1].Id, Story(s).Line!.Id);         // beside the line that asks it
        var soldier = s.Game.Document.Members.Values.Any(m => m.Character.Background == "soldier");
        Assert.Equal(soldier ? ["why", "truth", "cases"] : ["why", "cases"], Options(s));     // "truth" is for a soldier
        Assert.False(Ask(s).Party);
        Assert.Equal(TacticsGame.Screens.Vote, s.Screen(StageBattleTests.Lead(s))!.Screen);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_tagged_option_shows_for_a_party_that_has_the_tag(bool aSoldier)
    {
        var at = NearSpot(FordChoice);
        var doc = TacticsDocument.Read(ExplorationTests.DocumentAt(null));
        var last = doc.Members.Keys.Last();
        foreach (var member in doc.Members.Keys)
            doc = doc with
            {
                Members = doc.Members.SetItem(member, doc.Members[member] with
                {
                    Character = doc.Members[member].Character with { Background = aSoldier && member == last ? "soldier" : "sage" },
                }),
            };
        var s = new Session(document: doc.ToJson(), game: new TacticsGame(Rules, lore: Lore, seed: SeedFor(FordChoice) with { At = at }));
        s.Harness.StartCampaign(s.Lease);
        s.Harness.SetConnected(3, false);      // the soldier's away, and still counts (5d default 8)
        s.Send(StageBattleTests.Lead(s), "interact");
        Until(s, () => Story(s).State == TacticsGame.StoryStates.Asking, "the mayor's question");

        Assert.Equal(aSoldier ? ["why", "truth", "cases"] : ["why", "cases"], Options(s));
    }

    // ---- Who votes ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Only_present_players_vote()
    {
        var s = AtTheVote();
        s.Harness.SetConnected(3, false);

        Vote(s, 3, "force");         // a tap from a phone that's gone

        Assert.Equal((0, 3), (Ask(s).Voted, Ask(s).Of));
        Assert.All(Ask(s).Options, o => Assert.Empty(o.Voters));
    }

    /// <summary>
    /// The TV names who's away as it stands. A phone names who was away when the question was asked, so someone's phone sleeping mid-vote
    /// doesn't change everyone else's view under their thumbs.
    /// </summary>
    [Fact]
    public void Away_players_are_named()
    {
        var s = AtTheMayorsQuestion();
        s.Harness.SetConnected(3, false);
        Vote(s, StageBattleTests.Lead(s), "cases");
        Until(s, () => Cursor(s)!.Node == "vote" && Story(s).State == TacticsGame.StoryStates.Asking, "the vote");
        Assert.Equal(["Player4"], Ask(s).Away);
        Assert.Equal(["Player4"], Data(s, 0).GetProperty("away").EnumerateArray().Select(a => a.GetString()));
        var before = PhoneJson(s, 0);

        s.Harness.SetConnected(2, false);

        Assert.Equal(["Player3", "Player4"], Ask(s).Away);
        Assert.Equal(before, PhoneJson(s, 0));
    }

    [Fact]
    public void A_vote_changes_only_the_voters_phone()
    {
        var s = AtTheVote();
        var before = Phones(s);

        Vote(s, 1, "parley");

        var after = Phones(s);
        Assert.NotEqual(before[1], after[1]);
        Assert.Equal("parley", Data(s, 1).GetProperty("mine").GetString());
        foreach (var p in new[] { 0, 2, 3 })
            Assert.Equal(before[p], after[p]);
    }

    /// <summary>
    /// The last vote in starts the settle, and that changes no one else's phone: their countdown stays the question's own, so a change of
    /// mind in the settle isn't dropped for a view that moved under it.
    /// </summary>
    [Fact]
    public void The_last_vote_in_changes_only_the_last_voters_phone()
    {
        var s = AtTheVote();
        for (var p = 0; p < 3; p++)
            Vote(s, p, "parley");
        var before = Phones(s);

        Vote(s, 3, "force");

        Assert.Equal(s.Harness.Now + StoryTiming.Default.Settle, Ask(s).Deadline);      // the TV shows it closing
        var after = Phones(s);
        for (var p = 0; p < 3; p++)
            Assert.Equal(before[p], after[p]);
    }

    /// <summary>
    /// A scene that comes back to a question, as a hub does: Chapter 1 has none, so here Halden's answer to "why" leads back to the opening. An
    /// option asked once is gone the next time round, and stays gone across a restart (the nodes played are saved).
    /// </summary>
    [Fact]
    public void A_hub_doesnt_offer_a_question_already_asked_even_after_a_restart()
    {
        var lore = EditedLore(scenes =>
        {
            var ford = scenes.Single(x => x!["id"]!.GetValue<string>() == "ch1_ford")!;
            var why = ford["nodes"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == "why")!;
            why.AsObject().Remove("options");
            why["next"] = "open";
        });
        var s = Telling(FordChoice, lore);
        Until(s, () => Story(s).State == TacticsGame.StoryStates.Asking, "the mayor's question");
        Assert.Contains("why", Options(s));
        Vote(s, StageBattleTests.Lead(s), "why");
        Until(s, () => Cursor(s)!.Node == "why", "Halden's answer");
        Until(s, () => Story(s).State == TacticsGame.StoryStates.Asking && Cursor(s)!.Node == "open", "the mayor's question again");

        Assert.DoesNotContain("why", Options(s));
        Assert.Contains("cases", Options(s));

        var again = new Session(document: s.Lease.Document, game: new TacticsGame(Rules, lore: lore));
        again.Harness.Resume(again.Lease);
        StoryResumeTests.AllBack(again);
        Assert.Equal(TacticsGame.StoryStates.Asking, Story(again).State);
        Assert.DoesNotContain("why", Ask(again).Options.Select(o => o.Id));
    }

    [Fact]
    public void Four_votes_in_one_tick_all_count()
    {
        var s = AtTheVote(heard: false);

        Vote(s, 0, "force");
        Vote(s, 1, "parley");
        Vote(s, 2, "parley");
        Vote(s, 3, "pay");

        Assert.Equal((4, 4), (Ask(s).Voted, Ask(s).Of));
        Assert.Equal(["Player1"], Ask(s).Options.Single(o => o.Id == "force").Voters);
        Assert.Equal(["Player2", "Player3"], Ask(s).Options.Single(o => o.Id == "parley").Voters);
        Assert.Equal(["Player4"], Ask(s).Options.Single(o => o.Id == "pay").Voters);
        Assert.DoesNotContain(Data(s, 0).GetProperty("options").EnumerateArray(), o => o.TryGetProperty("voters", out _));   // no tally on a phone
    }

    [Fact]
    public void A_vote_can_change_before_it_closes()
    {
        var s = AtTheVote();

        Vote(s, 1, "force");
        Vote(s, 1, "parley");

        Assert.Equal(1, Ask(s).Voted);
        Assert.Equal(["Player2"], Ask(s).Options.Single(o => o.Id == "parley").Voters);
        Assert.Empty(Ask(s).Options.Single(o => o.Id == "force").Voters);
    }

    [Fact]
    public void A_vote_for_something_not_offered_counts_for_nothing()
    {
        var s = AtTheVote();

        Vote(s, 1, "cases");        // the last question's
        Vote(s, 2, "nonsense");

        Assert.Equal(0, Ask(s).Voted);
    }

    // ---- Closing ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Everyone_voting_closes_after_the_settle()
    {
        var s = AtTheVote();
        for (var p = 0; p < 4; p++)
            Vote(s, p, p == 0 ? "force" : "parley");

        Assert.Equal(s.Harness.Now + StoryTiming.Default.Settle, Ask(s).Deadline);
        s.Harness.Advance(StoryTiming.Default.Settle - Ms1);
        Vote(s, 0, "parley");       // one last change, inside the settle
        Assert.Equal(TacticsGame.StoryStates.Asking, Story(s).State);
        s.Harness.Advance(Ms1);

        Assert.Equal("parley", Cursor(s)!.Node);
    }

    [Fact]
    public void A_close_waits_for_the_question_to_be_heard()
    {
        var s = AtTheVote(heard: false);
        var heard = Story(s).Until!.Value;
        Assert.True(heard - s.Harness.Now > StoryTiming.Default.Settle);

        for (var p = 0; p < 4; p++)
            Vote(s, p, "force");

        Assert.Equal(heard, Ask(s).Deadline);
        s.Harness.Advance(heard - s.Harness.Now - Ms1);
        Assert.Equal("vote", Cursor(s)!.Node);
        s.Harness.Advance(Ms1);
        Assert.Equal("force", Cursor(s)!.Node);
    }

    [Fact]
    public void An_unanswered_vote_takes_the_first_option()
    {
        var s = AtTheVote(heard: false);
        var deadline = Ask(s).Deadline!.Value;
        Assert.Equal(deadline, s.Screen(0)!.DeadlineUtc);      // the phones' countdown is the TV's
        Assert.Equal(OpenedAt(s) + StoryTiming.Default.Vote, deadline);

        s.Harness.Advance(deadline - s.Harness.Now - Ms1);
        Assert.Equal("vote", Cursor(s)!.Node);
        s.Harness.Advance(Ms1);

        Assert.Equal("force", Cursor(s)!.Node);      // the user's decision 2: by force
    }

    [Fact]
    public void The_winning_choice_sets_its_flags()
    {
        var s = AtTheVote();
        for (var p = 0; p < 4; p++)
            Vote(s, p, "parley");

        s.Harness.Advance(StoryTiming.Default.Settle);

        Assert.Contains("ch1_skarrow_spared", TacticsDocument.Read(s.Lease.Document).World!.Flags);
        Assert.DoesNotContain("ch1_skarrow_slain", TacticsDocument.Read(s.Lease.Document).World!.Flags);
    }

    [Fact]
    public void A_decided_vote_is_saved_with_its_flags()
    {
        var s = AtTheVote();
        for (var p = 0; p < 4; p++)
            Vote(s, p, "force");
        var saves = s.Lease.Saves.Count;

        s.Harness.Advance(StoryTiming.Default.Settle);

        // The first save after the close has the choice's flag and the cursor on the node it chose, together.
        var first = TacticsDocument.Read(s.Lease.Saves[saves]).World!;
        Assert.Contains("ch1_skarrow_slain", first.Flags);
        Assert.Equal(("force", 0), (first.Story!.Node, first.Story.Line));
    }

    // ---- Ties ------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A 1–1 tie between force and parley, with the leader not voting and the fourth player away: the vote runs its time, and the leader
    /// breaks the tie. (A leader whose vote is in the tie decides at once: <see cref="A_tied_leader_decides_at_once"/>.)
    /// </summary>
    private static Session Tied()
    {
        var s = AtTheVote();
        var lead = StageBattleTests.Lead(s);
        var others = Enumerable.Range(0, 4).Where(p => p != lead).ToArray();
        s.Harness.SetConnected(others[2], false);
        Vote(s, others[0], "force");
        Vote(s, others[1], "parley");
        s.Harness.Advance(Ask(s).Deadline!.Value - s.Harness.Now);
        Assert.Equal(TacticsGame.StoryStates.TieBreak, Story(s).State);
        return s;
    }

    [Fact]
    public void A_tied_leader_decides_at_once()
    {
        var s = AtTheVote();
        var lead = StageBattleTests.Lead(s);
        var others = Enumerable.Range(0, 4).Where(p => p != lead).ToArray();
        Vote(s, lead, "parley");
        Vote(s, others[0], "force");
        Vote(s, others[1], "force");
        Vote(s, others[2], "parley");

        s.Harness.Advance(StoryTiming.Default.Settle);

        Assert.Equal("parley", Cursor(s)!.Node);      // 2–2, and the leader's vote is one of the two
    }

    [Fact]
    public void A_tie_break_goes_to_the_leader()
    {
        var s = Tied();
        var lead = StageBattleTests.Lead(s);

        var ask = Ask(s);
        Assert.True(ask.Tie);
        Assert.Equal(s.Game.Leader!.Name, ask.Decider);
        Assert.Equal(["force", "parley"], ask.Options.Select(o => o.Id));     // only the tied
        Assert.Equal(s.Harness.Now + StoryTiming.Default.TieBreak, ask.Deadline);
        var data = Data(s, lead);
        Assert.True(data.GetProperty("canChoose").GetBoolean());
        Assert.True(data.GetProperty("tie").GetBoolean());
        Assert.Equal(["force", "parley"], data.GetProperty("options").EnumerateArray().Select(o => o.GetProperty("id").GetString()));
        Assert.False(Data(s, (lead + 1) % 4 is var other && s.Harness.Players[other].Id != s.Game.Leader.Id ? other : (lead + 2) % 4)
            .GetProperty("canChoose").GetBoolean());

        Vote(s, (lead + 1) % 4, "force");      // not the decider's to break
        Assert.Equal(TacticsGame.StoryStates.TieBreak, Story(s).State);
        Vote(s, lead, "pay");                  // not one of the tied
        Assert.Equal(TacticsGame.StoryStates.TieBreak, Story(s).State);
        Vote(s, lead, "parley");

        Assert.Equal("parley", Cursor(s)!.Node);
    }

    [Fact]
    public void A_leader_who_didnt_vote_gets_a_tie_break()
    {
        var s = Tied();

        Assert.Equal(s.Game.Leader!.Name, Ask(s).Decider);
    }

    [Fact]
    public void A_tie_break_unanswered_takes_the_first_tied()
    {
        var s = Tied();

        s.Harness.Advance(StoryTiming.Default.TieBreak - Ms1);
        Assert.Equal(TacticsGame.StoryStates.TieBreak, Story(s).State);
        s.Harness.Advance(Ms1);

        Assert.Equal("force", Cursor(s)!.Node);
    }

    [Fact]
    public void A_leader_lost_on_a_tie_hands_it_on()
    {
        var s = Tied();
        var (lead, deadline) = (StageBattleTests.Lead(s), Ask(s).Deadline);

        s.Harness.SetConnected(lead, false);

        var next = StageBattleTests.Lead(s);
        Assert.NotEqual(lead, next);
        Assert.Equal(s.Game.Leader!.Name, Ask(s).Decider);
        Assert.Equal(deadline, Ask(s).Deadline);       // its timer unchanged
        Assert.True(Data(s, next).GetProperty("canChoose").GetBoolean());
        Vote(s, next, "parley");
        Assert.Equal("parley", Cursor(s)!.Node);
    }

    // ---- Phones coming and going ---------------------------------------------------------------------------------------------------

    [Fact]
    public void A_voter_who_drops_keeps_their_vote()
    {
        var s = AtTheVote();
        Vote(s, 1, "parley");

        s.Harness.SetConnected(1, false);
        Assert.Equal((1, 4), (Ask(s).Voted, Ask(s).Of));       // still counted, and still one of those who voted
        s.Harness.Advance(StoryTiming.Default.Vote);

        Assert.Equal("parley", Cursor(s)!.Node);
    }

    [Fact]
    public void A_dropped_voter_no_longer_holds_up_the_close()
    {
        var s = AtTheVote();
        for (var p = 0; p < 3; p++)
            Vote(s, p, "parley");
        Assert.NotEqual(s.Harness.Now + StoryTiming.Default.Settle, Ask(s).Deadline);

        s.Harness.SetConnected(3, false);

        Assert.Equal(s.Harness.Now + StoryTiming.Default.Settle, Ask(s).Deadline);
        s.Harness.Advance(StoryTiming.Default.Settle);
        Assert.Equal("parley", Cursor(s)!.Node);
    }

    [Fact]
    public void Someone_joining_can_vote_and_holds_the_close_until_they_do()
    {
        var s = AtTheVote();
        s.Harness.SetConnected(3, false);
        for (var p = 0; p < 3; p++)
            Vote(s, p, "parley");
        Assert.Equal(s.Harness.Now + StoryTiming.Default.Settle, Ask(s).Deadline);

        s.Harness.SetConnected(3, true);
        Assert.Equal((3, 4), (Ask(s).Voted, Ask(s).Of));
        Assert.NotEqual(s.Harness.Now + StoryTiming.Default.Settle, Ask(s).Deadline);
        Vote(s, 3, "force");

        Assert.Equal(s.Harness.Now + StoryTiming.Default.Settle, Ask(s).Deadline);
    }

    [Fact]
    public void Nobody_connected_holds_the_question_and_runs_no_timer()
    {
        var s = AtTheVote();
        for (var p = 0; p < 4; p++)
            s.Harness.SetConnected(p, false);

        Assert.DoesNotContain(TacticsGame.Timers.Vote, s.Harness.ActiveTimers);
        Assert.Null(Ask(s).Deadline);
        s.Harness.Advance(TimeSpan.FromMinutes(10));

        Assert.Equal(TacticsGame.StoryStates.Asking, Story(s).State);
        Assert.Equal("vote", Cursor(s)!.Node);
    }

    [Fact]
    public void A_phone_coming_back_restarts_the_timer()
    {
        var s = AtTheVote();
        s.Harness.Advance(TimeSpan.FromSeconds(30));
        for (var p = 0; p < 4; p++)
            s.Harness.SetConnected(p, false);
        s.Harness.Advance(TimeSpan.FromMinutes(2));

        s.Harness.SetConnected(2, true);

        Assert.Equal(s.Harness.Now + StoryTiming.Default.Vote, Ask(s).Deadline);      // in full
        Assert.Equal(s.Harness.Now + StoryTiming.Default.Vote, s.Screen(2)!.DeadlineUtc);
    }

    [Fact]
    public void Lines_run_on_with_nobody_connected()
    {
        var s = AtTheMayorsQuestion();
        Vote(s, StageBattleTests.Lead(s), "cases");
        for (var p = 0; p < 4; p++)
            s.Harness.SetConnected(p, false);

        Until(s, () => Story(s).State == TacticsGame.StoryStates.Asking && Cursor(s)!.Node == "vote", "the vote, with nobody here");

        Assert.Equal("vote", Cursor(s)!.Node);      // every line of "cases" played, and the vote holds
        Assert.Null(Ask(s).Deadline);
    }

    // ---- Choices that are the leader's ------------------------------------------------------------------------------------------------

    [Fact]
    public void A_choice_is_the_leaders_alone()
    {
        var s = AtTheMayorsQuestion();
        var lead = StageBattleTests.Lead(s);
        var other = (lead + 1) % 4;
        LineEnds(s);

        Assert.False(Data(s, other).GetProperty("canChoose").GetBoolean());
        Assert.Equal(s.Game.Leader!.Name, Data(s, other).GetProperty("decider").GetString());
        Vote(s, other, "why");
        Assert.Equal((0, 1), (Ask(s).Voted, Ask(s).Of));

        Assert.True(Data(s, lead).GetProperty("canChoose").GetBoolean());
        Vote(s, lead, "why");
        Assert.Equal(1, Ask(s).Voted);
        s.Harness.Advance(StoryTiming.Default.Settle);

        Assert.Equal("why", Cursor(s)!.Node);
    }

    /// <summary>
    /// A leader who takes over a choice already made sees it as the party's (<c>chosen</c>), not as theirs (<c>mine</c>): they never made
    /// it. It stands unless they change it. Everyone else sees the pick read-only.
    /// </summary>
    [Fact]
    public void A_new_leader_sees_an_inherited_choice_as_the_partys()
    {
        var s = AtTheMayorsQuestion();
        var lead = StageBattleTests.Lead(s);
        Vote(s, lead, "why");
        Assert.Equal("why", Data(s, lead).GetProperty("mine").GetString());
        Assert.Equal("why", Data(s, (lead + 1) % 4).GetProperty("chosen").GetString());

        s.Harness.SetConnected(lead, false);

        var next = StageBattleTests.Lead(s);
        Assert.Equal(JsonValueKind.Null, Data(s, next).GetProperty("mine").ValueKind);
        Assert.Equal("why", Data(s, next).GetProperty("chosen").GetString());
        Vote(s, next, "cases");
        Assert.Equal("cases", Data(s, next).GetProperty("mine").GetString());
        s.Harness.Advance(Ask(s).Deadline!.Value - s.Harness.Now);
        Assert.Equal("cases", Cursor(s)!.Node);
    }

    [Fact]
    public void An_unanswered_choice_takes_its_first_option_after_30_seconds()
    {
        var s = AtTheMayorsQuestion();
        var opened = OpenedAt(s);
        Assert.Equal(opened + StoryTiming.Default.Choice, Ask(s).Deadline);

        s.Harness.Advance(opened + StoryTiming.Default.Choice - s.Harness.Now - Ms1);
        Assert.Equal("open", Cursor(s)!.Node);
        s.Harness.Advance(Ms1);

        Assert.Equal("why", Cursor(s)!.Node);
    }

    [Fact]
    public void A_question_already_asked_isnt_offered_again()
    {
        var s = AtTheMayorsQuestion();
        Vote(s, StageBattleTests.Lead(s), "why");
        Until(s, () => Story(s).State == TacticsGame.StoryStates.Asking && Cursor(s)!.Node == "why", "Halden's answer");

        Assert.Equal("why", Cursor(s)!.Node);
        Assert.Equal(["insight", "fair"], Options(s));
    }
}
