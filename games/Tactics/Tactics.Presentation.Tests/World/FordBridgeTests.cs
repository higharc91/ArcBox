using System.Collections.Immutable;
using System.Numerics;
using Tactics.Logic;
using Tactics.Presentation.Board;
using Tactics.Presentation.Tests.Board;
using static Tactics.Presentation.Tests.World.WalkFeed;

namespace Tactics.Presentation.Tests.World;

public sealed class FordBridgeTests
{
    [Fact]
    public void The_gate_opens_only_after_a_won_crossing_and_parapets_remain_blocked()
    {
        var g=Pack.Ground("ashen_ford");var at=new Tile(105,61);
        Assert.False(g.With([],new HashSet<string>{"ch1_skarrow_slain"}).Walkable(at));
        foreach(var branch in new[]{"ch1_skarrow_slain","ch1_skarrow_spared"})
        {
            var ground=g.With([],new HashSet<string>{branch,"ch1_ford_settled"});
            foreach(var y in new[]{61,62})foreach(var x in new[]{105,106,107})Assert.True(ground.Walkable(new Tile(x,y)));
            foreach(var y in new[]{60,63})foreach(var x in new[]{105,106,107})Assert.False(ground.Walkable(new Tile(x,y)));
            Assert.True(ground.Walkable(new Tile(108,59))); // shallows' east landing, beside gatehouse
        }
    }
    [Fact]
    public void Only_the_matching_outcome_is_drawn_and_permanent_road_art_is_not_duplicated()
    {
        var t=new BoardTerrain(Pack.Ground("ashen_ford").Map,null);
        foreach(var (flag,name) in new[]{("ch1_skarrow_slain","force"),("ch1_skarrow_spared","parley")})
        {
            var plan=BoardPlan.StoryProps(t,Pack.Art,new HashSet<string>{flag,"ch1_ford_settled"});
            var paths=plan.Items.OfType<Sprite>().Select(s=>s.Image.Path).ToArray();
            Assert.NotEmpty(paths);Assert.All(paths,p=>Assert.Contains("warden_"+name+"_",p));
        }
    }
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void Real_walking_formation_follows_the_native_surface_every_frame_in_both_directions(int party)
    {
        var flags=new HashSet<string>{"ch1_ford_settled","ch1_skarrow_spared"};
        var t=new BoardTerrain(Pack.Ground("ashen_ford").Map.WithStoryFlags(flags),null);
        var script=new Script(new Tile(102,61),party);var track=new Tactics.Presentation.World.WalkTrack();
        track.Receive(script.Now(false));Run(track,t,1);
        var samples=0;
        foreach(var dir in new[]{1,-1})
        for(var step=0;step<8;step++)
        {
            script.Step(dir,0);track.Receive(script.Now(true));
            for(var frame=0;frame<40;frame++)
            {
                track.Advance(.3/40);
                foreach(var place in track.Places(t).Values)
                {
                    var cell=new Tile((int)MathF.Floor(place.Map.X),(int)MathF.Floor(place.Map.Y));
                    var material=t.Map.Materials[t.Map[cell].Material];
                    if(material=="warden_ramp/west")
                    {Assert.Equal(place.Map.X-102,place.Height,3);samples++;}
                    else if(material.StartsWith("warden_ramp/east_",StringComparison.Ordinal))
                    {Assert.Equal(3-(place.Map.X-108)*.5f,place.Height,3);samples++;}
                }
            }
        }
        Assert.True(samples>30,$"Only {samples} ramp frames checked");
    }
    [Fact]
    public void Every_generic_road_ramp_meets_its_neighbours_at_the_same_height()
    {
        var t=new BoardTerrain(Pack.Ground("ashen_ford").Map,null);var checkedEdges=0;
        foreach(var tile in t.Tiles)
        {
            if(!t.Map.Materials[t.Map[tile].Material].StartsWith("road_ramp/",StringComparison.Ordinal))continue;
            foreach(var delta in new[]{new Tile(1,0),new Tile(0,1)})
            {
                var next=new Tile(tile.X+delta.X,tile.Y+delta.Y);
                if(!t.Map.InBounds(next))continue;
                var nextMaterial=t.Map.Materials[t.Map[next].Material];
                if(!nextMaterial.StartsWith("road_ramp/",StringComparison.Ordinal))continue;
                foreach(var share in new[]{.05f,.25f,.5f,.75f,.95f})
                {
                    var a=delta.X==1?new Vector2(tile.X+1-.0001f,tile.Y+share):new Vector2(tile.X+share,tile.Y+1-.0001f);
                    var b=a+new Vector2(delta.X*.0002f,delta.Y*.0002f);
                    Assert.True(RoadSurface.TryHeight(t,a,out var ha));Assert.True(RoadSurface.TryHeight(t,b,out var hb));Assert.InRange(Math.Abs(ha-hb),0,.001f);checkedEdges++;
                }
            }
        }
        Assert.True(checkedEdges>20);
    }
}
