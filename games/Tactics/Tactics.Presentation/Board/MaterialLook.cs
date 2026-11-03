using System.Collections.Immutable;

namespace Tactics.Presentation.Board;

/// <summary>
/// How a map material (<c>"grass/lush"</c>) is drawn. A map names only its tops, so this table chooses what goes with
/// each: the side under it, the lip over that side, and the wall where it drops into a chasm. The material names don't
/// all match the art's (<c>crypt_tile</c> is <c>crypt_tiles</c>, and <c>dungeon_wall</c> is only a side, so a wall's top
/// is floor).
/// </summary>
/// <param name="Variant">The material's variant, which props on it try to match.</param>
/// <param name="Top">The tile's top.</param>
/// <param name="Side">Its sides.</param>
/// <param name="Chasm">Its side where it drops into a void.</param>
/// <param name="Lip">What hangs over the top of its sides, or null.</param>
/// <param name="Void">Nothing to stand on: drawn sunk, with chasm walls around it.</param>
/// <param name="Water">Water, which lies a little below its banks when deep.</param>
/// <param name="Deck">Planks: a thin deck, not a solid column, where they span a void.</param>
public sealed record MaterialLook(string Variant, ArtRef Top, ArtRef Side, ArtRef Chasm, ArtRef? Lip = null,
    bool Void = false, bool Water = false, bool Deck = false)
{
    private static readonly ArtRef EarthChasm = new("chasm_side", "brown");
    private static readonly ArtRef CaveChasm = new("chasm_side", "cool");

    private static readonly ImmutableDictionary<string, string> FordMaterials = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["ash/grey"] = "ash_ford/charred",
        ["dirt/brown"] = "dirt_ford/dry",
        ["dirt/dark"] = "dirt_ford/wet",
        ["grass/autumn"] = "grass_ford/autumn",
        ["grass/dry"] = "grass_ford/dry",
        ["grass/frost"] = "grass_ford/dry",
        ["path/packed"] = "path_ford/packed",
        ["planks/oak"] = "planks_ford/oak",
        ["planks/dark"] = "planks_ford/dark",
        ["cave_floor/cool"] = "rock_ford/bank",
        ["mine_gravel/brown"] = "rock_ford/bank",
        ["rock/grey"] = "rock_ford/bank",
        ["rock/dark"] = "rock_ford/bank",
        ["water/clear"] = "ford_water/clear",
    }.ToImmutableDictionary(StringComparer.Ordinal);

    /// <summary>The local surface art for a region, without changing its material or any game rules.</summary>
    public static string ArtMaterial(string regionId, string material)
        => regionId == "ashen_ford" ? FordMaterials.GetValueOrDefault(material, material) : material;

    /// <summary>The look of <paramref name="material"/>; an unknown family draws its top (if the pack has it) on grey rock.</summary>
    public static MaterialLook Of(string material)
    {
        var slash = material.IndexOf('/');
        var (family, variant) = slash < 0 ? (material, "") : (material[..slash], material[(slash + 1)..]);
        var top = new ArtRef(family, variant);
        MaterialLook With(ArtRef side, ArtRef chasm) => new(variant, top, side, chasm);
        var cliff = new ArtRef("cliff_side", variant);
        var ashlar = new ArtRef("ashlar_side", "grey");
        var dungeon = new ArtRef("dungeon_wall", variant);
        var cave = new ArtRef("cave_wall", "cool");
        return family switch
        {
            "grass" => With(new ArtRef("dirt_side", "brown"), EarthChasm) with { Lip = variant == "frost" ? new ArtRef("snow_lip", "fresh") : new ArtRef("grass_lip", variant) },
            "grass_ford" => With(new ArtRef("dirt_side", "brown"), EarthChasm) with { Lip = new ArtRef("grass_lip", "autumn") },
            "dirt" => With(new ArtRef("dirt_side", variant), EarthChasm),
            "dirt_ford" => With(new ArtRef("dirt_side", "brown"), EarthChasm),
            "ash" or "mud" or "forest_needles" or "forest_floor" => With(new ArtRef("dirt_side", "dark"), EarthChasm),
            "ash_ford" => With(new ArtRef("dirt_side", "dark"), EarthChasm),
            "path" => With(new ArtRef("dirt_side", "brown"), EarthChasm),
            "path_ford" => With(new ArtRef("dirt_side", "brown"), EarthChasm),
            "planks" => With(new ArtRef("plank_side", "oak"), EarthChasm),
            "planks_ford" => With(new ArtRef("plank_side", "oak"), EarthChasm),
            "rock" => With(cliff, EarthChasm),
            "rock_ford" => With(new ArtRef("cliff_ford", "grey"), new ArtRef("cliff_ford", "grey")),
            "lava" => With(new ArtRef("lava_side", variant), EarthChasm),
            "basalt" => With(new ArtRef("basalt_side", variant), EarthChasm),
            "water" => With(new ArtRef("dirt_side", "brown"), EarthChasm) with { Water = true },
            "ford_water" => With(new ArtRef("dirt_side", "brown"), EarthChasm) with { Water = true },
            "flagstone" => With(ashlar, EarthChasm) with { Lip = variant == "mossy" ? new ArtRef("moss_lip", "green") : null },
            "cobble" => With(ashlar, EarthChasm),
            "dungeon_floor" or "dungeon_floor_mossy" or "dungeon_floor_cracked" => With(dungeon, EarthChasm),
            "crypt_tiles" => With(new ArtRef("dungeon_wall", "dark"), EarthChasm),    // its variants are glows, which the walls don't come in
            "crypt_tile" => With(dungeon, EarthChasm) with { Top = new ArtRef("crypt_tiles", variant) },
            "dungeon_wall" => With(dungeon, EarthChasm) with { Top = new ArtRef("dungeon_floor", variant) },
            "cave_floor" => With(new ArtRef("cave_wall", variant), CaveChasm),
            "mine_gravel" => With(cave, CaveChasm),
            "wet_stone" => With(cave, CaveChasm),
            "mine_boards" => With(new ArtRef("plank_side", "oak"), EarthChasm) with { Deck = true },
            "void" => With(new ArtRef("chasm_side", "brown"), EarthChasm) with { Void = true },
            _ => With(new ArtRef("cliff_side", "grey"), EarthChasm),
        };
    }
}

/// <summary>Which hazard's art draws a zone of each kind (the zones in the rules' abilities).</summary>
public static class ZoneArt
{
    private static readonly ImmutableDictionary<string, string> Hazards = new Dictionary<string, string>
    {
        ["fire"] = "burning",
        ["burning"] = "burning",
        ["ice_sheet"] = "ice",
        ["oil"] = "oil",
        ["ember_rune"] = "burning",
        ["poison"] = "poison",
        ["ice"] = "ice",
        ["frost_rune"] = "ice",
        ["web"] = "web",
        ["thorns"] = "thorns",
        ["entangle"] = "thorns",
        ["vines"] = "thorns",
        ["spikes"] = "spikes",
        ["caltrops"] = "caltrops",
        ["rubble"] = "caltrops",
        ["snap_trap"] = "caltrops",
        ["grease"] = "oil",
        ["holy"] = "consecrated",
        ["moonbeam"] = "consecrated",
        ["darkness"] = "darkness",
        ["smoke"] = "darkness",
        ["smoke_cloud"] = "darkness",
        ["void"] = "darkness",
    }.ToImmutableDictionary(StringComparer.Ordinal);

    /// <summary>The hazard id for a zone kind, or null for one with no art of its own, which shows as a tint.</summary>
    public static string? HazardFor(string kind) => Hazards.GetValueOrDefault(kind);
}
