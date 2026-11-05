#:project ../../../../../games/Tactics/Tactics.Presentation/Tactics.Presentation.csproj
#:property PublishAot=false
using System.Text.Json;
using Tactics.Logic;
using Tactics.Presentation.Board;
using Tactics.Presentation.World;

var beforeRoot=Path.GetFullPath(args[0]);var afterRoot=Path.GetFullPath(args[1]);
var before=RulesPack.Load([Path.Combine(beforeRoot,"rules")],ArtManifests.Load(beforeRoot));
var after=RulesPack.Load([Path.Combine(afterRoot,"rules")],ArtManifests.Load(afterRoot));
var art=ArtIndex.Load(afterRoot);
void Need(bool value,string message){if(!value)throw new InvalidDataException(message);}
foreach(var id in new[]{"river_crossing","ford_assault"})
{
    Need(JsonSerializer.Serialize(before.Encounters[id])==JsonSerializer.Serialize(after.Encounters[id]),"Encounter definition changed");
    var a=before.Maps[before.Encounters[id].Map];var b=after.Maps[after.Encounters[id].Map];
    for(var y=0;y<a.Height;y++)for(var x=0;x<a.Width;x++)
    {
        var p=new Tile(x,y);Need(a[p].Height==b[p].Height&&a[p].Flags==b[p].Flags,"Height/flags changed");
        Need(before.Environment.IsFuelMaterial(a.Materials[a[p].Material])==after.Environment.IsFuelMaterial(b.Materials[b[p].Material]),"Fuel changed");
    }
    var plan=BoardPlan.Build(new BoardTerrain(b,null),art);
    Need(plan.Items.Length>100,"Empty battle plan");
}
var oldWorld=before.GroundFor("ashen_ford")!;var world=after.GroundFor("ashen_ford")!;
var profiles=new[]{new HashSet<string>(),new HashSet<string>{"ch1_ford_settled","ch1_skarrow_slain"},new HashSet<string>{"ch1_ford_settled","ch1_skarrow_spared"}};
foreach(var flags in profiles)
{
    var a=oldWorld.With([],flags);var b=world.With([],flags);
    for(var y=0;y<world.Map.Height;y++)for(var x=0;x<world.Map.Width;x++)
    {
        var p=new Tile(x,y);Need(a.Walkable(p)==b.Walkable(p),"Walkability changed");
        Need(a.HeightAt(p)==b.HeightAt(p)&&a.Map[p].Flags==b.Map[p].Flags,"World height/flags changed");
        Need(before.Environment.IsFuelMaterial(a.Map.Materials[a.Map[p].Material])==after.Environment.IsFuelMaterial(b.Map.Materials[b.Map[p].Material]),"World fuel changed");
    }
    Need(BoardPlan.StoryProps(new BoardTerrain(world.Map,null),art,flags).Items.Length>0,"Missing story bridge");
}
var terrain=new BoardTerrain(world.Map,null);var worldPlan=WorldPlan.Build(terrain,art);
Need(worldPlan.Blocks.Length==80,"World blocks missing");
Need(art.Texture(new ArtRef("ash_ford","roots")) is {ClusteredEdges:true,Span:4},"Native edge profile missing");
Need(!ArtIndex.Load(beforeRoot).Texture(new ArtRef("ash_ford","charred"))!.ClusteredEdges,"Installed art unexpectedly opted in");
Console.WriteLine(JsonSerializer.Serialize(new{rulesLoaded=true,battlePlans=2,worldBlocks=80,bridgeStates=3,allWorldTilesMechanicallyEquivalent=true,installedGroundUnchanged=true}));
