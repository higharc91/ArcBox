using System.Numerics;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.World;

/// <summary>Where the world goes in a window, and what of it that shows. Fits are for the 1920x1080 layout; a window of another shape shows more of the world round it.</summary>
public static class WorldView
{
    private static readonly Vector2 Layout = new(1920, 1080);

    /// <summary>The fit that puts a point of the map, in art pixels, at the layout's middle, on whole pixels.</summary>
    public static BoardFit Centred(Vector2 art)
    {
        var origin = Layout / 2 - art * WorldTuning.Scale;
        return new BoardFit(new Vector2(MathF.Round(origin.X), MathF.Round(origin.Y)), WorldTuning.Scale);
    }

    /// <summary>Where <paramref name="fit"/>'s origin goes in a window, on whole pixels.</summary>
    public static Vector2 OriginIn(BoardFit fit, Vector2 window)
    {
        var margin = (window - Layout) / 2;
        return fit.Origin + new Vector2(MathF.Round(margin.X), MathF.Round(margin.Y));
    }

    /// <summary>What a window shows, in art pixels, with the world placed by <paramref name="fit"/>.</summary>
    public static ArtArea Seen(BoardFit fit, Vector2 window) => Seen(OriginIn(fit, window), window);

    /// <summary>What a window shows, in art pixels, with the world's origin at <paramref name="origin"/>.</summary>
    public static ArtArea Seen(Vector2 origin, Vector2 window)
    {
        var min = -origin / WorldTuning.Scale;
        return new ArtArea(min, min + window / WorldTuning.Scale);
    }
}
