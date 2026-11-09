using Tactics.Logic;

namespace Tactics.Testing;

/// <summary>One immutable shipped pack per test process. Edited and negative-loader fixtures deliberately bypass this cache.</summary>
internal static class BaseContent
{
    private static readonly Lazy<string> RootOnce = new(() =>
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "ArcBox.slnx")))
                return Path.Combine(dir.FullName, "content", "tactics", "base");
        throw new DirectoryNotFoundException("No ArcBox.slnx above the test binaries.");
    });
    private static readonly Lazy<ArtManifest> ArtOnce = new(() => ArtManifests.Load(Root));
    private static readonly Lazy<RulesPack> RulesOnce = new(() => RulesPack.Load([Path.Combine(Root, "rules")], Art));
    private static readonly Lazy<RulesPack> LogicOnce = new(() => RulesPack.Load([Path.Combine(Root, "rules")]));
    public static string Root => RootOnce.Value;
    public static ArtManifest Art => ArtOnce.Value;
    public static RulesPack Rules => RulesOnce.Value;
    public static RulesPack LogicRules => LogicOnce.Value;
}
