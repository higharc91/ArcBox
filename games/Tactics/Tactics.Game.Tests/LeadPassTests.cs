using System.Text.Json;
using static Tactics.Game.Tests.StoryTests;
using static Tactics.Game.Tests.TacticsGameTests;

namespace Tactics.Game.Tests;

/// <summary>#154: the leader can hand the lead on, and followers can point things out on the TV.</summary>
public sealed class LeadPassTests
{
    private static string ProfileOf(Session s, int player) => s.Harness.Players[player].Profile!.Value.Value;

    /// <summary>A session in the square where every phone has been heard from and the lead has settled.</summary>
    private static Session Settled()
    {
        var s = Near(Mayor);
        for (var p = 0; p < 4; p++) s.Send(p, "walk", new { dir = (string?)null });
        s.Harness.Advance(TimeSpan.FromSeconds(5));
        return s;
    }

    [Fact]
    public void A_passed_lead_sticks_and_only_the_leader_can_pass_it()
    {
        var s = Settled();
        var lead = StageBattleTests.Lead(s);
        var (heir, other) = ((lead + 1) % 4, (lead + 2) % 4);

        var offered = Data(s, lead).GetProperty("passTo").EnumerateArray().Select(h => h.GetProperty("id").GetString()).ToList();
        Assert.Contains(ProfileOf(s, heir), offered);
        Assert.DoesNotContain(ProfileOf(s, lead), offered);
        Assert.False(Data(s, heir).TryGetProperty("passTo", out _));         // only the leader gets the choice

        s.Send(other, "passLead", new { to = ProfileOf(s, other) });          // a follower can't take it
        Assert.Equal(lead, StageBattleTests.Lead(s));

        s.Send(lead, "passLead", new { to = ProfileOf(s, heir) });
        Assert.Equal(heir, StageBattleTests.Lead(s));
        for (var t = 0; t < 6; t++)                                             // 30 s, well past the stand-in takeback, with the heir
        {                                                                       // still at their phone (a silent leader is skipped)
            s.Harness.Advance(TimeSpan.FromSeconds(5));
            s.Send(heir, "walk", new { dir = (string?)null });
        }
        Assert.Equal(heir, StageBattleTests.Lead(s));
        Assert.Equal(ProfileOf(s, heir), s.Game.Document.World!.LastLeader);
        Assert.Contains("leads now", s.Host.Notice);
    }

    [Fact]
    public void A_follower_points_something_out_on_the_TV_now_and_then()
    {
        var s = Near(Mayor);
        var follower = (StageBattleTests.Lead(s) + 1) % 4;
        var target = Data(s, follower).GetProperty("pointAt").EnumerateArray().First();
        var label = target.GetProperty("label").GetString()!;
        Assert.False(Data(s, StageBattleTests.Lead(s)).TryGetProperty("pointAt", out _));   // the leader just does it

        s.Send(follower, "point", new { target = target.GetProperty("id").GetString() });
        var said = s.Host.Explore!.ThingSpeech!;
        Assert.Equal($"{s.Harness.Players[follower].Name} suggests: {label}.", said.Line.Text);

        s.Send(follower, "point", new { target = target.GetProperty("id").GetString() });
        Assert.Equal(said.Seq, s.Host.Explore!.ThingSpeech!.Seq);             // not again so soon
        s.Harness.Advance(TacticsGame.PointEvery);
        s.Send(follower, "point", new { target = target.GetProperty("id").GetString() });
        Assert.True(s.Host.Explore!.ThingSpeech!.Seq > said.Seq);
    }
}
