namespace Tactics.Logic;

/// <summary>
/// The code behind one effect key. A block reads its <see cref="EffectDef"/> and changes the battle through
/// <see cref="EffectContext.W"/>; anything that should resolve later (branches, reactions) is pushed onto the queue.
/// </summary>
public interface IEffectBlock
{
    /// <summary>The <see cref="EffectDef"/> record this block runs.</summary>
    Type DefType { get; }

    /// <summary>The JSON key, e.g. <c>"damage"</c>.</summary>
    string Key { get; }

    /// <summary>Runs <paramref name="def"/> (always of <see cref="DefType"/>) in <paramref name="ctx"/>.</summary>
    void Run(EffectDef def, EffectContext ctx);
}

/// <summary>A block for one <see cref="EffectDef"/> record type, keyed by <see cref="Key"/>.</summary>
public abstract class EffectBlock<TDef> : IEffectBlock where TDef : EffectDef
{
    /// <inheritdoc />
    public Type DefType => typeof(TDef);

    /// <inheritdoc />
    public abstract string Key { get; }

    /// <inheritdoc />
    public void Run(EffectDef def, EffectContext ctx) => Run((TDef)def, ctx);

    /// <summary>Runs the block.</summary>
    protected abstract void Run(TDef def, EffectContext ctx);
}
