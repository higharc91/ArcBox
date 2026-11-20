namespace Meridian.Logic.Tests.Core;

public sealed class IdsJsonTests
{
    [Fact]
    public void Ids_serialise_as_bare_strings()
    {
        Assert.Equal("\"med_bay\"", Json.Write(new TileId("med_bay")));
        Assert.Equal("\"c12\"", Json.Write(CompartmentId.Numbered(12)));
        Assert.Equal(new CrewId("ivo"), Json.RoundTrip(new CrewId("ivo")));
    }

    [Fact]
    public void Ids_work_as_dictionary_keys()
    {
        var map = new SortedDictionary<CardId, int> { [new CardId("flare")] = 2 };
        Assert.Equal("{\"flare\":2}", Json.Write(map));
        Assert.Equal(2, Json.RoundTrip(map)[new CardId("flare")]);
    }

    [Fact]
    public void Enums_use_lower_case_wire_names()
    {
        Assert.Equal("\"engineering\"", Json.Write(DeckId.Engineering));
        Assert.Equal("\"nerve\"", Json.Write(Trait.Nerve));
        Assert.Equal("\"w\"", Json.Write(Side.W));
        Assert.Equal("\"anomaly\"", Json.Write(CardKind.Anomaly));
        Assert.Equal(Side.S, Json.Read<Side>("\"s\""));
    }

    [Fact]
    public void Sides_rotate_clockwise()
    {
        Assert.Equal(Side.E, Side.N.Rotate(1));
        Assert.Equal(Side.N, Side.W.Rotate(1));
        Assert.Equal(Side.S, Side.N.Opposite());
        Assert.Equal(Side.W, Side.N.Rotate(-1));
    }
}
