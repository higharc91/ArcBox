using System.Numerics;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Board;
using Tactics.Presentation.Tests.Board;
using Tactics.Presentation.World;

namespace Tactics.Presentation.Tests.World;

/// <summary>
/// <see cref="WallPlugs"/>: a secret door nobody has found is drawn as the wall it stands in, so its gap gives nothing away (interactables design
/// §2.9), with the art check's courtyard, whose secret door stands in a wall of rock.
/// </summary>
public sealed class WallPlugsTests
{
    private static readonly Lazy<WorldGround> Ground = new(ThingsDemo.Ground);
    private static readonly Lazy<WorldPlan> Plan = new(() => WorldPlan.Build(new BoardTerrain(Ground.Value.Map, null), Pack.Art));

    private static WallPlug Gap => new(ThingsDemo.Steps()[^1].View.Explore!.Things!.Value.Single(t => t.Id == "secret").At, "sw");

    [Fact]
    public void A_plug_is_its_walls_own_tile()
    {
        var map = Ground.Value.Map;
        var gap = Gap;
        Assert.True(map[gap.At].Height < map[new Tile(gap.At.X + 1, gap.At.Y)].Height);         // painted open
        Assert.Equal(map[new Tile(gap.At.X + 1, gap.At.Y)], WallPlugs.WallFor(map, gap));
        Assert.Equal(map[new Tile(gap.At.X, gap.At.Y - 1)], WallPlugs.WallFor(map, gap with { Facing = "nw" }));   // along y: the ground beside it
        var plugged = WallPlugs.With(map, [gap]);
        Assert.Equal(map[new Tile(gap.At.X + 1, gap.At.Y)], plugged[gap.At]);
        Assert.Equal(map[new Tile(5, 5)], plugged[new Tile(5, 5)]);
    }

    [Fact]
    public void A_plug_draws_as_the_wall_beside_it_drawn_one_tile_over()
    {
        var gap = Gap;
        var items = WallPlugs.Items(Plan.Value, Pack.Art, [gap]);
        Assert.NotEmpty(items);
        Assert.Equal(items.Select(i => i.Order).Order(), items.Select(i => i.Order));

        // The wall tile beside it, as the world draws it, moved one tile along x onto the gap: the same faces, the same art, the same places.
        var neighbour = new Tile(gap.At.X + 1, gap.At.Y);
        var wall = LitBoard.Build(BoardPlan.BuildBlock(new BoardTerrain(Ground.Value.Map, null), Pack.Art, new TileRect(neighbour.X, neighbour.Y, 1, 1), grid: false),
            Plan.Value.Lighting).Items;
        var step = BoardProjection.Corner(gap.At.X, gap.At.Y, 0) - BoardProjection.Corner(neighbour.X, neighbour.Y, 0);
        var top = items.OfType<LitFace>().Single(f => f.Face.Kind == FaceKind.Top).Face;
        var wallTop = wall.OfType<LitFace>().Single(f => f.Face.Kind == FaceKind.Top).Face;
        Assert.Equal(wallTop.Image!.Path, top.Image!.Path);
        Assert.Equal(wallTop.Points.Select(p => p + step), top.Points);
        // Its front faces come down to the floor in front, as the wall's do.
        Assert.Contains(items.OfType<LitFace>(), f => f.Face.Kind == FaceKind.Side && f.Face.Facing == Facing.V);
    }

    /// <summary>The hidden path's plug in the courtyard's hedge, one step high (design §7.8), and the secret door's in its wall of rock.</summary>
    public static TheoryData<string> Plugged => ["path", "secret"];

    private static WallPlug PlugOf(string which) => which == "secret"
        ? Gap
        : ThingsDemo.Steps().First(s => s.Label.StartsWith("path", StringComparison.Ordinal)).View.Explore!.Plugs!.Value.First(p => p.At != Gap.At);

