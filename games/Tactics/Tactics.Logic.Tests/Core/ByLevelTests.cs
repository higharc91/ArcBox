using System.Collections.Immutable;
using System.Text.Json;
using ArcBox.Sdk;

namespace Tactics.Logic.Tests;

public sealed class ByLevelTests
{
    private static ByLevel<string> Table() => new(ImmutableSortedDictionary<int, string>.Empty
        .Add(1, "1d6").Add(3, "2d6").Add(5, "3d6"));

    [Fact]
    public void At_picks_highest_key_at_or_below()
    {
        var table = Table();
        Assert.Equal("1d6", table.At(1));
        Assert.Equal("1d6", table.At(2));
        Assert.Equal("2d6", table.At(4));
        Assert.Equal("3d6", table.At(10));
    }

    [Fact]
    public void At_below_lowest_key_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Table().At(0));
    }

    [Fact]
    public void Constant_answers_every_level()
    {
        var table = ByLevel<int>.Constant(7);
        Assert.Equal(7, table.At(1));
        Assert.Equal(7, table.At(10));
    }

    [Fact]
    public void Serialises_as_a_level_keyed_object()
    {
        var json = JsonSerializer.Serialize(Table(), ArcJson.Options);
        Assert.Equal("{\"1\":\"1d6\",\"3\":\"2d6\",\"5\":\"3d6\"}", json);
        var back = JsonSerializer.Deserialize<ByLevel<string>>(json, ArcJson.Options)!;
        Assert.Equal(Table().Steps, back.Steps);
    }
}
