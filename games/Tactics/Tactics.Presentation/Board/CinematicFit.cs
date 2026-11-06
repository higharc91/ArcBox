using System.Numerics;
using Tactics.Presentation.Beats;

namespace Tactics.Presentation.Board;

/// <summary>Composes a cut-in with the existing board fit; held positions land on screen pixels.</summary>
public static class CinematicFit
{
    /// <summary>An opening area frame still keeps at least one figure-sized focus during the caster close-up.</summary>
    public static bool Focuses(CutInFrame frame, Vector2 point, float screenScale)
    {
        var minimum = new Vector2(150, 170) / screenScale;
        var half = Vector2.Max(frame.Extent ?? minimum, minimum);
        var delta = Vector2.Abs(point - frame.Focus);
        return delta.X <= half.X && delta.Y <= half.Y;
    }

    public static BoardFit Of(BoardFit basis, CutInFrame? frame, Vector2? centre = null)
    {
        if (frame is null || frame.Zoom == 1 && frame.Dim == 0) return basis;
        var strength = Math.Clamp(frame.Dim / 0.35f, 0, 1);
        var scale = basis.Scale * frame.Zoom;
        var target = (centre ?? new Vector2(960, 568)) - frame.Focus * scale;
        var origin = Vector2.Lerp(basis.Origin, target, strength);
        if (MathF.Abs(frame.Zoom - MathF.Round(frame.Zoom)) < 0.0001f)
            origin = new(MathF.Round(origin.X), MathF.Round(origin.Y));
        return new(origin, scale);
    }
}
