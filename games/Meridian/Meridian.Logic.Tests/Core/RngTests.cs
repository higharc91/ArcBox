namespace Meridian.Logic.Tests.Core;

public sealed class RngTests
{
    [Fact]
    public void Pcg32_matches_reference_sequence()
    {
        // pcg32-demo output for seed 42, stream 54.
        uint[] expected = [0xa15c02b7, 0x7b47f409, 0xba1d3330, 0x83d2f293, 0xbfa4784b, 0xcbed606e];
        var rng = Rng.Seeded(42, 54);
        foreach (var want in expected)
        {
            (var got, rng) = rng.NextUInt();
            Assert.Equal(want, got);
        }
    }

    [Fact]
    public void Shuffle_is_a_permutation_and_repeatable()
    {
        EqArray<int> items = [.. Enumerable.Range(0, 40)];
        var (a, _) = Rng.Seeded(7).Shuffle(items);
        var (b, _) = Rng.Seeded(7).Shuffle(items);
        Assert.Equal(a, b);
        Assert.NotEqual(items, a);
        Assert.Equal(items.Items.Order(), a.Items.Order());
    }

    [Fact]
    public void Round_trips_through_json_and_continues_the_sequence()
    {
        var (_, rng) = Rng.Seeded(3).NextUInt();
        var restored = Json.RoundTrip(rng);
        Assert.Equal(rng, restored);
        Assert.Equal(rng.NextUInt().Value, restored.NextUInt().Value);
    }

    [Fact]
    public void A_fork_does_not_move_the_parent()
    {
        var parent = Rng.Seeded(9);
        var fork = parent.Fork(77);
        Assert.NotEqual(parent.NextUInt().Value, fork.NextUInt().Value);
        Assert.Equal(Rng.Seeded(9), parent);
    }
}
