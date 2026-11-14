using ArcBox.Sdk.Content;

namespace Meridian.Logic.Tests;

/// <summary>The shipped pack, content/meridian/base, loaded from the repository the tests run in.</summary>
internal static class BasePack
{
    private static readonly Lazy<RulesPack> Loaded = new(() => PackLoader.Load(PhysicalFileSource.Instance, Root));

    public static RulesPack Pack => Loaded.Value;

    public static string Root
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ArcBox.slnx")))
            {
                dir = dir.Parent;
            }
            return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("No ArcBox.slnx above the tests."),
                                "content", "meridian", "base");
        }
    }
}
