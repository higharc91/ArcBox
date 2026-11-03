using System.Collections.Immutable;
using Tactics.Game;
using Tactics.Logic;

namespace Tactics.Presentation.World;

/// <summary>Complete camp presentation on the authored ford when a pack root is supplied; a small test courtyard otherwise.</summary>
public static class CampDemo
{
    public static IReadOnlyList<ThingsDemoStep> Steps(string? packRoot = null, string campId = "af_road_campfire")
    {
        var basis = ThingsDemo.Steps()[0].View;
        var explore = basis.Explore!;
        var fire = new Tile(explore.At.X + 2, explore.At.Y);
        var source = "demo_ford_fire";
        RulesPack? rules = null;
        if (packRoot is not null)
        {
            rules = RulesPack.Load([Path.Combine(packRoot, "rules")], ArtManifests.Load(packRoot));
            var placed = rules.InteractablesIn("ashen_ford").Single(t => t.Id == campId);
            fire = placed.At; source = placed.Id;
            var at = rules.ReachFor("ashen_ford")!.Where(t => t != fire && Interactables.InReach(rules.GroundFor("ashen_ford")!.Terrain, t, placed)).Order().First();
            explore = explore with { Region = "ashen_ford", Name = "The Ashen Ford", At = at, Facing = fire, Legs = [] };
        }
        string[] names = ["Ana", "Bo", "Cy", "Dee", "Eli", "Fen"];
        string[] classes = ["fighter", "cleric", "bard", "ranger", "rogue", "wizard"];
        string[] jobs = ["tend", "cook", "rally", "watch", "rest", "rest"];
        explore = explore with { Party = [.. names.Select((name, i) => explore.Party[0] with { Id = "camp_" + i, Name = name, Sprite = "characters.class_" + classes[i], Here = true, Leads = i == 0 })] };
        var thing = ThingView.Of(new InteractableDef(source, "campfire", fire, Requires: [], Unless: []), new InteractableSave("lit"), new HashSet<string>())!;
        explore = explore with { Things = [thing], ThingEvents = [], Markers = [], Traders = null };
        basis = basis with { Banner = "Camp at the fire", Phase = nameof(TacticsPhase.Story), Explore = explore, Notice = null };
        var people = explore.Party.Select((p, i) => new CampPersonView(p.Id, p.Name, jobs[i], new Tile(fire.X - 2 + i % 3 * 2, fire.Y - 1 + i / 3 * 3), false,
            jobs[i] is "tend" or "rally" ? "camp_1" : null)).ToImmutableArray();
        if (rules is not null)
        {
            var ground = rules.GroundFor("ashen_ford")!.Terrain; var reach = rules.ReachFor("ashen_ford")!;
            var seats = RecoveryRules.Seats(ground, fire, reach, people.Length);
            people = [.. people.Select((p, i) => p with { At = seats[i] })];
        }
        var members = people.Select((p, i) => new RecoveryMemberView(p.Member, p.Name, classes[i], true, i == 1 ? 5 : 12, i == 1 ? 5 : 12,
            i == 0 ? [new("superiority", 2, 2), new("second_wind", 2, 2)] : i == 1 ? [new("devotion", 4, 4)] : [new(i == 5 ? "mana" : "focus", 3, 3)],
            i == 1 ? [new("black_eye", "Black eye", "rest", "Mends at dawn"), new("cracked_ribs", "Cracked ribs", "body", "Maximum HP −5"), new("shaken", "Shaken", "spirit", "Frightened at battle start")]
                : [new("black_eye", "Black eye", "rest", "Mends at dawn")], false, false)).ToImmutableArray();
        if (rules is not null)
            members = [.. members.Select(m =>
            {
                var character = SheetBuilder.DefaultCharacter(m.Name, "human", m.Class, null, 1, rules) with { Injuries = [.. m.Injuries.Select(i => new InjuryId(i.Id))] };
                var sheet = SheetBuilder.ForCharacter(character, rules);
                return m with { Hp = sheet.MaxHp, MaxHp = sheet.MaxHp, Resources = [.. sheet.Resources.Where(r => r.Value > 0).Select(r => new RecoveryResourceView(r.Key, r.Value, r.Value))] };
            })];
        var camp = new CampView("camp", source, "jobs", fire, 0, [.. people.Select(p => p with { Job = null })], null, 0, null, members);
        var steps = new List<ThingsDemoStep>();
        void Step(string label, double at, TacticsHostView view, double shot = 1) => steps.Add(new(label, at, view, [(shot, label)], false));
        Step("camp-gather", 0, basis with { Camp = camp }, 3);
        Step("camp-choosing", 2, basis with { Camp = camp with { People = [.. people.Select((p, i) => i < 3 ? p : p with { Job = null })] } });
        camp = camp with { People = people };
        Step("camp-jobs", 4, basis with { Camp = camp });
        for (var i = 0; i < people.Length; i++)
        {
            var actor = people[i]; var job = jobs[i];
            var result = new CampResult(actor.Member, job, job == "tend" ? "medicine" : job == "cook" ? "survival" : job == "rally" ? "persuasion" : "perception",
                job == "rest" ? 0 : 16, null, 2, job is "tend" or "rally" ? 12 : job == "cook" ? 10 : 0, job != "rest",
                job switch { "tend" => "Ana binds Bo's ribs. Cracked ribs mends.", "cook" => "Bo's pot is ready. Everyone is Well fed.", "rally" => "Cy's kind words steady Bo. Shaken mends.", "watch" => "Dee takes the first watch. The river keeps talking.", _ => actor.Name + " rests easy." },
                job is "tend" or "rally" ? "camp_1" : null, job == "tend" ? "cracked_ribs" : job == "rally" ? "shaken" : null);
            if (job == "tend") members = members.SetItem(1, members[1] with { Hp = members[1].Hp + 5, MaxHp = members[1].MaxHp + 5, Injuries = [.. members[1].Injuries.Where(x => x.Id != "cracked_ribs")] });
            if (job == "cook") members = [.. members.Select(m => m with { WellFed = true })];
            if (job == "rally") members = members.SetItem(1, members[1] with { Injuries = [.. members[1].Injuries.Where(x => x.Id != "shaken")] });
            camp = camp with { Stage = "results", Result = result, ResultCount = i + 1, Members = members, People = [.. people.Select((p, j) => p with { WellFed = i >= 1, Outcome = j <= i ? p.Job == "rest" ? "rested" : "success" : null })] };
            Step(job == "tend" ? "camp-mend" : job == "cook" ? "camp-well-fed" : "camp-" + job + "-" + i, 6 + i * 3, basis with { Camp = camp });
        }
        camp = camp with { Stage = "sleep" }; Step("camp-sleep", 25, basis with { Camp = camp });
        members = [.. members.Select(m => m with { Injuries = [.. m.Injuries.Where(x => x.Kind != "rest")] })];
        camp = camp with { Stage = "dawn", Members = members, Visit = 1 };
        Step("camp-dawn", 28, basis with { Camp = camp, Explore = explore with { Morning = true } }, 5);
        var walking = basis with { Phase = nameof(TacticsPhase.Exploration), Camp = null, Explore = explore with { Morning = true, Things = [thing with { State = "unlit" }] } };
        Step("camp-leave", 36, walking);
        var innCamp = camp with { Mode = "inn", Source = "demo_inn", Stage = "rooms", Result = null, ResultCount = 0, People = [.. people.Select(p => p with { Job = null, Target = null, Outcome = null })] };
        var inn = basis with { Camp = innCamp, Shop = new ShopView("demo_inn", "The Last Coal", "Innkeeper", "A bed and a bowl.", 100, 0, [], [], "characters.innkeeper",
            Services: [new ShopService("room", "A room for the night", "room", 10)], Members: members) };
        Step("inn-rooms", 38, inn);
        var paidMembers = members.SetItem(1, members[1] with { Room = true });
        inn = inn with { Camp = innCamp with { Members = paidMembers }, Shop = inn.Shop! with { Seq = 1, Members = paidMembers } };
        Step("inn-paid", 41, inn);
        Step("inn-sleep", 45, inn with { Camp = inn.Camp! with { Stage = "sleep" } });
        Step("inn-dawn", 48, inn with { Camp = inn.Camp! with { Stage = "dawn" } }, 5);
        Step("rest-leave", 56, walking);
        return steps;
    }
}
