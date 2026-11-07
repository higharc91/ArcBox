using System.Globalization;
using System.Collections.Immutable;
using Tactics.Logic;

namespace Tactics.Game;

/// <summary>Player-facing reaction names and costs, shared by the phone and TV.</summary>
public static class ReactionWords
{
    public const string PassLabel = "Don't react";

    public static string Label(ReactionChoice choice, RulesPack rules)
        => choice.Ability is { } ability ? rules.Abilities.GetValueOrDefault(ability)?.Name ?? ability.Value : PassLabel;

    public static string Cost(ReactionChoice choice, UnitState reactor, RulesPack rules)
    {
        if (choice.Id == Reactions.Pass) return "Costs nothing.";
        var cost = choice.Id == Reactions.Opportunity ? new CostDef(Reaction: 1)
            : choice.Ability is { } id && rules.Abilities.TryGetValue(id, out var ability) ? ability.Cost : new CostDef();
        var parts = new List<string>();
        if (cost.Reaction > 0) parts.Add($"{cost.Reaction} Reaction");
        if (cost.Action > 0) parts.Add($"{cost.Action} Action");
        if (cost.Bonus > 0) parts.Add($"{cost.Bonus} Bonus action");
        if (cost.Move > 0) parts.Add($"{cost.Move} Move");
        foreach (var (resource, amount) in cost.Resource ?? ImmutableSortedDictionary<string, int>.Empty)
            parts.Add($"{amount} {CultureInfo.InvariantCulture.TextInfo.ToTitleCase(resource.Replace('_', ' '))} ({reactor.Resources.GetValueOrDefault(resource)} left)");
        if (cost.Uses is { } max && choice.Ability is { } use)
            parts.Add($"1 use ({reactor.UsesLeft.GetValueOrDefault(use.Value, max)} left)");
        if (cost.Concentration) parts.Add("Concentration");
        return parts.Count == 0 ? "Costs nothing." : $"Costs {string.Join(" + ", parts)}.";
    }
}
