using System.Text.Json;
using ArcBox.Sdk;
using Tactics.Logic;
using static Tactics.Game.Tests.StoryTests;
using static Tactics.Game.Tests.StoryVoteTests;
using static Tactics.Game.Tests.TacticsGameTests;

namespace Tactics.Game.Tests;

/// <summary>
/// Skill checks in a scene (overworld 5d, Task 5): a check that wins goes to the party to pick who rolls, away characters included; nobody
/// picking takes the best bonus; the game's die decides pass or fail, saved with the node it leads to, and the TV is never waited for.
/// </summary>
public sealed class StoryCheckTests
{
    private static readonly TimeSpan Ms1 = TimeSpan.FromMilliseconds(1);

    private static StoryCheckView Check(Session s) => Story(s).Check!;

    private static void Pick(Session s, int seat, string member) => s.Send(seat, "pickRoller", new { member });

    private static string Member(Session s, int seat) => s.Profiles[seat]!.Value.Value;

    /// <summary>The ford vote, with everyone voting to ask for more pay: the check is the party's to roll.</summary>
    private static Session Paying(int harnessSeed = 1, int away = -1)
    {
        var s = AtTheVote(harnessSeed: harnessSeed);
        if (away >= 0)
            s.Harness.SetConnected(away, false);
        for (var p = 0; p < 4; p++)
            if (p != away)
                Vote(s, p, "pay");
        s.Harness.Advance(StoryTiming.Default.Settle);
        Assert.Equal(TacticsGame.StoryStates.PickRoller, Story(s).State);
        return s;
    }

    private static int BonusOf(Session s, int seat, string skill)
        => SkillCheck.Bonus(s.Game.Document.For(s.Profiles[seat]!.Value)!.Character, skill, Rules);

    [Fact]
    public void Pay_goes_to_a_check_and_then_vote2()
    {
        var s = Paying();

        var check = Check(s);
        Assert.Equal(("persuasion", 15), (check.Skill, check.Dc));
        Assert.Equal(SceneNamed("ch1_ford").Node("vote")!.Options.Single(o => o.Id == "pay").Label, check.Title);
        Assert.Null(check.Chosen);
        Assert.Null(check.Roll);
        Assert.Equal(TacticsGame.Screens.Check, s.Screen(0)!.Screen);

        for (var p = 0; p < 4; p++)
            Pick(s, p, Member(s, 2));
        s.Harness.Advance(StoryTiming.Default.Settle);
        Assert.Equal(TacticsGame.StoryStates.Rolled, Story(s).State);
        Assert.Equal(Member(s, 2), Check(s).Chosen);
        s.Harness.Advance(StoryTiming.Default.Reveal);

        Assert.Contains(Cursor(s)!.Node, new[] { "pay_pass", "pay_fail" });
        Until(s, () => Story(s).State == TacticsGame.StoryStates.Asking, "the second vote");
        Assert.Equal("vote2", Cursor(s)!.Node);
        Assert.Equal(["force", "parley"], Ask(s).Options.Select(o => o.Id));
    }

    [Fact]
    public void Check_cards_include_an_away_character()
    {
        var s = Paying(away: 3);

        var cards = Check(s).Cards;
        Assert.Equal(4, cards.Length);
        var away = cards.Single(c => c.Member == Member(s, 3));
        Assert.False(away.Here);
        Assert.Equal("Player4", away.Player);
        Assert.Equal(BonusOf(s, 3, "persuasion"), away.Bonus);
        Assert.All(cards.Where(c => c != away), c => Assert.True(c.Here));
        var phone = Data(s, 0).GetProperty("cards").EnumerateArray().Single(c => c.GetProperty("id").GetString() == Member(s, 3));
        Assert.False(phone.GetProperty("here").GetBoolean());

        Pick(s, 0, Member(s, 3));
        Pick(s, 1, Member(s, 3));
        Pick(s, 2, Member(s, 3));
        s.Harness.Advance(StoryTiming.Default.Settle);
        Assert.Equal(Member(s, 3), Cursor(s)!.Roll!.Member);       // eligible though away
    }

