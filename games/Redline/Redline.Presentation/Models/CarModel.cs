namespace Redline.Presentation.Models;

/// <summary>
/// The naming contract of the Blender car (<c>tools/blender/car.py</c> writes it, the TV's <c>CarFactory</c> reads it).
/// Same frame as <see cref="Meshes.CarMeshBuilder"/>: metres, +Y up, +Z forward, +X to the car's left, origin on the
/// ground midway between the axles.
/// <list type="bullet">
/// <item><see cref="Body"/>: every static part, one mesh whose surfaces use the material slots below.</item>
/// <item><see cref="Wheels"/>: tyre + rim of each wheel, origin at the wheel centre (spins about its X axis), in the
/// order of <see cref="Meshes.CarMeshes.WheelCentres"/>: front-left, front-right, rear-left, rear-right (left = +X).</item>
/// <item><see cref="Numbers"/>: empties where the number is drawn (nose, left and right rear endplates): local +Z is the
/// outward normal, local +Y the text's up.</item>
/// <item><see cref="Helmet"/>: an empty at the driver's helmet centre.</item>
/// </list>
/// Material slots are bound by name in Godot: <see cref="PaintSlots"/> take the team's livery colours from grid2026.json
/// (matte or gloss per team), the rest are shared.
/// </summary>
public static class CarModel
{
    public const string Lod0File = "car_2026.glb";
    public const string Lod1File = "car_2026_lod1.glb";
    public const int Lod0Budget = 120_000;
    public const int Lod1Budget = 30_000;

    public const string Body = "body";
    public const string Helmet = "helmet";
    public static readonly IReadOnlyList<string> Wheels = ["wheel_fl", "wheel_fr", "wheel_rl", "wheel_rr"];

    /// <summary>Nose, then the endplate on the car's left (+X), then right (-X): the order of <see cref="Meshes.CarMeshes.NumberDecals"/>.</summary>
    public static readonly IReadOnlyList<string> Numbers = ["number_nose", "number_endplate_l", "number_endplate_r"];

    /// <summary>Glyph height (m) of the number at each of <see cref="Numbers"/>: the slim 2026 endplates take a smaller one.</summary>
    public static readonly IReadOnlyList<float> NumberSizes = [0.24f, 0.16f, 0.16f];

    public const string PaintTop = "paint_top";
    public const string PaintSide = "paint_side";
    public const string Stripe = "stripe";
    public const string Nose = "nose";
    public const string Wing = "wing";
    public const string Endplate = "endplate";
    public const string Halo = "halo";
    public const string Carbon = "carbon";
    public const string Tyre = "tyre";
    public const string TyreStripe = "tyre_stripe";
    public const string Rim = "rim";
    public const string Light = "light";
    public const string Glass = "glass";
    public const string Tcam = "tcam";

    public static readonly IReadOnlyList<string> PaintSlots = [PaintTop, PaintSide, Stripe, Nose, Wing, Endplate, Halo];

    public static readonly IReadOnlyList<string> Slots = [.. PaintSlots, Carbon, Tyre, TyreStripe, Rim, Light, Glass, Tcam];

    /// <summary>Slots LOD1 must have (it may drop the mirror glass and compound stripe).</summary>
    public static readonly IReadOnlyList<string> Lod1Slots = [.. PaintSlots, Carbon, Tyre, Rim, Light, Tcam];

    public static IReadOnlyList<string> RequiredNodes { get; } = [Body, .. Wheels, .. Numbers, Helmet];

    /// <summary>
    /// What a loaded model lacks: required node names, then required material slots (prefixed "material "). Empty means
    /// the model can be used; anything else means the TV falls back to the procedural car.
    /// </summary>
    public static IReadOnlyList<string> Missing(IEnumerable<string> nodeNames, IEnumerable<string> materialNames, bool lod1 = false)
    {
        ArgumentNullException.ThrowIfNull(nodeNames);
        ArgumentNullException.ThrowIfNull(materialNames);
        var nodes = nodeNames.ToHashSet(StringComparer.Ordinal);
        var mats = materialNames.ToHashSet(StringComparer.Ordinal);
        return
        [
            .. RequiredNodes.Where(n => !nodes.Contains(n)),
            .. (lod1 ? Lod1Slots : Slots).Where(m => !mats.Contains(m)).Select(m => $"material {m}"),
        ];
    }

    /// <summary>Which livery colour (a <see cref="Logic.Livery"/> field) a paint slot takes, or null for a shared slot.</summary>
    public static string? LiveryColour(string slot, Logic.Livery livery)
    {
        ArgumentNullException.ThrowIfNull(livery);
        return slot switch
        {
            PaintTop => livery.Top,
            PaintSide => livery.Side,
            Stripe => livery.Stripe,
            Nose => livery.Nose,
            Wing => livery.Wing,
            Endplate => livery.Endplate,
            Halo => livery.Halo,
            _ => null,
        };
    }
}
