using System.Numerics;

namespace Tactics.Presentation.Board;

/// <summary>A rectangle on the 1920x1080 layout.</summary>
public readonly record struct BoardArea(float X, float Y, float Width, float Height);

/// <summary>
/// Puts the board on the screen: art pixels (<see cref="BoardProjection"/>) to the 1920x1080 layout, at a whole multiple
/// of the art (ui/manifest.json: nearest filtering, whole multiples), centred in <see cref="Area"/>.
/// </summary>
/// <param name="Origin">Where the art's origin lands.</param>
/// <param name="Scale">Layout pixels per art pixel.</param>
public sealed record BoardFit(Vector2 Origin, float Scale)
{
    /// <summary>The largest scale used: a small map shouldn't turn into a few enormous tiles.</summary>
    public const int MaxScale = 4;

    /// <summary>
    /// The room the board gets. A strip stays free across the top; the rest of the HUD (turn order, round, objectives) goes
    /// in the four corners, which a diamond board leaves empty.
    /// </summary>
    public static BoardArea Area { get; } = new(32, 72, 1856, 992);

    /// <summary>
    /// The fit for a battle on <paramref name="terrain"/>'s map, which stays the same for the whole battle: it comes from
    /// the map as its file has it, whatever the battle has done to it, so nothing a fight does can move or shrink the board.
    /// What grows past those bounds runs into the top strip and, past that, off the screen: a raised tile clips, it doesn't
    /// rescale. The rules' maximum height (9) stays on screen from five tiles in from the back corner.
    /// </summary>
    public static BoardFit For(BoardTerrain terrain, ArtIndex art)
    {
        var plan = BoardPlan.Build(new BoardTerrain(terrain.Map, null), art);
        return Of(plan.Min, plan.Max);
    }

    /// <summary>The fit for a board covering <paramref name="min"/> to <paramref name="max"/> in art pixels.</summary>
    public static BoardFit Of(Vector2 min, Vector2 max)
    {
        var size = Vector2.Max(max - min, Vector2.One);
        var room = MathF.Min(Area.Width / size.X, Area.Height / size.Y);
        var scale = room >= 1 ? MathF.Min(MathF.Floor(room), MaxScale) : room;   // a map too big for 1x has to shrink
        var middle = (min + max) / 2;
        var origin = new Vector2(Area.X + Area.Width / 2 - middle.X * scale, Area.Y + Area.Height / 2 - middle.Y * scale);
        return new BoardFit(new Vector2(MathF.Round(origin.X), MathF.Round(origin.Y)), scale);
    }

    /// <summary>An art pixel's place on the 1920x1080 layout.</summary>
    public Vector2 ToScreen(Vector2 art) => Origin + art * Scale;
}
