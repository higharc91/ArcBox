using System.Buffers.Binary;
using System.Text.Json;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.Tests.Board;

public sealed class CreatureAtlasTests
{
    [Fact]
    public void Every_registered_creature_view_and_action_stays_inside_its_runtime_atlas()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Pack.Root, "creatures", "manifest.json")));
        var art = UnitArt.Load(Pack.Root);
        foreach (var item in doc.RootElement.GetProperty("sprites").EnumerateObject())
        {
            var entry = item.Value;
            var sprite = art.Sprite(entry.GetProperty("keys")[0].GetString())!;
            var bytes = File.ReadAllBytes(Path.Combine(Pack.Root, sprite.Path));
            var width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4));
            var height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4));
            Assert.Equal(8, sprite.Directions.Length);
            Assert.Equal(8, sprite.Directions.Distinct().Count());
            Assert.InRange(sprite.AnchorX, 0, sprite.Width - 1);
            Assert.InRange(sprite.AnchorY, 0, sprite.Height - 1);
            foreach (var anim in sprite.Anims.Values)
                foreach (var frame in anim.Frames)
                    for (var row = 0; row < 8; row++)
                    {
                        var source = sprite.Source(frame, row);
                        Assert.InRange(source.X, 0, width - source.Width);
                        Assert.InRange(source.Y, 0, height - source.Height);
                    }
            if (entry.GetProperty("family").GetString() == "townsfolk")
            {
                // Named townsfolk retain their authored counts; Skarrow also appears in battle.
                Assert.Equal(item.Name switch { "mother_aubrine" or "tobin_fell" or "lark" or "corvin" or "pip" or "dagna" or "celandine" or "rook" or "mother_skarrow" or "old_maud" => 10, "bree_tallow" => 5, _ => 4 }, sprite.Idle.Frames.Length);
                Assert.Equal(6, sprite.Anims["walk"].Frames.Length);
                Assert.Equal(item.Name is "mother_aubrine" or "tobin_fell" or "lark" or "corvin" or "pip" or "dagna" or "celandine" or "rook" or "mother_skarrow" or "old_maud" ? 10 : 8, sprite.Anims["talk"].Frames.Length);
                if (item.Name == "mother_skarrow")
                {
                    Assert.Equal(6, sprite.Anims["attack"].Frames.Length);
                    Assert.Equal(6, sprite.Anims["cast"].Frames.Length);
                    Assert.Equal([33, 34, 35], sprite.Down.Frames);
                    Assert.False(sprite.Down.Loop);
                }
                else Assert.False(sprite.Anims.ContainsKey("attack"));
            }
            else
            {
                Assert.Equal(4, sprite.Idle.Frames.Length);
                Assert.Equal(entry.GetProperty("family").GetString() is "ashmaw" or "kobold_foreman" or "kobold" ? 6 : 8,
                    sprite.Anim("walk").Frames.Length);
                Assert.InRange(sprite.Anim("attack").Frames.Length, 4, 8);
            }
        }
    }

    [Fact]
    public void Distinct_structural_forms_keep_their_identity_and_their_original_art()
    {
        var art = UnitArt.Load(Pack.Root);
        Assert.Equal("bestiary/warg_rider.png", art.Sprite("bestiary.warg.rider")!.Path);
        Assert.Equal("beasts/bear_undead.png", art.Sprite("beasts.bear.undead")!.Path);
        Assert.Equal("beasts/slime_crystal.png", art.Sprite("beasts.slime.crystal")!.Path);
    }

    [Fact]
    public void The_dire_wolf_retains_its_larger_physical_size_and_distinct_coat()
    {
        var art = UnitArt.Load(Pack.Root);
        var normal = art.Sprite("beasts.wolf.grey")!;
        var dire = art.Sprite("beasts.wolf.dire")!;
        Assert.StartsWith("creatures/", dire.Path);
        Assert.NotEqual(normal.Path, dire.Path);
        Assert.True(dire.PixelScale > normal.PixelScale);
        Assert.Equal(normal.Directions, dire.Directions);
    }
    [Fact]
    public void All_twenty_one_world_creature_families_have_real_compass_views_and_stable_ground_contact()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Pack.Root, "creatures", "manifest.json")));
        var art = UnitArt.Load(Pack.Root);
        var primaries = doc.RootElement.GetProperty("sprites").EnumerateObject()
            .Where(e => e.Value.GetProperty("keys").EnumerateArray().Any(k => k.GetString()!.Split('.').Length == 2
                && !k.GetString()!.StartsWith("monsters.", StringComparison.Ordinal)
                && !k.GetString()!.StartsWith("characters.", StringComparison.Ordinal))).ToArray();
        Assert.Equal(21, primaries.Length);
        foreach (var primary in primaries)
        {
            var e = primary.Value;
            var key = e.GetProperty("keys").EnumerateArray().First(k => k.GetString()!.Split('.').Length == 2).GetString()!;
            var sprite = art.Sprite(key)!;
            Assert.Equal(8, sprite.Directions.Length);
            if (e.GetProperty("family").GetString() is "bat" or "harpy" or "wisp")
                continue;
            var (rgba, width, _) = PngPixels.Read(Path.Combine(Pack.Root, sprite.Path));
            for (var row = 0; row < 8; row++)
            {
                var source = sprite.Source(sprite.Idle.Frames[0], row);
                var bottom = -1;
                for (var y = 0; y < source.Height; y++)
                    for (var x = 0; x < source.Width; x++)
                        if (rgba[((source.Y + y) * width + source.X + x) * 4 + 3] != 0)
                            bottom = y;
                Assert.Equal(sprite.AnchorY, bottom);
            }
        }
        Assert.True(art.Sprite("bestiary.wisp.marsh")!.Unlit);
    }
    [Fact]
    public void Creature_sizes_are_proportionate_to_the_actual_player_at_game_scale()
    {
        var art = UnitArt.Load(Pack.Root);
        var person = art.Sprite("characters.class_fighter")!.HeadLift;
        (string Key, float MaxHeightRatio, float MaxWidthRatio)[] budgets =
        [
            ("beasts.wolf.grey", .9f, 1.15f),
            ("beasts.boar.brown", .9f, 1.1f),
            ("beasts.bear.brown", 1.3f, 1.4f),
            ("beasts.spider.black", 1.3f, 1.6f),
            ("beasts.bat.dusk", 1.0f, 1.8f),
            ("beasts.drake.red", 1.85f, 2.1f),
            ("bestiary.warg.black", 1.4f, 1.7f),
            ("bestiary.hookbeak.brown", 1.5f, 1.6f),
            ("bestiary.scorpion.sand", 1.5f, 1.7f),
            ("bestiary.harpy.dusk", 1.4f, 1.7f),
            ("bestiary.gargoyle.granite", 1.75f, 1.8f),
            ("bestiary.crab.shore", 1.2f, 1.8f),
        ];
        foreach (var (key, maxHeight, maxWidth) in budgets)
        {
            var sprite = art.Sprite(key)!;
            var (rgba, width, _) = PngPixels.Read(Path.Combine(Pack.Root, sprite.Path));
            for (var facing = 0; facing < 8; facing++)
            {
                var source = sprite.Source(sprite.Idle.Frames[0], facing);
                var left = source.Width; var top = source.Height; var right = -1; var bottom = -1;
                for (var y = 0; y < source.Height; y++)
                    for (var x = 0; x < source.Width; x++)
                        if (rgba[((source.Y + y) * width + source.X + x) * 4 + 3] != 0)
                        { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y); }
                Assert.True((bottom - top + 1) * sprite.PixelScale <= person * maxHeight,
                    $"{key}, facing {facing}: exceeds its height budget beside the player");
                Assert.True((right - left + 1) * sprite.PixelScale <= person * maxWidth,
                    $"{key}, facing {facing}: exceeds its width budget beside the player");
            }
        }
    }

}