    /// <summary>
    /// The picture of a plugged tile against the wall tile beside it (design §7.8's seam): every pixel of the plug's tile is the plug's, as the world draws, the
    /// open ground the region has there is never painted back over it, and its top is the colour of its neighbour's, in the world's grade.
    /// </summary>
    [Theory]
    [MemberData(nameof(Plugged))]
    public void A_plugged_tile_is_drawn_like_the_wall_beside_it(string which)
    {
        var plug = PlugOf(which);
        var plan = Plan.Value;
        var items = WallPlugs.Items(plan, Pack.Art, [plug]);

        // Nothing of the open tile goes back over the plug: its top, later in the order than the plug's sides, did, and showed as a notch.
        var over = plan.Overlay([], extra: items);
        var onTile = over.Select(at => plan.Blocks[at.Block].Lit.Items[at.Item]).OfType<LitFace>().Where(f => f.Face.Tile == plug.At).ToArray();
        Assert.True(onTile.Length == 0, $"{onTile.Length} of the open tile's faces painted over the plug: {string.Join(", ", onTile.Select(f => f.Face.Kind))}");

        // The plug's front faces are the wall's: the same art, lip and depth as its neighbour along the run.
        var neighbour = plug.Facing is "se" or "nw" ? plug.At with { Y = plug.At.Y + 1 } : plug.At with { X = plug.At.X + 1 };
        if (Ground.Value.Map[neighbour].Height != WallPlugs.WallFor(Ground.Value.Map, plug).Height)
            neighbour = plug.Facing is "se" or "nw" ? plug.At with { Y = plug.At.Y - 1 } : plug.At with { X = plug.At.X - 1 };
        var world = plan.Blocks.SelectMany(b => b.Lit.Items).OfType<LitFace>().Where(f => f.Face.Tile == neighbour).ToArray();
        foreach (var kind in new[] { FaceKind.Top, FaceKind.Side, FaceKind.Lip })
        {
            var (mine, theirs) = (items.OfType<LitFace>().Where(f => f.Face.Kind == kind && f.Face.Facing != Facing.U).ToArray(), world.Where(f => f.Face.Kind == kind && f.Face.Facing != Facing.U).ToArray());
            Assert.Equal(theirs.Select(f => f.Face.Image?.Path).Distinct(), mine.Select(f => f.Face.Image?.Path).Distinct());
            if (theirs.Length == 0)
                continue;     // rock has no lip
            Assert.Equal(theirs.Sum(f => BoardPlan.Bounds(f.Face).Max.Y - BoardPlan.Bounds(f.Face).Min.Y), mine.Sum(f => BoardPlan.Bounds(f.Face).Max.Y - BoardPlan.Bounds(f.Face).Min.Y), 0.01);
            // In the world's grade and drift: the plug took neither, and stood out lighter than the wall it's in.
            for (var corner = 0; corner < 4; corner++)
            {
                var (a, b) = (mine.First().Face.TintAt(corner), theirs.First().Face.TintAt(corner));
                Assert.True(Math.Abs(a.R - b.R) < 0.04f && Math.Abs(a.G - b.G) < 0.04f && Math.Abs(a.B - b.B) < 0.04f, $"{kind} corner {corner}: {a} against the wall's {b}");
            }
        }
    }

    [Fact]
    public void No_plugs_or_plugs_off_the_map_draw_nothing()
    {
        Assert.Empty(WallPlugs.Items(Plan.Value, Pack.Art, []));
        Assert.Empty(WallPlugs.Items(Plan.Value, Pack.Art, [new WallPlug(new Tile(500, 500), "sw")]));
    }

    [Fact]
    public void The_demo_sends_a_plug_for_its_secret_door_until_its_found_and_no_more()
    {
        var steps = ThingsDemo.Steps();
        var found = steps.ToList().FindIndex(s => s.Label == "secret-found");
        // Only the secret door's gap: the hidden path's plug (design §7.3) is ThingsDemoTests'.
        Assert.All(steps.Take(found), s => Assert.Equal([Gap], s.View.Explore!.Plugs!.Value.Where(p => p.At == Gap.At)));
        Assert.All(steps.Skip(found), s => Assert.DoesNotContain(s.View.Explore!.Plugs!.Value, p => p.At == Gap.At));
    }
}
