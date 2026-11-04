using ArcBox.Host.Shell;

namespace ArcBox.Host.Core.Tests;

public sealed class AppArgsTests
{
    [Fact]
    public void Relative_screenshot_paths_are_rejected_with_an_actionable_message()
    {
        var args = AppArgs.Parse(["--shots=docs/shots"]);
        Assert.Null(args.ShotsDir);
        Assert.Contains(args.Unknown, message => message.Contains("absolute path", StringComparison.Ordinal));
    }

    [Fact]
    public void Absolute_screenshot_paths_are_preserved()
    {
        var path = Path.Combine(Path.GetTempPath(), "arcbox-shots");
        var args = AppArgs.Parse([$"--shots={path}"]);
        Assert.Equal(path, args.ShotsDir);
        Assert.Empty(args.Unknown);
    }
}
