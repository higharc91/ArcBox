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
    public void Hash_is_deterministic_base64url_43_chars()
    {
        var t = Secrets.NewToken();
        var h = Secrets.Hash(t);
        Assert.Equal(h, Secrets.Hash(t));
        Assert.NotEqual(h, Secrets.Hash(t + "x"));
        Assert.Equal(43, h.Length);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", h);
    }

    [Fact]
    public void Matches_hash_against_plaintext()
    {
        var t = Secrets.NewToken();
        Assert.True(Secrets.Matches(Secrets.Hash(t), t));
        Assert.False(Secrets.Matches(Secrets.Hash(t), t + "x"));
        Assert.False(Secrets.Matches(Secrets.Hash(t), ""));
        // A plaintext is never a valid stored value.
        Assert.False(Secrets.Matches(t, t));
    }
}
