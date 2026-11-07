using Tactics.Presentation.Board;
using Tactics.Presentation.Hud;
using Tactics.Presentation.Tests.Board;

namespace Tactics.Presentation.Tests.Hud;

/// <summary><see cref="HudArt"/> against the real pack: ui/manifest.json's rectangles and each unit's portrait.</summary>
public sealed class HudArtTests
{
    [Fact]
    public void Mimic_hud_shows_the_complete_disguised_chest_instead_of_an_invented_head()
    {
        var sprite = Units.Value.Sprite("bosses.mimic.oak")!;
        var portrait = Art.Value.Portrait("bosses.mimic.oak", UnitTone.Enemy);
        Assert.Equal("creatures/mob_refresh_mimic.png", portrait.Path);
        Assert.Equal(sprite.PortraitSource, portrait.Source);
        var (pixels, width, _) = PngPixels.Read(Path.Combine(Pack.Root, sprite.Path));
        var source = sprite.Source(0, 0);
        var count = 0;
        for (var y = 0; y < source.Height; y++)
            for (var x = 0; x < source.Width; x++)
                if (pixels[((source.Y + y) * width + source.X + x) * 4 + 3] >= 128)
                {
                    Assert.InRange(x, portrait.Source.X, portrait.Source.X + portrait.Source.Width - 1);
                    Assert.InRange(y, portrait.Source.Y, portrait.Source.Y + portrait.Source.Height - 1);
                    count++;
                }
        Assert.True(count > 100);
        Assert.Equal("portraits/backdrops/enemy.png", portrait.Backdrop);
    }

    [Theory]
    [InlineData("mother_skarrow", "mother_skarrow", "mother_skarrow", "idle")]
    [InlineData("mother_skarrow", "mother_skarrow", "mother_skarrow", "hurt")]
    [InlineData("mother_skarrow", "mother_skarrow", "mother_skarrow", "happy")]
    [InlineData("mother_skarrow", "mother_skarrow", "mother_skarrow", "angry")]
    [InlineData("mother_skarrow", "mother_skarrow", "mother_skarrow", "sad")]
    [InlineData("mother_skarrow", "mother_skarrow", "mother_skarrow", "surprised")]
    [InlineData("guard_captain", "dagna_holt", "dagna", "idle")]
    [InlineData("guard_captain", "dagna_holt", "dagna", "hurt")]
    [InlineData("guard_captain", "dagna_holt", "dagna", "happy")]
    [InlineData("guard_captain", "dagna_holt", "dagna", "angry")]
    [InlineData("guard_captain", "dagna_holt", "dagna", "sad")]
    [InlineData("guard_captain", "dagna_holt", "dagna", "surprised")]
    [InlineData("bandit_captain", "rook", "rook", "idle")]
    [InlineData("bandit_captain", "rook", "rook", "hurt")]
    [InlineData("bandit_captain", "rook", "rook", "happy")]
    [InlineData("bandit_captain", "rook", "rook", "angry")]
    [InlineData("bandit_captain", "rook", "rook", "sad")]
    [InlineData("bandit_captain", "rook", "rook", "surprised")]
    [InlineData("noble", "celandine_marrowby", "celandine", "idle")]
    [InlineData("noble", "celandine_marrowby", "celandine", "hurt")]
    [InlineData("noble", "celandine_marrowby", "celandine", "happy")]
    [InlineData("noble", "celandine_marrowby", "celandine", "angry")]
    [InlineData("noble", "celandine_marrowby", "celandine", "sad")]
    [InlineData("noble", "celandine_marrowby", "celandine", "surprised")]
    [InlineData("innkeeper", "bree_tallow", "bree_tallow", "idle")]
    [InlineData("innkeeper", "bree_tallow", "bree_tallow", "hurt")]
    [InlineData("innkeeper", "bree_tallow", "bree_tallow", "happy")]
    [InlineData("innkeeper", "bree_tallow", "bree_tallow", "angry")]
    [InlineData("innkeeper", "bree_tallow", "bree_tallow", "sad")]
    [InlineData("innkeeper", "bree_tallow", "bree_tallow", "surprised")]
    [InlineData("mayor", "halden_varrow", "halden_varrow", "idle")]
    [InlineData("mayor", "halden_varrow", "halden_varrow", "hurt")]
    [InlineData("mayor", "halden_varrow", "halden_varrow", "happy")]
    [InlineData("mayor", "halden_varrow", "halden_varrow", "angry")]
    [InlineData("mayor", "halden_varrow", "halden_varrow", "sad")]
    [InlineData("mayor", "halden_varrow", "halden_varrow", "surprised")]
    public void Approved_named_portrait_aliases_select_each_authored_mood(string preset, string character, string file, string mood)
    {
        foreach (var key in new[] { preset, character })
        {
            var face = Art.Value.Speaker(key, mood, "ally");
            Assert.NotNull(face);
            Assert.Equal($"portraits/{file}_{mood}.png", face.Path);
            Assert.Equal(new ArtRect(0, 0, 40, 40), face.Source);
            Assert.True(File.Exists(Path.Combine(Pack.Root, face.Path)));
        }
    }
    private static readonly Lazy<UnitArt> Units = new(() => UnitArt.Load(Pack.Root));
    private static readonly Lazy<HudArt> Art = new(() => HudArt.Load(Pack.Root, Units.Value));

