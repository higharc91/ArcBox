using System.Text.Json.Nodes;

namespace Meridian.Logic.Tests.Content;

public sealed class ValidationTests
{
    private static ContentException Broken(Action<MemoryFiles> edit)
    {
        var files = TestPack.Files();
        edit(files);
        return Assert.Throws<ContentException>(() => files.Load());
    }

    private static JsonNode Tile(MemoryFiles f, int i) => f[PackLoader.TilesFile]["tiles"]![i]!;
    private static JsonNode Track(MemoryFiles f) => f[PackLoader.CrewFile]["crew"]![0]!["tracks"]!["brawn"]!;
    private static JsonNode Bands(MemoryFiles f) => f["rules/cards/incidents.json"]["cards"]![0]!["check"]!["bands"]!;

    [Theory]
    [InlineData("Med_Bay")]
    [InlineData("9lives")]
    [InlineData("med-bay")]
    public void Ids_must_be_lower_snake_case(string id)
        => Assert.Contains("ids are", Broken(f => Tile(f, 0)["id"] = id).Message);

    [Fact]
    public void Tile_ids_are_unique_across_start_and_placeable_tiles()
        => Assert.Contains("defined twice", Broken(f => Tile(f, 0)["id"] = "airlock").Message);

    [Fact]
    public void Card_ids_are_unique_across_decks()
        => Assert.Contains("defined twice", Broken(f => f["rules/cards/anomalies.json"]["cards"]![0]!["id"] = "pry_bar").Message);

    [Fact]
    public void A_tile_needs_one_to_four_distinct_hatches()
    {
        Assert.Contains("hatches", Broken(f => Tile(f, 0)["hatches"] = new JsonArray()).Message);
        Assert.Contains("hatches", Broken(f => Tile(f, 0)["hatches"] = new JsonArray("n", "n")).Message);
    }

    [Fact]
    public void A_tile_needs_a_deck()
        => Assert.Contains("deck", Broken(f => Tile(f, 0)["decks"] = new JsonArray()).Message);

    [Fact]
    public void A_track_has_exactly_eight_values()
        => Assert.Contains("exactly 8", Broken(f => Track(f)["values"] = new JsonArray(1, 2, 3)).Message);

    [Fact]
    public void A_track_never_decreases()
        => Assert.Contains("never decrease", Broken(f => Track(f)["values"] = new JsonArray(1, 2, 3, 2, 4, 5, 6, 7)).Message);

    [Fact]
    public void A_track_start_is_on_the_track()
        => Assert.Contains("start must be", Broken(f => Track(f)["start"] = 9).Message);

    [Fact]
    public void Bands_must_start_at_zero()
        => Assert.Contains("start at a total of 0", Broken(f => Bands(f)[0]!["min"] = 1).Message);

    [Fact]
    public void Bands_must_not_leave_a_gap()
        => Assert.Contains("no gap", Broken(f => Bands(f)[1]!["min"] = 3).Message);

    [Fact]
    public void The_last_band_is_open_ended()
        => Assert.Contains("no max", Broken(f => Bands(f)[2]!["max"] = 9).Message);

    [Fact]
    public void A_draw_must_be_the_last_effect()
        => Assert.Contains("must be the last effect", Broken(f => Bands(f)[0]!["effects"] = JsonNode.Parse(
            """[ { "kind": "draw", "card": "salvage" }, { "kind": "lose", "traits": "nerve", "steps": 1 } ]""")).Message);

    [Fact]
    public void Extra_dice_are_passive_only()
        => Assert.Contains("only a passive", Broken(f => Bands(f)[0]!["effects"] = JsonNode.Parse(
            """[ { "kind": "extraDice", "trait": "tech", "dice": 1 } ]""")).Message);

    [Fact]
    public void An_item_card_needs_an_item_body()
        => Assert.Contains("exactly one of check, choice or item", Broken(f => f["rules/cards/salvage.json"]["cards"]![0]!.AsObject().Remove("item")).Message);

    [Fact]
    public void A_start_tile_must_be_reachable_from_the_entry()
        => Assert.Contains("can't be reached", Broken(f => f[PackLoader.TilesFile]["start"]!["links"] = new JsonArray()).Message);

    [Fact]
    public void Every_deck_has_a_start_tile()
        => Assert.Contains("places nothing on the Command deck", Broken(f =>
        {
            var start = f[PackLoader.TilesFile]["start"]!;
            start["place"]!.AsArray().RemoveAt(3);
            start["links"] = JsonNode.Parse("""[ { "from": 2, "to": 3 } ]""");
        }).Message);

    [Fact]
    public void Move_to_must_name_a_real_tile()
        => Assert.Contains("unknown tile", Broken(f => Bands(f)[0]!["effects"] = JsonNode.Parse(
            """[ { "kind": "moveTo", "tile": "nowhere" } ]""")).Message);
}
