using ArcBox.Sdk;
using ArcBox.Sdk.Testing;
using Tactics.Logic;
using static Tactics.Game.Tests.TacticsGameTests;

namespace Tactics.Game.Tests;

public sealed class InSessionLevelUpTests
{
    private static RulesPack Rules => TacticsGameTests.Rules;

    [Fact]
    public void Mid_session_subclass_choice_at_level_3_updates_character()
    {
        var session = new Session(game: new TacticsGame(Rules, lore: StoryTests.Lore, seed: StoryTests.SeedFor(StoryTests.Mayor)));
        session.Harness.StartCampaign(session.Lease);
        Assert.Equal(TacticsPhase.Muster, session.Game.Phase);

        for (var i = 0; i < 4; i++)
            session.Send(i, "pickClass", new { @class = "fighter" });

        Assert.Equal(TacticsPhase.Exploration, session.Game.Phase);

        // Level up the fighter to 3 and resume into session
        var profile = session.Profiles[0]!.Value;
        var member = session.Game.Document.For(profile)!;
        var lvl3Char = CharacterRules.LevelUp(member.Character, 3, Rules);
        var doc = session.Game.Document with
        {
            Members = session.Game.Document.Members.SetItem(profile.Value, member with { Character = lvl3Char }),
            PartyLevel = 3,
        };
        var again = new Session(document: doc.ToJson(), game: new TacticsGame(Rules, lore: StoryTests.Lore));
        again.Harness.Resume(again.Lease);
        for (var i = 0; i < 4; i++) again.Harness.SetConnected(i, true);

        Assert.True(CharacterRules.NeedsSubclass(again.Game.Document.For(profile)!.Character));

        // Player 0 chooses subclass mid-session
        again.Send(0, "chooseSubclass", new { subclass = "fighter.vanguard" });

        var updated = again.Game.Document.For(profile)!.Character;
        Assert.Equal("fighter.vanguard", updated.Subclass);
        Assert.False(CharacterRules.NeedsSubclass(updated));
    }

    [Fact]
    public void In_session_spendIncrease_and_setPicks_update_character()
    {
        var session = new Session(game: new TacticsGame(Rules, lore: StoryTests.Lore, seed: StoryTests.SeedFor(StoryTests.Mayor)));
        session.Harness.StartCampaign(session.Lease);

        for (var i = 0; i < 4; i++)
            session.Send(i, "pickClass", new { @class = "wizard" });

        Assert.Equal(TacticsPhase.Exploration, session.Game.Phase);

        var profile = session.Profiles[0]!.Value;
        var member = session.Game.Document.For(profile)!;
        var lvl4Char = CharacterRules.LevelUp(member.Character, 4, Rules);
        var doc = session.Game.Document with
        {
            Members = session.Game.Document.Members.SetItem(profile.Value, member with { Character = lvl4Char }),
            PartyLevel = 4,
        };
        var again = new Session(document: doc.ToJson(), game: new TacticsGame(Rules, lore: StoryTests.Lore));
        again.Harness.Resume(again.Lease);
        for (var i = 0; i < 4; i++) again.Harness.SetConnected(i, true);

        // Spend increase: +1 INT, +1 CON
        var intBefore = lvl4Char.Increases.GetValueOrDefault(Stat.Int);
        again.Send(0, "spendIncrease", new { stats = new[] { "int", "con" } });

        var afterIncrease = again.Game.Document.For(profile)!.Character;
        Assert.Equal(intBefore + 1, afterIncrease.Increases.GetValueOrDefault(Stat.Int));
        Assert.Equal(1, afterIncrease.Increases.GetValueOrDefault(Stat.Con));

        // Set picks with legal level 4 wizard loadout
        var legalPicks = new[]
        {
            "wizard.fire_bolt", "wizard.ray_of_frost", "wizard.shocking_grasp", "wizard.acid_splash",
            "wizard.magic_missile", "wizard.burning_hands", "wizard.shield", "wizard.sleep",
            "wizard.thunderwave", "wizard.scorching_ray", "wizard.misty_step"
        };
        again.Send(0, "setPicks", new { picks = legalPicks });
        var afterPicks = again.Game.Document.For(profile)!.Character;
        Assert.Contains(new AbilityId("wizard.misty_step"), afterPicks.Picks);
    }

