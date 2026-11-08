using Tactics.Logic;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.Tests.Board;

/// <summary><see cref="UnitArt"/>: sprite keys, frames and status art, against the real pack.</summary>
public sealed class UnitArtTests
{
    private static readonly Lazy<UnitArt> Art = new(() => UnitArt.Load(Pack.Root));

    private static bool OnDisk(string path) => File.Exists(Path.Combine(Pack.Root, path));

    [Theory]
    [InlineData("guard_captain", "dagna_holt", "dagna")]
    [InlineData("noble", "celandine_marrowby", "celandine")]
    [InlineData("bandit_captain", "rook", "rook")]
    [InlineData("goblin_shaman_mother_skarrow", "mother_skarrow", "mother_skarrow")]
    public void Approved_named_adults_and_their_preset_keys_select_the_compact_sprite(string preset, string character, string file)
    {
        var sprite = Art.Value.Sprite($"characters.{preset}")!;
        Assert.Same(sprite, Art.Value.Sprite($"characters.{character}"));
        Assert.Equal($"creatures/{file}.png", sprite.Path);
        Assert.True(OnDisk(sprite.Path));
        Assert.Equal((96, 96, 48, 84), (sprite.Width, sprite.Height, sprite.AnchorX, sprite.AnchorY));
        Assert.Equal(.5f, sprite.PixelScale);
        Assert.Equal(8, sprite.Directions.Distinct().Count());
        Assert.Equal([0, 1, 2, 3, 4, 5, 4, 3, 2, 1], sprite.Idle.Frames);
        Assert.Equal([6, 7, 8, 9, 10, 11], sprite.Anims["walk"].Frames);
        Assert.Equal([12, 13, 14, 15, 16, 17, 16, 15, 14, 13], sprite.Anims["talk"].Frames);
    }

    [Fact]
    public void A_sprite_has_its_sheets_animations_by_their_manifest_name_or_presentation_key()
    {
        var fighter = Art.Value.Sprite("characters.class_fighter")!;

        Assert.Equal(Pack.Frames("characters", "attack"), fighter.Anim("attack").Frames);
        Assert.Equal(Pack.Frames("characters", "cast"), fighter.Anim("characters.cast").Frames);
        Assert.Equal(Pack.Frames("characters", "walk"), fighter.Anim("walk").Frames);
    }

    [Theory]
    [InlineData("characters.cast", "special")]     // a beast has no cast: it does its special
    [InlineData("characters.pray", "special")]     // nor any of the spell poses
    [InlineData("characters.channel", "special")]
    [InlineData("characters.summon", "special")]
    [InlineData("characters.point", "special")]
    [InlineData("characters.buff", "special")]
    [InlineData("characters.shoot", "attack")]     // or attacks
    [InlineData("characters.bash", "attack")]
    [InlineData("nonsense", "attack")]
    public void An_animation_a_sheet_lacks_falls_back_to_one_it_has(string key, string anim)
    {
        var wolf = Art.Value.Sprite("beasts.wolf.grey")!;
        Assert.Same(wolf.Anims[anim], wolf.Anim(key));
    }

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
        var legacy = Art.Value.Sprite("characters.farmer")!; // retained legacy preset, rather than a refreshed compass atlas
        var fighter = Art.Value.Sprite("characters.class_fighter")!;   // a "large" build

