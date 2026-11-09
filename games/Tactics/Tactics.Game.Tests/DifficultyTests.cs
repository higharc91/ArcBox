using System.Text.Json;
using Tactics.Logic;

namespace Tactics.Game.Tests;

/// <summary>The campaign's difficulty (issue #114): the VIP sets it outside a battle, it persists, and it reaches every battle.</summary>
public sealed class DifficultyTests
{
    private static Difficulty Saved(TacticsGameTests.Session s) => TacticsDocument.Read(s.Lease.Document).Difficulty;

    private static string? ShownOnPhone(TacticsGameTests.Session s, int player)
        => s.Screen(player)!.Data.TryGetProperty("difficulty", out var d) ? d.GetString() : null;

    [Fact]
    public void An_old_document_reads_as_Normal()
    {
        var old = JsonDocument.Parse("""{ "version": 1, "rulesHash": "h", "partyLevel": 2, "members": {}, "completed": [], "battle": null }""").RootElement;

        Assert.Equal(Difficulty.Normal, TacticsDocument.Read(old).Difficulty);
        Assert.Equal(Difficulty.Normal, TacticsDocument.New.Difficulty);
    }

    [Theory]
    [InlineData(Difficulty.Story)]
    [InlineData(Difficulty.Normal)]
    [InlineData(Difficulty.Hard)]
    public void A_difficulty_survives_a_round_trip(Difficulty difficulty)
        => Assert.Equal(difficulty, TacticsDocument.Read((TacticsDocument.New with { Difficulty = difficulty }).ToJson()).Difficulty);

    [Theory]
    [InlineData("Story", Difficulty.Story)]
    [InlineData("Hard", Difficulty.Hard)]
    public void The_VIP_sets_it_outside_a_battle_and_it_persists_into_the_next_battle(string level, Difficulty expected)
    {
        var s = new TacticsGameTests.Session();
        s.ToWalking();

        s.Send(0, "difficulty", new { level });

        Assert.Equal(expected, Saved(s));
        Assert.Equal(level, ShownOnPhone(s, 0));
        s.Send(0, "battles");
        s.Send(0, "pickEncounter", new { encounter = "river_crossing" });
        Assert.Equal(TacticsPhase.Battle, s.Game.Phase);
        Assert.Equal(level, s.Host.Battle!.Difficulty);
    }

    [Fact]
    public void A_campaign_that_never_chose_fights_at_Normal()
    {
        var s = new TacticsGameTests.Session();
        s.ToBattle();

        Assert.Equal("Normal", s.Host.Battle!.Difficulty);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void It_can_be_changed_at_results_too()
    {
        var s = new TacticsGameTests.Session();
        s.ToBattle(everyoneAway: true);
        s.PlayOut();
        Assert.Equal(TacticsPhase.Results, s.Game.Phase);
        s.Harness.SetConnected(0, true);

        s.Send(0, "difficulty", new { level = "Story" });

        Assert.Equal(Difficulty.Story, Saved(s));
        Assert.Equal("Story", ShownOnPhone(s, 0));
    }

    [Fact]
    public void Only_the_VIP_can_change_it()
    {
        var s = new TacticsGameTests.Session();
        s.ToWalking();

        s.Send(1, "difficulty", new { level = "Hard" });

        Assert.Equal(Difficulty.Normal, Saved(s));
    }

    [Theory]
    [InlineData("Brutal")]
    [InlineData("")]
    [InlineData("7")]
    [InlineData("0")]
    [InlineData("2")]
    [InlineData("Story,Hard")]
    public void A_word_that_is_not_a_difficulty_changes_nothing(string level)
    {
        var s = new TacticsGameTests.Session();
        s.ToWalking();

        s.Send(0, "difficulty", new { level });

        Assert.Equal(Difficulty.Normal, Saved(s));
    }

    [Fact]
    public void Inputs_during_a_battle_are_refused()
    {
        var s = new TacticsGameTests.Session();
        s.ToBattle();

        s.Send(0, "difficulty", new { level = "Hard" });

        Assert.Equal(Difficulty.Normal, Saved(s));
        Assert.Equal("Normal", s.Host.Battle!.Difficulty);
    }

    [Fact]
    public void The_development_override_wins_over_the_campaign()
    {
        var s = new TacticsGameTests.Session(game: new TacticsGame(TacticsGameTests.Rules, battleList: true) { BattleDifficulty = Difficulty.Story });
        s.ToWalking();

        s.Send(0, "difficulty", new { level = "Hard" });
        s.Send(0, "battles");
        s.Send(0, "pickEncounter", new { encounter = "river_crossing" });

        Assert.Equal(Difficulty.Hard, Saved(s));
        Assert.Equal("Story", s.Host.Battle!.Difficulty);
    }
}
