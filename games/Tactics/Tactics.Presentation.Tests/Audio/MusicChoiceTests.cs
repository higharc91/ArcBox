using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Audio;
using Tactics.Presentation.Tests.Board;

namespace Tactics.Presentation.Tests.Audio;

/// <summary><see cref="MusicChoice"/>: what plays for each screen, and that it's all in the pack.</summary>
public sealed class MusicChoiceTests
{
    private static readonly Lazy<SoundBank> Bank = new(() => SoundBank.Load(Pack.Root));

    private static BattleView Fight(string encounter, string map = "river_crossing")
        => new(encounter, map, 1, [], null, [], [], null, BoardChanges.None);

    private static TacticsHostView View(string phase, BattleView? battle = null, string? outcome = null)
        => new(phase, "", null, 1, [], null, battle, outcome, null);

    [Fact]
    public void Every_track_it_names_is_in_the_manifest_and_on_disk()
    {
        Assert.NotEmpty(MusicChoice.Ids);
        foreach (var id in MusicChoice.Ids)
        {
            var track = Bank.Value.Track(id);
            Assert.NotNull(track);
            Assert.True(File.Exists(Path.Combine(Pack.Root, track.File)), track.File);
        }
    }

    [Fact]
    public void Every_ambience_it_names_is_a_looping_sound_in_the_manifest()
    {
        foreach (var map in Pack.Maps)
        {
            var sound = Bank.Value.Sound(MusicChoice.Ambience(map)!);
            Assert.NotNull(sound);
            Assert.True(sound.Loop);
            Assert.Equal("ambience", sound.Group);
        }
    }

    [Fact]
    public void A_stings_track_does_not_loop_and_a_battles_does()
    {
        Assert.False(Bank.Value.Track("victory")!.Loop);
        Assert.False(Bank.Value.Track("defeat")!.Loop);
        Assert.True(Bank.Value.Track("battle")!.Loop);
    }

    [Theory]
    [InlineData("river_crossing", "battle")]
    [InlineData("forest_ruins", "battle")]
    [InlineData("crypt_hall", "battle")]
    [InlineData("crystal_cave", "battle")]
    [InlineData("ford_assault", "battle")]
    [InlineData("rime_tower", "battle")]
    [InlineData("warlord_bridge", "boss")]
    [InlineData("keep_gate", "boss")]
    [InlineData("hall_of_the_flame", "finale")]
    public void A_fight_plays_what_its_encounter_calls_for(string encounter, string track)
        => Assert.Equal(new MusicCue(track), MusicChoice.Of(View("Battle", Fight(encounter))));

    [Fact]
    public void Every_encounter_in_the_rules_has_music()
    {
        foreach (var file in Directory.GetFiles(Path.Combine(Pack.Root, "rules", "encounters")))
            Assert.NotNull(MusicChoice.Of(View("Battle", Fight(Path.GetFileNameWithoutExtension(file)))));
    }

    [Theory]
    [InlineData("Won", "victory")]
    [InlineData("Lost", "defeat")]
    public void Results_play_a_sting_and_then_settle(string outcome, string sting)
        => Assert.Equal(new MusicCue(sting, Then: "camp"), MusicChoice.Of(View("Results", outcome: outcome)));

    [Theory]
    [InlineData("warlord_bridge")]
    [InlineData("keep_gate")]
    [InlineData("hall_of_the_flame")]
    public void Beating_a_boss_plays_the_grand_fanfare(string encounter)
        => Assert.Equal(new MusicCue("victory_boss", Then: "camp"),
            MusicChoice.Of(View("Results", outcome: "Won") with { LastBattle = Fight(encounter) }));

    [Theory]
    [InlineData("river_crossing")]
    [InlineData("ford_assault")]
    public void Beating_an_ordinary_fight_plays_the_usual_fanfare(string encounter)
        => Assert.Equal(new MusicCue("victory", Then: "camp"),
            MusicChoice.Of(View("Results", outcome: "Won") with { LastBattle = Fight(encounter) }));

    [Fact]
    public void Losing_to_a_boss_is_still_the_defeat_theme()
        => Assert.Equal(new MusicCue("defeat", Then: "camp"),
            MusicChoice.Of(View("Results", outcome: "Lost") with { LastBattle = Fight("keep_gate") }));

    [Fact]
    public void The_boss_fanfare_is_a_sting_that_ends()
        => Assert.False(Bank.Value.Track("victory_boss")!.Loop);

    [Fact]
    public void A_party_that_fled_gets_no_sting() => Assert.Equal(new MusicCue("camp"), MusicChoice.Of(View("Results", outcome: "Fled")));

    [Theory]
    [InlineData("Muster", "title")]
    [InlineData("EncounterPick", "explore")]
    [InlineData("Exploration", "explore")]
    public void The_screens_before_a_fight_have_their_own_tunes(string phase, string track)
        => Assert.Equal(new MusicCue(track), MusicChoice.Of(View(phase)));

    [Fact]
    public void The_ashen_ford_has_the_track_the_manifest_gives_it()
    {
        var explore = new ExploreView("ashen_ford", "The Ashen Ford", 1, 0, new Tile(1, 2), new Tile(2, 2), false, [], [], null);

        Assert.Equal(new MusicCue("town_emberwick"), MusicChoice.Of(View("Exploration") with { Explore = explore }));
        Assert.Equal(new MusicCue("explore"), MusicChoice.Of(View("EncounterPick") with { Explore = explore }));
    }

    [Fact]
    public void A_region_with_no_track_of_its_own_gets_the_field_tune()
    {
        var explore = new ExploreView("mirewood", "Mirewood", 1, 0, new Tile(1, 2), new Tile(2, 2), false, [], [], null);

        Assert.Equal(new MusicCue("explore"), MusicChoice.Of(View("Exploration") with { Explore = explore }));
    }

    [Theory]
    [InlineData("Resuming")]
    [InlineData("SomePhaseFromTheFuture")]
    public void A_screen_with_no_tune_of_its_own_leaves_the_music_alone(string phase) => Assert.Null(MusicChoice.Of(View(phase)));

    [Fact]
    public void Only_a_fight_has_ambience()
    {
        Assert.Equal("amb_river", MusicChoice.AmbienceOf(View("Battle", Fight("river_crossing", "river_crossing"))));
        Assert.Null(MusicChoice.AmbienceOf(View("Results", outcome: "Won")));
        Assert.Null(MusicChoice.AmbienceOf(View("Muster")));
        Assert.Null(MusicChoice.AmbienceOf(View("Exploration")));
    }

    [Fact]
    public void A_map_with_no_ambience_of_its_own_is_quiet() => Assert.Null(MusicChoice.Ambience("no_such_map"));
}
