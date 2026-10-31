namespace Tactics.Logic.Tests;

/// <summary>
/// Where the content lives, found by walking up from the test binaries to the folder holding <c>ArcBox.slnx</c>, so the
/// tests read the real files (and the fixtures in their source folder) without copying them to the output.
/// </summary>
public static class ContentPaths
{
    private static readonly Lazy<string> Root = new(() =>
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "ArcBox.slnx")))
                return dir.FullName;
        throw new DirectoryNotFoundException("Can't find the repository root (no ArcBox.slnx above the test binaries).");
    });

    /// <summary>The repository root.</summary>
    public static string RepoRoot => Root.Value;

    /// <summary><c>content/tactics/base</c>: the art sets and the <c>rules</c> pack.</summary>
    public static string ContentBase => Path.Combine(RepoRoot, "content", "tactics", "base");

    /// <summary><c>content/tactics/base/rules</c>: the base rules pack.</summary>
    public static string BaseRules => Path.Combine(ContentBase, "rules");

    /// <summary>A folder under the loader fixtures, <c>Tactics.Logic.Tests/Content/Fixtures</c>.</summary>
    public static string Fixture(params string[] parts)
        => Path.Combine([RepoRoot, "games", "Tactics", "Tactics.Logic.Tests", "Content", "Fixtures", .. parts]);
}
