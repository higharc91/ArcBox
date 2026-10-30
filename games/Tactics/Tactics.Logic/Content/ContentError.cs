using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>
/// One problem found while loading rules content (spec §4.10): the pack file (<c>"&lt;pack&gt;/&lt;path&gt;"</c>, e.g.
/// <c>"base/classes/wizard.json"</c>), a JSON pointer into it (<c>"/subclasses/2/features/6/0"</c>; empty for the whole
/// file) and what is wrong.
/// </summary>
public sealed record ContentError(string File, string Pointer, string Message)
{
    /// <summary><c>"file#pointer: message"</c>.</summary>
    public override string ToString() => $"{File}#{Pointer}: {Message}";
}

/// <summary>Rules content failed to load. <see cref="Errors"/> holds every problem found, not just the first.</summary>
public sealed class ContentException(ImmutableArray<ContentError> errors)
    : Exception($"The rules content has {errors.Length} error{(errors.Length == 1 ? "" : "s")}:"
                + string.Concat(errors.Select(e => Environment.NewLine + "  " + e)))
{
    /// <summary>Every error found, in the order the packs and files were read.</summary>
    public ImmutableArray<ContentError> Errors { get; } = errors;
}
