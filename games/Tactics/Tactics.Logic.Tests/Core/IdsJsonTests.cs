using System.Text.Json;
using ArcBox.Sdk;

namespace Tactics.Logic.Tests;

public sealed class IdsJsonTests
{
    [Fact]
    public void Ids_and_tiles_serialise_compactly()
    {
        Assert.Equal("\"kara\"", JsonSerializer.Serialize(new UnitId("kara"), ArcJson.Options));
        Assert.Equal("[3,4]", JsonSerializer.Serialize(new Tile(3, 4), ArcJson.Options));
        Assert.Equal(new UnitId("kara"), JsonSerializer.Deserialize<UnitId>("\"kara\"", ArcJson.Options));
        Assert.Equal(new Tile(3, 4), JsonSerializer.Deserialize<Tile>("[3,4]", ArcJson.Options));
    }

    [Fact]
    public void Every_id_type_is_a_plain_string()
    {
        Assert.Equal("\"a\"", JsonSerializer.Serialize(new AbilityId("a"), ArcJson.Options));
        Assert.Equal("\"c\"", JsonSerializer.Serialize(new ConditionId("c"), ArcJson.Options));
        Assert.Equal("\"i\"", JsonSerializer.Serialize(new InjuryId("i"), ArcJson.Options));
        Assert.Equal("\"it\"", JsonSerializer.Serialize(new ItemId("it"), ArcJson.Options));
        Assert.Equal("\"l\"", JsonSerializer.Serialize(new LootTableId("l"), ArcJson.Options));
        Assert.Equal(new LootTableId("l"), JsonSerializer.Deserialize<LootTableId>("\"l\"", ArcJson.Options));
    }

    [Fact]
    public void Ids_work_as_dictionary_keys()
    {
        var map = new Dictionary<UnitId, int> { [new UnitId("kara")] = 1 };
        var json = JsonSerializer.Serialize(map, ArcJson.Options);
        Assert.Equal("{\"kara\":1}", json);
        Assert.Equal(map, JsonSerializer.Deserialize<Dictionary<UnitId, int>>(json, ArcJson.Options));
    }

    [Fact]
    public void Ids_order_ordinally()
    {
        Assert.True(new UnitId("B").CompareTo(new UnitId("a")) < 0);
        Assert.True(new Tile(1, 5).CompareTo(new Tile(2, 0)) < 0);
        Assert.True(new Tile(1, 5).CompareTo(new Tile(1, 6)) < 0);
    }
}