    [Fact]
    public void A_panel_has_its_slice_outset_and_padding()
    {
        var main = Art.Value.Panel("main");

        Assert.Equal(new ArtRect(0, 0, 42, 34), main.Rect);
        Assert.Equal(new ArtEdges(11, 10, 11, 5), main.Slice);
        Assert.Equal(new ArtEdges(4, 4, 4, 0), main.Outset);
        Assert.Equal(new ArtEdges(7, 6, 7, 6), main.Padding);
    }

    [Fact]
    public void A_piece_with_no_outset_has_none()
        => Assert.Equal(default, Art.Value.Panel("card").Outset);

    [Theory]
    [InlineData("frame_active", 0, 52)]
    [InlineData("frame_inactive", 53, 52)]
    [InlineData("frame_enemy", 106, 52)]
    [InlineData("now_tab", 0, 23)]
    [InlineData("ai_badge", 24, 15)]
    [InlineData("stripe_p3", 62, 10)]
    public void The_turn_orders_pieces_are_where_the_manifest_says(string piece, int x, int width)
    {
        var rect = Art.Value.Turn(piece).Rect;

        Assert.Equal(x, rect.X);
        Assert.Equal(width, rect.Width);
    }

    [Theory]
    [InlineData("characters.farmer", UnitTone.Neutral, "portraits/generic_farmer_a_idle.png", "portraits/backdrops/neutral.png")]
    public void A_character_has_its_portrait_on_its_sides_backdrop(string sprite, UnitTone tone, string path, string backdrop)
    {
        var portrait = Art.Value.Portrait(sprite, tone);

        Assert.Equal(path, portrait.Path);
        Assert.Equal(backdrop, portrait.Backdrop);
        Assert.Equal(new ArtRect(0, 0, 40, 40), portrait.Source);
    }

