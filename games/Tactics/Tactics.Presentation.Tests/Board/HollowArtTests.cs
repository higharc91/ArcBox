using Tactics.Logic;
using Tactics.Presentation.Board;
using Tactics.Presentation.Hud;

namespace Tactics.Presentation.Tests.Board;

public sealed class HollowArtTests
{
    [Theory]
    [InlineData("ashmaw", "ashmaw_bounty", "ashmaw", "roar", "shake")]
    [InlineData("kobold_foreman", "grisk_bounty", "grisk", "shout", "attack")]
    public void Approved_named_art_resolves_its_own_atlas_portrait_and_full_action_facings(
        string key, string atlas, string portrait, string special, string other)
    {
        var art = UnitArt.Load(Pack.Root);
        var sprite = Assert.IsType<UnitSprite>(art.Sprite("monsters." + key));
        Assert.Equal("creatures/" + atlas + ".png", sprite.Path);
        Assert.Equal(8, sprite.Directions.Length);
        foreach (var action in new[] { "idle", "walk", "attack", "hurt", "down", special, other })
            Assert.True(sprite.Anims.ContainsKey(action), $"{key} needs its own {action} clip.");
        Assert.False(sprite.Anim("down").Loop);
        var hud = HudArt.Load(Pack.Root, art).Portrait("monsters." + key, UnitTone.Enemy);
        Assert.Equal("portraits/" + portrait + ".png", hud.Path);
        Assert.Equal(new ArtRect(0, 0, 40, 40), hud.Source);

        // Generated breathing can shift a paw by a source pixel. At actual game scale,
        // every idle facing must remain within two display pixels of its fixed contact anchor.
        var (rgba, width, _) = PngPixels.Read(Path.Combine(Pack.Root, sprite.Path));
        foreach (var frame in sprite.Idle.Frames)
            for (var direction = 0; direction < 8; direction++)
            {
                var source = sprite.Source(frame, direction);
                var bottom = -1;
                for (var y = 0; y < source.Height; y++)
                    for (var x = 0; x < source.Width; x++)
                        if (rgba[((source.Y + y) * width + source.X + x) * 4 + 3] != 0) bottom = y;
                Assert.InRange(Math.Abs(bottom - sprite.AnchorY) * sprite.PixelScale, 0, 2);
            }
    }

    [Theory]
    [InlineData("kobold", "worker")]
    [InlineData("kobold_slinger", "slinger")]
    public void Refreshed_kobolds_use_matching_atlases_and_portraits(string preset, string role)
    {
        var art = UnitArt.Load(Pack.Root);
        var sprite = art.Sprite("characters." + preset)!;
        Assert.Equal("creatures/kobold_refresh_" + role + ".png", sprite.Path);
        foreach (var action in new[] { "idle", "walk", "attack", "hurt", "down" })
            Assert.True(sprite.Anims.ContainsKey(action));
        Assert.Equal(8, sprite.Directions.Length);
        var portrait = HudArt.Load(Pack.Root, art).Portrait("characters." + preset, UnitTone.Enemy);
        Assert.Equal("portraits/" + preset + "_refresh.png", portrait.Path);
        Assert.Equal(new ArtRect(0, 0, 40, 40), portrait.Source);
        if (role == "slinger")
        {
            Assert.True(sprite.Anims.ContainsKey("shoot"));
            Assert.True(sprite.Anims.ContainsKey("throw"));
        }
    }

    [Fact]
    public void Grisk_uses_his_approved_art_in_conversations_and_world_markers()
    {
        var grisk = LoreBook.Load(Pack.Root).Character("grisk")!;
        Assert.Equal("creatures.grisk_bounty", grisk.SpriteKey);
        Assert.Equal("grisk", grisk.PortraitKey);
    }
}
