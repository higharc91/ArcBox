#:project ../../../../../games/Tactics/Tactics.Sim/Tactics.Sim.csproj
#:property PublishAot=false
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json;
using Tactics.Logic;
using Tactics.Sim;

var rules=RulesPack.Load([Path.GetFullPath(args[0])]);
var output=Path.GetFullPath(args[1]);
var unfilled=args.Skip(2).Contains("--unfilled");
var cases=(from encounter in new[]{"ford_assault","river_crossing"} from level in new[]{1,2,3}
           from players in new[]{1,2,3,4} from seed in Enumerable.Range(1,20) select(encounter,level,players,seed)).ToArray();
var rows=new ConcurrentBag<Row>();
Parallel.ForEach(cases,new ParallelOptions{MaxDegreeOfParallelism=4}, c=>
{
    var available=rules.Classes.Keys.Order(StringComparer.Ordinal).ToArray();
    var classes=Enumerable.Range(0,c.players).Select(i=>available[(c.seed-1+i*3)%available.Length]).ToList();
    // Same role-filling rule as TacticsGame.Companions: 1–4 session players
    // receive enough companions to meet the unchanged four-hero encounter minimum.
    while(classes.Count<rules.Encounters[c.encounter].Party.Min)
    {
        string[] roles=["healer","defender","striker","control"];
        var have=classes.Select(k=>rules.Classes[k].Role).ToArray();
        var role=roles.FirstOrDefault(r=>!have.Contains(r))??roles[have.Length%roles.Length];
        classes.Add(rules.Classes.Values.Where(k=>k.Role==role).Select(k=>k.Id).Order(StringComparer.Ordinal).FirstOrDefault()??available[0]);
    }
    var party=classes.Select((cls,i)=>new PartyMember(new UnitId("p"+i),
        SheetBuilder.DefaultCharacter("p"+i,"human",cls,null,c.level,rules),
        new AiControlled(i<c.players?SimRunner.ProfileFor(rules.Classes[cls]):"companion"))).ToImmutableArray();
    var setup=Encounters.Setup(c.encounter,party,Difficulty.Normal,rules);
    if(unfilled)
    {
        // Stress-only: retain the authored enemy setup and remove test companions.
        // Production encounters keep their four-hero minimum unchanged.
        var removed=party.Skip(c.players).Select(p=>p.Id).ToHashSet();
        setup=setup with {Units=[.. setup.Units.Where(u=>!removed.Contains(u.Id))]};
    }
    if(!setup.Environment)throw new InvalidDataException("Environment disabled");
    var state=Battle.Start(setup,rules,(ulong)c.seed).State;
    var rejected=0;var turns=0;
    while(state.Outcome is null&&state.Round<=30)
    {
        var unit=state.Active??throw new InvalidDataException("No active unit");
        bool ended=false;
        for(var retry=0;retry<8&&!ended&&state.Outcome is null;retry++)
        foreach(var command in Ai.Plan(state,unit,rules).Commands)
        {
            var result=Battle.Apply(state,command,rules);
            if(result.Rejected is not null){rejected++;break;}
            state=result.State;ended=result.Events.Any(e=>e is TurnEnded t&&t.Unit==unit);
            if(ended||state.Outcome is not null||command is Move move&&state.Units[unit].Pos!=move.Path[^1])break;
        }
        if(!ended&&state.Outcome is null)state=Battle.Apply(state,new EndTurn(unit),rules).State;
        if(++turns>3000)throw new InvalidDataException("Turn loop did not advance");
    }
    rows.Add(new(c.encounter,c.level,c.players,c.seed,string.Join(",",unfilled?classes.Take(c.players):classes),state.Outcome?.ToString()??"Capped",Math.Min(state.Round,30),rejected));
    if(rows.Count%40==0)Console.Error.WriteLine($"{rows.Count}/{cases.Length} battles");
});
var ordered=rows.OrderBy(r=>r.Encounter).ThenBy(r=>r.Level).ThenBy(r=>r.Players).ThenBy(r=>r.Seed).ToArray();
var summary=ordered.GroupBy(r=>(r.Encounter,r.Level,r.Players)).Select(g=>new {g.Key.Encounter,g.Key.Level,g.Key.Players,Battles=g.Count(),Wins=g.Count(r=>r.Outcome=="Won"),MeanRounds=g.Average(r=>r.Rounds),Caps=g.Count(r=>r.Outcome=="Capped")});
File.WriteAllText(output,JsonSerializer.Serialize(new{Environment=true,Difficulty="Normal",Seeds=20,RoundCap=30,CompanionFill=!unfilled,UnsupportedSmallPartyStress=unfilled,Summary=summary,Cases=ordered},new JsonSerializerOptions{WriteIndented=true}));
record Row(string Encounter,int Level,int Players,int Seed,string Classes,string Outcome,int Rounds,int Rejected);
