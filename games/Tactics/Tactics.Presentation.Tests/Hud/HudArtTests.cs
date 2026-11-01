using Tactics.Presentation.Board;
using Tactics.Presentation.Hud;
using Tactics.Presentation.Tests.Board;

namespace Tactics.Presentation.Tests.Hud;

/// <summary><see cref="HudArt"/> against the real pack: ui/manifest.json's rectangles and each unit's portrait.</summary>
public sealed class HudArtTests
{
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
    [InlineData("characters.class_fighter", UnitTone.Party, "portraits/class_fighter_idle.png", "portraits/backdrops/ally.png")]
    [InlineData("characters.goblin_cutthroat", UnitTone.Enemy, "portraits/goblin_cutthroat_idle.png", "portraits/backdrops/enemy.png")]
    [InlineData("characters.farmer", UnitTone.Neutral, "portraits/farmer_idle.png", "portraits/backdrops/neutral.png")]
    public void A_character_has_its_portrait_on_its_sides_backdrop(string sprite, UnitTone tone, string path, string backdrop)
    {
        var portrait = Art.Value.Portrait(sprite, tone);

        Assert.Equal(path, portrait.Path);
        Assert.Equal(backdrop, portrait.Backdrop);
        Assert.Equal(new ArtRect(0, 0, 40, 40), portrait.Source);
    }

    [Fact]
    public void A_beast_with_no_portrait_is_its_own_sprites_first_idle_frame()
    {
        var sprite = Units.Value.Sprite("beasts.wolf.grey")!;

        var portrait = Art.Value.Portrait("beasts.wolf.grey", UnitTone.Enemy);

        Assert.Equal(sprite.Path, portrait.Path);
        Assert.Equal(sprite.Source(sprite.Idle.Frames[0], 0), portrait.Source);
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