    [Fact]
    public void A_cards_note_says_where_its_bonus_comes_from()
    {
        var s = Paying();

        foreach (var card in Check(s).Cards)
        {
            var sheet = SheetBuilder.ForCharacter(s.Game.Document.Members[card.Member].Character, Rules);
            var score = $"CHA {sheet.Score(Stat.Cha)}";
            Assert.Equal(SkillCheck.Proficient(sheet, "persuasion") ? $"Proficient · {score}" : score, card.Note);
            Assert.Equal(Rules.Classes[s.Game.Document.Members[card.Member].Character.Class].Name, card.Class);   // for the TV's card, as the phone's has it
        }
    }

    [Fact]
    public void The_party_picks_who_rolls()
    {
        var s = Paying();
        var before = Enumerable.Range(0, 4).Select(p => PhoneJson(s, p)).ToArray();

        Pick(s, 0, Member(s, 1));
        Assert.Equal(before[1], PhoneJson(s, 1));          // a pick changes only the picker's phone
        Assert.NotEqual(before[0], PhoneJson(s, 0));
        Assert.Equal(Member(s, 1), Data(s, 0).GetProperty("mine").GetString());
        Pick(s, 1, Member(s, 1));
        Pick(s, 2, Member(s, 2));
        Pick(s, 3, "pr_nobody");                            // not a card: counts for nothing
        Assert.Equal(1, Check(s).Cards.Single(c => c.Member == Member(s, 2)).Votes);
        Assert.Equal(2, Check(s).Cards.Single(c => c.Member == Member(s, 1)).Votes);

        s.Harness.Advance(StoryTiming.Default.Roller);

        Assert.Equal(Member(s, 1), Cursor(s)!.Roll!.Member);
    }

    [Fact]
    public void An_unanswered_pick_takes_the_best_bonus()
    {
        var s = Paying();
        var best = SkillCheck.Best(Enumerable.Range(0, 4).Select(p => (Member(s, p), BonusOf(s, p, "persuasion")))
            .OrderBy(c => c.Item1, StringComparer.Ordinal));

        s.Harness.Advance(StoryTiming.Default.Roller - Ms1);
        Assert.Equal(TacticsGame.StoryStates.PickRoller, Story(s).State);
        s.Harness.Advance(Ms1);

        Assert.Equal(best, Cursor(s)!.Roll!.Member);
    }

    [Fact]
    public void A_tied_pick_goes_to_the_leader()
    {
        var s = Paying();
        var lead = StageBattleTests.Lead(s);
        var others = Enumerable.Range(0, 4).Where(p => p != lead).ToArray();
        s.Harness.SetConnected(others[2], false);
        Pick(s, lead, Member(s, lead));
        Pick(s, others[0], Member(s, others[0]));
        Pick(s, others[1], Member(s, others[1]));
        s.Harness.Advance(StoryTiming.Default.Settle);

        Assert.Equal(Member(s, lead), Cursor(s)!.Roll!.Member);     // a three-way tie the leader is in: theirs at once
    }

    [Fact]
    public void A_roll_goes_to_pass_or_fail()
    {
        for (var seed = 1; seed <= 12; seed++)
        {
            var s = Paying(harnessSeed: seed);
            s.Harness.Advance(StoryTiming.Default.Roller);

            var roll = Cursor(s)!.Roll!;
            var bonus = SkillCheck.Bonus(s.Game.Document.Members[roll.Member].Character, "persuasion", Rules);
            Assert.InRange(roll.Die, 1, 20);
            Assert.Equal(bonus, roll.Bonus);
            Assert.Equal(roll.Die + roll.Bonus >= 15, roll.Passed);       // no natural 20 or 1 (5d default 7)
            Assert.Equal(roll.Passed ? "pay_pass" : "pay_fail", Cursor(s)!.Node);
            Assert.Equal((roll.Die, roll.Die + roll.Bonus, roll.Passed), (Check(s).Roll!.Kept, Check(s).Roll!.Total, Check(s).Roll!.Success));
        }
    }

