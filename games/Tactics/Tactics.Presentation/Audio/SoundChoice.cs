using System.Collections.Immutable;

namespace Tactics.Presentation.Audio;

/// <summary>
/// Which sound goes with what's happening, by its id in <c>sfx/manifest.json</c>. Events don't name a weapon, so the weapon
/// comes from who's swinging it (<see cref="UnitGear"/>). Pure: a test holds every id here against the pack.
/// </summary>
public static class SoundChoice
{
    /// <summary>The footstep for ground nothing here names.</summary>
    public const string DefaultStep = "step_dirt";

    /// <summary>The same sound closer together than this is one sound: the targets of an area land a few hundredths apart.</summary>
    public const double SameSoundGap = 0.15;

    private static readonly ImmutableHashSet<string> Physical = ["slashing", "piercing", "bludgeoning"];

    private static readonly ImmutableDictionary<string, string> Swings = new Dictionary<string, string>
    {
        ["sword"] = "swing_sword", ["longsword"] = "swing_sword", ["shortsword"] = "swing_sword", ["scimitar"] = "swing_sword", ["rapier"] = "swing_sword", ["sickle"] = "swing_sword",
        ["greatsword"] = "swing_heavy", ["greataxe"] = "swing_heavy", ["maul"] = "swing_heavy", ["halberd"] = "swing_heavy", ["glaive"] = "swing_heavy", ["pike"] = "swing_heavy",
        ["axe"] = "swing_axe", ["handaxe"] = "swing_axe", ["battleaxe"] = "swing_axe",
        ["club"] = "swing_blunt", ["greatclub"] = "swing_blunt", ["mace"] = "swing_blunt", ["flail"] = "swing_blunt", ["morningstar"] = "swing_blunt", ["warhammer"] = "swing_blunt",
        ["light_hammer"] = "swing_blunt", ["quarterstaff"] = "swing_blunt", ["staff"] = "swing_blunt", ["holy_staff"] = "swing_blunt", ["lich_staff"] = "swing_blunt", ["flame_staff"] = "swing_blunt",
        ["war_pick"] = "swing_blunt", ["smith_hammer"] = "swing_blunt", ["tankard"] = "swing_blunt", ["broom"] = "swing_blunt",
        ["dagger"] = "swing_dagger", ["ritual_dagger"] = "swing_dagger",
        ["spear"] = "thrust_spear", ["lance"] = "thrust_spear", ["trident"] = "thrust_spear", ["pitchfork"] = "thrust_spear",
        ["whip"] = "whip_crack",
    }.ToImmutableDictionary();

    private static readonly ImmutableHashSet<string> Thrown = ["dagger", "handaxe", "javelin", "dart", "light_hammer", "spear", "trident"];

    private static readonly (string Prefix, string Step)[] Steps =
    [
        ("grass", "step_grass"), ("dirt", "step_dirt"), ("path", "step_dirt"), ("cave_floor", "step_dirt"), ("mine_gravel", "step_dirt"),
        ("water", "step_water"), ("mud", "step_mud"), ("bog", "step_mud"), ("sand", "step_sand"), ("dunes", "step_sand"), ("snow", "step_snow"), ("ice", "step_ice"),
        ("lava", "step_lava"), ("mine_boards", "step_wood"), ("planks", "step_wood"),
        ("cobble", "step_stone"), ("flagstone", "step_stone"), ("rock", "step_stone"), ("dungeon", "step_stone"), ("crypt", "step_stone"), ("wet_stone", "step_stone"), ("basalt", "step_stone"),
    ];

    private static readonly string[] Mundane = ["fx.dust", "fx.smoke", "fx.poof"];

    private static readonly ImmutableHashSet<string> Boons = ["hasted", "inspired", "raging", "barkskin", "mage_armor", "death_ward", "elemental_weapon", "aura_of_protection", "divine_smite", "invisible"];

    private static readonly ImmutableHashSet<string> Banes =
        ["poisoned", "frightened", "slowed", "stunned", "paralysed", "blinded", "charmed", "restrained", "burning", "bleeding", "grappled", "prone", "hex", "ensnared", "unconscious"];

    /// <summary>The conditions that have a sound. The rest are bookkeeping, like dodging or hiding, with nothing to announce.</summary>
    public static IEnumerable<string> Conditions => Boons.Concat(Banes);

    /// <summary>The sound of a blow landing: the kind of hit for a weapon, on armour or flesh; the damage's own sound for magic; nothing for damage with no type.</summary>
    /// <param name="type">The damage's type.</param>
    /// <param name="armoured">What it hit wears armour.</param>
    /// <param name="shot">It arrived through the air.</param>
    public static string? Impact(string? type, bool armoured, bool shot)
    {
        if (type is null)
            return null;
        if (!Physical.Contains(type))
            return $"dmg_{type}";
        var kind = shot ? "arrow" : type switch { "slashing" => "blade", "bludgeoning" => "blunt", _ => "pierce" };
        return $"{kind}_{(armoured ? "armor" : "flesh")}";
    }

    /// <summary>The sound of swinging <paramref name="weapon"/> (a rules id or a preset's layer), or null for claws and the like, whose sound is the creature's bark.</summary>
    public static string? Swing(string? weapon) => weapon is not null && Swings.TryGetValue(weapon, out var sound) ? sound : null;

    /// <summary>The sound of a weapon that's only fired, or null for one that's swung.</summary>
    public static string? Release(string? weapon) => weapon switch
    {
        null => null,
        _ when weapon.Contains("crossbow", StringComparison.Ordinal) => "crossbow_release",
        _ when weapon.EndsWith("bow", StringComparison.Ordinal) || weapon == "blowgun" => "bow_release",
        "sling" => "sling_release",
        "javelin" or "dart" => "throw",
        _ => null,
    };

    /// <summary>The sound of something leaving <paramref name="weapon"/> for a target at range. With nothing better to go on, a bow: the TV draws an arrow.</summary>
    public static string Shot(string? weapon)
        => Release(weapon) ?? (weapon is not null && Thrown.Contains(weapon) ? "throw" : "bow_release");

    /// <summary>True for an effect that's magic gathering, not dust from a dash or a puff of smoke.</summary>
    public static bool IsSpell(string? effect)
        => effect is not null && !Mundane.Any(m => effect.StartsWith(m, StringComparison.Ordinal));

    /// <summary>The footstep on <paramref name="material"/>, as a map names it.</summary>
    public static string Step(string material)
    {
        foreach (var (prefix, step) in Steps)
            if (material.StartsWith(prefix, StringComparison.Ordinal))
                return step;
        return DefaultStep;
    }

    /// <summary>The sound of gaining a condition, or null if it isn't worth one.</summary>
    public static string? Condition(string id)
    {
        if (Boons.Contains(id) || id.StartsWith("divine_smite", StringComparison.Ordinal))
            return "buff_applied";
        return Banes.Contains(id) ? "debuff_applied" : null;
    }
}
