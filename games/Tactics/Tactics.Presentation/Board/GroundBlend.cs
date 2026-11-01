using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Tactics.Logic;

namespace Tactics.Presentation.Board;

/// <summary>A neighbour's ground running out over a tile.</summary>
/// <param name="Material">The neighbour's material.</param>
/// <param name="Mask">Which of <see cref="GroundBlend.Ways"/> it comes from.</param>
/// <param name="Stamped">The neighbour is a stamp's, whose tiles have no drift of colour.</param>
public readonly record struct GroundEdge(string Material, int Mask, bool Stamped);

/// <summary>
/// Ground that changes tile by tile zigzags along any line that isn't a row or a column, and the pack has no transition art. So in the world, and only there, one ground's top runs
/// out over the next tile in the pack's own dither: solid where it leaves its own tile, an upright checker, then sparse, then nothing, with a slow wobble so the edge wanders. A top
/// laid on a tile again through a mask needs no new art and doesn't change a top: a battle never draws one, a stamp's tile never takes one, and a stamp's edge runs out into the world round it.
/// <para>
/// Ground that lies loose runs over ground that's firmer: <see cref="Rank"/>. What was laid by hand, or flows, keeps its hard edge. Each way a neighbour can lie is one mask over the
/// whole of a top's four by four tiles, so a tile with ground on two sides takes two, and they meet without a seam as they share one wobble. A texture is baked once for each ground
/// and way the region uses (<c>BoardPainter</c>): this is the masks, and which go where.
/// </para>
/// </summary>
public static class GroundBlend
{
    /// <summary>The texels along a side of the doubled top a mask fits (<see cref="DitherTexels"/>).</summary>
    public const int Size = 256;

    private const int Tiles = 4;                // the manifests' "span"
    private const int TileTexels = Size / Tiles;
    private const float Solid = 0.2f;           // how far across a tile each level of cover reaches
    private const float Half = 0.46f;
    private const float Sparse = 0.68f;
    private const float Wobble = 0.5f;          // how far, in tiles, the noise can move an edge, from side to side
    private const float WobbleFrom = 0.22f;     // nothing moves right at the seam
    private const int NoiseCells = 8;           // a top's width in noise cells, so it tiles with the art
    private const int NoiseSeed = 7211;

    /// <summary>Where a neighbour can lie from a tile: the four sides, then the four corners, each a mask.</summary>
    public static readonly ImmutableArray<(int Dx, int Dy)> Ways = [(1, 0), (0, 1), (-1, 0), (0, -1), (1, 1), (-1, 1), (-1, -1), (1, -1)];

    private static readonly Lazy<float[]> Noise = new(BuildNoise);

    private static readonly Lazy<byte[]>[] Masks = [.. Enumerable.Range(0, 8).Select(way => new Lazy<byte[]>(() => Build(Ways[way])))];

    /// <summary>
    /// How firmly a material lies: a higher one runs over a lower one, and 0 is what neither runs nor is run into. Ash blows over everything, grass grows over earth, earth over a worn
    /// track, and all of them over scree and bare rock.
    /// </summary>
    public static int Rank(string material)
    {
        var slash = material.IndexOf('/');
        return (slash < 0 ? material : material[..slash]) switch
        {
            "ash" => 7,
            "grass" => 6,
            "forest_needles" or "forest_floor" => 5,
            "dirt" or "mud" => 4,
            "path" => 3,
            "mine_gravel" => 2,
            "rock" or "cave_floor" or "basalt" or "sandstone" => 1,
            _ => 0,
        };
    }

    /// <summary>Whether <paramref name="over"/>, lying beside <paramref name="under"/>, runs over it. Of two the same rank, one does, by name.</summary>
    public static bool RunsOver(string over, string under, int overRank, int underRank)
        => overRank != underRank ? overRank > underRank : string.CompareOrdinal(over, under) > 0;

    /// <summary>
    /// What runs over <paramref name="tile"/>, lowest first, so a looser ground lies over a firmer one. Ground only meets at one level, as a step has its own side between them, and a
    /// corner goes only where neither side next to it already brings the same ground.
    /// </summary>
    public static IEnumerable<GroundEdge> Of(BoardTerrain terrain, Tile tile)
    {
        var map = terrain.Map;
        if (map.StampedTiles.Contains(tile))
            return [];
        var own = map[tile].Material;
        var ownRank = terrain.Ranks[own];
        if (ownRank == 0 || terrain.Look(tile).Void || terrain.Changes.Sunk.Contains(tile))
            return [];

        var top = terrain.Top(tile);
        List<(GroundEdge Edge, int Rank)>? found = null;
        for (var way = 0; way < Ways.Length; way++)
        {
            var (dx, dy) = Ways[way];
            var next = new Tile(tile.X + dx, tile.Y + dy);
            if (!Runs(terrain, next, top, own, ownRank, out var material, out var stamped))
                continue;
            if (dx != 0 && dy != 0 && (Same(terrain, new Tile(next.X, tile.Y), top, material) || Same(terrain, new Tile(tile.X, next.Y), top, material)))
                continue;
            (found ??= []).Add((new GroundEdge(map.Materials[material], way, stamped), terrain.Ranks[material]));
        }
        return found is null ? [] : found.OrderBy(f => f.Rank).ThenBy(f => f.Edge.Material, StringComparer.Ordinal).ThenBy(f => f.Edge.Mask).Select(f => f.Edge);
    }