    [Fact]
    public void A_roll_doesnt_wait_for_the_tv()
    {
        var s = Paying();
        s.Harness.Advance(StoryTiming.Default.Roller);
        Assert.Equal(TacticsGame.StoryStates.Rolled, Story(s).State);
        var next = Cursor(s)!.Node!;

        s.Harness.Advance(StoryTiming.Default.Reveal);

        Assert.Equal(TacticsGame.StoryStates.Lines, Story(s).State);
        Assert.Equal(SceneNamed("ch1_ford").Node(next)!.Lines[0].Id, Story(s).Line!.Id);
        Assert.Null(Cursor(s)!.Roll);
    }

    [Fact]
    public void The_roll_is_saved_with_the_node_it_leads_to()
    {
        var s = Paying();
        var saves = s.Lease.Saves.Count;

        s.Harness.Advance(StoryTiming.Default.Roller);

        var first = TacticsDocument.Read(s.Lease.Saves[saves]).World!.Story!;
        Assert.NotNull(first.Roll);
        Assert.Equal((first.Roll!.Passed ? "pay_pass" : "pay_fail", 0), (first.Node, first.Line));
    }

    [Fact]
    public void A_restart_after_a_roll_doesnt_roll_again()
    {
        var s = Paying();
        s.Harness.Advance(StoryTiming.Default.Roller);
        var roll = Cursor(s)!.Roll!;

        var again = new Session(seed: 99, document: s.Lease.Document, game: new TacticsGame(Rules, lore: Lore));
        again.Harness.Resume(again.Lease);
        StoryResumeTests.AllBack(again);

        Assert.Equal(TacticsGame.StoryStates.Rolled, Story(again).State);
        Assert.Equal(roll, Cursor(again)!.Roll);
        Assert.Equal((roll.Die, roll.Die + roll.Bonus), (Story(again).Check!.Roll!.Kept, Story(again).Check!.Roll!.Total));
        again.Harness.Advance(StoryTiming.Default.Reveal);
        Assert.Equal(roll.Passed ? "pay_pass" : "pay_fail", Cursor(again)!.Node);
    }

    [Fact]
    public void An_unanswered_choice_doesnt_spend_a_roll()
    {
        var s = AtTheMayorsQuestion();
        Vote(s, StageBattleTests.Lead(s), "why");
        Until(s, () => Story(s).State == TacticsGame.StoryStates.Asking && Cursor(s)!.Node == "why", "Halden's answer");
        Assert.Equal(["insight", "fair"], Story(s).Ask!.Options.Select(o => o.Id));     // the check comes first

        s.Harness.Advance(StoryTiming.Default.Choice);

        Assert.Equal("cases", Cursor(s)!.Node);      // the way on, with no roll
        Assert.Null(Cursor(s)!.Roll);
    }

    [Fact]
    public void A_choice_of_a_check_still_lets_the_party_pick_who_rolls()
    {
        var s = AtTheMayorsQuestion();
        Vote(s, StageBattleTests.Lead(s), "why");
        Until(s, () => Story(s).State == TacticsGame.StoryStates.Asking && Cursor(s)!.Node == "why", "Halden's answer");
        LineEnds(s);

        Vote(s, StageBattleTests.Lead(s), "insight");
        s.Harness.Advance(StoryTiming.Default.Settle);

        Assert.Equal(TacticsGame.StoryStates.PickRoller, Story(s).State);
        Assert.Equal(("insight", 13), (Check(s).Skill, Check(s).Dc));
        Assert.True(Data(s, (StageBattleTests.Lead(s) + 1) % 4).GetProperty("canChoose").GetBoolean());
    }
}
