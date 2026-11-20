namespace Meridian.Logic;

/// <summary>One problem in a content pack: the file, where in it, and what is wrong.</summary>
public sealed record ContentError(string File, string Where, string Message)
{
    public override string ToString() => $"{File}: {Where}: {Message}";
}

/// <summary>A pack failed to load. Lists every error found, not only the first.</summary>
public sealed class ContentException(IReadOnlyList<ContentError> errors)
    : Exception($"The Meridian content pack has {errors.Count} error(s):\n" + string.Join("\n", errors))
{
    public IReadOnlyList<ContentError> Errors { get; } = errors;
}
