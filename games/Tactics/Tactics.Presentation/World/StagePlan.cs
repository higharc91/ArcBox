using System.Collections.Immutable;
using System.Numerics;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.World;

/// <summary>What a battle has done to its map, moved onto the region's tiles (overworld 5c §4).</summary>
public static class BoardChangesOnWorld
{
    /// <summary>The changes of a battle on <paramref name="stage"/>, at world tiles. Height deltas stay as they are.</summary>
    public static BoardChanges Of(BoardChanges? changes, BattleStage stage) => Moved(changes ?? BoardChanges.None, stage.Origin.X, stage.Origin.Y);

    /// <summary>
    /// The tiles whose height or sinking differs between <paramref name="before"/> and <paramref name="after"/>, in the tiles they're given in:
    /// what the world's blocks have to be rebuilt round (<see cref="StagePlan.WorldBlocks"/>). Zones draw on the stage alone, so they don't count.
    /// </summary>
    public static ImmutableArray<Tile> Touched(BoardChanges? before, BoardChanges? after)
    {
        var (was, now) = (before ?? BoardChanges.None, after ?? BoardChanges.None);
        var heights = new HashSet<HeightChange>(was.Heights);
        heights.SymmetricExceptWith(now.Heights);
        var sunk = new HashSet<Tile>(was.Sunk);
        sunk.SymmetricExceptWith(now.Sunk);
        return [.. heights.Select(h => h.At).Concat(sunk).Distinct().Order()];
    }

    internal static BoardChanges Moved(BoardChanges changes, int dx, int dy)
    {
        if (dx == 0 && dy == 0)
            return changes;
        Tile Move(Tile t) => new(t.X + dx, t.Y + dy);
        return new BoardChanges(
            [.. changes.Zones.Select(z => z with { Tiles = [.. z.Tiles.Select(Move)] })],
            [.. changes.Heights.Select(h => h with { At = Move(h.At) })],
            [.. changes.Sunk.Select(Move)]);
    }
}

/// <summary>
/// A battle's board drawn in place on its region (overworld 5c §4): the plan of the stamp and of what stands in front of it, built from the
/// region's own tiles by <see cref="BoardPlan"/>'s own code, so the board meets the world with no seam and no skirt. It covers the stamp and
/// <see cref="Reach"/> tiles on along +x and +y, which is everything that can stand in front of a unit on it, and keeps of those only what
/// overlaps the stage on the screen. Items keep the region's tiles and keys, and are drawn in the battle map's art pixels, so
/// <see cref="Beats.BeatPlayer"/>, <see cref="UnitPlan"/> (with its world offset) and <see cref="BoardFit.Stage"/> work unchanged.
/// <para>
/// The region is read through a window round what's drawn (<see cref="GroundWindow"/>), not whole: a <see cref="BoardTerrain"/> of all of a
/// 160 x 120 region takes 4 ms in Release and 9 in Debug, and a battle that changes the ground rebuilds this on the beat that does it. The
/// light is the world's, which a fight doesn't change, so it's worked out once for the stage and kept by <see cref="With"/>.
/// </para>
/// </summary>
public sealed class StagePlan
{
    /// <summary>How many tiles past the stamp, along +x and +y, can stand in front of a unit on it.</summary>
    public const int Reach = 10;

    /// <summary>How far round what's drawn a light can stand and still reach it, in tiles: the pack's widest radius is 4.</summary>
    public const int LightReach = 6;

    /// <summary>How far above the stage's own ground a unit can reach, in art pixels: what in front of the stage can hide part of one.</summary>
    public const float Headroom = 96;

    private readonly WorldGround _ground;
    private readonly ArtIndex _art;
    private readonly float _skirt;

    private StagePlan(WorldGround ground, ArtIndex art, float skirt, BattleStage stage, BoardChanges changes, BoardPlan plan, BoardLighting lighting)
    {
        (_ground, _art, _skirt) = (ground, art, skirt);
        (Stage, Changes, Plan, Lighting) = (stage, changes, plan, lighting);
        Lit = LitBoard.Build(plan, lighting);
    }

    /// <summary>Where the battle stands.</summary>
    public BattleStage Stage { get; }

    /// <summary>What the battle has done to the map, in battle tiles, as this plan draws it.</summary>
    public BoardChanges Changes { get; }

    /// <summary>What to draw, in painter's order, in the battle map's art pixels.</summary>
    public BoardPlan Plan { get; }

    /// <summary>The region's light on and round the stage, in the same pixels, for the board and its units alike.</summary>
    public BoardLighting Lighting { get; }

    /// <summary>The plan in that light.</summary>
    public LitBoard Lit { get; }

    /// <summary>The tiles of the stamp, in the region.</summary>
    public TileRect Stamp => new(Stage.Origin.X, Stage.Origin.Y, Stage.Width, Stage.Depth);

