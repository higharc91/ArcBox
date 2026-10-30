using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace Tactics.Logic;

internal sealed partial class Planner
{
    private TurnPlan Finish(Candidate chosen, ImmutableArray<Command> commands, List<Candidate> all)
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"{_me.Id} ({_p.Id}{(_flee ? ", fleeing" : "")}): ");
        text.Append(string.Join("; ", commands.Select(Describe)));
        text.Append(CultureInfo.InvariantCulture,
            $" [score {chosen.Score:0.###}: action {chosen.ActionValue:0.###}, end tile {chosen.EndPosition:0.###}]");
        foreach (var other in all.Where(c => !ReferenceEquals(c, chosen)).Take(3))
            text.Append(CultureInfo.InvariantCulture,
                $"\n  also {other.Kind} {other.Usable?.Ability.Id.Value ?? "-"} {other.TargetKey} from {other.ActTile} to {other.End}: {other.Score:0.###}");
        return new TurnPlan(commands, chosen.Score, text.ToString());
    }

    private static string Describe(Command command) => command switch
    {
        Move m => $"move {m.Path[0]}→{m.Path[^1]}",
        Use { Item: { } item } u => $"use {item.Value} on {Describe(u.Target)}",
        Use u => $"{u.Ability.Value} on {Describe(u.Target)}",
        EndTurn => "end turn",
        _ => command.GetType().Name,
    };

    private static string Describe(Target target) => target.Unit?.Value ?? target.Tile?.ToString() ?? "self";
}
