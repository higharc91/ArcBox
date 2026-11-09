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
    [InlineData("bosses.hound.tan", "hound")]
    [InlineData("bosses.imp.crimson", "imp")]
    [InlineData("bosses.sapling.spring", "sapling")]
    [InlineData("bosses.air_elemental.gale", "air_elemental")]
    [InlineData("bosses.earth_elemental.stone", "earth_elemental")]
    [InlineData("bosses.fire_elemental.ember", "fire_elemental")]
    [InlineData("bosses.water_elemental.sea", "water_elemental")]
    [InlineData("bosses.dragon.red", "dragon")]
    [InlineData("bosses.air_elemental.storm", "air_storm")]
    [InlineData("bosses.earth_elemental.magma", "earth_magma")]
    [InlineData("bosses.water_elemental.swamp", "water_swamp")]
    [InlineData("bosses.golem.basalt", "golem_basalt")]
    [InlineData("bosses.golem.crystal", "golem_crystal")]
    [InlineData("bosses.golem.temple", "golem_temple")]
    [InlineData("bosses.fire_elemental.azure", "fire_azure")]
    [InlineData("bosses.fire_elemental.radiant", "fire_radiant")]
    [InlineData("bosses.fire_elemental.shadow", "fire_shadow")]
    [InlineData("bosses.hound.black", "hound_black")]
    [InlineData("bosses.hound.grey", "hound_grey")]
    [InlineData("bosses.imp.violet", "imp_violet")]
    [InlineData("beasts.bat.vampire", "bat_vampire")]
    [InlineData("beasts.bear.undead", "bear_undead")]
    [InlineData("beasts.slime.crystal", "slime_crystal")]
    [InlineData("beasts.spider.phase", "spider_phase")]
    [InlineData("beasts.wolf.alpha", "wolf_alpha")]
    [InlineData("bestiary.crab.deep", "crab_deep")]
    [InlineData("bestiary.scorpion.black", "scorpion_black")]
    [InlineData("bestiary.warg.rider", "warg_rider")]
    [InlineData("bosses.mimic.ironbound", "mimic_ironbound")]
    [InlineData("bosses.dragon.black", "dragon_black")]
    [InlineData("bosses.dragon.blue", "dragon_blue")]
    [InlineData("bosses.dragon.green", "dragon_green")]
    [InlineData("bosses.dragon.gold", "dragon_gold")]
    [InlineData("bosses.troll.frost", "troll_frost")]
    [InlineData("bosses.troll.ogre", "troll_ogre")]
    [InlineData("bosses.sapling.autumn", "sapling_autumn")]
    [InlineData("bosses.sapling.blossom", "sapling_blossom")]
    [InlineData("bosses.wraith.shade", "wraith_shade")]
    [InlineData("bosses.wraith.spectre", "wraith_spectre")]
    public void Approved_mobs_have_real_directional_motion_and_complete_baked_figures(string key, string name)
    {
        var sprite = UnitArt.Load(Pack.Root).Sprite(key)!;
        Assert.Equal($"creatures/mob_refresh_{name}.png", sprite.Path);
        Assert.Equal(8, sprite.Directions.Length);
        var (pixels, width, height) = PngPixels.Read(Path.Combine(Pack.Root, sprite.Path));
        Assert.InRange(width, 1, 16384);
        Assert.InRange(height, 1, 16384);
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
    [InlineData("bosses.sapling.autumn", true)]
    [InlineData("bosses.sapling.blossom", true)]
    [InlineData("bosses.wraith.shade", false)]
    [InlineData("bosses.wraith.spectre", false)]
    public void Seasonal_plants_keep_root_wood_and_shades_never_turn_into_orange_foliage(string key, bool plant)
    {
        var sprite = UnitArt.Load(Pack.Root).Sprite(key)!;
        var (pixels, width, _) = PngPixels.Read(Path.Combine(Pack.Root, sprite.Path));
        for (var row = 0; row < 8; row++)
            foreach (var action in sprite.Anims.Values)
                foreach (var frame in action.Frames.Distinct())
                {
                    var box = sprite.Source(frame, row);
                    var visible = 0; var wood = 0; var orange = 0;
                    for (var y = box.Y; y < box.Y + box.Height; y++)
                        for (var x = box.X; x < box.X + box.Width; x++)
                        {
                            var at = (y * width + x) * 4;
                            if (pixels[at + 3] < 128) continue;
                            visible++;
                            var r = (int)pixels[at]; var g = (int)pixels[at + 1]; var b = (int)pixels[at + 2];
                            if (r > g + 12 && g > b + 8) wood++;
                            if (r > g + 35 && g > b + 20) orange++;
                        }
                    Assert.True(plant ? wood > 30 : orange < visible * .02,
                        $"Mixed creature material in {key}/{row}/{frame}: wood={wood}, orange={orange}, visible={visible}");
                }
    }

    [Theory]
    [InlineData("bosses.wraith.spectre")]
    public void Spectres_have_a_low_complete_collapsed_robe_silhouette_in_every_facing(string key)
    {
        var sprite = UnitArt.Load(Pack.Root).Sprite(key)!;
        var (pixels, width, _) = PngPixels.Read(Path.Combine(Pack.Root, sprite.Path));
        int Height(int frame, int row)
        {
            var rect = sprite.Source(frame, row); var top = rect.Height; var bottom = -1;
            for (var y = 0; y < rect.Height; y++)
                for (var x = 0; x < rect.Width; x++)
                    if (pixels[((rect.Y + y) * width + rect.X + x) * 4 + 3] >= 128)
                    { top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
            Assert.True(bottom >= top);
            return bottom - top + 1;
        }
        for (var row = 0; row < 8; row++)
            Assert.True(Height(sprite.Anim("down").Frames[0], row) < Height(sprite.Idle.Frames[0], row) * .65,
                $"Upright down pose: {key}/{row}");
    }

    [Theory]
    [InlineData("bosses.troll.troll", "troll.boulder_toss", "troll.frenzy_roar")]
    [InlineData("bosses.golem.granite", "golem.seismic_slam", "golem.stone_aegis")]
    [InlineData("bosses.golem.basalt", "golem.seismic_slam", "golem.stone_aegis")]
    [InlineData("bosses.golem.crystal", "golem.seismic_slam", "golem.stone_aegis")]
    [InlineData("bosses.golem.temple", "golem.seismic_slam", "golem.stone_aegis")]
    [InlineData("bosses.troll.frost", "troll.boulder_toss", "troll.frenzy_roar")]
    [InlineData("bosses.troll.ogre", "troll.boulder_toss", "troll.frenzy_roar")]
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

    [Theory]
    [InlineData("beasts.drake.red")]
    [InlineData("bosses.dragon.black")]
    [InlineData("bosses.dragon.blue")]
    [InlineData("bosses.dragon.green")]
    [InlineData("bosses.dragon.gold")]
    public void Winged_creatures_spread_their_wings_in_every_attack_facing(string key)
    {
        var sprite = UnitArt.Load(Pack.Root).Sprite(key)!;
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
            Assert.True(attack.Width > idle.Width * (key == "beasts.drake.red" ? 1.25 : 1.2), $"Wings did not widen facing {row}");
            if (key == "beasts.drake.red") Assert.True(attack.Top < idle.Top - 4, $"Wings did not rise facing {row}");
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
        Assert.InRange(art.Sprite("bosses.golem.basalt")!.HeadLift / person, 1.35f, 1.85f);
        Assert.InRange(art.Sprite("bosses.golem.crystal")!.HeadLift / person, 1.35f, 1.85f);
        Assert.InRange(art.Sprite("bosses.golem.temple")!.HeadLift / person, 1.35f, 1.85f);

        Assert.InRange(art.Sprite("bosses.air_elemental.gale")!.HeadLift / person, 1.1f, 1.6f);
        Assert.InRange(art.Sprite("bosses.earth_elemental.stone")!.HeadLift / person, 1.35f, 1.9f);
        Assert.InRange(art.Sprite("bosses.fire_elemental.ember")!.HeadLift / person, 1.2f, 1.7f);
        Assert.InRange(art.Sprite("bosses.fire_elemental.azure")!.HeadLift / person, 1.1f, 1.7f);
        Assert.InRange(art.Sprite("bosses.fire_elemental.radiant")!.HeadLift / person, 1.1f, 1.7f);
        Assert.InRange(art.Sprite("bosses.fire_elemental.shadow")!.HeadLift / person, 1.1f, 1.7f);

        Assert.InRange(art.Sprite("bosses.water_elemental.sea")!.HeadLift / person, 1.2f, 1.7f);
        Assert.InRange(art.Sprite("bosses.air_elemental.storm")!.HeadLift / person, 1.1f, 1.6f);
        Assert.InRange(art.Sprite("bosses.earth_elemental.magma")!.HeadLift / person, 1.35f, 1.9f);
        Assert.InRange(art.Sprite("bosses.water_elemental.swamp")!.HeadLift / person, 1.2f, 1.7f);
        Assert.InRange(art.Sprite("bosses.dragon.red")!.HeadLift / person, 1.7f, 2.3f);
        Assert.InRange(art.Sprite("bosses.dragon.black")!.HeadLift / person, 1.7f, 2.3f);
        Assert.InRange(art.Sprite("bosses.dragon.blue")!.HeadLift / person, 1.7f, 2.3f);
        Assert.InRange(art.Sprite("bosses.dragon.green")!.HeadLift / person, 1.7f, 2.3f);
        Assert.InRange(art.Sprite("bosses.dragon.gold")!.HeadLift / person, 1.7f, 2.3f);
        Assert.InRange(art.Sprite("bosses.troll.frost")!.HeadLift / person, 1.35f, 1.95f);
        Assert.InRange(art.Sprite("bosses.troll.ogre")!.HeadLift / person, 1.35f, 1.95f);
        Assert.InRange(art.Sprite("bosses.sapling.autumn")!.HeadLift / person, .45f, .8f);
        Assert.InRange(art.Sprite("bosses.sapling.blossom")!.HeadLift / person, .45f, .8f);
        Assert.InRange(art.Sprite("bosses.wraith.spectre")!.HeadLift / person, .95f, 1.3f);
        Assert.InRange(art.Sprite("bosses.wraith.shade")!.HeadLift / person, .95f, 1.3f);
        Assert.InRange(art.Sprite("bosses.sapling.spring")!.HeadLift / person, .45f, .8f);
        Assert.InRange(art.Sprite("bosses.imp.crimson")!.HeadLift / person, .4f, .75f);
        Assert.InRange(art.Sprite("bosses.hound.tan")!.HeadLift / person, .35f, .65f);
        Assert.InRange(art.Sprite("bosses.hound.black")!.HeadLift / person, .35f, .65f);
        Assert.InRange(art.Sprite("bosses.hound.grey")!.HeadLift / person, .35f, .65f);
        Assert.InRange(art.Sprite("bosses.imp.violet")!.HeadLift / person, .4f, .75f);
        // Neutral wing clearance can rise above the small torso in three-quarter views.
        Assert.InRange(art.Sprite("beasts.bat.vampire")!.HeadLift / person, .6f, 1.1f);
        Assert.InRange(art.Sprite("beasts.bear.undead")!.HeadLift / person, .9f, 1.35f);
        Assert.InRange(art.Sprite("beasts.slime.crystal")!.HeadLift / person, .4f, .8f);
        Assert.InRange(art.Sprite("beasts.spider.phase")!.HeadLift / person, .75f, 1.25f);
        Assert.InRange(art.Sprite("beasts.wolf.alpha")!.HeadLift / person, .75f, 1.15f);
        // Rear carapace depth contributes to the clearance envelope above this low animal.
        Assert.InRange(art.Sprite("bestiary.crab.deep")!.HeadLift / person, .6f, 1.25f);
        Assert.InRange(art.Sprite("bestiary.scorpion.black")!.HeadLift / person, .8f, 1.5f);
        Assert.InRange(art.Sprite("bestiary.warg.rider")!.HeadLift / person, 1.0f, 1.9f);
        Assert.InRange(art.Sprite("bosses.mimic.ironbound")!.HeadLift / person, .4f, .9f);
        Assert.InRange(art.Sprite("bosses.mimic.oak")!.HeadLift / person, .4f, .8f);
        Assert.InRange(art.Sprite("beasts.drake.red")!.HeadLift / person, .5f, 1.1f);
    }
}
