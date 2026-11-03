using static Tactics.Game.Tests.TacticsGameTests;

namespace Tactics.Game.Tests;

public sealed class WorldRecoveryTests
{
    [Theory]
    [InlineData("walk")]
    [InlineData("story")]
    [InlineData("interact")]
    [InlineData("shop")]
    [InlineData("rest")]
    [InlineData("quest")]
    [InlineData("timer")]
    public void Failed_world_actions_keep_members_and_return_to_a_usable_world(string path)
    {
        var s = path switch
        {
            "story" => StoryVoteTests.AtTheMayorsQuestion(),
            "shop" => ShopTests.AtShop(),
            "quest" => ConversationQuestTests.At("halden_varrow", "sq_grain_asked"),
            "rest" or "interact" => RestTests.AtCamp(),
            _ => Walking(),
        };
        if (path == "shop") s.Send(0, "interact", new { target = "shop:af_market_herbalist" });
        if (path == "rest") { s.Send(0, "interact", new { target = "fire" }); RestTests.Vote(s); }
        var members = s.Game.Document.Members;
        var shops = s.Game.Document.Shops;
        var pin = s.Game.Document.World!.PinnedQuest;
        s.Game.WorldActionCompleted = _ => throw new InvalidOperationException("injected " + path);
        switch (path)
        {
            case "timer": s.Game.OnTimer(TacticsGame.Timers.Lead); break;
            case "walk": s.Send(0, "walk", new { dir = "e" }); break;
            case "story": s.Send(0, "vote", new { option = "goblins" }); break;
            case "shop": s.Send(0, "shopBuy", new { shop = "af_market_herbalist", item = "potion_of_healing", count = 1, trade = "rollback" }); break;
            case "rest": s.Send(0, "campJob", new { job = "cook" }); break;
            case "quest": s.Send(0, "questLog", new { action = "pin", quest = "sq_missing_grain" }); break;
            default: s.Send(0, "interact", new { target = "fire" }); break;
        }
        Assert.Equal(members, s.Game.Document.Members);
        Assert.Equal(shops, s.Game.Document.Shops);
        Assert.Equal(pin, s.Game.Document.World!.PinnedQuest);
        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
        Assert.Null(s.Game.Document.World!.Story);
        Assert.Null(s.Game.Document.World.Camp);
        Assert.Null(s.Game.Document.World.Shop);
        Assert.Contains("changed", s.Host.Notice);
        Assert.Contains("changed", s.Screen(0)!.Header);
        s.Game.WorldActionCompleted = null;
        var leader = Enumerable.Range(0, 4).First(i => s.Screen(i)!.Data.GetProperty("leader").GetBoolean());
        var seq = s.Host.Explore!.Seq;
        foreach (var dir in new[] { "e", "w", "ne", "sw", "n", "s", "se", "nw" })
        {
            s.Send(leader, "walk", new { dir });
            s.Send(leader, "walk", new { dir = (string?)null });
            s.Harness.Advance(TimeSpan.FromSeconds(1));
            if (s.Host.Explore!.Seq != seq) break;
        }
        Assert.NotEqual(seq, s.Host.Explore!.Seq);
        Assert.Contains("changed", s.Screen(leader)!.Header);
        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
    }

    [Fact]
    public void A_failed_resume_saves_a_safe_fallback_that_can_be_reopened()
    {
        var original = Walking();
        var game = new TacticsGame(Rules) { WorldActionCompleted = _ => throw new InvalidOperationException("resume failure") };
        var resumed = new Session(document: original.Game.Document.ToJson(), game: game);
        resumed.Harness.Resume(resumed.Lease);
        Assert.Equal(TacticsPhase.Exploration, game.Phase);
        Assert.Contains("changed", resumed.Host.Notice);
        var reopened = new Session(document: resumed.Lease.Document);
        reopened.Harness.Resume(reopened.Lease);
        for (var i = 0; i < 4; i++) reopened.Harness.SetConnected(i, true);
        Assert.Equal(TacticsPhase.Exploration, reopened.Game.Phase);
    }

    [Fact]
    public void A_failed_normal_session_reopen_also_recovers_the_saved_world()
    {
        var original = Walking();
        var game = new TacticsGame(Rules) { WorldActionCompleted = _ => throw new InvalidOperationException("session reopen failure") };
        var reopened = new Session(document: original.Game.Document.ToJson(), game: game);
        reopened.Harness.StartCampaign(reopened.Lease);
        Assert.Equal(TacticsPhase.Exploration, game.Phase);
        Assert.Contains("changed", reopened.Host.Notice);
        Assert.Null(game.Document.World!.Story);
    }

