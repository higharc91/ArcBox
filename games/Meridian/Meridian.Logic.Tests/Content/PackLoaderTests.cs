using System.Text.Json.Nodes;

namespace Meridian.Logic.Tests.Content;

public sealed class PackLoaderTests
{
    [Fact]
    public void The_test_pack_loads()
    {
        var pack = TestPack.Load();
        Assert.Equal(5, pack.StartTiles.Count);
        Assert.Equal(6, pack.Tiles.Count);
        Assert.Equal(3, pack.Crew.Count);
        Assert.Equal(CardKind.Incident, pack.Card(new CardId("power_flicker")).Kind);
        Assert.Equal(CardKind.Anomaly, pack.Card(new CardId("humming_shard")).Kind);
        Assert.Equal(2, pack.Tile(new TileId("corridor")).Copies);
        Assert.IsType<HealRule>(pack.Tile(new TileId("med_bay")).Rule);
        Assert.Equal(5, pack.CrewMember(new CrewId("mara")).Tracks.Agility.ValueAt(5));
    }

    [Fact]
    public void A_missing_file_is_reported_with_its_name()
    {
        var files = TestPack.Files();
        files.Remove(PackLoader.CrewFile);
        var e = Assert.Throws<ContentException>(() => files.Load());
        Assert.Contains(e.Errors, x => x.File == PackLoader.CrewFile && x.Message.Contains("missing"));
    }

    [Fact]
    public void Malformed_json_is_reported_not_thrown_raw()
    {
        var files = TestPack.Files();
        files["rules/cards/salvage.json"] = JsonNode.Parse("""{ "cards": [ { "id": "x" } ] }""")!;
        var e = Assert.Throws<ContentException>(() => files.Load());
        Assert.Contains(e.Errors, x => x.File == "rules/cards/salvage.json");
    }

    [Fact]
    public void Every_error_is_reported_not_only_the_first()
    {
        var files = TestPack.Files();
        files[PackLoader.TilesFile]["tiles"]![0]!["hatches"] = new JsonArray();
        files[PackLoader.CrewFile]["crew"]![0]!["tracks"]!["brawn"]!["start"] = 0;
        var e = Assert.Throws<ContentException>(() => files.Load());
        Assert.True(e.Errors.Count >= 2, string.Join("\n", e.Errors));
    }

    [Fact]
    public void The_kind_discriminator_may_come_anywhere_in_an_effect()
    {
        var files = TestPack.Files();
        files["rules/cards/salvage.json"]["cards"]![0]!["item"]!["passive"] =
            JsonNode.Parse("""[ { "trait": "brawn", "dice": 2, "kind": "extraDice" } ]""");
        var pack = files.Load();
        Assert.Equal(new ExtraDice(Trait.Brawn, 2), pack.Card(new CardId("pry_bar")).Item!.Passive[0]);
    }
}