    [Fact]
    public void Refuses_choices_during_battle_with_toast()
    {
        var session = new Session();
        session.ToBattle();
        Assert.Equal(TacticsPhase.Battle, session.Game.Phase);

        var profile = session.Profiles[0]!.Value;
        var before = session.Game.Document.For(profile)!.Character;

        session.Send(0, "chooseSubclass", new { subclass = "fighter.vanguard" });

        var after = session.Game.Document.For(profile)!.Character;
        Assert.Equal(before.Subclass, after.Subclass);

        // Verify toast message
        var view = (CustomView)session.Game.PlayerView(session.Harness.Players[0].Id);
        Assert.Equal("You can't change your character during battle.", view.Header);
    }

    [Fact]
    public void Returning_player_at_muster_with_pending_choices_is_prompted()
    {
        var session = new Session();
        session.Harness.StartCampaign(session.Lease);
        Assert.Equal(TacticsPhase.Muster, session.Game.Phase);

        // Player 0 creates fighter
        session.Send(0, "pickClass", new { @class = "fighter" });

        // Advance to level 3 so they need a subclass
        var profile = session.Profiles[0]!.Value;
        var member = session.Game.Document.For(profile)!;
        var lvl3Char = CharacterRules.LevelUp(member.Character, 3, Rules);

        // Construct a document at Muster where Player 0 already has a level 3 character with pending choices,
        // and other players haven't picked yet
        var musterDoc = session.Game.Document with
        {
            Members = session.Game.Document.Members.SetItem(profile.Value, member with { Character = lvl3Char }),
            PartyLevel = 3,
        };

        // Create new session at Muster with this document
        var returningSession = new Session(document: musterDoc.ToJson());
        returningSession.Harness.StartCampaign(returningSession.Lease);
        Assert.Equal(TacticsPhase.Muster, returningSession.Game.Phase);

        // Player 0 should see PickClass screen with choices prompt instead of waiting
        var view = returningSession.Game.PlayerView(returningSession.Harness.Players[0].Id);
        Assert.IsType<CustomView>(view);
        var custom = (CustomView)view;
        Assert.Equal(TacticsGame.Screens.PickClass, custom.Screen);
        Assert.True(custom.Data.TryGetProperty("me", out var me));
        Assert.True(me.TryGetProperty("waiting", out var waiting));
        Assert.True(waiting.GetInt32() > 0);

        // Player chooses subclass at Muster
        returningSession.Send(0, "chooseSubclass", new { subclass = "fighter.vanguard" });
        var updated = returningSession.Game.Document.For(profile)!.Character;
        Assert.Equal("fighter.vanguard", updated.Subclass);
    }

    [Fact]
    public void Results_screen_has_xp_progress_and_loot()
    {
        var s = new Session(seed: 2);
        s.ToBattle(everyoneAway: true);
        s.PlayOut();
        Assert.Equal(TacticsPhase.Results, s.Game.Phase);

        var host = s.Host;
        Assert.NotNull(host.Results);
        Assert.All(host.Results.Value, r =>
        {
            Assert.True(r.TotalXp > 0);
            Assert.True(r.NextLevelXp > 0);
        });

        var playerView = (CustomView)s.Game.PlayerView(s.Harness.Players[0].Id);
        Assert.Equal(TacticsGame.Screens.Results, playerView.Screen);
        Assert.True(playerView.Data.TryGetProperty("lines", out var lines));
        Assert.True(playerView.Data.TryGetProperty("choices", out var choices));
    }
}
