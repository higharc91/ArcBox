namespace Redline.Presentation.Models;

/// <summary>
/// The naming contracts of the Blender surroundings (<c>tools/blender/surroundings.py</c> writes them into
/// <c>models/surroundings/</c>; the TV's <c>World/Surroundings</c> places them from
/// <see cref="Course.Surroundings.Plan"/>). Props drawn as MultiMeshes have one node (one mesh) per variant, in the
/// order of the plan's variant indices (<see cref="Course.PlazaModelsIndex"/>, <see cref="Course.TentModelsIndex"/>,
/// <see cref="Course.CarModelsIndex"/>, shrubs 0..2, groves 0..1, the far clump 0).
/// </summary>
public static class SurroundingsModels
{
    public const string Dir = "surroundings/";

    /// <summary>All the surroundings' exports together, counting each file once.</summary>
    public const int TotalBudget = 70_000;

    /// <summary>
    /// The amphitheatre: origin at the middle of the stage's front edge (on the highest ground under it), +Z towards
    /// the audience; footprint inside <see cref="Course.Surroundings.AmphiHalfWidth"/> and
    /// <see cref="Course.Surroundings.AmphiBack"/>..<see cref="Course.Surroundings.AmphiRim"/>, skirted down to
    /// <see cref="AmphiSkirt"/>.
    /// </summary>
    public static readonly ModelContract Amphitheatre = new(Dir + "amphitheatre.glb", 40_000, ["amphitheatre"],
        ["lawn", "concrete", "paving", "stage", "panel", "brand_white", "screen", "canopy", "steel", "steel_dark", "led_warm"]);

    public const float AmphiSkirt = -8;

    public static readonly IReadOnlyList<string> PlazaProps = ["light_pole", "planter", "kiosk", "flag_pole"];

    public static readonly ModelContract Plaza = new(Dir + "plaza_props.glb", 8_000, PlazaProps,
        ["concrete", "steel", "steel_dark", "led_white", "red", "brand_white", "kiosk", "panel", "shrub", "bark", "deck"]);

    public static readonly IReadOnlyList<string> Tents = ["marquee", "pagoda"];

    public static readonly ModelContract Tent = new(Dir + "tents.glb", 8_000, Tents, ["canvas", "glass", "steel", "red", "brand_white", "deck"]);

    public static readonly IReadOnlyList<string> Cars = ["sedan", "suv", "pickup", "hatch"];

    /// <summary>Parked cars: <c>car_paint</c> takes the per-instance colour.</summary>
    public static readonly ModelContract ParkedCars = new(Dir + "parked_cars.glb", 1_000, Cars, ["car_paint", "car_glass", "car_dark"]);

    public static readonly IReadOnlyList<string> Shrubs = ["shrub_round", "shrub_juniper", "shrub_low"];

    public static readonly ModelContract Shrub = new(Dir + "shrubs.glb", 400, Shrubs, ["shrub"]);

    public static readonly IReadOnlyList<string> Groves = ["grove_oak", "grove_cedar"];

    /// <summary>Tree clumps for treelines and creek lines: <c>grove</c> takes the per-instance green.</summary>
    public static readonly ModelContract Grove = new(Dir + "groves.glb", 700, Groves, ["grove", "bark"]);

    /// <summary>One low dome per distant tree clump, out to the horizon.</summary>
    public static readonly ModelContract FarClump = new(Dir + "far_clumps.glb", 60, ["far_clump"], ["grove"]);

    public static readonly ModelContract Skyline = new(Dir + "skyline.glb", 8_000, ["skyline"], ["skyline", "skyline_glass"]);

    public static IReadOnlyList<ModelContract> All { get; } = [Amphitheatre, Plaza, Tent, ParkedCars, Shrub, Grove, FarClump, Skyline];
}
