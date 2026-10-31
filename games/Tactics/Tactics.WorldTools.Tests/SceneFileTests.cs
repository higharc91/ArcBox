namespace Tactics.WorldTools.Tests;

public sealed class SceneFileTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("tactics-scene-test-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string Scene => Path.Combine(_folder, "region.tscn");

    [Fact]
    public void A_new_scene_may_be_written() => SceneFile.Refuse(Scene, overwrite: false);

    [Fact]
    public void A_scene_that_exists_is_refused_and_named()
    {
        File.WriteAllText(Scene, "painted");
        var refused = Assert.Throws<SceneExistsException>(() => SceneFile.Refuse(Scene, overwrite: false));
        Assert.Contains(Scene, refused.Message, StringComparison.Ordinal);
        Assert.Equal("painted", File.ReadAllText(Scene));
    }

    [Fact]
    public void A_caller_can_mean_to_replace_it()
    {
        File.WriteAllText(Scene, "painted");
        SceneFile.Refuse(Scene, overwrite: true);
    }
}
