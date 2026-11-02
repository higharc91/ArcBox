using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>
/// The checks of a region's things against its story (interactables design §5.6). Lore isn't part of the rules pack, so the loader can't
/// make them; a base pack test does, as it does for beat spots (spec §3.4).
/// </summary>
public static class InteractableLore
{
    /// <summary>
    /// Every flag the story of <paramref name="arc"/> can set: its beats' and choices', its scenes' nodes and options', and the battle flags of
    /// its encounters' triggers.
    /// </summary>
    public static ImmutableHashSet<string> StoryFlags(Arc arc, LoreBook lore, RulesPack rules)
    {
        var flags = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        foreach (var beat in arc.Beats)
        {
            flags.UnionWith(beat.Sets);
            flags.UnionWith(beat.Choices.SelectMany(c => c.Sets));
            foreach (var scene in lore.ScenesOf(beat.Id))
                foreach (var node in scene.Nodes)
                {
                    flags.UnionWith(node.Sets);
                    flags.UnionWith(node.Options.SelectMany(o => o.Sets));
                }
            if (beat.Encounter is { } id && rules.Encounters.TryGetValue(id, out var encounter))
                flags.UnionWith(encounter.Triggers.SelectMany(t => t.Do).Select(a => a.SetFlag).OfType<string>());
        }
        return flags.ToImmutable();
    }

    /// <summary>The flags only a choice of <paramref name="arc"/> sets: a thing setting one would make the party's choice for it.</summary>
    public static ImmutableHashSet<string> ChoiceFlags(Arc arc)
        => [.. arc.Beats.SelectMany(b => b.Choices).SelectMany(c => c.Sets)];

    /// <summary>
    /// What's wrong between <paramref name="defs"/> and the story: a flag a thing needs (its <c>requires</c>, <c>unless</c> or a lock's key)
    /// that neither the story (<paramref name="story"/>) nor any thing sets, so it's never there or never opens; and a thing that sets a flag
    /// a choice sets (<paramref name="choices"/>), which would choose for the party. One line per problem, naming the thing.
    /// </summary>
    public static ImmutableArray<string> Problems(IReadOnlyList<InteractableDef> defs, IReadOnlySet<string> story, IReadOnlySet<string> choices)
    {
        var given = defs.SelectMany(d => d.Sets.Concat(d.Trap is { } t ? t.Sets : [])).ToHashSet(StringComparer.Ordinal);
        bool Known(string flag) => story.Contains(flag) || given.Contains(flag);
        var problems = new List<string>();
        foreach (var def in defs)
        {
            foreach (var flag in def.Requires.Where(f => !Known(f)))
                problems.Add($"'{def.Id}' requires '{flag}', which nothing sets, so it's never there.");
            foreach (var flag in def.Unless.Where(f => !Known(f)))
                problems.Add($"'{def.Id}' is gone once '{flag}' is set, but nothing sets it.");
            if (def.Lock?.Key is { } key && !Known(key))
                problems.Add($"The key '{key}' of '{def.Id}' is never given: nothing sets it.");
            foreach (var flag in def.Sets.Concat(def.Trap is { } trap ? trap.Sets : []).Where(choices.Contains))
                problems.Add($"'{def.Id}' sets '{flag}', which a choice of the story sets: it would choose for the party.");
        }
        return [.. problems];
    }
}
