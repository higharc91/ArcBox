namespace Tactics.Logic.Tests;

public sealed class RngTests
{
    [Fact]
    public void Pcg32_matches_reference_sequence()
    {
        uint[] expected = [0xa15c02b7, 0x7b47f409, 0xba1d3330, 0x83d2f293, 0xbfa4784b, 0xcbed606e];
        var rng = Rng.Seeded(42, 54);
        foreach (var want in expected)
        {
            var (value, next) = rng.NextUInt();
            Assert.Equal(want, value);
            rng = next;
        }
    }

    [Fact]
    public void Same_seed_same_rolls()
    {
        var a = Rng.Seeded(7);
        var b = Rng.Seeded(7);
        for (var i = 0; i < 100; i++)
        {
            var (va, na) = a.Roll(20);
            var (vb, nb) = b.Roll(20);
            Assert.Equal(va, vb);
            a = na;
            b = nb;
        }
        Assert.Equal(a, b);
    }

    [Fact]
    public void Different_streams_diverge()
    {
        var (a, _) = Rng.Seeded(7, 1).NextUInt();
        var (b, _) = Rng.Seeded(7, 2).NextUInt();
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Roll_stays_in_range_and_hits_every_face()
    {
        var seen = new HashSet<int>();
        var rng = Rng.Seeded(1);
        for (var i = 0; i < 10_000; i++)
        {
            var (value, next) = rng.Roll(20);
            Assert.InRange(value, 1, 20);
            seen.Add(value);
            rng = next;
        }
        Assert.Equal(20, seen.Count);
    }

    [Fact]
    public void Below_one_is_always_zero()
    {
        var rng = Rng.Seeded(3);
        for (var i = 0; i < 20; i++)
        {
            var (value, next) = rng.Below(1);
            Assert.Equal(0, value);
            rng = next;
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Below_rejects_non_positive_bound(int bound)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Rng.Seeded(1).Below(bound));
    }

    [Fact]
    public void Rng_round_trips_as_json()
    {
        var rng = Rng.Seeded(99).NextUInt().Next;
        var json = System.Text.Json.JsonSerializer.Serialize(rng, ArcBox.Sdk.ArcJson.Options);
        var back = System.Text.Json.JsonSerializer.Deserialize<Rng>(json, ArcBox.Sdk.ArcJson.Options);
        Assert.Equal(rng, back);
    }
}
