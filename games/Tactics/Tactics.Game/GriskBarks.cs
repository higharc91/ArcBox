using System.Collections.Immutable;
using Tactics.Logic;

namespace Tactics.Game;

/// <summary>Grisk's authored battle barks. Cosmetic event selection never rolls or alters the battle state.</summary>
internal static class GriskBarks
{
    public static ImmutableArray<BattleEvent> Enrich(BattleState state, ImmutableArray<BattleEvent> events, BarkBook book, int beat)
    {
        var grisk = state.Units.Values.FirstOrDefault(u => u.Sheet.Tags.Contains("grisk"));
        if (grisk is null) return events;
        var defeated = events.Any(e => e is Died died && died.Unit == grisk.Id);
        if (!defeated && grisk.Life != LifeState.Up) return events;
        var moment = defeated ? "defeat"
            : events.OfType<Damaged>().Any(d => d.Unit == grisk.Id && d.HpAfter > 0 && d.HpAfter * 4 < grisk.Sheet.MaxHp && (d.HpAfter + d.Amount) * 4 >= grisk.Sheet.MaxHp) ? "low_hp"
            : events.OfType<Died>().Any(d => d.Unit != grisk.Id && state.Units.TryGetValue(d.Unit, out var ally) && ally.Sheet.Side == grisk.Sheet.Side && ally.Sheet.Tags.Contains("kobold")) ? "ally_down"
            : events.OfType<AbilityUsed>().Any(a => a.Source == grisk.Id && a.Ability.Value == "grisk.dig_faster") ? "turn_start"
            : events.OfType<Damaged>().Any(d => d.Unit == grisk.Id && d.HpAfter > 0) ? "hit"
            : events.OfType<RoundStarted>().Any(r => r.Round == 1) ? "battle_start" : null;
        return moment is not null && book.ForBattle("grisk", moment)?.PickWeighted(state.Flags, beat) is { } line
            ? events.Add(new Said("grisk", line.Text)) : events;
    }
}
