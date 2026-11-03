using Tactics.Logic;
using static Tactics.Game.Tests.TacticsGameTests;

namespace Tactics.Game.Tests;

public sealed class RestTests
{
    internal static readonly Lazy<RulesPack> CampRules = new(() => Vault.Load([
        """{ "id":"fire", "kind":"campfire", "at":[3,1] }""",
        """{ "id":"secret", "kind":"cache", "at":[7,1], "hidden":20, "loot":[{"gold":20}] }"""]));

    internal static Session AtCamp(bool ready = true)
    {
        var s = Vault.At(new Tile(2, 1), CampRules.Value);
        var doc = s.Game.Document;
        doc = doc with { World = doc.World! with { RestReady = ready },
            Members = Ordinal.Map(doc.Members.Select(m => (m.Key, m.Value with { Character = m.Value.Character with
            { Injuries = [new("black_eye"), new("cracked_ribs"), new("shaken")] } }))) };
        return Again(doc);
    }

    internal static Session Again(TacticsDocument doc, int die = 20, RulesPack? rules = null)
    {
        var game = new TacticsGame(rules ?? CampRules.Value, battleList: true) { CampDie = () => die };
        var s = new Session(document: doc.ToJson(), game: game);
        s.Harness.Resume(s.Lease);
        for (var i = 0; i < 4; i++) s.Harness.SetConnected(i, true);
        return s;
    }

    internal static void Vote(Session s)
    {
        for (var i = 0; i < 4; i++) s.Send(i, "vote", new { option = "rest" });
        StoryVoteTests.Until(s, () => s.Game.Document.World!.Camp?.Stage is "jobs" or "rooms", "the agreed rest");
    }

    internal static void Finish(Session s)
        => StoryVoteTests.Until(s, () => s.Game.Phase == TacticsPhase.Exploration, "dawn", TimeSpan.FromMinutes(5));

    [Fact]
    public void A_fire_without_an_earned_rest_does_not_start_a_vote()
    {
        var s = AtCamp(false);
        s.Send(0, "interact", new { target = "fire" });
        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
        Assert.Null(s.Game.Document.World!.Camp);
        Assert.Contains("not tired", s.Host.Notice);
    }

    [Fact]
    public void Unanswered_votes_count_as_rest_and_a_tied_majority_does_not_spend_it()
    {
        var s = AtCamp();
        s.Send(0, "interact", new { target = "fire" });
        s.Send(0, "vote", new { option = "later" });
        s.Harness.Advance(TimeSpan.FromSeconds(21));
        Assert.Equal("jobs", s.Game.Document.World!.Camp!.Stage);
        s = AtCamp();
        s.Send(0, "interact", new { target = "fire" });
        s.Send(0, "vote", new { option = "later" });
        s.Send(1, "vote", new { option = "later" });
        s.Harness.Advance(TimeSpan.FromSeconds(21));
        Assert.Null(s.Game.Document.World!.Camp);
        Assert.True(s.Game.Document.World.RestReady);
    }

    [Fact]
    public void Dawn_mends_only_rest_injuries_for_everyone_including_away_characters_and_spends_one_rest()
    {
        var s = AtCamp();
        s.Send(0, "interact", new { target = "fire" });
        Vote(s);
        s.Harness.SetConnected(3, false);
        for (var i = 0; i < 3; i++) s.Send(i, "campJob", new { job = "rest" });
        Finish(s);
        Assert.False(s.Game.Document.World!.RestReady);
        Assert.Equal(1, s.Game.Document.World.RestSeq);
        Assert.All(s.Game.Document.Members.Values, m => Assert.Equal([new InjuryId("cracked_ribs"), new InjuryId("shaken")], m.Character.Injuries));
        s.Send(0, "interact", new { target = "fire" });
        Assert.Null(s.Game.Document.World.Camp);
    }

    [Fact]
    public void Jobs_roll_for_their_own_character_apply_once_and_survive_restart_after_the_result()
    {
        var s = AtCamp();
        s.Send(0, "interact", new { target = "fire" });
        Vote(s);
        var patient = s.Profiles[2]!.Value.Value;
        s.Send(0, "campJob", new { job = "tend", target = patient, injury = "cracked_ribs" });
        s.Send(1, "campJob", new { job = "rally", target = patient, injury = "shaken" });
        s.Send(2, "campJob", new { job = "cook" });
        s.Send(3, "campJob", new { job = "watch" });
        var rolled = s.Game.Document.World!.Camp!.Results.Single();
        Assert.Equal(s.Profiles[0]!.Value.Value, rolled.Member);
        Assert.Equal(20, rolled.Die);
        Assert.DoesNotContain(new InjuryId("cracked_ribs"), s.Game.Document.Members[patient].Character.Injuries);
        s = Again(s.Game.Document, die: 1);
        Assert.Equal(rolled, s.Game.Document.World!.Camp!.Results.Single());
        Finish(s);
        Assert.DoesNotContain(new InjuryId("cracked_ribs"), s.Game.Document.Members[patient].Character.Injuries);
        Assert.False(s.Game.Document.World!.RestReady);
    }

