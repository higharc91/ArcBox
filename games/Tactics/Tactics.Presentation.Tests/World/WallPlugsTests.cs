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
        Assert.All(steps.Take(found), s => Assert.Equal([Gap], s.View.Explore!.Plugs!.Value));
        Assert.All(steps.Skip(found), s => Assert.Empty(s.View.Explore!.Plugs!.Value));
    }
}