    [Theory]
    [InlineData("characters.goblin_cutthroat", "goblin", 48)]
    [InlineData("characters.orc_brute", "orc", 48)]
    [InlineData("beasts.drake.red", "drake", 64)]
    [InlineData("characters.goblin_archer", "goblin_archer", 56)]
    [InlineData("characters.goblin_shaman", "goblin_shaman", 56)]
    [InlineData("characters.goblin_spear", "goblin_boss", 56)]
    [InlineData("characters.hobgoblin", "hobgoblin", 64)]
    [InlineData("characters.bandit", "bandit", 64)]
    [InlineData("characters.cultist", "cultist", 64)]
    [InlineData("characters.cult_leader", "cult_leader", 64)]
    [InlineData("characters.skeleton", "skeleton", 64)]
    [InlineData("characters.skeleton_archer", "skeleton_archer", 64)]
    [InlineData("characters.zombie", "zombie", 64)]
    [InlineData("bosses.wraith.wraith", "wraith", 64)]
    [InlineData("characters.necromancer", "necromancer", 64)]
    [InlineData("characters.lich", "lich", 64)]
    [InlineData("characters.black_knight", "black_knight", 64)]
    [InlineData("characters.dark_elf", "dark_elf", 64)]
    [InlineData("bosses.troll.troll", "troll", 64)]
    [InlineData("bosses.golem.granite", "golem", 64)]
    public void Refreshed_mobs_show_their_current_atlas_head_in_the_battle_hud(string key, string file, int size)
    {
        var portrait = Art.Value.Portrait(key, UnitTone.Enemy);
        Assert.Equal($"creatures/mob_refresh_{file}.png", portrait.Path);
        Assert.Equal(Units.Value.Sprite(key)!.PortraitSource, portrait.Source);
        Assert.Equal(size, portrait.Source.Width);
        Assert.Equal(size, portrait.Source.Height);
        Assert.Equal("portraits/backdrops/enemy.png", portrait.Backdrop);
    }

