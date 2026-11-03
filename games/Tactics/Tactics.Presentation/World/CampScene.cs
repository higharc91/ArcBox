using System.Collections.Immutable;
using Tactics.Game;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.World;

/// <summary>The camp's visible phase, derived from saved choices/results rather than a fabricated progress timer.</summary>
public static class CampScene
{
    public static bool Active(CampView? camp) => camp is { Stage: not "vote" };
    public static string Phase(CampView camp) => camp.Stage switch
    { "jobs" => "Camp jobs", "rooms" => "Taking rooms", "results" => "Helping each other", "talk" => "Fireside", "sleep" => "A quiet night", "dawn" => "Sunrise", _ => "Make camp" };
    public static string Progress(CampView camp) => camp.Stage switch
    {
        "jobs" => $"{camp.People.Count(p => p.Job is not null)} / {camp.People.Length} jobs chosen",
        "results" => $"{camp.ResultCount} / {camp.People.Length} jobs resolved",
        "rooms" => $"{camp.Members?.Count(m => m.Room) ?? 0} rooms booked",
        "sleep" => "The party settles down to sleep",
        "dawn" => "Wounds tended · A new day begins",
        "talk" => "A story by the fire", _ => "The party decides",
    };
    public static string Theme(CampView camp) => camp.Mode == "inn" ? "hearth" : camp.Source.Contains("ford", StringComparison.Ordinal) ? "memorial" : "road";
    public static string Animation(CampView camp, CampPersonView person, double elapsed) => camp.Stage == "jobs" && elapsed < 1.8 ? "walk" : camp.Stage switch
    {
        "sleep" => "doze",
        "dawn" => elapsed < 1.2 ? "doze" : elapsed < 2.2 ? "rise" : "idle",
        "talk" => "talk",
        "results" when person.Outcome is not null && camp.Result?.Member != person.Member => "sit",
        _ => person.Job switch { "cook" => "item", "tend" => "kneel", "rally" => "talk", "watch" => "ready", _ => "sit" },
    };

    /// <summary>Use the pack's actual class animations; standing combat rings and crowns are quiet at camp.</summary>
    public static ImmutableArray<BoardUnit> Pose(ImmutableArray<BoardUnit> units, CampView? camp, double elapsed)
    {
        if (camp is not { Mode: "camp", Stage: not "vote" }) return units;
        return [.. units.Select(unit =>
        {
            var person = camp.People.FirstOrDefault(p => p.Member == unit.Id.Value);
            if (person is null) return unit;
            var animation = unit.Sprite.Anim(Animation(camp, person, elapsed)) with { Loop = true };
            var target = camp.People.FirstOrDefault(p => p.Member == person.Target)?.At ?? camp.At;
            var du = target.X - person.At.X; var dv = target.Y - person.At.Y;
            return unit with { Anim = animation, Ring = 0, CrownAt = null, Outlined = false,
                Row = du + dv >= 0 ? 0 : 1, Mirror = du + dv >= 0 ? du - dv < 0 : du - dv > 0 };
        })];
    }
}