    /// <summary>The tiles the plan can draw, in the region: the stamp and <see cref="Reach"/> on.</summary>
    public TileRect Drawn => new(Stage.Origin.X, Stage.Origin.Y, Stage.Width + Reach, Stage.Depth + Reach);

    /// <summary>The stage's plan on <paramref name="ground"/>, with what the battle did (<see cref="BattleView.Board"/>, in battle tiles).</summary>
    public static StagePlan Build(WorldGround ground, BattleStage stage, BoardChanges? changes, ArtIndex art)
    {
        var skirt = GroundWindow.SkirtOf(ground.Map);
        var stamp = new TileRect(stage.Origin.X, stage.Origin.Y, stage.Width, stage.Depth);
        var drawn = new TileRect(stamp.X, stamp.Y, stamp.W + Reach, stamp.H + Reach);
        var lit = new TileRect(drawn.X - LightReach, drawn.Y - LightReach, drawn.W + 2 * LightReach, drawn.H + 2 * LightReach);
        var lights = GroundWindow.Of(ground, lit, BoardChanges.None, BoardTerrain.ChasmMaterial(ground.Map, stamp), skirt)
            .Block(art, lit, grid: false).Shifted(-BoardFit.StageOffset(stage));
        var lighting = BoardLighting.Of(lights, ground.Map.Id, art.Lighting);
        return Draw(ground, art, skirt, stage, changes ?? BoardChanges.None, lighting);
    }

    /// <summary>The same stage with what the battle has done now, in the same light: what a beat that changes the ground rebuilds.</summary>
    public StagePlan With(BoardChanges? changes) => Draw(_ground, _art, _skirt, Stage, changes ?? BoardChanges.None, Lighting);

    /// <summary>
    /// The blocks of <paramref name="world"/> round <paramref name="touched"/> (<see cref="BoardChangesOnWorld.Touched"/>, in world tiles), built
    /// again on the ground as the battle has left it, or with no changes to heal it: a stage drawn over the world can't hide a tile the world still
    /// draws higher (plan 5c §4). See <see cref="WorldPlan.Near"/> for which blocks those are.
    /// </summary>
    public ImmutableArray<BlockPlan> WorldBlocks(WorldPlan world, IEnumerable<Tile> touched, BoardChanges? changes)
    {
        var blocks = world.Near(touched);
        if (blocks.IsEmpty)
            return [];
        var (x0, y0) = (blocks.Min(b => b.Tiles.X), blocks.Min(b => b.Tiles.Y));
        var (x1, y1) = (blocks.Max(b => b.Tiles.X + b.Tiles.W), blocks.Max(b => b.Tiles.Y + b.Tiles.H));
        var window = GroundWindow.Of(_ground, new TileRect(x0, y0, x1 - x0, y1 - y0), BoardChangesOnWorld.Of(changes, Stage),
            BoardTerrain.ChasmMaterial(_ground.Map, Stamp), _skirt);
        return [.. blocks.Select(b => world.Relit(b, window.Block(_art, b.Tiles, grid: false)))];
    }

    private static StagePlan Draw(WorldGround ground, ArtIndex art, float skirt, BattleStage stage, BoardChanges changes, BoardLighting lighting)
    {
        var stamp = new TileRect(stage.Origin.X, stage.Origin.Y, stage.Width, stage.Depth);
        var drawn = new TileRect(stamp.X, stamp.Y, stamp.W + Reach, stamp.H + Reach);
        var built = GroundWindow.Of(ground, drawn, BoardChangesOnWorld.Of(changes, stage), BoardTerrain.ChasmMaterial(ground.Map, stamp), skirt)
            .Block(art, drawn, grid: true).Shifted(-BoardFit.StageOffset(stage));

        // What's in front matters only where it can cover the stage: the stamp and a tile round it, which walkers step in from, and above it.
        var near = new TileRect(stamp.X - 1, stamp.Y - 1, stamp.W + 2, stamp.H + 2);
        var (min, max) = (new Vector2(float.MaxValue), new Vector2(float.MinValue));
        foreach (var item in built.Items.Where(i => near.Contains(i.Tile)))
        {
            var (from, to) = BoardPlan.Bounds(item);
            (min, max) = (Vector2.Min(min, from), Vector2.Max(max, to));
        }
        var room = new ArtArea(min - new Vector2(0, Headroom), max);
        var plan = BoardPlan.Of([.. built.Items
            .Where(i => stamp.Contains(i.Tile) || Overlaps(i, room))
            .Select(i => i is Face { Grid: true } face && !stamp.Contains(face.Tile) ? face with { Grid = false } : i)]);
        return new StagePlan(ground, art, skirt, stage, changes, plan, lighting);
    }

