using System.Collections.Immutable;
using Tactics.Game;
using Tactics.Logic;
namespace Tactics.Presentation.Beats;

public sealed record CombatReadout(string Heading, ImmutableArray<string> Rolls, string? Effect)
{
    public static CombatReadout? Of(IEnumerable<CombatCue> cues, BattleView? shown, RulesPack? rules)
    {
        var active = cues.ToArray();
        if (active.Length == 0) return null;
        string Name(UnitId unit) => shown?.Units.FirstOrDefault(u => u.Id == unit)?.Name ?? unit.Value;
        var last = active.LastOrDefault(c => c.Effect is null);
        var heading = last is null ? "Effects" : Name(last.Actor) + " · " + (last.Ability is { } ability
            ? rules?.Abilities.GetValueOrDefault(ability)?.Name ?? Words.Title(ability.Value.Split('.')[^1])
            : last.Kind == RollKind.Attack ? "Attack" : "Saving throw");
        var rows = last is null ? [] : active.Where(c => c.Effect is null && c.Actor == last.Actor && c.Ability == last.Ability && c.Roll is not null)
            .Select(c => Line(c, Name)).ToImmutableArray();
        var effects = active.Where(c => c.Effect is not null).Select(c => Name(c.Actor) + ": " + c.Effect).Distinct().ToArray();
        return new(heading, rows, effects.Length == 0 ? null : string.Join(" · ", effects));
    }

    public static string ConditionName(ConditionId id, RulesPack? rules)
        => rules?.Conditions.GetValueOrDefault(id)?.Name
           ?? (id.Value.StartsWith("statMod.", StringComparison.Ordinal)
               ? rules?.Abilities.GetValueOrDefault(new AbilityId(id.Value[8..]))?.Name : null)
           ?? Words.Title(id.Value.Split('.')[^1]);

    public static string Line(CombatCue cue, Func<UnitId, string> name)
    {
        var roll = cue.Roll!;
        var result = cue.Kind == RollKind.Attack ? roll.Crit ? "CRIT" : roll.Success ? "HIT" : "MISS" : roll.Success ? "SAVED" : "FAILED";
        var dice = roll.D20s.Length > 1 ? $"[{string.Join(",", roll.D20s)}] keep {roll.Kept}" : roll.Kept.ToString();
        var total = $"{dice} {(roll.Modifier < 0 ? "−" : "+")}{Math.Abs(roll.Modifier)} = {roll.Total}";
        return $"{(cue.Target is { } target ? name(target) + ": " : "")}{total} vs {(cue.Kind == RollKind.Attack ? "AC" : "DC")} {roll.Vs} {result}";
    }
}
