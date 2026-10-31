using Tactics.Logic;
using Tactics.Presentation.Hud;

namespace Tactics.Presentation.Tests.Hud;

/// <summary><see cref="ObjectiveWords"/>: what a battle's win expression says on the TV.</summary>
public sealed class ObjectiveWordsTests
{
    private static ObjectivesDef Win(ObjectiveExpr win) => new(win, ObjectiveExpr.Of(Objectives.PartyOut));

    private static ObjectiveExpr Any(params string[] leaves) => new(null, [.. leaves.Select(ObjectiveExpr.Of)], []);

    private static ObjectiveExpr All(params string[] leaves) => new(null, [], [.. leaves.Select(ObjectiveExpr.Of)]);

    [Fact]
    public void The_warlords_bridge_reads_as_a_sentence()
        => Assert.Equal("Defeat the warlord, or survive 11 rounds", ObjectiveWords.Of(Win(Any("defeat:warlord", "survive:11"))));

    [Theory]
    [InlineData("defeat_all", "Defeat every enemy")]
    [InlineData("defeat:ridge_archers", "Defeat the ridge archers")]
    [InlineData("survive:8", "Survive 8 rounds")]
    [InlineData("survive:1", "Survive 1 round")]
    [InlineData("reach:exit:2", "Get 2 heroes to the exit")]
    [InlineData("reach:north_gate:1", "Get 1 hero to the north gate")]
    [InlineData("protect:farmer", "Keep the farmer alive")]
    [InlineData("flag:bridge_down", "Bridge down")]
    [InlineData("party_out", "Party out")]              // a leaf nobody words stays readable
    public void Each_leaf_has_its_words(string leaf, string words)
        => Assert.Equal(words, ObjectiveWords.Of(Win(ObjectiveExpr.Of(leaf))));

    [Fact]
    public void All_of_several_joins_with_and()
        => Assert.Equal("Defeat every enemy, and keep the farmer alive", ObjectiveWords.Of(Win(All("defeat_all", "protect:farmer"))));

    [Fact]
    public void A_group_inside_a_group_is_read_in_order()
    {
        var nested = new ObjectiveExpr(null, [All("defeat:warlord", "protect:farmer"), ObjectiveExpr.Of("survive:3")], []);

        Assert.Equal("Defeat the warlord, and keep the farmer alive, or survive 3 rounds", ObjectiveWords.Of(Win(nested)));
    }

    [Fact]
    public void No_objectives_says_nothing()
        => Assert.Null(ObjectiveWords.Of(null));

    [Fact]
    public void An_empty_expression_says_nothing()
        => Assert.Null(ObjectiveWords.Of(Win(new ObjectiveExpr(null, [], []))));
}
