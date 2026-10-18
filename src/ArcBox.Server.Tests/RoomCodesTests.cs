using ArcBox.Server.Rooms;
using Xunit;

namespace ArcBox.Server.Tests;

public class RoomCodesTests
{
    [Fact]
    public void Generate_uses_alphabet_and_length_4()
    {
        var c = RoomCodes.Generate(_ => false);
        Assert.Equal(4, c.Length);
        Assert.All(c, ch => Assert.Contains(ch, RoomCodes.Alphabet));
    }

    [Fact]
    public void Generate_skips_taken_codes()
    {
        var taken = new HashSet<string>();
        for (int i = 0; i < 200; i++)
        {
            Assert.True(taken.Add(RoomCodes.Generate(taken.Contains)));
        }
    }

    [Fact]
    public void Normalize_trims_and_uppercases()
        => Assert.Equal("QXRT", RoomCodes.Normalize(" qxrt "));
}