    [Fact]
    public void Two_tenders_cannot_reserve_the_same_injury()
    {
        var s = AtCamp();
        s.Send(0, "interact", new { target = "fire" }); Vote(s);
        var target = s.Profiles[2]!.Value.Value;
        s.Send(0, "campJob", new { job = "tend", target, injury = "cracked_ribs" });
        s.Send(1, "campJob", new { job = "tend", target, injury = "cracked_ribs" });
        Assert.Single(s.Game.Document.World!.Camp!.Jobs);
        Assert.Contains("already being tended", s.Screen(1)!.Header);
    }

    [Fact]
    public void Saved_job_outcomes_and_targets_survive_reconnect_for_the_TV()
    {
        var s = AtCamp(); s.Send(0, "interact", new { target = "fire" }); Vote(s);
        var actor = s.Profiles[0]!.Value.Value;
        var target = s.Profiles[2]!.Value.Value;
        s.Send(0, "campJob", new { job = "tend", target, injury = "cracked_ribs" });
        StoryVoteTests.Until(s, () => s.Game.Document.World!.Camp!.Results.Length == 1, "first camp result");
        var person = s.Host.Camp!.People.Single(p => p.Member == actor);
        Assert.Equal("success", person.Outcome);
        Assert.Equal(target, person.Target);
        var resumed = Again(s.Game.Document);
        Assert.Equal(person, resumed.Host.Camp!.People.Single(p => p.Member == actor));
    }

    [Fact]
    public void Cooking_and_watch_help_the_whole_party_without_stacking_and_credit_the_watcher()
    {
        var s = AtCamp();
        s.Send(0, "interact", new { target = "fire" }); Vote(s);
        s.Send(0, "campJob", new { job = "cook" });
        s.Send(1, "campJob", new { job = "cook" });
        s.Send(2, "campJob", new { job = "watch" });
        s.Send(3, "campJob", new { job = "rest" });
        Finish(s);
        Assert.All(s.Game.Document.Members.Values, m => Assert.True(m.WellFed));
        Assert.True(s.Game.Document.World!.Things!["secret"].Found);
        Assert.Contains(s.Host.Explore!.ThingEvents!.Value, e => e.Id == "secret" && e.By == "Player3");
    }

    [Theory]
    [InlineData(BattleOutcome.Won, true)]
    [InlineData(BattleOutcome.Lost, true)]
    [InlineData(BattleOutcome.Fled, false)]
    public void Battle_end_earns_one_rest_and_consumes_well_fed(BattleOutcome outcome, bool ready)
    {
        var s = ShopTests.AtShop(0);
        var doc = s.Game.Document with { Members = Ordinal.Map(s.Game.Document.Members.Select(m => (m.Key, m.Value with { WellFed = true }))) };
        s = Again(doc, rules: Rules);
        s.Send(0, "battles");
        s.Send(0, "pickEncounter", new { encounter = "river_crossing" });
        Assert.All(s.Game.Document.Battle!.State.Units.Where(u => u.Key.Value.StartsWith("hero", StringComparison.Ordinal)), u => Assert.Equal(3, u.Value.TempHp));
        StageEndTests.Ended(s, outcome);
        Assert.Equal(ready, s.Game.Document.World!.RestReady);
        Assert.All(s.Game.Document.Members.Values, m => Assert.False(m.WellFed));
    }

    [Fact]
    public void A_story_beat_earns_a_rest_but_an_interaction_does_not()
    {
        var s = StoryTests.Telling(StoryTests.Mayor);
        StoryTests.PlayOut(s);
        Assert.True(s.Game.Document.World!.RestReady);
        s = Vault.At(new Tile(2, 1));
        s.Send(0, "interact", new { target = "sign" });
        StoryTests.PlayOut(s);
        Assert.False(s.Game.Document.World!.RestReady);
    }
    [Fact]
    public void Jobs_hold_with_no_phones_and_end_session_keeps_choices_for_the_next_session()
    {
        var s = AtCamp(); s.Send(0, "interact", new { target = "fire" }); Vote(s);
        s.Send(0, "campJob", new { job = "cook" });
        for (var i = 0; i < 4; i++) s.Harness.SetConnected(i, false);
        s.Harness.Advance(TimeSpan.FromHours(1));
        Assert.Equal("jobs", s.Game.Document.World!.Camp!.Stage);
        Assert.Null(s.Game.Document.World.Camp.Due);
        Assert.Empty(s.Game.Document.World.Camp.Results);
        for (var i = 0; i < 4; i++) s.Harness.SetConnected(i, true);
        s.Send(0, "endSession");
        var next = new Session(document: s.Lease.Document, game: new TacticsGame(CampRules.Value));
        next.Harness.StartCampaign(next.Lease);
        Assert.Equal(TacticsPhase.Story, next.Game.Phase);
        Assert.Equal("cook", next.Game.Document.World!.Camp!.Jobs[s.Profiles[0]!.Value.Value].Job);
    }

