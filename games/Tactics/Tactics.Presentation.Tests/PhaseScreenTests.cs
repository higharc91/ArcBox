using System.Collections.Immutable;
using Tactics.Game;
using Tactics.Logic;

namespace Tactics.Presentation.Tests;

/// <summary><see cref="PhaseScreen"/>: what the TV says on each phase's screen, from hand-made host views.</summary>
public sealed class PhaseScreenTests
{
    private static TacticsHostView View(string phase, string banner = "The banner", string? notice = null, int level = 1,
        ImmutableArray<string>? mustered = null, ImmutableArray<EncounterOption>? encounters = null, BattleView? battle = null,
        string? outcome = null, ImmutableArray<ResultLine>? results = null)
        => new(phase, banner, notice, level, mustered ?? [], encounters, battle, outcome, results);

    private static BattleView Fight(string encounter = "river_crossing", int round = 2)
        => new(encounter, encounter, round, [], null, [], [], null);

    [Fact]
    public void Muster_lists_who_is_ready()
    {
        var screen = PhaseScreen.Of(View("Muster", "Choose your classes on your phones", mustered: ["Ana", "Bo"], level: 3));

        Assert.Equal("Muster · 2 ready", screen.Meta);
        Assert.Equal("Choose your classes on your phones", screen.Title);
        Assert.Equal(3, screen.PartyLevel);
        Assert.Equal(["Ana", "Bo"], screen.Rows.Select(r => r.Title));
        Assert.All(screen.Rows, r => Assert.Equal("READY", r.Chip));
        Assert.Null(screen.Empty);
    }

    [Fact]
    public void Muster_with_nobody_ready_says_so()
    {
        var screen = PhaseScreen.Of(View("Muster"));

        Assert.Equal("Muster · 0 ready", screen.Meta);
        Assert.Empty(screen.Rows);
        Assert.Equal("Nobody's ready yet", screen.Empty);
    }

    [Fact]
    public void Each_new_arrival_at_muster_is_a_new_shot()
    {
        Assert.Equal("tactics-muster-0", PhaseScreen.Of(View("Muster")).Shot);
        Assert.Equal("tactics-muster-2", PhaseScreen.Of(View("Muster", mustered: ["Ana", "Bo"])).Shot);
    }

    [Fact]
    public void An_encounter_row_says_its_level_party_and_companions()
    {
        var screen = PhaseScreen.Of(View("EncounterPick", "Ana is choosing a battle",
            encounters: [new EncounterOption("river_crossing", "River Crossing", 1, 4, 6, true, 2)]));

        Assert.Equal("Encounter pick · 1 battle", screen.Meta);
        Assert.Equal("tactics-encounterpick", screen.Shot);
        var row = Assert.Single(screen.Rows);
        Assert.Equal("River Crossing", row.Title);
        Assert.Equal("4–6 players · +2 AI companions", row.Detail);
        Assert.Equal("Lv 1", row.Value);
        Assert.False(row.Dim);
        Assert.Null(row.Chip);
    }

    [Theory]
    [InlineData(4, 4, 0, "4 players")]
    [InlineData(4, 6, 0, "4–6 players")]
    [InlineData(1, 1, 0, "1 player")]
    [InlineData(4, 6, 1, "4–6 players · +1 AI companion")]
    public void The_party_size_reads_naturally(int min, int max, int companions, string expected)
    {
        var screen = PhaseScreen.Of(View("EncounterPick", encounters: [new EncounterOption("a", "A", 1, min, max, true, companions)]));

        Assert.Equal(expected, Assert.Single(screen.Rows).Detail);
    }

    [Fact]
    public void An_encounter_without_a_level_shows_a_question_mark()
    {
        var screen = PhaseScreen.Of(View("EncounterPick", encounters: [new EncounterOption("a", "A", null, 1, 6, true)]));

        Assert.Equal("Lv ?", Assert.Single(screen.Rows).Value);
    }

    [Fact]
    public void An_encounter_too_small_for_the_party_is_dimmed_and_says_why()
    {
        var screen = PhaseScreen.Of(View("EncounterPick", encounters:
        [
            new EncounterOption("a", "A", 1, 4, 6, true),
            new EncounterOption("b", "B", 2, 1, 2, false),
        ]));

        Assert.Equal("Encounter pick · 2 battles", screen.Meta);
        Assert.False(screen.Rows[0].Dim);
        Assert.True(screen.Rows[1].Dim);
        Assert.Equal("PARTY TOO BIG", screen.Rows[1].Chip);
    }

    [Fact]
    public void A_won_battle_shows_each_members_xp_and_injuries()
    {
        var screen = PhaseScreen.Of(View("Results", "Victory at River Crossing!", outcome: "Won", results:
        [
            new ResultLine("Ana", 120, []),
            new ResultLine("Bo", 80, ["Broken Rib", "Scar"]),
        ]));

        Assert.Equal("Results · Battle won", screen.Meta);
        Assert.Equal("Victory at River Crossing!", screen.Title);
        Assert.Equal(Tone.Good, screen.Tone);
        Assert.Equal("tactics-results", screen.Shot);
        Assert.Equal(new ScreenRow("Ana", "No injuries", null, "+120 XP"), screen.Rows[0]);
        Assert.Equal(new ScreenRow("Bo", "Broken Rib, Scar", null, "+80 XP"), screen.Rows[1]);
    }

    [Theory]
    [InlineData("Lost", "Results · Battle lost")]
    [InlineData("Fled", "Results · The party fled")]
    public void A_battle_not_won_reads_as_bad_news(string outcome, string meta)
    {
        var screen = PhaseScreen.Of(View("Results", outcome: outcome, results: []));

        Assert.Equal(meta, screen.Meta);
        Assert.Equal(Tone.Bad, screen.Tone);
        Assert.Equal("No results to show", screen.Empty);
    }

    [Fact]
    public void The_battle_placeholder_names_the_encounter_and_round()
    {
        var screen = PhaseScreen.Of(View("Battle", "Ana's turn", battle: Fight()));

        Assert.Equal("Battle · River Crossing · Round 2", screen.Meta);
        Assert.Equal("Ana's turn", screen.Title);
        Assert.Equal("River Crossing", screen.Empty);
        Assert.Equal("tactics-battle", screen.Shot);
        Assert.Empty(screen.Rows);
    }

    [Fact]
    public void A_battle_with_no_banner_falls_back_to_the_round()
    {
        Assert.Equal("Round 2", PhaseScreen.Of(View("Battle", "", battle: Fight())).Title);
    }

    [Fact]
    public void Resuming_says_the_battle_is_waiting()
    {
        var screen = PhaseScreen.Of(View("Resuming", "Resuming the battle…", battle: Fight("forest_ruins")));

        Assert.Equal("Resuming · Forest Ruins", screen.Meta);
        Assert.Equal("Resuming the battle…", screen.Title);
        Assert.Equal("Waiting for the players to come back", screen.Empty);
        Assert.Equal("tactics-resuming", screen.Shot);
    }

    [Fact]
    public void The_notice_is_carried_through()
    {
        Assert.Equal("The rules changed", PhaseScreen.Of(View("Battle", notice: "The rules changed", battle: Fight())).Notice);
    }

    [Fact]
    public void A_phase_this_build_does_not_know_still_shows_the_banner()
    {
        var screen = PhaseScreen.Of(View("Intermission", "Back soon"));

        Assert.Equal("Back soon", screen.Title);
        Assert.Equal("Intermission", screen.Meta);
        Assert.Empty(screen.Rows);
    }
}