    private static bool Overlaps(BoardItem item, ArtArea room)
    {
        var (min, max) = BoardPlan.Bounds(item);
        return new ArtArea(min, max).Overlaps(room);
    }
}

/// <summary>
/// A part of a region read as a map of its own, so <see cref="BoardPlan.BuildBlock"/> can build a few tiles of it without a
/// <see cref="BoardTerrain"/> of all of it. It keeps a tile round what it's asked for, so every side reads its real neighbour, and starts on a
/// whole texture, so textures line up with the world's. What it builds is put back on the region's tiles, keys and art pixels, so it's exactly
/// what the whole region's terrain would build.
/// </summary>
internal sealed class GroundWindow
{
    private const int TextureSpan = 4;      // BoardPlan's: a texture covers 4 x 4 tiles

    private readonly BoardTerrain _terrain;
    private readonly int _x;
    private readonly int _y;

    private GroundWindow(BoardTerrain terrain, int x, int y) => (_terrain, _x, _y) = (terrain, x, y);

    /// <summary>
    /// The window of <paramref name="ground"/> that can build <paramref name="need"/>, with <paramref name="changes"/> (in world tiles), a sunk tile
    /// drawn as <paramref name="chasm"/> and the region's skirt at <paramref name="skirt"/>.
    /// </summary>
    public static GroundWindow Of(WorldGround ground, TileRect need, BoardChanges changes, string chasm, float skirt)
    {
        var world = ground.Map;
        var (x0, y0) = (Math.Max(0, Align(need.X - 1)), Math.Max(0, Align(need.Y - 1)));
        var (x1, y1) = (Math.Min(world.Width, need.X + need.W + 1), Math.Min(world.Height, need.Y + need.H + 1));
        var map = Cut(world, x0, y0, Math.Max(0, x1 - x0), Math.Max(0, y1 - y0));
        return new GroundWindow(new BoardTerrain(map, BoardChangesOnWorld.Moved(changes, -x0, -y0), chasm, skirt), x0, y0);
    }

    /// <summary><see cref="BoardPlan.BuildBlock"/> of the region's tiles <paramref name="rect"/>, at the region's tiles, keys and art pixels.</summary>
    public BoardPlan Block(ArtIndex art, TileRect rect, bool grid)
    {
        var built = BoardPlan.BuildBlock(_terrain, art, rect with { X = rect.X - _x, Y = rect.Y - _y }, grid);
        return BoardPlan.Of([.. built.Items.Select(Rekey)]).Shifted(BoardProjection.Corner(_x, _y, 0));
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<BattleMap, Box> Skirts = [];

    private sealed class Box(float value)
    {
        public float Value { get; } = value;
    }

    /// <summary>
    /// Where the region's own skirt goes down to, as <see cref="BoardTerrain.Base"/> works it out for the whole region. It reads every tile, so
    /// it's worked out once for each region's map, and a fight starting doesn't pay for it.
    /// </summary>
    public static float SkirtOf(BattleMap world) => Skirts.GetValue(world, map => new Box(Lowest(map))).Value;

    private static float Lowest(BattleMap world)
    {
        var lowest = int.MaxValue;
        for (var y = 0; y < world.Height; y++)
            for (var x = 0; x < world.Width; x++)
                lowest = Math.Min(lowest, world[new Tile(x, y)].Height);
        return lowest - 1;
    }

    private static int Align(int tile) => tile >= 0 ? tile / TextureSpan * TextureSpan : 0;

    private BoardItem Rekey(BoardItem item)
    {
        var tile = new Tile(item.Tile.X + _x, item.Tile.Y + _y);
        var order = BoardOrder.Key(tile, (BoardLayer)(item.Order & 0xFF));
        return item switch
        {
            Face face => face with { Tile = tile, Order = order },
            Sprite sprite => sprite with { Tile = tile, Order = order },
            _ => throw new InvalidOperationException($"Unknown board item {item.GetType().Name}."),
        };
    }

    private static BattleMap Cut(BattleMap world, int x, int y, int width, int height)
    {
        var bytes = new byte[width * height * 3];
        for (var row = 0; row < height; row++)
            for (var column = 0; column < width; column++)
            {
                var info = world[new Tile(x + column, y + row)];
                var at = (row * width + column) * 3;
                (bytes[at], bytes[at + 1], bytes[at + 2]) = ((byte)info.Height, (byte)info.Material, (byte)info.Flags);
            }
        ImmutableArray<PropPlacement> props =
        [
            .. world.Props.Where(p => p.At.X >= x && p.At.Y >= y && p.At.X < x + width && p.At.Y < y + height)
                .Select(p => p with { At = new Tile(p.At.X - x, p.At.Y - y) }),
        ];
        return BattleMap.FromBytes(world.Id, width, height, bytes, world.Materials, props, ImmutableSortedDictionary<string, ImmutableArray<Tile>>.Empty);
    }
}
