using System.Collections.Immutable;
using Tactics.Logic;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.World;

/// <summary>A place on a region worth checking by eye.</summary>
/// <param name="Name">What it shows.</param>
/// <param name="Look">The tile to look at.</param>
/// <param name="Stand">Where a figure stands, on or near it.</param>
public sealed record WorldSpot(string Name, Tile Look, Tile Stand);

/// <summary>
/// Dev-only: finds the places where drawing a region in blocks could go wrong, so <c>--world-demo</c> can show them on any region: a figure behind what
/// should hide it, seams where blocks meet, and where the ground stops. A region with no such place has no such spot.
/// </summary>
public static class WorldSpots
{
    private const int CliffSteps = 3;       // a figure is about this tall
    private const int NearbyTiles = 12;
    private const string Crate = "crate";

    /// <summary>
    /// The places <c>--world-looks</c> names: <c>name@x,y</c>, or <c>name@x,y@x,y</c> with the second tile where the figure stands, each ended by a semicolon.
    /// </summary>
    public static ImmutableArray<WorldSpot> Parse(string looks)
    {
        return
        [
            .. looks.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(look =>
            {
                var parts = look.Split('@');
                return new WorldSpot(parts[0], Read(parts[1]), Read(parts[^1]));
            }),
        ];
    }

    /// <summary>The tiles of a walk <c>--world-route</c> names, <c>x,y;x,y;…</c>: a line from corner to corner, on the diagonal and then straight.</summary>
    public static ImmutableArray<Tile> Route(string corners)
    {
        Tile[] at = [.. corners.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(Read)];
        var tiles = new List<Tile>();
        foreach (var (from, to) in at.Zip(at.Skip(1)))
            for (var tile = from; tile != to; tile = new Tile(tile.X + Math.Sign(to.X - tile.X), tile.Y + Math.Sign(to.Y - tile.Y)))
                tiles.Add(tile);
        return [.. tiles, at[^1]];
    }

    private static Tile Read(string tile)
    {
        var parts = tile.Split(',');
        return new Tile(int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>The spots of <paramref name="terrain"/>, whose rules are <paramref name="ground"/>.</summary>
    public static ImmutableArray<WorldSpot> Of(BoardTerrain terrain, Terrain ground)
    {
        var map = terrain.Map;
        var tiles = terrain.Tiles.ToArray();
        var standing = tiles.Where(ground.Walkable).ToArray();
        var spots = new List<WorldSpot>();
        void Add(string name, Tile? tile)
        {
            if (tile is { } found)
                spots.Add(new WorldSpot(name, found, ground.Walkable(found) ? found : NearestStand(ground, found)));
        }

        Add("behind-cliff", Best(standing.Where(t => map.InBounds(Front(t)) && Rise(terrain, t) >= CliffSteps), t => Rise(terrain, t)));
        Add("behind-crate", map.Props.Where(p => p.Id == Crate).Select(p => new Tile(p.At.X - 1, p.At.Y - 1)).Where(t => map.InBounds(t) && ground.Walkable(t)).Cast<Tile?>().FirstOrDefault());
        Add("water-block-edge", tiles.Where(t => OnBlockEdge(t, out var before) && map.InBounds(before) && terrain.Look(t).Water && terrain.Look(before).Water).Cast<Tile?>().FirstOrDefault());
        var middle = new Tile(map.Width / 2, map.Height / 2);
        Add("block-corner", Best(standing.Where(t => t.X > 0 && t.Y > 0 && t.X % WorldTuning.BlockSize == 0 && t.Y % WorldTuning.BlockSize == 0), t => -Distance(t, middle)));
        Add("region-edge", Best(standing.Where(t => t.X == map.Width - 1 || t.Y == map.Height - 1), t => t.X + t.Y));
        Add("void-near", Best(tiles.Where(t => Touches(terrain, t, 1)), t => t.X + t.Y));
        Add("void-far", Best(tiles.Where(t => Touches(terrain, t, -1)), t => -(t.X + t.Y)));
        return [.. spots];
    }

    private static Tile Front(Tile tile) => new(tile.X + 1, tile.Y + 1);

    private static float Rise(BoardTerrain terrain, Tile tile) => terrain.Top(Front(tile)) - terrain.Top(tile);

    private static int Distance(Tile a, Tile b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

    private static Tile? Best(IEnumerable<Tile> tiles, Func<Tile, float> score)
    {
        var chosen = tiles.ToArray();
        return chosen.Length == 0 ? null : chosen.MaxBy(score);
    }

    private static bool OnBlockEdge(Tile tile, out Tile before)
    {
        var across = tile.X % WorldTuning.BlockSize == 0;
        before = across ? new Tile(tile.X - 1, tile.Y) : new Tile(tile.X, tile.Y - 1);
        return across || tile.Y % WorldTuning.BlockSize == 0;
    }

    private static bool Touches(BoardTerrain terrain, Tile tile, int way)
    {
        Tile[] beside = [new Tile(tile.X + way, tile.Y), new Tile(tile.X, tile.Y + way)];
        return !terrain.Look(tile).Void && beside.Any(t => terrain.Map.InBounds(t) && terrain.Look(t).Void);
    }

    private static Tile NearestStand(Terrain ground, Tile tile)
    {
        for (var reach = 1; reach <= NearbyTiles; reach++)
            for (var dy = -reach; dy <= reach; dy++)
                for (var dx = -reach; dx <= reach; dx++)
                {
                    var near = new Tile(tile.X + dx, tile.Y + dy);
                    if (ground.Walkable(near))
                        return near;
                }
        return tile;
    }
}
