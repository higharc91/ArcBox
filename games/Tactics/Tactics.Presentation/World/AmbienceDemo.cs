using Tactics.Game;
using Tactics.Logic;

namespace Tactics.Presentation.World;

/// <summary>Exercise the normal TV view/audio path across the Ford's places, rain variants and a return to dry weather.</summary>
public static class AmbienceDemo
{
    public static IReadOnlyList<ThingsDemoStep> Steps(string packRoot)
    {
        var basis = NpcsDemo.Steps(packRoot)[0].View;
        var explore = basis.Explore! with { QuestMarkers = null, Cast = null, Bark = null };
        var places = new (string Name, Tile At, string? Weather)[]
        {
            ("town", new(26, 58), null), ("river", new(100, 93), null), ("fields", new(38, 13), null),
            ("rain", new(26, 58), "rain"), ("storm", new(26, 58), "storm"), ("drizzle", new(26, 58), "drizzle"),
            ("clear", new(26, 58), null),
        };
        return places.Select((p, i) => new ThingsDemoStep(p.Name, i == 0 ? 0 : 24 + (i - 1) * 6,
            basis with { Phase = nameof(TacticsPhase.Exploration), Explore = explore with
            { Epoch = i + 1, At = p.At, Facing = p.At with { X = p.At.X + 1 }, Weather = p.Weather } },
            [(i == 0 ? 20 : 3, "ambience-" + p.Name)], false)).ToArray();
    }
}