    [Fact]
    public void A_healing_spell_gives_the_tender_two_saved_dice_without_a_roller_vote()
    {
        var s = AtCamp(); s.Send(0, "interact", new { target = "fire" }); Vote(s);
        var dice = new Queue<int>([4, 17]); s.Game.CampDie = dice.Dequeue;
        s.Send(0, "campJob", new { job = "rest" });
        s.Send(1, "campJob", new { job = "tend", target = s.Profiles[0]!.Value.Value, injury = "cracked_ribs" });
        s.Send(2, "campJob", new { job = "rest" }); s.Send(3, "campJob", new { job = "rest" });
        StoryVoteTests.Until(s, () => s.Game.Document.World!.Camp!.Results.Length == 2, "the healer's own roll");
        var result = s.Game.Document.World!.Camp!.Results[^1];
        Assert.Equal(s.Profiles[1]!.Value.Value, result.Member);
        Assert.Equal(4, result.Die); Assert.Equal(17, result.SecondDie); Assert.True(result.Passed);
        Assert.Equal("camp-roll", s.Screen(1)!.Screen);
    }
    [Theory]
    [InlineData("vote")]
    [InlineData("jobs")]
    [InlineData("results")]
    [InlineData("sleep")]
    [InlineData("dawn")]
    public void Ending_the_session_at_each_camp_stage_preserves_the_cursor_and_a_departing_leader_cannot_stall_it(string stage)
    {
        var s = AtCamp(); s.Send(0, "interact", new { target = "fire" });
        if (stage != "vote") Vote(s);
        if (stage is "results" or "sleep" or "dawn")
        {
            for (var i = 0; i < 4; i++) s.Send(i, "campJob", new { job = "rest" });
            if (stage == "sleep") StoryVoteTests.Until(s, () => s.Game.Document.World!.Camp!.Stage == "sleep", "saved sleep");
            if (stage == "dawn") StoryVoteTests.Until(s, () => s.Game.Document.World!.Camp!.Stage == "dawn", "saved dawn");
        }
        var cursor = s.Game.Document.World!.Camp!;
        var restSeq = s.Game.Document.World.RestSeq;
        s.Send(0, "endSession");
        var next = new Session(document: s.Lease.Document, game: new TacticsGame(CampRules.Value));
        next.Harness.StartCampaign(next.Lease);
        Assert.Equal(stage, next.Game.Document.World!.Camp!.Stage);
        Assert.Equal(cursor.Results, next.Game.Document.World.Camp.Results);
        Assert.Equal(restSeq, next.Game.Document.World.RestSeq);
        next.Harness.SetConnected(0, false);
        Finish(next);
        Assert.Equal(1, next.Game.Document.World!.RestSeq);
        Assert.False(next.Game.Document.World.RestReady);
        Assert.All(next.Game.Document.Members.Values, m => Assert.Equal([new InjuryId("cracked_ribs"), new InjuryId("shaken")], m.Character.Injuries));
    }

    [Fact]
    public void Sleep_does_not_heal_early_and_the_sunrise_holds_then_survives_return_to_walking()
    {
        var s = AtCamp(); s.Send(0, "interact", new { target = "fire" }); Vote(s);
        for (var i = 0; i < 4; i++) s.Send(i, "campJob", new { job = "rest" });
        StoryVoteTests.Until(s, () => s.Game.Document.World!.Camp!.Stage == "sleep", "the sleep transition");
        Assert.True(s.Game.Document.World!.RestReady);
        Assert.All(s.Game.Document.Members.Values, m => Assert.Contains(new InjuryId("black_eye"), m.Character.Injuries));
        s = Again(s.Game.Document);
        StoryVoteTests.Until(s, () => s.Game.Document.World!.Camp!.Stage == "dawn", "sunrise");
        Assert.True(s.Host.Explore!.Morning);
        s.Harness.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal("dawn", s.Game.Document.World!.Camp!.Stage);
        Finish(s);
        Assert.True(s.Host.Explore!.Morning);
        Assert.Equal(1, s.Game.Document.World!.RestSeq);
    }

    [Fact]
    public void A_blocking_fire_still_gives_every_present_character_a_different_reachable_seat()
    {
        var s = AtCamp(); s.Send(0, "interact", new { target = "fire" }); Vote(s);
        var camp = s.Host.Camp!;
        Assert.Equal(4, camp.People.Select(p => p.At).Distinct().Count());
        Assert.DoesNotContain(camp.People, p => p.At == camp.At);
        var ground = CampRules.Value.GroundFor("vault")!.With([camp.At]);
        Assert.False(ground.Walkable(camp.At));
        Assert.All(camp.People, p => Assert.True(ground.Walkable(p.At)));
    }

}
