using ArcBox.Server.Rooms;
using Xunit;

namespace ArcBox.Server.Tests;

public class SecretsTests
{
    [Fact]
    public void Tokens_are_128_bit_and_unique()
    {
        var a = Secrets.NewToken();
        Assert.Equal(22, a.Length);
        Assert.NotEqual(a, Secrets.NewToken());
    }

    [Fact]
    public void Matches_is_exact()
    {
        var t = Secrets.NewToken();
        Assert.True(Secrets.Matches(t, t));
        Assert.False(Secrets.Matches(t, t + "x"));
        Assert.False(Secrets.Matches(t, ""));
    }
}
