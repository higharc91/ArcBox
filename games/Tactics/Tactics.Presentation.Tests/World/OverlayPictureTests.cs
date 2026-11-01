using System.Collections.Immutable;
using System.Numerics;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Board;
using Tactics.Presentation.Tests.Board;
using Tactics.Presentation.World;


namespace Tactics.Presentation.Tests.World;

/// <summary>
/// The whole picture, not a rule about it: for a party walking a real route, what's on screen (every block as it was recorded, then the frame's overlay) is
/// rastered in art pixels and held against true painter's order. The shape each thing covers stands in for its pixels, opaque all the way to its edge, which is the
/// harshest case: a repainted item shows at once wherever it's wrong.
/// </summary>
public sealed class OverlayPictureTests
{
    private const int Crop = 96;    // art pixels round the party: further than anything repainted can reach and still be tested

    private static readonly Lazy<UnitArt> Art = new(() => UnitArt.Load(Pack.Root));

    private readonly record struct Shape(int Id, long Order, Vector2 Min, Vector2 Max, Vector2[]? Quad);

    private sealed record Sheet(WorldPlan Plan, Shape[] All, Dictionary<ItemAt, Shape> Of);

    private static Sheet Read(WorldPlan plan)
    {
        var all = new List<Shape>();
        var of = new Dictionary<ItemAt, Shape>();
        foreach (var block in plan.Blocks)
            for (var i = 0; i < block.Lit.Items.Length; i++)
            {
                var item = block.Lit.Items[i];
                var (min, max) = BoardPlan.Bounds(item is LitFace f ? f.Face : ((LitSprite)item).Sprite);
                var shape = new Shape(all.Count + 1, item.Order, min, max, item is LitFace face ? [.. face.Face.Points] : null);
                all.Add(shape);
                of[new ItemAt(block.Index, i)] = shape;
            }
        return new Sheet(plan, [.. all], of);
    }

    private static void Paint(int[] buffer, Vector2 corner, int size, in Shape shape)
    {
        var from = Vector2.Max(shape.Min - corner, Vector2.Zero);
        var to = Vector2.Min(shape.Max - corner, new Vector2(size));
        for (var y = (int)from.Y; y < to.Y; y++)
            for (var x = (int)from.X; x < to.X; x++)
            {
                var at = new Vector2(x + 0.5f, y + 0.5f) + corner;
                if (at.X >= shape.Min.X && at.X < shape.Max.X && at.Y >= shape.Min.Y && at.Y < shape.Max.Y && (shape.Quad is null || Inside(shape.Quad, at)))
                    buffer[y * size + x] = shape.Id;
            }
    }

    private static bool Inside(Vector2[] quad, Vector2 at)
    {
        var (above, below) = (false, false);
        for (var i = 0; i < quad.Length; i++)
        {
            var (a, b) = (quad[i], quad[(i + 1) % quad.Length]);
            var side = (b.X - a.X) * (at.Y - a.Y) - (b.Y - a.Y) * (at.X - a.X);
            above |= side > 0;
            below |= side < 0;
        }
        return !(above && below);
    }

    private static Shape UnitShape(BoardUnit unit, int index)
    {
        var area = WorldPlan.AreaOf(unit);
        return new Shape(-1 - index, unit.Order, area.Min, area.Max, null);
    }

    /// <summary>Paints <paramref name="items"/>, in order, with <paramref name="units"/> where they sort among them.</summary>
    private static void PaintWith(int[] buffer, Vector2 corner, int size, Shape[] items, Shape[] units)
    {
        var at = 0;
        foreach (var item in items)
        {
            while (at < units.Length && units[at].Order <= item.Order)
                Paint(buffer, corner, size, units[at++]);
            Paint(buffer, corner, size, item);
        }
        while (at < units.Length)
            Paint(buffer, corner, size, units[at++]);
    }

    /// <summary>The pixels this frame gets wrong, for a party at <paramref name="units"/>.</summary>
    private static int Wrong(Sheet sheet, ImmutableArray<BoardUnit> units, IReadOnlyList<ItemAt> over, int[] drawn, int[] truth)
    {
        var (min, max) = (new Vector2(float.MaxValue), new Vector2(float.MinValue));
        foreach (var unit in units)
        {
            var area = WorldPlan.AreaOf(unit);
            (min, max) = (Vector2.Min(min, area.Min), Vector2.Max(max, area.Max));
        }
        var corner = new Vector2(MathF.Floor(min.X), MathF.Floor(min.Y)) - new Vector2(Crop);
        var size = (int)Math.Max(max.X - min.X, max.Y - min.Y) + 2 * Crop;
        if (size * size > drawn.Length)
            size = (int)Math.Sqrt(drawn.Length);
        Array.Clear(drawn);
        var near = sheet.All.Where(s => s.Max.X > corner.X && s.Min.X < corner.X + size && s.Max.Y > corner.Y && s.Min.Y < corner.Y + size).ToArray();
        foreach (var item in near)                     // the blocks, each as it was recorded, in their own order
            Paint(drawn, corner, size, item);
        var shapes = units.Select(UnitShape).OrderBy(u => u.Order).ToArray();
        PaintWith(drawn, corner, size, [.. over.Select(at => sheet.Of[at])], shapes);

        Array.Clear(truth);
        PaintWith(truth, corner, size, [.. near.OrderBy(s => s.Order).ThenBy(s => s.Id)], shapes);

        var wrong = 0;
        for (var i = 0; i < size * size; i++)
            wrong += drawn[i] != truth[i] ? 1 : 0;
        return wrong;
    }

