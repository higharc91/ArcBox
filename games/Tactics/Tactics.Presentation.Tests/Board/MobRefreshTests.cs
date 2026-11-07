using Tactics.Presentation.Board;

namespace Tactics.Presentation.Tests.Board;

public sealed class MobRefreshTests
{
    [Theory]
    [InlineData("characters.goblin_cutthroat", "goblin")]
    [InlineData("characters.orc_brute", "orc")]
    [InlineData("beasts.drake.red", "drake")]
    [InlineData("characters.goblin_archer", "goblin_archer")]
    [InlineData("characters.goblin_shaman", "goblin_shaman")]
    [InlineData("characters.goblin_spear", "goblin_boss")]
    public void Approved_mobs_have_real_directional_motion_and_complete_baked_figures(string key, string name)
    {
        var sprite = UnitArt.Load(Pack.Root).Sprite(key)!;
        Assert.Equal($"creatures/mob_refresh_{name}.png", sprite.Path);
        Assert.Equal(8, sprite.Directions.Length);
        var (pixels, width, _) = PngPixels.Read(Path.Combine(Pack.Root, sprite.Path));
        for (var row = 0; row < 8; row++)
        {
            foreach (var action in new[] { "idle", "walk", "attack", "hurt", "down" }.Concat(sprite.Anims.ContainsKey("cast") ? ["cast"] : Array.Empty<string>()))
            {
                var shapes = new HashSet<string>();
                foreach (var frame in sprite.Anim(action).Frames.Distinct())
                {
                    var source = sprite.Source(frame, row);
                    var occupied = new HashSet<int>();
                    for (var y = 0; y < source.Height; y++)
                        for (var x = 0; x < source.Width; x++)
                            if (pixels[((source.Y + y) * width + source.X + x) * 4 + 3] >= 128)
                            {
                                Assert.InRange(x, 2, source.Width - 3);
                                Assert.InRange(y, 2, source.Height - 3);
                                occupied.Add(y * source.Width + x);
                            }
                    Assert.True(occupied.Count > 100, $"Empty {key}/{row}/{action}/{frame}");
                    var signature = System.Security.Cryptography.SHA256.HashData(
                        [.. occupied.Order().SelectMany(BitConverter.GetBytes)]);
                    shapes.Add(Convert.ToHexString(signature));
                    // Eight-neighbour connectivity catches a severed blade/hand or detached wing panel.
                    var pending = new HashSet<int>(occupied);
                    var largest = 0;
                    while (pending.Count > 0)
                    {
                        var queue = new Queue<int>(); queue.Enqueue(pending.First()); pending.Remove(queue.Peek());
                        var count = 0;
                        while (queue.TryDequeue(out var at))
                        {
                            count++;
                            for (var dy = -1; dy <= 1; dy++)
                                for (var dx = -1; dx <= 1; dx++)
                                {
                                    var next = at + dy * source.Width + dx;
                                    if (Math.Abs(next % source.Width - at % source.Width) <= 1 && pending.Remove(next)) queue.Enqueue(next);
                                }
                        }
                        largest = Math.Max(largest, count);
                    }
                    Assert.True(largest >= occupied.Count * .98, $"Detached silhouette in {key}/{row}/{action}/{frame}");
                }
                if (action is "idle" or "walk") Assert.True(shapes.Count > 1, $"Static {key}/{row}/{action}");
            }
        }
    }

    [Fact]
    public void Goblin_roles_use_their_authored_shooting_spell_and_rally_poses()
    {
        var art = UnitArt.Load(Pack.Root);
        var archer = art.Sprite("characters.goblin_archer")!;
        Assert.Equal(archer.Anim("attack").Frames.ToArray(), archer.Anim("characters.shoot").Frames.ToArray());
        Assert.Equal(archer.Anim("attack").Fps, archer.Anim("characters.shoot").Fps);
        foreach (var key in new[] { "characters.goblin_shaman", "characters.goblin_spear" })
        {
            var sprite = art.Sprite(key)!;
            Assert.Equal(sprite.Anim("cast").Frames.ToArray(), sprite.Anim("characters.point").Frames.ToArray());
            Assert.Equal(sprite.Anim("cast").Frames.ToArray(), sprite.Anim("characters.summon").Frames.ToArray());
            Assert.NotEqual(sprite.Anim("attack").Frames, sprite.Anim("cast").Frames);
        }
        Assert.Equal("creatures/mother_skarrow.png", art.Sprite("characters.mother_skarrow")!.Path);
    }

    [Fact]
    public void Drake_spreads_its_wings_in_every_attack_facing()
    {
        var sprite = UnitArt.Load(Pack.Root).Sprite("beasts.drake.red")!;
        var (pixels, width, _) = PngPixels.Read(Path.Combine(Pack.Root, sprite.Path));
        (int Width, int Top) Bounds(int frame, int row)
        {
            var source = sprite.Source(frame, row);
            var left = source.Width; var right = 0; var top = source.Height;
            for (var y = 0; y < source.Height; y++)
                for (var x = 0; x < source.Width; x++)
                    if (pixels[((source.Y + y) * width + source.X + x) * 4 + 3] >= 128)
                    { left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); }
            return (right - left + 1, top);
        }
        for (var row = 0; row < 8; row++)
        {
            var idle = Bounds(sprite.Idle.Frames[0], row);
            var attack = Bounds(10, row);
            Assert.True(attack.Width > idle.Width * 1.25, $"Wings did not widen facing {row}");
            Assert.True(attack.Top < idle.Top - 4, $"Wings did not rise facing {row}");
        }
    }

    [Fact]
    public void Visible_mob_heights_keep_the_approved_party_scale_regardless_of_atlas_padding()
    {
        var art = UnitArt.Load(Pack.Root);
        var person = art.Sprite("characters.class_fighter")!.HeadLift;
        Assert.InRange(art.Sprite("characters.goblin_cutthroat")!.HeadLift / person, .5f, .9f);
        Assert.InRange(art.Sprite("characters.orc_brute")!.HeadLift / person, 1f, 1.5f);
        Assert.InRange(art.Sprite("beasts.drake.red")!.HeadLift / person, .5f, 1.1f);
    }
}