    [Theory]
    [InlineData("goblin_cutthroat")]
    [InlineData("orc_brute")]
    [InlineData("goblin_archer")]
    [InlineData("goblin_shaman")]
    [InlineData("goblin_spear")]
    [InlineData("hobgoblin")]
    [InlineData("bandit")]
    [InlineData("cultist")]
    [InlineData("cult_leader")]
    [InlineData("skeleton")]
    [InlineData("skeleton_archer")]
    [InlineData("zombie")]
    [InlineData("necromancer")]
    [InlineData("lich")]
    [InlineData("black_knight")]
    [InlineData("dark_elf")]
    public void Refreshed_speakers_keep_all_six_expression_roles(string preset)
    {
        var files = new HashSet<string>();
        foreach (var mood in new[] { "idle", "hurt", "happy", "angry", "sad", "surprised" })
        {
            var face = Art.Value.Speaker(preset, mood, "villain")!;
            Assert.Equal($"portraits/{preset}_refresh_{mood}.png", face.Path);
            Assert.Equal(new ArtRect(0, 0, 40, 40), face.Source);
            files.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(Pack.Root, face.Path)))));
        }
        Assert.Equal(6, files.Count);
    }

    [Theory]
    [InlineData("idle")]
    [InlineData("hurt")]
    [InlineData("happy")]
    [InlineData("angry")]
    [InlineData("sad")]
    [InlineData("surprised")]
    public void Aubrines_priest_and_named_portrait_aliases_select_each_authored_mood(string mood)
    {
        foreach (var preset in new[] { "priest", "mother_aubrine" })
        {
            var face = Art.Value.Speaker(preset, mood, "ally");
            Assert.NotNull(face);
            Assert.Equal($"portraits/mother_aubrine_{mood}.png", face.Path);
            Assert.Equal(new ArtRect(0, 0, 40, 40), face.Source);
            Assert.True(File.Exists(Path.Combine(Pack.Root, face.Path)));
        }
    }

    [Theory]
    [InlineData("idle")]
    [InlineData("hurt")]
    [InlineData("happy")]
    [InlineData("angry")]
    [InlineData("sad")]
    [InlineData("surprised")]
    public void Tobins_blacksmith_and_named_portrait_aliases_select_each_authored_mood(string mood)
    {
        foreach (var preset in new[] { "blacksmith", "tobin_fell" })
        {
            var face = Art.Value.Speaker(preset, mood, "ally");
            Assert.NotNull(face);
            Assert.Equal($"portraits/tobin_fell_{mood}.png", face.Path);
            Assert.Equal(new ArtRect(0, 0, 40, 40), face.Source);
            Assert.True(File.Exists(Path.Combine(Pack.Root, face.Path)));
        }
    }

    [Theory]
    [InlineData("idle")]
    [InlineData("hurt")]
    [InlineData("happy")]
    [InlineData("angry")]
    [InlineData("sad")]
    [InlineData("surprised")]
    public void Larks_minstrel_and_named_portrait_aliases_select_each_authored_mood(string mood)
    {
        foreach (var preset in new[] { "minstrel", "lark" })
        {
            var face = Art.Value.Speaker(preset, mood, "ally");
            Assert.NotNull(face);
            Assert.Equal($"portraits/lark_{mood}.png", face.Path);
            Assert.Equal(new ArtRect(0, 0, 40, 40), face.Source);
            Assert.True(File.Exists(Path.Combine(Pack.Root, face.Path)));
        }
    }

    [Theory]
    [InlineData("idle")]
    [InlineData("hurt")]
    [InlineData("happy")]
    [InlineData("angry")]
    [InlineData("sad")]
    [InlineData("surprised")]
    public void Corvins_sage_and_named_portrait_aliases_select_each_authored_mood(string mood)
    {
        foreach (var preset in new[] { "sage", "corvin_ashby" })
        {
            var face = Art.Value.Speaker(preset, mood, "ally");
            Assert.NotNull(face);
            Assert.Equal($"portraits/corvin_{mood}.png", face.Path);
            Assert.Equal(new ArtRect(0, 0, 40, 40), face.Source);
            Assert.True(File.Exists(Path.Combine(Pack.Root, face.Path)));
        }
    }

    [Theory]
    [InlineData("idle")]
    [InlineData("hurt")]
    [InlineData("happy")]
    [InlineData("angry")]
    [InlineData("sad")]
    [InlineData("surprised")]
    public void Pips_variant_and_named_portrait_aliases_select_each_authored_mood(string mood)
    {
        foreach (var preset in new[] { "child_pip", "pip_ashdown" })
        {
            var face = Art.Value.Speaker(preset, mood, "ally");
            Assert.NotNull(face);
            Assert.Equal($"portraits/pip_{mood}.png", face.Path);
            Assert.Equal(new ArtRect(0, 0, 40, 40), face.Source);
            Assert.True(File.Exists(Path.Combine(Pack.Root, face.Path)));
        }
    }

    [Fact]
    public void Tactical_portraits_crop_the_face_instead_of_shrinking_the_padded_frame()
    {
        var portrait = Art.Value.Portrait("characters.tiefling_warlock", UnitTone.Party);
        var sprite = Units.Value.Sprite("characters.tiefling_warlock")!;
        Assert.Equal(sprite.Path, portrait.Path);
        Assert.Equal(new ArtRect(sprite.AnchorX - 20, sprite.HeadTop - 4, 40, 40), portrait.Source);
    }

    [Fact]
    public void A_creature_portrait_uses_its_authored_region_or_first_idle_frame()
    {
        var sprite = Units.Value.Sprite("beasts.wolf.grey")!;

        var portrait = Art.Value.Portrait("beasts.wolf.grey", UnitTone.Enemy);

        Assert.Equal(sprite.Path, portrait.Path);
        Assert.Equal(sprite.PortraitSource ?? sprite.Source(sprite.Idle.Frames[0], 0), portrait.Source);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("nonsense.key")]
    public void A_unit_with_no_art_still_has_a_figure(string? sprite)
        => Assert.Contains("presets/", Art.Value.Portrait(sprite, UnitTone.Enemy).Path);

    [Fact]
    public void Every_monsters_sprite_has_a_picture_on_disk()
    {
        foreach (var name in new[] { "characters.class_fighter", "characters.class_warlock", "characters.hobgoblin", "characters.goblin_spear", "characters.goblin_shaman",
            "characters.kobold", "characters.skeleton", "characters.zombie", "characters.bandit_captain", "bosses.troll.troll", "beasts.spider.black" })
            Assert.True(File.Exists(Path.Combine(Pack.Root, Art.Value.Portrait(name, UnitTone.Enemy).Path)), name);
    }
}