    [Fact]
    public void Invalid_saved_battle_is_called_off_without_rewards_or_injuries()
    {
        var original = new Session(); original.ToBattle();
        var doc = original.Game.Document;
        var state = doc.Battle!.State;
        var unit = state.Units.First();
        doc = doc with { Battle = doc.Battle with { State = state with { Units = state.Units.SetItem(unit.Key, unit.Value with { Hp = -1 }) } } };
        var resumed = new Session(document: doc.ToJson());
        resumed.Harness.Resume(resumed.Lease);
        Assert.Null(resumed.Game.Document.Battle);
        Assert.Equal(doc.ToJson().GetProperty("members").GetRawText(), resumed.Game.Document.ToJson().GetProperty("members").GetRawText());
        Assert.Contains("called off", resumed.Host.Notice);
    }

    [Fact]
    public void A_failed_quest_payout_does_not_keep_gold_xp_or_the_paid_marker()
    {
        var s = ConversationQuestTests.At("halden_varrow", "sq_grain_asked", "sq_grain_clue", "sq_grain_tally");
        ConversationQuestTests.Talk(s, "halden_varrow");
        var before = s.Game.Document;
        var failed = false;
        s.Game.WorldActionCompleted = _ =>
        {
            if (s.Game.Document.Members.Values.Sum(m => m.Xp) <= before.Members.Values.Sum(m => m.Xp)) return;
            failed = true;
            throw new InvalidOperationException("quest payout failed");
        };
        ConversationQuestTests.Choose(s, "quiet");
        StoryVoteTests.Until(s, () => failed, "the injected reward failure");
        Assert.Equal(before.Members, s.Game.Document.Members);
        Assert.True(s.Game.Document.World!.QuestsRewarded.IsDefaultOrEmpty || !s.Game.Document.World.QuestsRewarded.Contains("sq_missing_grain"));
        Assert.Null(s.Host.Explore!.QuestEvent);
        Assert.Contains("changed", s.Screen(0)!.Header);
        Assert.DoesNotContain("questResult", s.Harness.ActiveTimers);
        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
    }

    [Fact]
    public void Repeated_recoveries_in_one_clock_tick_always_advance_the_epoch()
    {
        var s = Walking();
        s.Game.WorldActionCompleted = _ => throw new InvalidOperationException("repeat failure");
        s.Send(0, "walk", new { dir = "e" });
        var epoch = s.Host.Explore!.Epoch;
        s.Send(0, "questLog", new { action = "open" });
        Assert.True(s.Host.Explore!.Epoch > epoch);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_failed_EnterWorld_during_recovery_saves_the_region_start_and_reopens(bool failInitialEntry)
    {
        var original = ShopTests.AtShop();
        original.Send(0, "interact", new { target = "shop:af_market_herbalist" });
        var before = original.Game.Document;
        Assert.NotNull(before.World!.Shop);
        var entries = 0;
        var game = new TacticsGame(Rules)
        {
            WorldEntering = () => { if (++entries > 1 || failInitialEntry) throw new InvalidOperationException("world validation failed"); },
            WorldActionCompleted = _ => throw new InvalidOperationException("resume failed after entering"),
            StoryLogged = _ => throw new InvalidOperationException("logging failed too"),
        };
        var resumed = new Session(document: before.ToJson(), game: game);
        resumed.Harness.Resume(resumed.Lease);
        Assert.Equal(2, entries); // Recovery retries entry, and that retry always fails.
        var saved = TacticsDocument.Read(resumed.Lease.Document);
        Assert.Null(saved.Battle);
        Assert.Null(saved.World!.Story);
        Assert.Null(saved.World.Shop);
        Assert.Null(saved.World.Camp);
        var start = Rules.RegionFor(before.World.Region)!.Def.Spawns.Start;
        Assert.NotEqual(start, before.World.At);
        Assert.Equal(start, saved.World.At);
        var expected = before with
        {
            Battle = null,
            World = before.World with { At = start, Facing = new(start.X + 1, start.Y), Story = null, Shop = null, Camp = null },
        };
        Assert.Equal(expected.ToJson().GetRawText(), resumed.Lease.Document.GetRawText());
        Assert.Contains("changed", resumed.Host.Notice);
        Assert.Contains("changed", resumed.Screen(0)!.Header);
        Assert.Empty(resumed.Harness.ActiveTimers);
        var reopened = new Session(document: resumed.Lease.Document);
        reopened.Harness.Resume(reopened.Lease);
        for (var i = 0; i < 4; i++) reopened.Harness.SetConnected(i, true);
        Assert.Equal(TacticsPhase.Exploration, reopened.Game.Phase);
        Assert.Equal(start, reopened.Game.Document.World!.At);
        var leader = Enumerable.Range(0, 4).First(i => reopened.Screen(i)!.Data.GetProperty("leader").GetBoolean());
        var seq = reopened.Host.Explore!.Seq;
        reopened.Send(leader, "walk", new { dir = "e" });
        reopened.Harness.Advance(TimeSpan.FromSeconds(1));
        Assert.NotEqual(seq, reopened.Host.Explore!.Seq);
    }

    private static Session Walking() { var s = new Session(); s.ToWalking(); return s; }
}