    private static bool Runs(BoardTerrain terrain, Tile next, float top, int own, int ownRank, out int material, out bool stamped)
    {
        (material, stamped) = (0, false);
        var map = terrain.Map;
        if (!map.InBounds(next) || terrain.Top(next) != top || terrain.Look(next).Void || terrain.Changes.Sunk.Contains(next))
            return false;
        material = map[next].Material;
        var rank = terrain.Ranks[material];
        if (material == own || rank == 0)
            return false;
        stamped = map.StampedTiles.Contains(next);
        return stamped || RunsOver(map.Materials[material], map.Materials[own], rank, ownRank);
    }

    private static bool Same(BoardTerrain terrain, Tile next, float top, int material)
        => terrain.Map.InBounds(next) && terrain.Top(next) == top && terrain.Map[next].Material == material;

    /// <summary>The mask of one of <see cref="Ways"/>: <see cref="Size"/> squared, 255 where the top shows. It tiles, as the art does. Built once.</summary>
    public static byte[] Mask(int way) => Masks[way].Value;

    /// <summary><paramref name="rgba"/>, a doubled top, with what <paramref name="way"/>'s mask leaves out made clear.</summary>
    public static byte[] Masked(ReadOnlySpan<byte> rgba, int way)
    {
        if (rgba.Length != Size * Size * sizeof(uint))
            throw new ArgumentException($"A blended top is {Size} texels a side.", nameof(rgba));
        var mask = Mask(way);
        var masked = rgba.ToArray();
        var texels = MemoryMarshal.Cast<byte, uint>(masked.AsSpan());
        for (var i = 0; i < texels.Length; i++)
            if (mask[i] == 0)
                texels[i] = 0;
        return masked;
    }

    /// <summary>The pack's dither at a doubled texel: an upright checker of art pixels, as <see cref="DitherTexels"/> lays it.</summary>
    public static bool Checker(int u, int v) => (Column(u, v) + Row(u, v) & 1) == 0;

    /// <summary>Whether a doubled texel of a mask from <paramref name="way"/> falls where that mask is half covered.</summary>
    public static bool HalfLevel(int u, int v, (int Dx, int Dy) way)
    {
        var distance = Across(u, v, way);
        return distance >= Solid && distance < Half;
    }

    private static byte[] Build((int Dx, int Dy) way)
    {
        var mask = new byte[Size * Size];
        for (var v = 0; v < Size; v++)
            for (var u = 0; u < Size; u++)
            {
                var distance = Across(u, v, way);
                var shown = distance < Solid || (distance < Half ? Checker(u, v) : distance < Sparse && Column(u, v) % 2 == 0 && Row(u, v) % 2 == 0);
                mask[v * Size + u] = (byte)(shown ? 255 : 0);
            }
        return mask;
    }

    /// <summary>How far a texel lies from the neighbour, in tiles, moved by the wobble.</summary>
    private static float Across(int u, int v, (int Dx, int Dy) way)
    {
        var (x, y) = ((u % TileTexels + 0.5f) / TileTexels, (v % TileTexels + 0.5f) / TileTexels);
        var (across, along) = (way.Dx > 0 ? 1 - x : x, way.Dy > 0 ? 1 - y : y);
        var distance = way.Dx == 0 ? along : way.Dy == 0 ? across : MathF.Sqrt(across * across + along * along);
        return distance + (Noise.Value[v * Size + u] - 0.5f) * Wobble * Math.Min(distance / WobbleFrom, 1);
    }

    private static int Column(int u, int v) => (u - v) >> 1;

    private static int Row(int u, int v) => (u + v) >> 2;

    /// <summary>Smooth noise from 0 to 1 over two scales that tiles at <see cref="Size"/>, from a fixed seed.</summary>
    private static float[] BuildNoise()
    {
        var random = new Random(NoiseSeed);
        float[] Layer(int cells)
        {
            var values = new float[cells * cells];
            for (var i = 0; i < values.Length; i++)
                values[i] = random.NextSingle();
            return values;
        }
        var (coarse, fine) = (Layer(NoiseCells), Layer(NoiseCells * 3));
        var noise = new float[Size * Size];
        for (var v = 0; v < Size; v++)
            for (var u = 0; u < Size; u++)
                noise[v * Size + u] = Sample(coarse, NoiseCells, u, v) * 0.65f + Sample(fine, NoiseCells * 3, u, v) * 0.35f;
        return noise;
    }

    private static float Sample(float[] values, int cells, int u, int v)
    {
        var (x, y) = ((float)u * cells / Size, (float)v * cells / Size);
        var (left, top) = ((int)x, (int)y);
        var (fx, fy) = (Smooth(x - left), Smooth(y - top));
        float At(int a, int b) => values[b % cells * cells + a % cells];
        var upper = At(left, top) * (1 - fx) + At(left + 1, top) * fx;
        var lower = At(left, top + 1) * (1 - fx) + At(left + 1, top + 1) * fx;
        return upper * (1 - fy) + lower * fy;
    }

    private static float Smooth(float t) => t * t * (3 - 2 * t);
}
