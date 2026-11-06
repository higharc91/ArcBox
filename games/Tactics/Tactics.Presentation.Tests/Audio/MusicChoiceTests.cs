using System.Text.Json;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Audio;
using Tactics.Presentation.Tests.Board;
using Tactics.Presentation.World;

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
    public void The_cave_uses_the_dungeon_score_and_returns_to_the_Ford_theme_outside()
    {
        var basis = ThingsDemo.Steps()[0].View with { Phase = "Exploration" };
        var cave = basis with { Explore = basis.Explore! with { Region = "cinder_hollow" } };
        Assert.Equal(new MusicCue("dungeon"), MusicChoice.Of(cave));
        Assert.Equal(new MusicCue("town_emberwick"), MusicChoice.Of(cave with { Explore = cave.Explore! with { Region = "ashen_ford" } }));
    }

    [Fact]
    public void Ashmaws_breath_is_local_to_the_lair_and_stops_on_death()
    {
        var explore = ThingsDemo.Steps()[0].View.Explore! with { Region = "cinder_hollow", At = new(10, 32), PeopleFlags = [] };
        var view = View("Exploration") with { Explore = explore };
        Assert.Equal("ashmaw_breath_loop", MusicChoice.CreatureAmbienceOf(view));
        Assert.Null(MusicChoice.CreatureAmbienceOf(view with { Explore = explore with { At = new(5, 2) } }));
        Assert.Null(MusicChoice.CreatureAmbienceOf(view with { Explore = explore with { PeopleFlags = ["sq_ashmaw_slain"] } }));
        var ashmaw = new UnitView(new("ashmaw"), "Ashmaw", "Enemy", "monsters.ashmaw", new(4, 6), 1, 32, 0, "Up", 0, [], null);
        var fight = Fight("hollow_lair", "hollow_lair") with { Units = [ashmaw] };
        Assert.Equal("ashmaw_breath_loop", MusicChoice.CreatureAmbienceOf(View("Battle", fight)));
        Assert.Null(MusicChoice.CreatureAmbienceOf(View("Battle", fight with { Units = [ashmaw with { Hp = 0, Life = "Dead" }] })));
        Assert.True(Bank.Value.Sound("ashmaw_breath_loop")!.Loop);
    }

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
    public void Every_manifest_track_is_chosen_by_the_music_policy()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Pack.Root, "music", "manifest.json")));
        var listed = manifest.RootElement.GetProperty("tracks").EnumerateObject().Select(track => track.Name).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(listed, MusicChoice.Ids.Order(StringComparer.Ordinal).ToArray());
    }

    [Theory]
    [InlineData("mirewood", "court_mirewood")]
    [InlineData("frostpeak", "town_greyhold")]
    public void Later_regions_play_their_own_scores(string region, string track)
    {
        var explore = new ExploreView(region, region, 1, 0, new Tile(1, 2), new Tile(2, 2), false, [], [], null);
        Assert.Equal(new MusicCue(track), MusicChoice.Of(View("Exploration") with { Explore = explore }));
        Assert.Equal(new MusicCue(track), MusicChoice.Of(View("Story") with { Explore = explore }));
    }

    [Fact]
    public void The_scarwalker_camp_uses_its_own_score_and_dawn_returns_to_the_field()
    {
        var explore = new ExploreView("sunscar", "The Sunscar", 1, 0, new Tile(1, 2), new Tile(2, 2), false, [], [], null);
        var camp = new CampView("camp", "fire", "jobs", new Tile(2, 2), 0, [], null, 0, null);
        var view = View("Story") with { Explore = explore, Camp = camp };
        Assert.Equal(new MusicCue("camp_scarwalker"), MusicChoice.Of(view));
        Assert.Equal(new MusicCue("camp_dawn", "explore"), MusicChoice.Of(view with { Camp = camp with { Stage = "dawn" } }));
    }

    [Fact]
    public void A_rekindled_epilogue_plays_the_festival_score()
    {
        var scene = new StoryView(1, "ch5_epilogue_rekindled", "Epilogue", "say", null, null, null, null);
        Assert.Equal(new MusicCue("town_emberwick_festival"), MusicChoice.Of(View("Story") with { Story = scene }));
        Assert.Equal(new MusicCue("explore"), MusicChoice.Of(View("Story") with { Story = scene with { Scene = "ch5_epilogue_returned" } }));
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

    /// <summary>
    /// The music each encounter in the rules is meant to have. Any encounter falls back to the battle theme, so this list is
    /// what makes the choice a decision: a new encounter fails <see cref="Every_encounter_in_the_rules_has_its_music_decided"/>
    /// until it's added here, with the battle theme or its own.
    /// </summary>
    private static readonly Dictionary<string, string> FightMusic = new()
    {
        ["river_crossing"] = "battle",
        ["forest_ruins"] = "battle",
        ["crypt_hall"] = "battle",
        ["mob_pine_wolves"] = "battle", ["mob_rooting_boars"] = "battle", ["mob_kobold_scouts"] = "battle",
        ["mob_river_slimes"] = "battle", ["mob_road_bandits"] = "battle",
        ["mob_hollow_bats"] = "battle", ["mob_hollow_kobolds"] = "battle",
        ["crystal_cave"] = "battle",
        ["ford_assault"] = "battle",
        ["hollow_throat"] = "battle", ["hollow_diggings"] = "battle", ["hollow_nest"] = "battle", ["hollow_lair"] = "boss",
        ["warlord_bridge"] = "boss",
        ["rime_tower"] = "boss",
        ["keep_gate"] = "boss",
        ["hall_of_the_flame"] = "finale",
    };

    public static TheoryData<string, string> Fights
    {
        get
        {
            var rows = new TheoryData<string, string>();
            foreach (var (encounter, track) in FightMusic)
                rows.Add(encounter, track);
            return rows;
        }
    }

    [Theory]
    [MemberData(nameof(Fights))]
    public void A_fight_plays_what_its_encounter_calls_for(string encounter, string track)
        => Assert.Equal(new MusicCue(track), MusicChoice.Of(View("Battle", Fight(encounter))));

    [Fact]
    public void Every_encounter_in_the_rules_has_its_music_decided()
    {
        var encounters = Directory.GetFiles(Path.Combine(Pack.Root, "rules", "encounters"), "*.json")
            .Select(Path.GetFileNameWithoutExtension).Order(StringComparer.Ordinal).ToArray();

        Assert.NotEmpty(encounters);
        Assert.Equal(encounters, FightMusic.Keys.Order(StringComparer.Ordinal).ToArray());
    }

    [Theory]
    [InlineData("Won", "victory")]
    [InlineData("Lost", "defeat")]
    public void Results_play_a_sting_and_then_settle(string outcome, string sting)
        => Assert.Equal(new MusicCue(sting, Then: "camp"), MusicChoice.Of(View("Results", outcome: outcome)));

    [Theory]
    [InlineData("warlord_bridge")]
    [InlineData("keep_gate")]
    [InlineData("rime_tower")]
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

    /// <summary>A scene keeps the region's music, so walking up to the mayor doesn't change the tune.</summary>
    [Fact]
    public void A_scene_keeps_the_regions_track()
    {
        var explore = new ExploreView("ashen_ford", "The Ashen Ford", 1, 0, new Tile(1, 2), new Tile(2, 2), false, [], [], null);

        Assert.Equal(new MusicCue("town_emberwick"), MusicChoice.Of(View("Story") with { Explore = explore }));
        Assert.Equal(new MusicCue("explore"), MusicChoice.Of(View("Story")));
    }

    [Theory]
    [InlineData("camp", "jobs", "camp", "amb_campfire")]
    [InlineData("camp", "results", "camp", "amb_campfire")]
    [InlineData("camp", "talk", "camp", "amb_campfire")]
    [InlineData("inn", "rooms", "camp", null)]
    [InlineData("camp", "vote", "town_emberwick", null)]
    [InlineData("camp", "dawn", "camp_dawn", null)]
    [InlineData("inn", "dawn", "camp_dawn", null)]
    public void Recovery_nights_play_the_cozy_loop_under_the_fire_and_return_to_region_music_at_dawn(string mode, string stage, string music, string? ambience)
    {
        var explore = new ExploreView("ashen_ford", "The Ashen Ford", 1, 0, new Tile(1, 2), new Tile(2, 2), false, [], [], null);
        var camp = new CampView(mode, "fire", stage, new Tile(2, 2), 0, [], null, 0, null);
        var view = View("Story") with { Explore = explore, Camp = camp };
        Assert.Equal(new MusicCue(music, stage == "dawn" ? "town_emberwick" : null), MusicChoice.Of(view));
        if (stage == "dawn") Assert.False(Bank.Value.Track("camp_dawn")!.Loop);
        Assert.Equal(ambience, MusicChoice.AmbienceOf(view));
        Assert.True(Bank.Value.Track("camp")!.Loop);
        Assert.True(Bank.Value.Sound("amb_campfire")!.Loop);
    }

    [Fact]
    public void Mirewood_uses_its_court_score()
    {
        var explore = new ExploreView("mirewood", "Mirewood", 1, 0, new Tile(1, 2), new Tile(2, 2), false, [], [], null);

        Assert.Equal(new MusicCue("court_mirewood"), MusicChoice.Of(View("Exploration") with { Explore = explore }));
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