    private static ImmutableArray<WalkerView> Party(int count)
        => [.. Enumerable.Range(0, count).Select(i => new WalkerView(WalkFeedId(i), WalkFeedId(i), "characters.class_rogue", true, i == 0))];

    private static string WalkFeedId(int i) => ((char)('a' + i)).ToString();

    /// <summary>Walks three walkers down <paramref name="route"/>, each a little behind the one in front.</summary>
    private static (int WrongFrames, int WorstPixels, double MeanItems, int MostItems) Sweep(Sheet sheet, ImmutableArray<Tile> route, float step, ITestOutputHelper? output, Func<ImmutableArray<BoardUnit>, IReadOnlyList<ItemAt>> overlay)
    {
        var terrain = sheet.Plan.Terrain;
        var (drawn, truth) = (new int[800 * 800], new int[800 * 800]);
        var party = Party(3);
        var pose = new Dictionary<string, UnitPose>();
        var (wrongFrames, worst, frames, items, most) = (0, 0, 0, 0L, 0);
        for (var at = 3f; at < route.Length - 1; at += step)
        {
            var places = new Dictionary<string, UnitPlace>
            {
                ["a"] = WorldRoute.At(terrain, route, at),
                ["b"] = WorldRoute.At(terrain, route, at - 1),
                ["c"] = WorldRoute.At(terrain, route, at - 2),
            };
            var units = UnitPlan.Walkers(party, places, pose, Art.Value);
            var over = overlay(units);
            var wrong = Wrong(sheet, units, over, drawn, truth);
            frames++;
            items += over.Count;
            most = Math.Max(most, over.Count);
            worst = Math.Max(worst, wrong);
            wrongFrames += wrong > 0 ? 1 : 0;
            if (wrong > 0 && wrongFrames < 4)
                output?.WriteLine($"step {at}: {wrong} px wrong at {places["a"].Map}");
        }
        return (wrongFrames, worst, items / (double)frames, most);
    }

    private static readonly Lazy<WorldGround> FordGround = new(() => Pack.Ground("ashen_ford"));
    private static readonly Lazy<Sheet> Ford = new(() => Read(WorldPlan.Build(new BoardTerrain(FordGround.Value.Map, null), Pack.Art)));
    private static readonly Lazy<(WorldGround Ground, Sheet Sheet)> Hilly = new(() =>
    {
        var ground = DemoRegion.Hilly(160, 120);
        return (ground, Read(WorldPlan.Build(new BoardTerrain(ground.Map, null), Pack.Art)));
    });

    private readonly ITestOutputHelper _output;

    public OverlayPictureTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void A_walk_across_the_ashen_ford_comes_out_as_true_painters_order()
    {
        var route = WorldRoute.Across(FordGround.Value.Terrain, new Tile(30, 65), ClassDef.DefaultJump);

        var (wrong, worst, mean, most) = Sweep(Ford.Value, route, 0.5f, _output, u => Ford.Value.Plan.Overlay(u));

        _output.WriteLine($"{wrong} wrong frames, worst {worst} px, mean {mean:0.0} items, most {most}");
        Assert.Equal(0, wrong);
    }

    [Fact]
    public void A_walk_across_hilly_ground_comes_out_as_true_painters_order()
    {
        var (ground, sheet) = Hilly.Value;
        var route = WorldRoute.Across(ground.Terrain, new Tile(80, 60), ClassDef.DefaultJump);

        var (wrong, worst, mean, most) = Sweep(sheet, route, 1f, _output, u => sheet.Plan.Overlay(u));

        _output.WriteLine($"{wrong} wrong frames, worst {worst} px, mean {mean:0.0} items, most {most}");
        Assert.Equal(0, wrong);
    }

    [Fact]
    public void Without_the_closure_the_walk_goes_wrong_so_this_test_can_fail()
    {
        var plan = Ford.Value.Plan;
        var route = WorldRoute.Across(FordGround.Value.Terrain, new Tile(30, 65), ClassDef.DefaultJump);

        var (wrong, _, _, _) = Sweep(Ford.Value, route, 1f, null, units =>
            [.. units.SelectMany(u => plan.Over(WorldPlan.AreaOf(u), u.Order)).Distinct().OrderBy(a => plan.Blocks[a.Block].Lit.Items[a.Item].Order).ThenBy(a => a.Block).ThenBy(a => a.Item)]);

        Assert.True(wrong > 0);
    }
}
