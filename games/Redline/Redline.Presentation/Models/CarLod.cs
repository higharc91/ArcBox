namespace Redline.Presentation.Models;

/// <summary>A distance band (metres from the camera) a mesh is drawn in; <c>End = 0</c> means no far limit.</summary>
public readonly record struct LodBand(bool Visible, float Begin, float End, float Margin);

/// <summary>
/// Which car LOD draws where (plan Review Focus 3): LOD0 up to <see cref="SwitchDistance"/>, LOD1 beyond it; the Low
/// preset uses LOD1 at every distance. Godot applies the bands through each mesh's <c>visibility_range_*</c>.
/// </summary>
public static class CarLod
{
    public const float SwitchDistance = 60f;

    /// <summary>
    /// No hysteresis: Godot measures each mesh's own distance, so with a margin a car near the boundary could have its LOD0
    /// mesh already hidden and its LOD1 mesh not yet shown. With 0 every part is in exactly one LOD at every distance.
    /// </summary>
    public const float Margin = 0f;

    public static (LodBand Lod0, LodBand Lod1) Bands(bool lowDetail) => lowDetail
        ? (new LodBand(false, 0, 0, 0), new LodBand(true, 0, 0, 0))
        : (new LodBand(true, 0, SwitchDistance, Margin), new LodBand(true, SwitchDistance, 0, Margin));

    /// <summary>The LOD (0 or 1) drawn for a car <paramref name="distance"/> metres from the camera (ignoring the margin).</summary>
    public static int Pick(float distance, bool lowDetail) => lowDetail || distance >= SwitchDistance ? 1 : 0;

    /// <summary>True when the band draws at <paramref name="distance"/> (ignoring the margin).</summary>
    public static bool Draws(LodBand band, float distance) =>
        band.Visible && distance >= band.Begin && (band.End <= 0 || distance < band.End);
}
