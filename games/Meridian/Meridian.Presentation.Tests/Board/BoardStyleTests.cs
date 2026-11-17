using Meridian.Presentation.Board;

namespace Meridian.Presentation.Tests.Board;

public sealed class BoardStyleTests
{
    /// <summary>The north edge's doorway band, as the art samples' hatch manifest gives it ([23, 0, 41, 8] in 64 px).</summary>
    private static readonly Box North = new(23, 0, 18, 8);

    [Theory]
    [InlineData(Side.N)]
    [InlineData(Side.E)]
    [InlineData(Side.S)]
    [InlineData(Side.W)]
    public void A_north_overlay_turned_for_a_side_lands_on_that_side(Side side)
    {
        // Review focus 6, for art: the overlay rotation the scene uses puts a north-edge drawing on the right edge.
        var b = BoardStyle.Turn(North, TileDraw.OverlayDegrees(side), 64);
        var touches = side switch
        {
            Side.N => b.Y == 0,
            Side.E => b.X + b.W == 64,
            Side.S => b.Y + b.H == 64,
            _ => b.X == 0,
        };
        Assert.True(touches, $"{side}: {b}");
        Assert.Equal(North.W * North.H, b.W * b.H);
    }

    [Theory]
    [InlineData(new[] { "Ann", "Bo", "Cy" }, new[] { "A", "B", "C" })]
    [InlineData(new[] { "Bot1", "Bot2", "Bot3" }, new[] { "B1", "B2", "B3" })]
    [InlineData(new[] { "Sam", "Sue", "Jo" }, new[] { "Sa", "Su", "J" })]
    [InlineData(new[] { "Sam", "Sammy" }, new[] { "S1", "S2" })]
    public void No_two_seats_show_the_same_mark(string[] names, string[] marks)
        => Assert.Equal(marks, BoardStyle.Marks(names));

    [Fact]
    public void Each_deck_has_its_own_accent()
        => Assert.Equal(3, new[] { DeckId.Command, DeckId.Habitat, DeckId.Engineering }.Select(BoardStyle.DeckAccent).Distinct().Count());

    [Fact]
    public void Every_token_kind_the_scenarios_use_has_a_glyph_and_an_unknown_one_still_draws()
    {
        foreach (var kind in new[] { "brood", "repair", "core", "engine", "pods", "prize" })
        {
            Assert.False(string.IsNullOrEmpty(BoardStyle.Token(kind).Label));
        }
        Assert.Equal("H", BoardStyle.Token("hazard").Label);
        Assert.NotEqual(BoardStyle.Token("repair", "broken"), BoardStyle.Token("repair", "fixed"));
    }
}
