using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>Every effect block, by JSON key and by <see cref="EffectDef"/> type. Content can't add blocks.</summary>
public static class BlockRegistry
{
    private static readonly ImmutableSortedDictionary<string, IEffectBlock> ByKey;
    private static readonly ImmutableDictionary<Type, IEffectBlock> ByType;   // lookup only, never iterated

    static BlockRegistry()
    {
        IEffectBlock[] blocks =
        [
            new AttackBlock(), new SaveBlock(),
            new DamageBlock(), new HealBlock(),
            new ApplyBlock(), new RemoveBlock(), new HelpBlock(),
            new PushBlock(), new PullBlock(), new TeleportBlock(), new SwapBlock(), new DashBlock(), new JumpToBlock(),
        ];
        ByKey = blocks.ToImmutableSortedDictionary(b => b.Key, b => b, StringComparer.Ordinal);
        ByType = blocks.ToImmutableDictionary(b => b.DefType, b => b);
    }

    /// <summary>Every registered key, in ordinal order.</summary>
    public static IReadOnlyCollection<string> Keys => ByKey.Keys.ToImmutableArray();

    /// <summary>The block for <paramref name="key"/>. Throws <see cref="KeyNotFoundException"/> naming an unknown key.</summary>
    public static IEffectBlock For(string key)
        => TryFor(key) ?? throw new KeyNotFoundException($"There is no effect block '{key}'.");

    /// <summary>The block that runs <paramref name="def"/>.</summary>
    public static IEffectBlock For(EffectDef def)
        => ByType.TryGetValue(def.GetType(), out var block)
            ? block
            : throw new KeyNotFoundException($"No effect block runs {def.GetType().Name}.");

    /// <summary>The block for <paramref name="key"/>, or null.</summary>
    internal static IEffectBlock? TryFor(string key) => ByKey.GetValueOrDefault(key);
}
