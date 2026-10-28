using System.Numerics;

namespace Redline.Presentation.Ceremony;

/// <summary>
/// Podium spec §5 camera: straight on to the podium from the grandstand side of the track. During the anthem a slow
/// push-in from the whole set to the three drivers; during the champagne a wider shot that drifts across the front, so
/// the sprays, the confetti and the cars all fit.
/// </summary>
public static class PodiumFraming
{
    /// <summary>The push-in runs over the anthem's length.</summary>
    public const double PushSeconds = 16;

    public const float AnthemFov = 27;
    public const float ChampagneFov = 40;

    // Local (PodiumLayout) eye and target positions.
    private static readonly Vector3 PushFrom = new(0.5f, 3.3f, 21.5f);
    private static readonly Vector3 PushTo = new(0.2f, 3.05f, 12.5f);
    private static readonly Vector3 PushTargetFrom = new(0, 2.75f, -3.0f);
    private static readonly Vector3 PushTargetTo = new(0, 3.0f, -3.0f);
    private static readonly Vector3 WideTarget = new(0, 2.7f, -2.4f);
    private const float WideZ = 21f;
    private const float WideHeight = 4.6f;
    private const float WideDrift = 2.6f;

    /// <summary>The camera <paramref name="seconds"/> into the Anthem (or the Champagne, when <paramref name="champagne"/>).</summary>
    public static CameraPose Pose(PodiumLayout layout, bool champagne, double seconds)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var u = (float)RevealMotion.SmoothStep(Math.Clamp(seconds / PushSeconds, 0, 1));
        if (!champagne)
        {
            var eye = Vector3.Lerp(PushFrom, PushTo, u);
            var target = Vector3.Lerp(PushTargetFrom, PushTargetTo, u);
            return new CameraPose(layout.World(eye), layout.World(target), AnthemFov);
        }
        var x = -WideDrift + (2 * WideDrift * u);
        return new CameraPose(layout.World(new Vector3(x, WideHeight, WideZ)), layout.World(WideTarget with { X = x * 0.3f }), ChampagneFov);
    }
}
