using Tactics.Presentation.Audio;
using Tactics.Presentation.Tests.Board;

namespace Tactics.Presentation.Tests.Audio;

/// <summary><see cref="TitleArt"/> against the real pack.</summary>
public sealed class TitleArtTests
{
    private static readonly Lazy<TitleArt> Title = new(() => TitleArt.Load(Pack.Root));

    [Theory]
    [InlineData("victory_anim")]
    [InlineData("defeat_anim")]
    public void A_banner_is_a_strip_that_plays_once_and_holds(string name)
    {
        var strip = Title.Value.Strip(name)!;

        Assert.True(File.Exists(Path.Combine(Pack.Root, strip.Path)), strip.Path);
        Assert.True(strip.Frames > 1);
        Assert.True(strip.Fps > 0);
        Assert.Equal(strip.Frames - 1, strip.FrameAt(1000));
        Assert.Equal(0, strip.FrameAt(0));
    }

    [Fact]
    public void A_strip_moves_on_at_its_own_speed()
    {
        var strip = Title.Value.Strip("victory_anim")!;

        Assert.Equal(3, strip.FrameAt(3.5 / strip.Fps));
    }

    [Fact]
    public void A_strip_the_pack_lacks_is_nothing() => Assert.Null(Title.Value.Strip("no_such_strip"));

    [Fact]
    public void The_logo_is_on_disk() => Assert.True(File.Exists(Path.Combine(Pack.Root, Title.Value.Logo)));
}
