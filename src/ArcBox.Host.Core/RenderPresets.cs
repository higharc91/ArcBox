namespace ArcBox.Host.Core;

/// <summary>
/// The TV's render preset names (plan Global Constraints): <c>ultra</c>, <c>high</c>, <c>low</c>. Which one is used:
/// the <c>--preset</c> argument, else the one saved in <c>user://settings.json</c>, else Ultra. F2 cycles
/// Ultra → High → Low → Ultra.
/// </summary>
public static class RenderPresets
{
    public const string Ultra = "ultra";
    public const string High = "high";
    public const string Low = "low";

    public static IReadOnlyList<string> All { get; } = [Ultra, High, Low];

    /// <summary>The preset name in lower case, or null when it isn't one.</summary>
    public static string? Normalise(string? name)
    {
        var n = name?.Trim().ToLowerInvariant();
        return n is not null && All.Contains(n) ? n : null;
    }

    /// <summary>The preset F2 switches to from <paramref name="current"/> (an unknown name counts as Ultra).</summary>
    public static string Next(string? current)
    {
        var i = All.ToList().IndexOf(Normalise(current) ?? Ultra);
        return All[(i + 1) % All.Count];
    }

    /// <summary>The <c>--preset</c> argument when given, else the saved choice, else Ultra.</summary>
    public static string Resolve(string? argument, string? saved) => Normalise(argument) ?? Normalise(saved) ?? Ultra;

    public static string Title(string name) => (Normalise(name) ?? name).ToUpperInvariant();
}
