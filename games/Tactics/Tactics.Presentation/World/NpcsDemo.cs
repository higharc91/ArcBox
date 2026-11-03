using System.Collections.Immutable;
using Tactics.Game;
using Tactics.Logic;

namespace Tactics.Presentation.World;

/// <summary>Real Ashen Ford, real lore and rendered voices, with deterministic journal and people-gate review shots.</summary>
public static class NpcsDemo
{
    public static IReadOnlyList<ThingsDemoStep> Steps(string packRoot)
    {
        var lore = LoreBook.Load(packRoot);
        var rules = RulesPack.Load([Path.Combine(packRoot,"rules")], ArtManifests.Load(packRoot));
        var region = rules.RegionFor("ashen_ford")!.Def;
        var start = DateTimeOffset.UtcNow;
        var steps = new List<ThingsDemoStep>();
        var basis = ThingsDemo.Steps()[0].View;
        var explore = basis.Explore! with { Region=region.Id,Name=region.Name,Chapter=1,Epoch=1,At=new Tile(29,61),
            Facing=new Tile(29,62),Things=null,ThingEvents=null,Traders=null,Markers=[],Cast=[],Plugs=null,
            QuestMarkers=[new(new Tile(29,62),false),new(new Tile(30,59),false)] };
        basis = basis with { Explore=explore,Notice=null };
        void Add(string label,double at,TacticsHostView view,params (double,string)[] shots)
            => steps.Add(new(label,at,view,[..shots],false));
        QuestEntryView Entry(QuestDef quest,string status,int index)
            => new(quest.Id,quest.Name,status,lore.Character(quest.Giver)!.Name,lore.Character(quest.Giver)!.Preset,
                quest.Stages[index].Objective,[..quest.Stages.Take(index+1).Select((s,i)=>new QuestHistoryView(s.Log,i<index||status!="active",s.Objective))],
                status=="done"?quest.Done.Log:null,status=="done"?new(quest.Rewards.Gold,quest.Rewards.Xp,[]):null);
        var grain=lore.Quests.Single(q=>q.Id=="sq_missing_grain");var forge=lore.Quests.Single(q=>q.Id=="sq_cold_forge");
        var journal=new QuestLogView([Entry(grain,"active",1),Entry(forge,"active",0)],grain.Id,grain.Id,true,"Ana");
        Add("npcs-give",0,basis,(12,"npcs-quest-givers"));
        var line=lore.Conversation("halden_varrow")!.Scene.Node("offer")!.Lines[0];
        var story=new StoryView(1,"talk_halden_varrow","Halden Varrow",TacticsGame.StoryStates.Lines,
            new(line.Id,line.Speaker,"Halden Varrow","mayor",line.Mood,line.Text,"ally"),null,null,start.AddSeconds(35),"npcs-demo");
        Add("npcs-talk",17,basis with {Phase=nameof(TacticsPhase.Story),Story=story,
            Explore=explore with {QuestMarkers=null,Cast=[new("halden_varrow","Halden Varrow","characters.mayor",new Tile(29,62),explore.At)]}},(2,"npcs-conversation"));
        var tracker=new QuestTrackerView(grain.Id,grain.Name,grain.Stages[1].Objective,"active",new Tile(92,102));
        Add("npcs-journal",34,basis with {Explore=explore with {QuestLog=journal,Pinned=tracker,QuestMarkers=null}},(1,"npcs-journal-spread"));
        Add("npcs-tracker",42,basis with {Explore=explore with {Pinned=tracker,QuestMarkers=null}},(1,"npcs-tracker-and-edge-arrow"));
        Add("npcs-waypoint",48,basis with {Explore=explore with {Epoch=2,At=new Tile(91,101),Facing=new Tile(92,102),Pinned=tracker,QuestMarkers=null}},(2,"npcs-waypoint"));
        var farmer=region.People.First(p=>p.Preset=="farmer");var bark=lore.Barks.For(farmer)!.Lines[0];
        Add("npcs-bark",55,basis with {Notice="The farmer: "+bark.Text,Explore=explore with {Epoch=3,At=new Tile(farmer.At.X,farmer.At.Y+1),Facing=farmer.At,
            Bark=new(farmer.Id!,1,start.AddSeconds(66),new(bark.Id,"townsfolk_farmer","The farmer",farmer.Preset,bark.Mood,bark.Text)),QuestMarkers=null}},(1,"npcs-bark"));
        Add("npcs-complete",67,basis with {Explore=explore with {Epoch=4,Pinned=tracker with {Status="done",Objective="Quest complete",Target=null},
            QuestMarkers=null,QuestEvent=new(1,grain.Id,grain.Name,"complete",start.AddSeconds(79),30,100)}},(1,"npcs-quest-complete"));
        Add("npcs-journal-done",73,basis with {Explore=explore with {QuestMarkers=null,QuestLog=journal with {Quests=[Entry(grain,"done",2),Entry(forge,"active",0)],Pinned=null}}},(1,"npcs-journal-complete"));
        var ford=explore with {Epoch=5,At=new Tile(99,64),Facing=new Tile(99,63),QuestMarkers=null};
        Add("npcs-ford-before",82,basis with {Explore=ford},(2,"npcs-ford-before"));
        Add("npcs-force-fade",88,basis with {Explore=ford with {PeopleFlags=["ch1_skarrow_slain"]}},(.15,"npcs-force-fading"),(1,"npcs-force-after"));
        var camp=explore with {Epoch=6,At=new Tile(134,25),Facing=new Tile(134,24),QuestMarkers=null};
        Add("npcs-camp-before",93,basis with {Explore=camp},(2,"npcs-camp-before"));
        Add("npcs-parley",99,basis with {Explore=camp with {PeopleFlags=["ch1_skarrow_spared"]}},(.15,"npcs-parley-fading"),(1,"npcs-parley-mother-stays"));
        return steps;
    }
}
