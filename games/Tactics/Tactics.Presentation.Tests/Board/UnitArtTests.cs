using Tactics.Logic;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.Tests.Board;

/// <summary><see cref="UnitArt"/>: sprite keys, frames and status art, against the real pack.</summary>
public sealed class UnitArtTests
{
    private static readonly Lazy<UnitArt> Art = new(() => UnitArt.Load(Pack.Root));

    private static bool OnDisk(string path) => File.Exists(Path.Combine(Pack.Root, path));

    [Fact]
    public void A_sprite_has_its_sheets_animations_by_their_manifest_name_or_presentation_key()
    {
        var fighter = Art.Value.Sprite("characters.class_fighter")!;

        Assert.Equal(12, fighter.Anim("attack").Frames[0]);
        Assert.Equal(25, fighter.Anim("characters.cast").Frames[0]);
        Assert.Equal(8, fighter.Anim("walk").Frames[0]);
    }

    [Theory]
    [InlineData("characters.cast", 12)]     // a beast has no cast: it does its special
    [InlineData("characters.shoot", 8)]     // or attacks
    [InlineData("characters.bash", 8)]
    [InlineData("nonsense", 8)]
    public void An_animation_a_sheet_lacks_falls_back_to_one_it_has(string key, int column)
        => Assert.Equal(column, Art.Value.Sprite("beasts.wolf.grey")!.Anim(key).Frames[0]);

    [Fact]
    public void A_dodge_on_a_sheet_with_none_stands_still_rather_than_swinging()
    {
        var wolf = Art.Value.Sprite("beasts.wolf.grey")!;

        Assert.Same(wolf.Idle, wolf.Anim("dodge"));
    }

    [Fact]
    public void A_special_on_a_sheet_with_none_is_its_cast()
    {
        var goblin = Art.Value.Sprite("characters.goblin_cutthroat")!;

        Assert.Same(goblin.Anim("cast"), goblin.Anim("special"));
    }

    [Fact]
    public void The_stand_in_is_the_same_figure_each_time_without_reading_the_disk_again()
        => Assert.Same(Art.Value.StandIn(UnitTone.Enemy), Art.Value.StandIn(UnitTone.Enemy));

    [Fact]
    public void Digits_come_in_styles_and_the_big_words_are_glyphs_too()
    {
        Assert.NotEqual(Art.Value.Glyph("damage", "5").Rect, Art.Value.Glyph("heal", "5").Rect);
        Assert.Equal(29, Art.Value.Glyph("damage", "MISS").Rect.Width);
        Assert.Equal(new ArtRect(0, 0, 32, 32), Art.Value.DieGold);
    }

    [Fact]
    public void A_character_key_is_a_preset_with_the_character_frames_and_its_builds_head()
    {
        var goblin = Art.Value.Sprite("characters.goblin_archer")!;
        var fighter = Art.Value.Sprite("characters.class_fighter")!;   // a "large" build

        Assert.Equal("characters/presets/goblin_archer.png", goblin.Path);
        Assert.Equal((32, 40, 16, 37), (goblin.Width, goblin.Height, goblin.AnchorX, goblin.AnchorY));
        Assert.Equal(4, goblin.Idle.Frames.Length);
        Assert.Equal(62, Assert.Single(goblin.Down.Frames));
        Assert.Equal(8, Art.Value.Sprite("characters.bandit")!.HeadTop);
        Assert.Equal(4, fighter.HeadTop);
    }

    [Theory]
    [InlineData("beasts.wolf.dire", "beasts/wolf_dire.png", 48, 48, 24, 44, 18)]
    [InlineData("bosses.troll.ogre", "bosses/troll_ogre.png", 64, 64, 32, 60, 22)]
    [InlineData("bosses.dragon.red", "bosses/dragon_red.png", 96, 96, 48, 90, 22)]
    [InlineData("bosses.imp.violet", "bosses/imp_violet.png", 32, 32, 16, 30, 22)]
    public void A_monster_key_names_its_set_and_variant_with_that_sets_frames(string key, string path, int w, int h, int ax, int ay, int lastDown)
    {
        var sprite = Art.Value.Sprite(key)!;

        Assert.Equal((path, w, h, ax, ay), (sprite.Path, sprite.Width, sprite.Height, sprite.AnchorX, sprite.AnchorY));
        Assert.Equal(lastDown, sprite.Down.Frames[^1]);
    }

    [Fact]
    public void A_monster_key_with_no_or_an_unknown_variant_takes_the_first()
    {
        Assert.Equal("beasts/wolf_grey.png", Art.Value.Sprite("beasts.wolf")!.Path);
        Assert.Equal("beasts/wolf_grey.png", Art.Value.Sprite("beasts.wolf.nonsense")!.Path);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("characters.nobody")]
    [InlineData("beasts.unicorn")]
    [InlineData("weapons.sword")]
    [InlineData("characters")]
    public void A_key_the_pack_has_no_sprite_for_is_null(string? key)
        => Assert.Null(Art.Value.Sprite(key));

