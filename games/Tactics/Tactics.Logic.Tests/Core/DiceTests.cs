namespace Tactics.Logic.Tests;

public sealed class DiceTests
{
    [Theory]
    [InlineData("2d6+1", 2, 6, 1)]
    [InlineData("1d4-1", 1, 4, -1)]
    [InlineData("3", 0, 0, 3)]
    [InlineData("10d10", 10, 10, 0)]
    public void Parse_and_format_round_trip(string text, int count, int sides, int bonus)
    {
        var dice = Dice.Parse(text);
        Assert.Equal(new Dice(count, sides, bonus), dice);
        Assert.Equal(text, dice.ToString());
    }

    [Fact]
    public void Average_of_two_d6_plus_one_is_eight()
    {
        Assert.Equal(8.0, Dice.Parse("2d6+1").Average);
    }

    [Theory]
    [InlineData("d6")]
    [InlineData("2x6")]
    [InlineData("")]
    public void Malformed_text_throws_FormatException_naming_the_text(string text)
    {
        var ex = Assert.Throws<FormatException>(() => Dice.Parse(text));
        Assert.Contains($"'{text}'", ex.Message);
    }

    [Fact]
    public void Dice_serialise_as_plain_string()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new Dice(2, 6, 1), ArcBox.Sdk.ArcJson.Options);
        // The shared encoder may escape '+' as \u002B; the JSON string value is still "2d6+1".
        Assert.Equal("2d6+1", System.Text.Json.JsonSerializer.Deserialize<string>(json));
        Assert.Equal(new Dice(2, 6, 1),
            System.Text.Json.JsonSerializer.Deserialize<Dice>(json, ArcBox.Sdk.ArcJson.Options));
        Assert.Equal(new Dice(2, 6, 1),
            System.Text.Json.JsonSerializer.Deserialize<Dice>("\"2d6+1\"", ArcBox.Sdk.ArcJson.Options));
    }

    [Fact]
    public void Bad_dice_in_json_is_a_JsonException()
    {
        Assert.Throws<System.Text.Json.JsonException>(
            () => System.Text.Json.JsonSerializer.Deserialize<Dice>("\"d6\"", ArcBox.Sdk.ArcJson.Options));
    }
}
