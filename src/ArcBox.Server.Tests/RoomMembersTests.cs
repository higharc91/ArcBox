using ArcBox.Server;
using ArcBox.Server.Rooms;
using Xunit;

namespace ArcBox.Server.Tests;

public class RoomMembersTests
{
    static RoomMembers New() => new(new RoomOptions());

    [Fact]
    public void Adds_trimmed_name_with_sequential_ids()
    {
        var m = New();
        Assert.True(m.TryAdd("  Ann ", "c1", out var a, out _));
        m.TryAdd("Bob", "c2", out var b, out _);
        Assert.Equal(("p1", "Ann"), (a!.Id.Value, a.Name));
        Assert.Equal("p2", b!.Id.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ThirteenChars")]
    public void Rejects_bad_name_length(string name)
    {
        Assert.False(New().TryAdd(name, "c1", out _, out var e));
        Assert.Equal("Name must be 1–12 characters.", e);
    }

    [Fact]
    public void Rejects_duplicate_name_case_insensitive()
    {
        var m = New();
        m.TryAdd("Ann", "c1", out _, out _);
        Assert.False(m.TryAdd("ANN", "c2", out _, out var e));
        Assert.Equal("That name is taken.", e);
    }

    [Fact]
    public void Rejects_ninth_player()
    {
        var m = New();
        for (int i = 0; i < 8; i++)
        {
            m.TryAdd($"P{i}", $"c{i}", out _, out _);
        }
        Assert.False(m.TryAdd("Late", "c9", out _, out var e));
        Assert.Equal("Room is full.", e);
    }

    [Fact]
    public void Vip_moves_to_next_connected_and_back()
    {
        var m = New();
        m.TryAdd("Ann", "c1", out var a, out _);
        m.TryAdd("Bob", "c2", out _, out _);
        Assert.Equal("p1", m.VipId!.Value.Value);
        m.Unbind("c1");
        Assert.Equal("p2", m.VipId!.Value.Value);
        m.Bind(a!, "c1b");
        Assert.Equal("p1", m.VipId!.Value.Value);
    }

    [Fact]
    public void FindBySecret_requires_matching_secret()
    {
        var m = New();
        m.TryAdd("Ann", "c1", out var a, out _);
        Assert.Same(a, m.FindBySecret(a!.Id, a.Secret));
        Assert.Null(m.FindBySecret(a.Id, "wrong"));
    }

    // Review Focus #1
    [Fact]
    public void Unbinding_a_stale_connection_does_not_disconnect_player()
    {
        var m = New();
        m.TryAdd("Ann", "old", out var a, out _);
        m.Bind(a!, "new");
        Assert.Null(m.Unbind("old"));
        Assert.True(a!.Connected);
        Assert.Equal("new", a.ConnectionId);
    }
}
