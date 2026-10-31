using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Hud;

namespace Tactics.Presentation.Tests.Hud;

/// <summary><see cref="ObjectiveWords"/>: what a battle's win expression says on the TV.</summary>
public sealed class ObjectiveWordsTests
{
    private static ObjectivesDef Win(ObjectiveExpr win) => new(win, ObjectiveExpr.Of(Objectives.PartyOut));

    private static ObjectiveExpr Any(params string[] leaves) => new(null, [.. leaves.Select(ObjectiveExpr.Of)], []);

    private static UnitView Villain(string id, string name)
        => new(new UnitId(id), name, "Enemy", null, new Tile(0, 0), 10, 10, 0, "Up", 0, [], null);

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
    public void A_villain_is_called_by_name_where_the_battle_has_them()
    {
        UnitView[] units = [Villain("halvard", "Ser Halvard"), Villain("mother_skarrow", "Mother Skarrow")];

        Assert.Equal("Defeat Ser Halvard", ObjectiveWords.Of(Win(ObjectiveExpr.Of("defeat:halvard")), units));
        Assert.Equal("Defeat Mother Skarrow, or defeat every enemy", ObjectiveWords.Of(Win(Any("defeat:mother_skarrow", "defeat_all")), units));
    }

    [Fact]
    public void A_tag_that_is_a_role_stays_a_role_whatever_its_unit_is_called()
        => Assert.Equal("Defeat the warlord", ObjectiveWords.Of(Win(ObjectiveExpr.Of("defeat:warlord")), [Villain("warlord", "Goblin Boss")]));

    [Fact]
    public void A_clock_that_loses_the_battle_is_said_with_the_win()
    {
        var finale = new ObjectivesDef(ObjectiveExpr.Of("defeat:maren_sorrel"), Any("party_out", "survive:14"));

        Assert.Equal("Defeat Maren Sorrel within 14 rounds", ObjectiveWords.Of(finale, [Villain("maren_sorrel", "Maren Sorrel")]));
    }

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