        Assert.Equal("characters/presets/farmer.png", legacy.Path);
        Assert.Equal((32, 40, 16, 37), (legacy.Width, legacy.Height, legacy.AnchorX, legacy.AnchorY));
        Assert.Equal(Pack.Frames("characters", "idle"), legacy.Idle.Frames);
        Assert.Equal(Pack.Frames("characters", "down"), legacy.Down.Frames);
        Assert.Equal(8, legacy.HeadTop);
        Assert.Equal(.5f, fighter.PixelScale);
        Assert.Equal(32, fighter.FootprintWidth);
    }

    [Fact]
    public void Tactical_atlas_rows_wrap_and_the_rear_facing_starts_after_all_front_rows()
    {
        var sprite = Art.Value.Sprite("characters.tiefling_warlock")!;
        Assert.Equal("tactical/tiefling_warlock.png", sprite.Path);
        Assert.True(OnDisk(sprite.Path));
        Assert.Equal(new ArtRect(0, 160, 160, 160), sprite.Source(24, 0));
        Assert.Equal(new ArtRect(0, 1280, 160, 160), sprite.Source(0, 1));
        Assert.Equal(32, sprite.FootprintWidth);
        Assert.Equal(80, sprite.WorldWidth);
        Assert.Equal(80, sprite.WorldHeight);
        Assert.Equal(Pack.Frames("characters", "walk"), sprite.Anim("walk").Frames);
    }

    [Fact]
    public void Every_tactical_race_class_sheet_resolves_and_has_its_final_rear_frame_in_bounds()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(Pack.Root, "tactical", "manifest.json")));
        var presets = doc.RootElement.GetProperty("presets").EnumerateArray().ToArray();
        Assert.Equal(132, presets.Length);
        foreach (var preset in presets)
        {
            var sprite = Art.Value.Sprite("characters." + preset.GetString())!;
            Assert.True(OnDisk(sprite.Path));
            var last = sprite.Source(188, 1);
            Assert.InRange(last.X + last.Width, 1, 3840);
            Assert.InRange(last.Y + last.Height, 1, 2560);
            Assert.Equal(32, sprite.FootprintWidth);
        }
    }

    [Fact]
    public void A_pack_without_tactical_art_keeps_its_classic_character_format()
    {
        var root = Path.Combine(Path.GetTempPath(), "arcbox-classic-art-" + Guid.NewGuid());
        try
        {
            foreach (var set in new[] { "characters", "beasts", "bosses", "status", "icons", "ui" })
            {
                Directory.CreateDirectory(Path.Combine(root, set));
                File.Copy(Path.Combine(Pack.Root, set, "manifest.json"), Path.Combine(root, set, "manifest.json"));
            }
            Directory.CreateDirectory(Path.Combine(root, "characters", "presets"));
            File.Copy(Path.Combine(Pack.Root, "characters", "presets", "class_fighter.json"),
                Path.Combine(root, "characters", "presets", "class_fighter.json"));
            var sprite = UnitArt.Load(root).Sprite("characters.class_fighter")!;
            Assert.Equal("characters/presets/class_fighter.png", sprite.Path);
            Assert.Equal((32, 40, 1f), (sprite.Width, sprite.Height, sprite.PixelScale));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Named_party_presets_use_their_race_and_class_without_replacing_enemy_art()
    {
        Assert.Equal("tactical/human_fighter.png", Art.Value.Sprite("characters.kara")!.Path);
        Assert.Equal("tactical/elf_wizard.png", Art.Value.Sprite("characters.ilyra")!.Path);
        Assert.Equal("creatures/mob_refresh_goblin_archer.png", Art.Value.Sprite("characters.goblin_archer")!.Path);
    }

    [Theory]
    [InlineData("bosses.troll.ogre", "creatures/mob_refresh_troll_ogre.png", 384, 384, 192, 320, true)]
    [InlineData("bosses.dragon.blue", "creatures/mob_refresh_dragon_blue.png", 512, 512, 256, 400, true)]
    [InlineData("bosses.sapling.autumn", "bosses/sapling_autumn.png", 40, 40, 20, 37, false)]
    public void A_monster_key_names_its_set_and_variant_with_that_sets_frames(string key, string path, int w, int h, int ax, int ay, bool refreshed)
    {
        var sprite = Art.Value.Sprite(key)!;

        Assert.Equal((path, w, h, ax, ay), (sprite.Path, sprite.Width, sprite.Height, sprite.AnchorX, sprite.AnchorY));
        if (refreshed) Assert.Equal([13], sprite.Down.Frames);
        else Assert.Equal(Pack.Frames(key.Split('.')[0], "down"), sprite.Down.Frames);
    }

    [Fact]
    public void A_monster_key_with_no_or_an_unknown_variant_takes_the_first()
    {
        var primary = Art.Value.Sprite("beasts.wolf.grey");
        Assert.Same(primary, Art.Value.Sprite("beasts.wolf"));
        Assert.Same(primary, Art.Value.Sprite("beasts.wolf.nonsense"));
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
        var legacy = Art.Value.Sprite("characters.farmer")!; // legacy two-row addressing remains supported

        Assert.Equal(new ArtRect(62 * 32, 40, 32, 40), legacy.Source(62, 1));
        Assert.Equal(new ArtRect(0, 0, 32, 40), legacy.Source(0, 0));
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
    [InlineData("paralysed", "paralysed", true)]
    [InlineData("invisible", "hidden", true)]
    [InlineData("hiding", "hidden", true)]          // the rogue's Hide (#108)
    [InlineData("vexed", "marked", true)]           // the rogue's Vex (#108)
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
    [InlineData("grappled")]
    [InlineData("disengaged")]
    [InlineData("no_such_condition")]
    public void A_condition_with_no_art_is_null(string condition)
        => Assert.Null(Art.Value.Condition(condition));

    [Fact]
    public void A_condition_that_has_its_own_art_now_shows_it_and_keeps_what_it_borrowed()
    {
        var look = Art.Value.Condition("paralysed")!;

        Assert.Equal("status/paralysed.png", look.Effect!.Path);
        Assert.Equal(Art.Value.Condition("stunned")!.Icon, look.Icon);     // the pack draws no paralysed icon
    }

    [Fact]
    public void An_effect_says_which_side_of_the_unit_it_goes()
    {
        Assert.True(Art.Value.Condition("hasted")!.Effect!.Under);
        Assert.False(Art.Value.Condition("poisoned")!.Effect!.Under);
    }

    [Fact]
    public void Overhead_effects_follow_the_head_a_tall_sprite_lifts_them_a_small_one_lowers_them()
    {
        Assert.Equal(0, Art.Value.Sprite("characters.farmer")!.EffectShift);                // standard build
        var fighter = Art.Value.Sprite("characters.class_fighter")!;
        var dwarf = Art.Value.Sprite("characters.dwarf_fighter")!;
        Assert.Equal((int)MathF.Round(29 - fighter.HeadLift), fighter.EffectShift);
        Assert.True(dwarf.EffectShift > fighter.EffectShift);
        Assert.Equal(-33, Art.Value.Sprite("bosses.troll.frost")!.EffectShift); // approved giant head clearance
        foreach (var key in new[] { "bosses.imp.crimson", "bosses.imp.violet" })
        {
            var imp = Art.Value.Sprite(key)!;
            Assert.Equal((int)MathF.Round(29 - imp.HeadLift), imp.EffectShift);
            Assert.True(imp.EffectShift > fighter.EffectShift);
        }
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
