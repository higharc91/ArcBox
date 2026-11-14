namespace Meridian.Logic.Tests.Core;

public sealed class EqArrayTests
{
    private sealed record Holder(EqArray<string> Names);

    [Fact]
    public void Records_holding_one_keep_value_equality_after_a_round_trip()
    {
        var holder = new Holder(["a", "b"]);
        var back = Json.RoundTrip(holder);
        Assert.Equal(holder, back);
        Assert.Equal(holder.GetHashCode(), back.GetHashCode());
        Assert.Equal("{\"names\":[\"a\",\"b\"]}", Json.Write(holder));
    }

    [Fact]
    public void Default_is_empty_and_equals_empty()
    {
        EqArray<int> missing = default;
        Assert.True(missing.IsEmpty);
        Assert.Equal(EqArray<int>.Empty, missing);
        Assert.Equal("[]", Json.Write(missing));
    }

    [Fact]
    public void Order_matters()
    {
        EqArray<int> a = [1, 2];
        EqArray<int> b = [2, 1];
        Assert.NotEqual(a, b);
    }
}