    [Fact]
    public void Each_side_has_a_stand_in_that_really_exists()
    {
        foreach (var tone in Enum.GetValues<UnitTone>())
            Assert.True(OnDisk(Art.Value.StandIn(tone).Path));
    }

    [Fact]
    public void Every_sprite_in_the_rules_and_every_class_resolves_to_a_real_file()
    {
        var rules = RulesPack.Load([Path.Combine(Pack.Root, "rules")]);
        var keys = rules.Monsters.Values.Select(m => m.Sprite).Concat(rules.Classes.Keys.Select(c => $"characters.class_{c}")).ToArray();

        Assert.Equal(12, rules.Classes.Count);
        foreach (var key in keys)
            Assert.True(Art.Value.Sprite(key) is { } sprite && OnDisk(sprite.Path), key);
    }

    [Fact]
    public void A_frames_place_in_the_sheet_is_its_column_and_row()
    {
        var goblin = Art.Value.Sprite("characters.goblin_archer")!;

        Assert.Equal(new ArtRect(62 * 32, 40, 32, 40), goblin.Source(62, 1));
        Assert.Equal(new ArtRect(0, 0, 32, 40), goblin.Source(0, 0));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.24, 0)]
    [InlineData(0.26, 1)]
    [InlineData(1.1, 0)]   // four frames at 4 fps: back round after a second
    public void A_loop_runs_and_wraps(double seconds, int frame)
        => Assert.Equal(frame, new UnitAnim([0, 1, 2, 3], 4, true).FrameAt(seconds));

    [Fact]
    public void A_one_shot_stops_on_its_last_frame()
        => Assert.Equal(22, new UnitAnim([21, 22], 4, false).FrameAt(100));

    [Theory]
    [InlineData("poisoned", "poisoned", true)]
    [InlineData("burning", "burning", true)]
    [InlineData("hasted", "hasted", true)]
    [InlineData("paralysed", "stunned", true)]
    [InlineData("invisible", "hidden", true)]
    [InlineData("hunters_mark", "marked", true)]
    [InlineData("mage_armor", "shielded", true)]
    [InlineData("unconscious", "sleeping", false)]    // the pack has the effect, but no icon of it
    public void A_condition_finds_its_status_art(string condition, string effect, bool icon)
    {
        var look = Art.Value.Condition(condition)!;

        Assert.Equal($"status/{effect}.png", look.Effect!.Path);
        Assert.True(OnDisk(look.Effect.Path));
        Assert.Equal(icon, look.Icon is not null);
    }

    [Fact]
    public void Prone_has_an_icon_and_no_effect()
    {
        var look = Art.Value.Condition("prone")!;

        Assert.Null(look.Effect);
        Assert.Equal(new ArtRect(73, 1, 16, 16), look.Icon);
    }

    [Theory]
    [InlineData("lit")]
    [InlineData("disengaged")]
    [InlineData("no_such_condition")]
    public void A_condition_with_no_art_is_null(string condition)
        => Assert.Null(Art.Value.Condition(condition));

    [Fact]
    public void An_effect_says_which_side_of_the_unit_it_goes()
    {
        Assert.True(Art.Value.Condition("hasted")!.Effect!.Under);
        Assert.False(Art.Value.Condition("poisoned")!.Effect!.Under);
    }

    [Fact]
    public void Overhead_effects_follow_the_head_a_tall_sprite_lifts_them_a_small_one_lowers_them()
    {
        Assert.Equal(0, Art.Value.Sprite("characters.bandit")!.EffectShift);                // standard build
        Assert.Equal(-4, Art.Value.Sprite("characters.class_fighter")!.EffectShift);        // large build
        Assert.Equal(-23, Art.Value.Sprite("bosses.troll.troll")!.EffectShift);
        Assert.Equal(7, Art.Value.Sprite("bosses.imp.crimson")!.EffectShift);
    }

    [Fact]
    public void The_hp_bars_and_digits_come_from_the_ui_manifest()
    {
        var party = Art.Value.Bar(UnitTone.Party);
        var enemy = Art.Value.Bar(UnitTone.Enemy);

        Assert.Equal(new ArtRect(0, 0, 16, 9), party.Frame);
        Assert.Equal((3, 3), (party.SliceLeft, party.SliceRight));
        Assert.Equal((2, 2, 5), (party.FillX, party.FillY, party.FillHeight));
        Assert.Equal(new ArtRect(36, 0, 4, 5), party.Mid);
        Assert.Equal(new ArtRect(53, 0, 4, 5), enemy.Mid);
        Assert.Equal((new ArtRect(30, 0, 9, 12), 8), Art.Value.Digit('3'));
        Assert.True(OnDisk(UnitArt.BarsPath) && OnDisk(UnitArt.NumbersPath) && OnDisk(UnitArt.IconsPath) && OnDisk(Art.Value.Arrow.Path));
    }

    [Fact]
    public void Manifests_that_cannot_be_read_say_so()
        => Assert.ThrowsAny<IOException>(() => UnitArt.Load(Path.Combine(Pack.Root, "no_such_folder")));
}
