using System.Collections.Immutable;
using System.Numerics;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Board;
using Tactics.Presentation.World;

namespace Tactics.Presentation.Tests.World;

/// <summary>Ground and views for the walking tests, in the shapes the server sends.</summary>
internal static class WalkFeed
{
    public const double Frame = 1.0 / 144;
    public const int StraightMs = 300;

    public static readonly int DiagonalMs = (int)Math.Round(StraightMs * Math.Sqrt(2));

    /// <summary>A map of grass with a height for each tile, and walls where <paramref name="wall"/> says.</summary>
    public static BoardTerrain Ground(int width, int height, Func<int, int, int>? heightAt = null, Func<int, int, bool>? wall = null)
    {
        var bytes = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var at = (y * width + x) * 3;
                bytes[at] = (byte)(heightAt?.Invoke(x, y) ?? 0);
                bytes[at + 2] = wall?.Invoke(x, y) == true ? (byte)TileFlags.Impassable : (byte)0;
            }
        return new BoardTerrain(BattleMap.FromBytes("test", width, height, bytes, ["grass/lush"], [], ImmutableSortedDictionary<string, ImmutableArray<Tile>>.Empty), null);
    }

    /// <summary>A party of <paramref name="count"/>, with a leading.</summary>
    public static ImmutableArray<WalkerView> Party(int count)
        => [.. Enumerable.Range(0, count).Select(i => new WalkerView(Id(i), Id(i), "characters.class_fighter", true, i == 0))];

    public static string Id(int index) => ((char)('a' + index)).ToString();

    public static ExploreView View(Tile at, Tile facing, bool held, IEnumerable<WalkLeg> legs, int seq, int epoch = 1, int party = 3)
        => new("test", "Test", epoch, seq, at, facing, held, [.. legs.TakeLast(12)], Party(party), "a");

    public static ExploreView View(Tile at, bool held = false, int party = 3, int epoch = 1)
        => View(at, new Tile(at.X + 1, at.Y), held, [], 0, epoch, party);

    public static int MsOf(Tile from, Tile to) => from.X != to.X && from.Y != to.Y ? DiagonalMs : StraightMs;

    /// <summary>A server walking a list of tiles, step by step, as the views would tell it.</summary>
    public sealed class Script(Tile start, int party = 3, int epoch = 1)
    {
        private readonly List<WalkLeg> _legs = [];

        public Tile At { get; private set; } = start;

        public Tile Facing { get; private set; } = new(start.X + 1, start.Y);

        public int Seq { get; private set; }

        public IReadOnlyList<WalkLeg> Legs => _legs;

        public ExploreView Now(bool held) => View(At, Facing, held, _legs, Seq, epoch, party);

        public int Step(int dx, int dy, int? ms = null)
        {
            var to = new Tile(At.X + dx, At.Y + dy);
            var took = ms ?? MsOf(At, to);
            _legs.Add(new WalkLeg(++Seq, to, took));
            (At, Facing) = (to, new Tile(to.X + dx, to.Y + dy));
            return took;
        }
    }

    /// <summary>Moves the clock on in frames, reading the places each frame, as the TV does.</summary>
    public static IReadOnlyDictionary<string, UnitPlace> Run(WalkTrack track, BoardTerrain terrain, double seconds, double frame = Frame)
    {
        var places = track.Places(terrain);
        for (var t = 0.0; t < seconds; t += frame)
        {
            track.Advance(frame);
            places = track.Places(terrain);
        }
        return places;
    }

    /// <summary>
    /// Walks <paramref name="script"/> in real time: each of <paramref name="steps"/> reaches the TV when the last has taken its time,
    /// the last with <paramref name="heldAtEnd"/>.
    /// </summary>
    public static void Walk(WalkTrack track, BoardTerrain terrain, Script script, (int Dx, int Dy)[] steps, bool heldAtEnd = true, Action? onFrame = null)
    {
        for (var i = 0; i < steps.Length; i++)
        {
            var ms = script.Step(steps[i].Dx, steps[i].Dy);
            track.Receive(script.Now(heldAtEnd || i < steps.Length - 1));
            for (var t = 0.0; t < ms / 1000.0; t += Frame)
            {
                track.Advance(Frame);
                track.Places(terrain);
                onFrame?.Invoke();
            }
        }
    }

    public static (int, int)[] Repeat(int dx, int dy, int count) => [.. Enumerable.Repeat((dx, dy), count)];

    public static float DistanceToPath(Vector2 point, IReadOnlyList<Tile> tiles)
    {
        var best = float.MaxValue;
        for (var i = 1; i < tiles.Count; i++)
        {
            var (a, b) = (Centre(tiles[i - 1]), Centre(tiles[i]));
            var share = Math.Clamp(Vector2.Dot(point - a, b - a) / (b - a).LengthSquared(), 0, 1);
            best = Math.Min(best, Vector2.Distance(point, Vector2.Lerp(a, b, share)));
        }
        return tiles.Count == 1 ? Vector2.Distance(point, Centre(tiles[0])) : best;
    }

    public static Vector2 Centre(Tile tile) => new(tile.X + 0.5f, tile.Y + 0.5f);

    public static Tile TileUnder(Vector2 map) => new((int)MathF.Floor(map.X), (int)MathF.Floor(map.Y));
}
