using Tactics.Presentation.Board;
using Tactics.Logic;

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
    [InlineData("characters.hobgoblin", "hobgoblin")]
    [InlineData("characters.bandit", "bandit")]
    [InlineData("characters.cultist", "cultist")]
    [InlineData("characters.cult_leader", "cult_leader")]
    [InlineData("characters.skeleton", "skeleton")]
    [InlineData("characters.skeleton_archer", "skeleton_archer")]
    [InlineData("characters.zombie", "zombie")]
    [InlineData("bosses.wraith.wraith", "wraith")]
    [InlineData("characters.necromancer", "necromancer")]
    [InlineData("characters.lich", "lich")]
    [InlineData("characters.black_knight", "black_knight")]
    [InlineData("characters.dark_elf", "dark_elf")]
    [InlineData("bosses.troll.troll", "troll")]
    [InlineData("bosses.golem.granite", "golem")]
    [InlineData("bosses.mimic.oak", "mimic")]
    public void Approved_mobs_have_real_directional_motion_and_complete_baked_figures(string key, string name)
    {
        var sprite = UnitArt.Load(Pack.Root).Sprite(key)!;
        Assert.Equal($"creatures/mob_refresh_{name}.png", sprite.Path);
        Assert.Equal(8, sprite.Directions.Length);
        var (pixels, width, _) = PngPixels.Read(Path.Combine(Pack.Root, sprite.Path));
        for (var row = 0; row < 8; row++)
        {
            foreach (var action in new[] { "idle", "walk", "attack", "hurt", "down" }.Concat(new[] { "cast", "special", "buff" }.Where(sprite.Anims.ContainsKey)))
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

    [Theory]
    [InlineData("bosses.troll.troll", "troll.boulder_toss", "troll.frenzy_roar")]
    [InlineData("bosses.golem.granite", "golem.seismic_slam", "golem.stone_aegis")]
    public void Giants_bind_their_two_special_moves_to_distinct_complete_clips(string key, string primary, string buff)
    {
        var sprite = UnitArt.Load(Pack.Root).Sprite(key)!;
        var rules = RulesPack.Load([Path.Combine(Pack.Root, "rules")], Pack.Manifests);
        var primaryBinding = rules.Abilities[new(primary)].Presentation!.Anim!;
        var buffBinding = rules.Abilities[new(buff)].Presentation!.Anim!;
        Assert.Equal("bosses.special", primaryBinding);
        Assert.Equal("bosses.buff", buffBinding);
        Assert.Equal(sprite.Anim("special").Frames.ToArray(), sprite.Anim(primaryBinding).Frames.ToArray());
        Assert.Equal(sprite.Anim("buff").Frames.ToArray(), sprite.Anim(buffBinding).Frames.ToArray());
        Assert.False(sprite.Anim(primaryBinding).Frames.SequenceEqual(sprite.Anim(buffBinding).Frames));
        Assert.False(sprite.Anim(primaryBinding).Loop);
        Assert.False(sprite.Anim(buffBinding).Loop);
        Assert.False(sprite.Anim("attack").Frames.SequenceEqual(sprite.Anim(primaryBinding).Frames));
    }

    [Theory]
    [InlineData("bosses.troll.frost")]
    [InlineData("bosses.golem.basalt")]
    public void Legacy_boss_variants_keep_their_existing_special_clip_for_the_buff_binding(string key)
    {
        var sprite = UnitArt.Load(Pack.Root).Sprite(key)!;
        Assert.Equal(sprite.Anim("special").Frames.ToArray(), sprite.Anim("bosses.buff").Frames.ToArray());
        Assert.Equal(sprite.Anim("special").Fps, sprite.Anim("bosses.buff").Fps);
        Assert.Equal(sprite.Anim("special").Loop, sprite.Anim("bosses.buff").Loop);
        Assert.Equal(Pack.Frames("bosses", "special"), sprite.Anim("bosses.buff").Frames.ToArray());
    }

    [Fact]
    public void Halvard_cleave_uses_the_greatsword_swing_and_ward_uses_a_separate_guard()
    {
        var sprite = UnitArt.Load(Pack.Root).Sprite("characters.black_knight")!;
        Assert.Equal(sprite.Anim("attack").Frames.ToArray(), sprite.Anim("characters.slam").Frames.ToArray());
        Assert.Equal(sprite.Anim("attack").Fps, sprite.Anim("characters.slam").Fps);
        Assert.Equal(sprite.Anim("cast").Frames.ToArray(), sprite.Anim("characters.buff").Frames.ToArray());
        Assert.False(sprite.Anim("cast").Frames.SequenceEqual(sprite.Anim("attack").Frames));
        Assert.False(sprite.Anim("characters.slam").Loop);
    }

    [Fact]
    public void Drow_shadow_step_uses_an_invocation_separate_from_the_rapier_thrust()
    {
        var sprite = UnitArt.Load(Pack.Root).Sprite("characters.dark_elf")!;
        Assert.Equal(sprite.Anim("cast").Frames.ToArray(), sprite.Anim("characters.cast").Frames.ToArray());
        Assert.False(sprite.Anim("cast").Loop);
        Assert.False(sprite.Anim("cast").Frames.SequenceEqual(sprite.Anim("attack").Frames));
    }

    [Theory]
    [InlineData("characters.necromancer", false)]
    [InlineData("characters.lich", true)]
    public void Rime_keeper_staff_focus_stays_on_the_right_side_when_walking_and_turning(string key, bool ivory)
    {
        var sprite = UnitArt.Load(Pack.Root).Sprite(key)!;
        var (pixels, width, _) = PngPixels.Read(Path.Combine(Pack.Root, sprite.Path));
        foreach (var row in new[] { 0, 1, 3, 4, 5, 7 })
            foreach (var action in new[] { "idle", "walk" })
                foreach (var frame in sprite.Anim(action).Frames.Distinct())
                {
                    var source = sprite.Source(frame, row);
                    var left = 0; var right = 0;
                    // The small glass cap or ivory ring is the staff's landmark. Exclude
                    // the central head, and keep the band above the hands and robe trim.
                    for (var y = sprite.HeadTop; y < sprite.HeadTop + 30; y++)
                        for (var x = 0; x < source.Width; x++)
                        {
                            var at = ((source.Y + y) * width + source.X + x) * 4;
                            var r = pixels[at]; var g = pixels[at + 1]; var b = pixels[at + 2];
                            var focus = ivory ? r > 155 && g > 140 && b > 100 && r >= g && g > b + 8
                                : r > 100 && g > 110 && b > 115 && g >= r && b >= r;
                            if (pixels[at + 3] < 128 || !focus) continue;
                            if (x < sprite.AnchorX - 22) left++;
                            if (x > sprite.AnchorX + 22) right++;
                        }
                    var rear = row is 3 or 4 or 5;
                    Assert.True(rear ? right > left + 3 : left > right + 3,
                        $"Staff changed side in {key}/{row}/{action}/{frame}: left={left}, right={right}");
                }
    }

    [Fact]
    public void Rime_keeper_spells_use_free_hand_casting_separate_from_staff_melee()
    {
        var art = UnitArt.Load(Pack.Root);
        foreach (var key in new[] { "characters.necromancer", "characters.lich" })
        {
            var sprite = art.Sprite(key)!;
            foreach (var binding in new[] { "characters.cast", "characters.point", "characters.summon" })
                Assert.Equal(sprite.Anim("cast").Frames.ToArray(), sprite.Anim(binding).Frames.ToArray());
            Assert.False(sprite.Anim("cast").Loop);
            Assert.False(sprite.Anim("cast").Frames.SequenceEqual(sprite.Anim("attack").Frames));
        }
    }

    [Fact]
    public void Wraith_life_drain_uses_gathering_poses_separate_from_its_physical_touch()
    {
        var sprite = UnitArt.Load(Pack.Root).Sprite("bosses.wraith.wraith")!;
        var special = sprite.Anim("bosses.special");
        Assert.Equal(sprite.Anim("cast").Frames.ToArray(), special.Frames.ToArray());
        Assert.False(special.Loop);
        Assert.False(special.Frames.SequenceEqual(sprite.Anim("attack").Frames));
        Assert.True(special.Frames.Distinct().Count() >= 3);
    }

    [Fact]
    public void Bone_warden_archer_uses_the_authored_bow_shot_for_ranged_attacks()
    {
        var sprite = UnitArt.Load(Pack.Root).Sprite("characters.skeleton_archer")!;
        var shoot = sprite.Anim("characters.shoot");
        Assert.Equal(sprite.Anim("attack").Frames.ToArray(), shoot.Frames.ToArray());
        Assert.Equal(sprite.Anim("attack").Fps, shoot.Fps);
        Assert.False(shoot.Loop);
        Assert.True(shoot.Frames.Distinct().Count() >= 4);
    }

    [Fact]
    public void Maren_keeps_the_staff_on_her_left_side_through_front_and_rear_motion()
    {
        var sprite = UnitArt.Load(Pack.Root).Sprite("characters.cult_leader")!;
        var (pixels, width, _) = PngPixels.Read(Path.Combine(Pack.Root, sprite.Path));
        foreach (var row in new[] { 0, 1, 3, 4, 5, 7 })
            foreach (var action in new[] { "idle", "walk", "cast" })
                foreach (var frame in sprite.Anim(action).Frames.Distinct())
                {
                    var source = sprite.Source(frame, row);
                    var ember = new List<int>();
                    // The caged coal is the bright amber landmark beside her head. Unlike an
                    // independently drawn weapon, it must stay with the same authored arm on turns.
                    for (var y = 0; y < Math.Min(source.Height, sprite.HeadTop + 35); y++)
                        for (var x = 0; x < source.Width; x++)
                        {
                            var at = ((source.Y + y) * width + source.X + x) * 4;
                            if (pixels[at + 3] >= 128 && pixels[at] > 230 && pixels[at + 1] > 140 && pixels[at + 2] < 115
                                && pixels[at] - pixels[at + 2] > 145 && pixels[at + 1] - pixels[at + 2] > 70)
                                ember.Add(x);
                        }
                    Assert.NotEmpty(ember);
                    var rear = row is 3 or 4 or 5;
                    Assert.True(rear ? ember.Average() < sprite.AnchorX : ember.Average() > sprite.AnchorX,
                        $"Staff changed anatomical side in {row}/{action}/{frame}");
                }
    }

    [Fact]
    public void Kindled_spells_use_casting_poses_separate_from_dagger_and_staff_melee()
    {
        var art = UnitArt.Load(Pack.Root);
        foreach (var key in new[] { "characters.cultist", "characters.cult_leader" })
        {
            var sprite = art.Sprite(key)!;
            Assert.Equal(sprite.Anim("cast").Frames.ToArray(), sprite.Anim("characters.point").Frames.ToArray());
            Assert.Equal(sprite.Anim("cast").Frames.ToArray(), sprite.Anim("characters.summon").Frames.ToArray());
            Assert.False(sprite.Anim("cast").Frames.SequenceEqual(sprite.Anim("attack").Frames));
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
        Assert.InRange(art.Sprite("characters.bandit")!.HeadLift / person, .9f, 1.2f);
        Assert.InRange(art.Sprite("characters.hobgoblin")!.HeadLift / person, 1f, 1.35f);
        Assert.InRange(art.Sprite("characters.cultist")!.HeadLift / person, .9f, 1.2f);
        Assert.InRange(art.Sprite("characters.cult_leader")!.HeadLift / person, .85f, 1.15f);
        Assert.InRange(art.Sprite("characters.skeleton")!.HeadLift / person, .9f, 1.2f);
        Assert.InRange(art.Sprite("characters.skeleton_archer")!.HeadLift / person, .9f, 1.2f);
        Assert.InRange(art.Sprite("characters.zombie")!.HeadLift / person, .8f, 1.15f);
        Assert.InRange(art.Sprite("bosses.wraith.wraith")!.HeadLift / person, .95f, 1.3f);
        Assert.InRange(art.Sprite("characters.necromancer")!.HeadLift / person, .9f, 1.2f);
        Assert.InRange(art.Sprite("characters.lich")!.HeadLift / person, .95f, 1.25f);
        Assert.InRange(art.Sprite("characters.black_knight")!.HeadLift / person, 1f, 1.4f);
        Assert.InRange(art.Sprite("characters.dark_elf")!.HeadLift / person, .9f, 1.2f);
        Assert.InRange(art.Sprite("bosses.troll.troll")!.HeadLift / person, 1.35f, 1.95f);
        Assert.InRange(art.Sprite("bosses.golem.granite")!.HeadLift / person, 1.35f, 1.85f);
        Assert.InRange(art.Sprite("bosses.mimic.oak")!.HeadLift / person, .4f, .8f);
        Assert.InRange(art.Sprite("beasts.drake.red")!.HeadLift / person, .5f, 1.1f);
    }
}
