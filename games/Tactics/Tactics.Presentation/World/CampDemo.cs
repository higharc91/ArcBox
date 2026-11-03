using System.Collections.Immutable;
using Tactics.Game;
using Tactics.Logic;

namespace Tactics.Presentation.World;

/// <summary>Scripted normal host views for the fire, recovery, cooking and dawn. Uses existing art only.</summary>
public static class CampDemo
{
    /// <summary>The real view contract displayed in a small existing courtyard.</summary>
    public static IReadOnlyList<ThingsDemoStep> Steps()
    {
        var basis = ThingsDemo.Steps()[0].View;
        var explore = basis.Explore!;
        var fire = new Tile(explore.At.X + 2, explore.At.Y);
        var people = explore.Party.Select((p, i) => new CampPersonView(p.Id, p.Name, i == 0 ? "tend" : i == 1 ? "cook" : "watch", new Tile(fire.X - 1 + i % 2 * 2, fire.Y + i / 2), false)).ToImmutableArray();
        var thing = ThingView.Of(new InteractableDef("demo_fire", "campfire", fire, Requires: [], Unless: []), new InteractableSave("lit"), new HashSet<string>())!;
        explore = explore with { Name = "Camp at the fire", Things = [thing], ThingEvents = [], Markers = [], Traders = null };
        basis = basis with { Banner = "Camp at the fire", Phase = nameof(TacticsPhase.Story), Explore = explore, Notice = null };
        var members = people.Select((p, i) => new RecoveryMemberView(p.Member, p.Name, i == 0 ? "fighter" : "cleric", true, i == 0 ? 12 : 9, i == 0 ? 12 : 9,
            i == 0 ? [new("superiority", 2, 2), new("second_wind", 2, 2)] : [new("devotion", 4, 4)],
            [new("black_eye", "Black eye", "rest", "Mends at dawn"), new("cracked_ribs", "Cracked ribs", "body", "Maximum HP −5")], false, false)).ToImmutableArray();
        var camp = new CampView("camp", "demo_fire", "jobs", fire, 0, people, null, 0, null, members);
        var jobs = basis with { Camp = camp };
        var first = people[0];
        var patient = people.Length > 1 ? people[1] : first;
        var mend = jobs with { Camp = camp with { Stage = "results", ResultCount = 1,
            Result = new CampResult(first.Member, "tend", "medicine", 15, 18, 3, 12, true, $"{first.Name} binds {patient.Name}'s ribs. Cracked ribs mends.", patient.Member, "cracked_ribs") } };
        mend = mend with { Camp = mend.Camp! with { Members = members.Select(m => m.Member == patient.Member ? m with { Hp = m.Hp + 5, MaxHp = m.MaxHp + 5, Injuries = [.. m.Injuries.Where(i => i.Id != "cracked_ribs")] } : m).ToImmutableArray() } };
        var fed = mend with { Camp = camp with { Stage = "results", ResultCount = 2, Members = [.. mend.Camp!.Members!.Value.Select(m => m with { WellFed = true })], People = [.. people.Select(p => p with { WellFed = true })],
            Result = new CampResult(patient.Member, "cook", "survival", 16, null, 2, 10, true, $"{patient.Name} cooks a warming meal. Everyone is Well fed.") } };
        var dawn = fed with { Camp = fed.Camp! with { Stage = "dawn", Members = [.. fed.Camp.Members!.Value.Select(m => m with { Injuries = [.. m.Injuries.Where(i => i.Kind != "rest")] })] } };
        var guests = dawn.Camp!.Members!.Value.AddRange(new[] { "Cy", "Dee", "Eli", "Fen" }.Select((name, i) => members[0] with { Member = "away_" + i, Name = name, Here = false }));
        var innCamp = camp with { Mode = "inn", Source = "demo_inn", Stage = "rooms", Members = guests, Visit = 1 };
        var inn = dawn with { Camp = innCamp, Shop = new ShopView("demo_inn", "The Last Coal", "Innkeeper", "A bed and a bowl.", 100, 0, [], [], "characters.innkeeper",
            Services: [new ShopService("room", "A room for the night", "room", 10)], Members: guests) };
        var paidGuests = guests.SetItem(1, guests[1] with { Room = true });
        var paid = inn with { Camp = innCamp with { Members = paidGuests }, Shop = inn.Shop! with { Seq = 1, Members = paidGuests, Feed = ["Ana booked a room for Bo · 10 gold"] } };
        return [new("camp-gather", 0, jobs, [(3, "camp-gather")], false),
            new("camp-mend", 6, mend, [(.3, "camp-mend")], false),
            new("camp-well-fed", 10, fed, [(1, "camp-well-fed")], false),
            new("camp-dawn", 14, dawn, [(1.2, "camp-dawn")], false),
            new("camp-leave", 17, basis with { Phase = nameof(TacticsPhase.Exploration), Camp = null }, [(1, "camp-leave")], false),
            new("inn-rooms", 19, inn, [(1, "inn-rooms")], false),
            new("inn-paid", 23, paid, [(1, "inn-paid")], false),
            new("rest-leave", 27, basis with { Phase = nameof(TacticsPhase.Exploration), Camp = null }, [(1, "rest-leave")], false)];
    }
}
