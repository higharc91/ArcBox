using System.Collections.Immutable;
using System.Text.Json;

namespace Tactics.Logic;

/// <summary>
/// A feature that doesn't fit the blocks (spec §4.3), written in C# and called from data by name through
/// <c>{"transform": {"hook": "&lt;name&gt;", "args": {...}}}</c>. Content can't add hooks.
/// </summary>
public interface INamedHook
{
    /// <summary>The name content calls it by, e.g. <c>"wild_shape"</c>.</summary>
    string Name { get; }

    /// <summary>
    /// True when the hook acts on the aimed tile rather than a unit, so an ability runs it once at its aim even with no unit
    /// in its area (see <see cref="EffectDef.OnTile"/>).
    /// </summary>
    bool OnTile => false;

    /// <summary>Runs the hook with its <paramref name="args"/> in <paramref name="ctx"/>.</summary>
    void Run(JsonElement args, EffectContext ctx);

    /// <summary>Checks <paramref name="args"/> at load, reporting each problem through <paramref name="error"/>.</summary>
    void Validate(JsonElement args, Action<string> error);

    /// <summary>
    /// The monster ids the hook needs with these <paramref name="args"/> (from its args, or fixed by the hook); the loader
    /// checks that each one exists.
    /// </summary>
    IEnumerable<string> Monsters(JsonElement args);
}

/// <summary>Every named hook, by name. Adding one takes a deploy.</summary>
public static class NamedHooks
{
    private static readonly ImmutableSortedDictionary<string, INamedHook> ByName =
        new INamedHook[] { new WildShape(), new Counterspell(), new Polymorph(), new AnimateDead() }
            .ToImmutableSortedDictionary(h => h.Name, h => h, StringComparer.Ordinal);

    /// <summary>Every hook's name, in ordinal order.</summary>
    public static ImmutableArray<string> Names { get; } = [.. ByName.Keys];

    /// <summary>The hook called <paramref name="name"/>, or null.</summary>
    public static INamedHook? TryFor(string name) => ByName.GetValueOrDefault(name);

    /// <summary>Reports an error unless <paramref name="args"/> is an object (a hook that takes no arguments has <c>{}</c>).</summary>
    internal static void RequireObject(string hook, JsonElement args, Action<string> error)
    {
        if (args.ValueKind != JsonValueKind.Object)
            error($"'{hook}' needs an object of args (\"args\": {{}} when it takes none).");
    }

    /// <summary>The string <paramref name="name"/> in <paramref name="args"/>, or null.</summary>
    internal static string? String(JsonElement args, string name)
        => args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>The monster sheet <paramref name="monster"/>, or an <see cref="InvalidOperationException"/> naming the hook.</summary>
    internal static UnitSheet Monster(EffectContext ctx, string hook, string monster)
        => ctx.Rules.MonsterSheet(monster) ?? throw new InvalidOperationException($"'{hook}' needs the monster '{monster}', which the rules pack lacks.");
}
