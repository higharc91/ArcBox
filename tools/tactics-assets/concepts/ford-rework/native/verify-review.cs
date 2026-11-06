#:project ../../../../../games/Tactics/Tactics.Presentation/Tactics.Presentation.csproj
#:property PublishAot=false
using System.Text.Json;
using Tactics.Logic;
using Tactics.Presentation.Board;
using Tactics.Presentation.World;

var stage=Path.GetFullPath(args.Single());
var results=new List<object>();
foreach(var state in new[]{"fortified","force","parley"})
{
    var root=Path.Combine(stage,state,"content/tactics/base");
    var rules=RulesPack.Load([Path.Combine(root,"rules")],ArtManifests.Load(root));
    var ground=rules.GroundFor("ashen_ford")!;
    var art=ArtIndex.Load(root);
    var plan=WorldPlan.Build(new BoardTerrain(ground.Map,null),art);
    var battle=rules.MapFor("river_crossing")!;
    var board=BoardPlan.Build(new BoardTerrain(battle,null),art);
    Need(plan.Blocks.Length>0,"Empty world plan");Need(board.Items.Length>0,"Empty battle plan");
    var west=new Tile(104,48);var east=new Tile(109,48);
    Need(WorldWalk.Reach(ground.Terrain,west,ClassDef.DefaultJump).Contains(east),"Ford no longer reachable");
    Need(!WorldWalk.Reach(ground.Terrain,west,ClassDef.DefaultJump,ground.Map.StampedTiles).Contains(east),"Second river crossing introduced");
    Need(!WorldWalk.Reach(ground.Terrain,east,ClassDef.DefaultJump,ground.Map.StampedTiles).Contains(west),"Second reverse crossing introduced");
    foreach(var x in Enumerable.Range(105,4))
        Need(ground.Terrain.Deep(new Tile(x,48))&&!ground.Terrain.Walkable(new Tile(x,48)),"Northern bridge reopened");
    foreach(var y in new[]{9,10})foreach(var x in new[]{5,6,7})
    {
        var tile=new Tile(100+x,52+y);
        Need(ground.Terrain.Walkable(tile),"Review deck blocked");
        Need(ground.Map.Materials[ground.Map[tile].Material]=="planks_ford/oak","Review deck material missing");
        Need(ground.Map[tile].Height==battle[new Tile(x,y)].Height+1,"Stamp height mismatch");
    }
    foreach(var x in new[]{6,7})Need(battle[new Tile(x,7)].Flags.HasFlag(TileFlags.Water),"Shallow bypass removed");
    foreach(var x in Enumerable.Range(99,15).Where(x=>x<105||x>=108))
        Need(ground.Map.Materials[ground.Map[new Tile(x,61)].Material]=="cobble/grey","Road approach disconnected");
    foreach(var y in Enumerable.Range(57,6))foreach(var x in new[]{112,113})
        Need(ground.Map.Materials[ground.Map[new Tile(x,y)].Material]=="cobble/grey","East road does not join existing exit road");
    Need(WorldWalk.Reach(ground.Terrain,new Tile(99,61),ClassDef.DefaultJump).Contains(new Tile(114,61)),"Approach path not traversable");
    results.Add(new {state,packLoaded=true,worldBlocks=plan.Blocks.Length,battleItems=board.Items.Length,onlyFordCrosses=true,northernRuinsUnwalkable=true,deckTiles=6,shallowRowPreserved=true,approachesConnected=true,productionSortingVerified=false});
}
Console.WriteLine(JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
static void Need(bool condition,string message){if(!condition)throw new InvalidDataException(message);}
