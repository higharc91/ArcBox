using System.Text.Json;
using Tactics.Logic;
using Tactics.Presentation.Audio;
using Tactics.Presentation.Tests.Board;

namespace Tactics.Presentation.Tests.Audio;

/// <summary><see cref="SoundBank"/> and <see cref="SoundChoice"/> against the real pack: every sound they name is there, and on disk.</summary>
public sealed class SoundBankTests
{
    private static readonly Lazy<SoundBank> Bank = new(() => SoundBank.Load(Pack.Root));
    private static readonly Lazy<RulesPack> Rules = new(() => RulesPack.Load([Path.Combine(Pack.Root, "rules")]));

    private static JsonDocument Rule(string file) => JsonDocument.Parse(File.ReadAllText(Path.Combine(Pack.Root, "rules", file)));

    public static TheoryData<string> Weapons()
    {
        var names = new TheoryData<string>();
        using var weapons = Rule("weapons.json");
        foreach (var weapon in weapons.RootElement.EnumerateArray())
            names.Add(weapon.GetProperty("id").GetString()!);
        return names;
    }

    [Fact]
    public void Every_sounds_files_are_on_disk()
    {
        Assert.NotEmpty(Bank.Value.Ids);
        foreach (var id in Bank.Value.Ids)
            Assert.All(Bank.Value.Sound(id)!.Files, f => Assert.True(File.Exists(Path.Combine(Pack.Root, f)), f));
    }

    [Theory]
    [InlineData("acid")]
    [InlineData("cold")]
    [InlineData("fire")]
    [InlineData("force")]
    [InlineData("lightning")]
    [InlineData("necrotic")]
    [InlineData("poison")]
    [InlineData("psychic")]
    [InlineData("radiant")]
    [InlineData("thunder")]
    public void A_magical_damage_type_has_its_own_impact(string type)
        => Assert.Equal($"dmg_{type}", SoundChoice.Impact(type, false, false));

    [Theory]
    [InlineData("slashing", false, false, "blade_flesh")]
    [InlineData("slashing", true, false, "blade_armor")]
    [InlineData("bludgeoning", false, false, "blunt_flesh")]
    [InlineData("bludgeoning", true, false, "blunt_armor")]
    [InlineData("piercing", false, false, "pierce_flesh")]
    [InlineData("piercing", true, false, "pierce_armor")]
    [InlineData("piercing", false, true, "arrow_flesh")]
    [InlineData("piercing", true, true, "arrow_armor")]
    public void A_weapons_impact_goes_by_its_damage_what_it_hit_and_how_it_got_there(string type, bool armoured, bool shot, string sound)
    {
        Assert.Equal(sound, SoundChoice.Impact(type, armoured, shot));
        Assert.True(Bank.Value.Has(sound));
    }

    [Fact]
    public void Every_damage_type_in_the_rules_has_a_sound()
    {
        var text = string.Concat(Directory.GetFiles(Path.Combine(Pack.Root, "rules", "abilities")).Select(File.ReadAllText));
        var types = System.Text.RegularExpressions.Regex.Matches(text, "\"type\":\\s*\"([a-z]+)\"").Select(m => m.Groups[1].Value)
            .Where(t => t.Length > 3 && (t is "acid" or "cold" or "fire" or "force" or "lightning" or "necrotic" or "poison" or "psychic" or "radiant" or "thunder"
                or "slashing" or "piercing" or "bludgeoning")).Distinct().ToArray();

        Assert.NotEmpty(types);
        Assert.All(types, t => Assert.True(Bank.Value.Has(SoundChoice.Impact(t, false, false)!), t));
    }

    [Fact]
    public void Damage_with_no_type_has_no_impact() => Assert.Null(SoundChoice.Impact(null, false, false));

    [Theory]
    [MemberData(nameof(Weapons))]
    public void A_rules_weapon_has_a_sound_that_exists(string weapon)
    {
        var sound = SoundChoice.Swing(weapon) ?? SoundChoice.Release(weapon);

        Assert.NotNull(sound);
        Assert.True(Bank.Value.Has(sound), sound);
    }

    [Theory]
    [InlineData("sword", "swing_sword")]
    [InlineData("rapier", "swing_sword")]
    [InlineData("greatsword", "swing_heavy")]
    [InlineData("axe", "swing_axe")]
    [InlineData("mace", "swing_blunt")]
    [InlineData("staff", "swing_blunt")]
    [InlineData("dagger", "swing_dagger")]
    [InlineData("spear", "thrust_spear")]
    [InlineData("whip", "whip_crack")]
    public void A_presets_weapon_has_its_swing(string weapon, string sound) => Assert.Equal(sound, SoundChoice.Swing(weapon));

    [Theory]
    [InlineData("bite")]
    [InlineData("claws")]
    [InlineData("slam")]
    [InlineData(null)]
    public void A_natural_weapon_has_no_swing_as_the_creatures_bark_is_the_sound(string? weapon) => Assert.Null(SoundChoice.Swing(weapon));

    [Theory]
    [InlineData("shortbow", "bow_release")]
    [InlineData("bow", "bow_release")]
    [InlineData("light_crossbow", "crossbow_release")]
    [InlineData("sling", "sling_release")]
    [InlineData("javelin", "throw")]
    [InlineData("dagger", "throw")]
    [InlineData(null, "bow_release")]
    public void A_shot_goes_by_the_weapon(string? weapon, string sound) => Assert.Equal(sound, SoundChoice.Shot(weapon));

