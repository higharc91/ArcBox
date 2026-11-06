using System.Numerics;
using System.Collections.Immutable;
using Tactics.Presentation.Board;
using Tactics.Logic;
namespace Tactics.Presentation.Tests.Board;
public sealed class RoadSurfaceTests
{
    private static BoardTerrain Ramp()
    {
        byte[] bytes=[0,0,0, 0,1,0, 1,1,0, 2,0,0];
        return new BoardTerrain(BattleMap.FromBytes("ramp",4,1,bytes,["cobble/grey","warden_ramp/west"],[],ImmutableSortedDictionary<string,ImmutableArray<Tile>>.Empty),null);
    }
    [Fact]
    public void Feet_follow_the_whole_west_grade_and_meet_the_road_and_deck_without_a_jump()
    {
        var t=Ramp();
        foreach(var x in new[]{1f,1.125f,1.5f,1.875f,2f,2.5f,2.9999f})
        {Assert.True(RoadSurface.TryHeight(t,new Vector2(x,.5f),out var height));Assert.Equal(x-1,height,3);}
        Assert.Equal(.5f,UnitPlace.On(t,new Tile(1,0)).Height);
        Assert.Equal(1.5f,UnitPlace.On(t,new Tile(2,0)).Height);
    }
    [Fact]
    public void Reverse_traversal_uses_the_same_surface_not_a_different_lerp_or_hop()
    {
        var t=Ramp();
        foreach(var x in new[]{2.9f,2.4f,1.9f,1.4f,1.01f})
        {Assert.True(RoadSurface.TryHeight(t,new Vector2(x,.5f),out var height));Assert.Equal(x-1,height,3);}
    }
    [Fact]
    public void Road_corner_meshes_share_their_boundary_and_have_no_step_at_the_join()
    {
        byte[] bytes=[0,0,0,1,1,0];
        var t=new BoardTerrain(BattleMap.FromBytes("road",2,1,bytes,
            ["road_ramp/q_0_2_2_0_d1","road_ramp/q_-2_0_0_-2_d1"],[],ImmutableSortedDictionary<string,ImmutableArray<Tile>>.Empty),null);
        foreach(var x in new[]{.01f,.25f,.5f,.999f,1f,1.001f,1.5f,1.99f})
        {Assert.True(RoadSurface.TryHeight(t,new Vector2(x,.5f),out var height));Assert.Equal(x*.5f,height,3);}
    }
    [Fact]
    public void Native_masonry_collision_does_not_raise_the_art_above_its_authored_foundation()
    {
        var map=BattleMap.FromBytes("wall",1,1,new byte[]{2,0,4},["warden_edge/stone"],[],ImmutableSortedDictionary<string,ImmutableArray<Tile>>.Empty);
        var t=new BoardTerrain(map,null);
        Assert.Equal(2,t.Top(new Tile(0,0)));
        Assert.False(t.Rules.Walkable(new Tile(0,0)));
    }
}
