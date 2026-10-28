using System.Numerics;
using Redline.Presentation.Ceremony;

namespace Redline.Presentation.Models;

/// <summary>
/// The naming contract of the Blender podium set (<c>tools/blender/podium.py</c> writes it, the TV's
/// <c>PodiumStage</c> reads it). Same local frame as <see cref="PodiumLayout"/>: origin on the ground at the middle of
/// the stage's front edge, +X to the right as the camera sees it, +Y up, +Z towards the camera.
/// <list type="bullet">
/// <item><see cref="Body"/>: the whole set (stage, stepped blocks with inset numerals, LED media wall, truss frame,
/// lamps, branded panels, stairs, confetti cannons), one mesh with the slots below.</item>
/// <item><see cref="ConfettiLeft"/>/<see cref="ConfettiRight"/>: empties at the header cannons' muzzles, local +Z along
/// the barrel: where the confetti leaves.</item>
/// <item><see cref="Steps"/>: empties at the middle of each block's top (step 1, 2, 3): must equal
/// <see cref="PodiumLayout.StepTopCentre"/>, where the drivers stand.</item>
/// </list>
/// <see cref="Screen"/> carries the media-wall artwork (UVs 0..1 across the wall); the rest are plain materials.
/// </summary>
public static class PodiumModel
{
    public const string File = "podium.glb";
    public const int Budget = 150_000;

    public const string Body = "podium";
    public const string ConfettiLeft = "confetti_l";
    public const string ConfettiRight = "confetti_r";
    public static readonly IReadOnlyList<string> Steps = ["step_1", "step_2", "step_3"];

    public const string Screen = "screen";

    public static readonly IReadOnlyList<string> Slots =
    [
        "stage", "carpet", "step", "gold", "silver", "bronze", "numeral", Screen, "panel", "brand_red", "brand_white",
        "steel", "steel_dark", "metal", "cannon", "led_white", "led_red", "led_warm",
    ];

    public static ModelContract Contract { get; } = new(File, Budget, [Body, ConfettiLeft, ConfettiRight, .. Steps], Slots);

    /// <summary>Where the driver on <paramref name="step"/> stands (the <see cref="Steps"/> empty must be here).</summary>
    public static Vector3 StepTop(int step) => PodiumLayout.StepTopCentre(step);
}
