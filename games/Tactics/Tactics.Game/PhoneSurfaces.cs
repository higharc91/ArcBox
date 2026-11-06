using System.Collections.Immutable;
using System.Text.Json;
using ArcBox.Sdk;
using Tactics.Logic;

namespace Tactics.Game;

/// <summary>Only the player's current tile, never a cursor preview or an inferred cached position.</summary>
public static class PhoneSurfaces
{
    public static ImmutableArray<string> At(BattleState state, UnitState unit, RulesPack rules)
    {
        if (!state.Environment || unit.Life is LifeState.Dead or LifeState.Out)
            return [];
        var kinds = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var zone in state.Zones.Where(z => z.Tiles.Contains(unit.Pos)))
        {
            var kind = zone.Kind switch
            {
                "fire" or "burning" => "burning",
                "ice" or "ice_sheet" => "ice_sheet",
                "grease" or "oil" => "oil",
                "web" => "web",
                "scorched" => "scorched",
                _ => null,
            };
            if (kind is not null)
                kinds.Add(kind);
        }
        if (!kinds.Contains("ice_sheet") && Surfaces.Water(new BattleWriter(state, rules), unit.Pos))
            kinds.Add("water");
        return [.. kinds];
    }

    public static CustomView Attach(CustomView view, BattleState state, UnitState unit, RulesPack rules)
    {
        var kinds = At(state, unit, rules);
        if (kinds.IsEmpty)
            return view;
        var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(view.Data, ArcJson.Options)!;
        data["surfaces"] = JsonSerializer.SerializeToElement(kinds, ArcJson.Options);
        return view with { Data = JsonSerializer.SerializeToElement(data, ArcJson.Options) };
    }
}
