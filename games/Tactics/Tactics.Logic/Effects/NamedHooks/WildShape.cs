using System.Text.Json;

namespace Tactics.Logic;

/// <summary>
/// <c>wild_shape</c> (args <c>{"monster": "&lt;beast id&gt;"}</c>): the target (a druid using it on itself) takes the
/// beast's sheet (<see cref="Transforms.Into"/>) at its own level, keeping its own INT, WIS and CHA, with temporary HP equal
/// to the beast's HP. When they run out it reverts, the rest of the damage carrying over.
/// </summary>
public sealed class WildShape : INamedHook
{
    /// <inheritdoc />
    public string Name => "wild_shape";

    /// <inheritdoc />
    public string? Describe(JsonElement args, RulesPack rules)
        => NamedHooks.String(args, "monster") is { } monster
            ? $"transforms into {TooltipText.Article(TooltipText.Humanize(monster))} {TooltipText.Humanize(monster)}"
            : null;

    /// <inheritdoc />
    public void Run(JsonElement args, EffectContext ctx)
    {
        var unit = ctx.Target is { } target ? ctx.W.Unit(target) : ctx.SourceUnit;
        var own = unit.TrueSheet ?? unit.Sheet;
        var monster = NamedHooks.String(args, "monster") ?? throw new InvalidOperationException("'wild_shape' needs a \"monster\" id in its args.");
        var beast = NamedHooks.Monster(ctx, Name, monster);
        var scores = beast.Scores;
        foreach (var stat in new[] { Stat.Int, Stat.Wis, Stat.Cha })
            scores = scores.SetItem(stat, own.Score(stat));
        Transforms.Into(ctx.W, unit.Id, beast with { Scores = scores, Level = own.Level }, beast.MaxHp);
    }

    /// <inheritdoc />
    public IEnumerable<string> Monsters(JsonElement args)
        => NamedHooks.String(args, "monster") is { } monster ? [monster] : [];

    /// <inheritdoc />
    public void Validate(JsonElement args, Action<string> error)
    {
        if (NamedHooks.String(args, "monster") is null)
            error("'wild_shape' needs a \"monster\" id in its args.");
    }
}
