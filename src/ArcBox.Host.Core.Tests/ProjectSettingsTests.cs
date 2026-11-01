namespace ArcBox.Host.Core.Tests;

public class ProjectSettingsTests
{
    /// <summary>
    /// With <c>audio/general/text_to_speech=true</c>, Godot 4.7.2 loads speech at start-up, and about 1 launch in 3 with an
    /// empty <c>user://shader_cache</c> dies with heap corruption before the room appears. Off, the first speech call loads
    /// it instead, and the built-in voice works just the same.
    /// </summary>
    [Fact]
    public void Speech_is_not_loaded_at_start_up()
    {
        var project = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "ArcBox.Host", "project.godot"));

        Assert.DoesNotContain("general/text_to_speech=true", project);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ArcBox.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new DirectoryNotFoundException("ArcBox.slnx not found above the test output.");
    }
}
