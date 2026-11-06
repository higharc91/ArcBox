using System.Numerics;
using Tactics.Presentation.Beats;

namespace Tactics.Presentation.Board;

/// <summary>Composes a cut-in with the existing board fit; held positions land on screen pixels.</summary>
public static class CinematicFit
{
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
