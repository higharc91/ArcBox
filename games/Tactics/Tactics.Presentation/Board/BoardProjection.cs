using System.Numerics;
using Tactics.Logic;

namespace Tactics.Presentation.Board;

/// <summary>
/// Where a point of the map lands, in art pixels at 1x, with tile (0, 0)'s back corner at the origin. This is the one
/// place that knows the projection, which is the art's own (ui/manifest.json, terrain/manifest.json): a 32 px tile
/// projected 2:1, so x = U - V and y = (U + V) / 2, and one height step is 16 px. <see cref="BoardFit"/> takes these
/// points to the screen. For a tile as the battle has left it, use <see cref="BoardTerrain.Centre"/>.
/// </summary>
public static class BoardProjection
{
    /// <summary>Half a tile's diamond, across.</summary>
    public const float HalfWidth = 32;

    /// <summary>Half a tile's diamond, down.</summary>
    public const float HalfHeight = 16;

    /// <summary>One step of height, in pixels.</summary>
    public const float HeightStep = 16;

    /// <summary>The map point (<paramref name="x"/>, <paramref name="y"/>), in tiles, at a height in steps.</summary>
    public static Vector2 Corner(float x, float y, float height)
        => new((x - y) * HalfWidth, (x + y) * HalfHeight - height * HeightStep);

    /// <summary>The middle of a tile's top at a height in steps: where props and units stand.</summary>
    public static Vector2 Centre(Tile tile, float height) => Corner(tile.X + 0.5f, tile.Y + 0.5f, height);
}