    [Theory]
    [InlineData("grass/lush", "step_grass")]
    [InlineData("dirt/dark", "step_dirt")]
    [InlineData("water/clear", "step_water")]
    [InlineData("rock/grey", "step_stone")]
    [InlineData("mine_boards/oak", "step_wood")]
    [InlineData("lava/molten", "step_lava")]
    [InlineData("flagstone/dark", "step_stone")]
    public void A_material_has_its_footstep(string material, string step) => Assert.Equal(step, SoundChoice.Step(material));

    [Fact]
    public void Every_material_on_every_map_has_a_footstep_that_exists()
    {
        foreach (var map in Pack.Maps)
            foreach (var material in Pack.Map(map).Materials)
                Assert.True(Bank.Value.Has(SoundChoice.Step(material)), material);
    }

    [Theory]
    [InlineData("river_crossing", 0, 0)]
    [InlineData("warlord_bridge", 5, 5)]
    public void A_tile_on_a_real_map_has_a_footstep(string map, int x, int y)
        => Assert.StartsWith("step_", Bank.Value.Step(map, new Tile(x, y)));

    [Fact]
    public void A_map_that_isnt_in_the_pack_still_has_a_footstep()
        => Assert.Equal(SoundChoice.DefaultStep, Bank.Value.Step("no_such_map", new Tile(0, 0)));

    [Theory]
    [InlineData("fx.heal.holy", "spell_holy_heal")]
    [InlineData("fx.restore.nature", "spell_nature_heal")]
    [InlineData("fx.bless.gold", "spell_bless")]     // fx.bless.* in the manifest
    [InlineData("fx.dust.earth", "dust_burst")]
    [InlineData("fx.dust", "dust_burst")]
    [InlineData("fx.fireball.fire", "spell_fireball")]
    [InlineData("fx.misty.fey", "spell_misty_step")]
    [InlineData("fx.lightning.storm", "spell_lightning")]
    public void An_effect_finds_the_sound_the_manifest_names_for_it(string key, string sound)
        => Assert.Equal(sound, Bank.Value.ForEffect(key));

    [Fact]
    public void An_effect_no_sound_names_has_none() => Assert.Null(Bank.Value.ForEffect("fx.no_such.thing"));

    [Fact]
    public void Most_effects_the_rules_use_have_a_sound()
    {
        var keys = Rules.Value.Abilities.Values.Select(a => a.Presentation?.Fx).OfType<string>().Where(k => k.StartsWith("fx.", StringComparison.Ordinal)).Distinct().ToArray();
        var found = keys.Count(k => Bank.Value.ForEffect(k) is not null);

        Assert.True(found * 2 > keys.Length, $"{found} of {keys.Length}");
    }

    [Theory]
    [InlineData("characters.goblin_cutthroat", "goblin")]
    [InlineData("characters.goblin_archer", "goblin")]
    [InlineData("beasts.wolf.wildshape", "wolf")]
    [InlineData("bosses.troll.troll", "troll")]
    [InlineData("characters.skeleton", "undead")]
    [InlineData("bosses.fire_elemental.ember", "elemental")]
    public void A_monsters_sprite_finds_its_creature_sounds(string sprite, string family)
    {
        var gear = Bank.Value.Gear(sprite);

        Assert.Equal(family, gear.Family);
        Assert.All(new[] { "attack", "hurt", "death" }, s => Assert.True(Bank.Value.Has($"{family}_{s}")));
    }

    [Fact]
    public void A_hero_has_its_presets_weapon_and_armour_and_no_creature_sounds()
    {
        var fighter = Bank.Value.Gear("characters.class_fighter");
        var wizard = Bank.Value.Gear("characters.class_wizard");

        Assert.Null(fighter.Family);
        Assert.NotNull(fighter.Weapon);
        Assert.True(fighter.Armoured);
        Assert.False(wizard.Armoured);
    }

    [Fact]
    public void A_monster_with_a_rules_weapon_uses_it()
        => Assert.Equal("shortbow", Bank.Value.Gear("characters.goblin_archer").Weapon);

    [Fact]
    public void A_beast_is_not_armoured_and_a_black_knight_is()
    {
        Assert.False(Bank.Value.Gear("beasts.wolf.wildshape").Armoured);
        Assert.True(Bank.Value.Gear("characters.black_knight").Armoured);
    }

    [Fact]
    public void Big_things_tread_heavily_and_winged_things_flap()
    {
        Assert.True(Bank.Value.Gear("bosses.troll.troll").Heavy);
        Assert.False(Bank.Value.Gear("characters.goblin_cutthroat").Heavy);
        Assert.True(Bank.Value.Gear("beasts.bat.dusk").Flies);
        Assert.False(Bank.Value.Gear("bosses.troll.troll").Flies);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("characters.nobody_at_all")]
    public void A_sprite_nothing_knows_has_no_gear(string? sprite) => Assert.Equal(new UnitGear(null, false, null, false, false), Bank.Value.Gear(sprite));

    [Theory]
    [InlineData("hasted", "buff_applied")]
    [InlineData("poisoned", "debuff_applied")]
    [InlineData("frightened", "debuff_applied")]
    [InlineData("dodging", null)]
    [InlineData("hidden", null)]
    public void A_condition_sounds_as_good_or_bad_news_or_not_at_all(string condition, string? sound)
        => Assert.Equal(sound, SoundChoice.Condition(condition));

    [Fact]
    public void Every_condition_named_for_a_sound_is_a_real_one()
    {
        var real = Rules.Value.Conditions.Keys.Select(k => k.Value).ToHashSet();

        Assert.All(SoundChoice.Conditions, c => Assert.Contains(c, real));
    }
}
