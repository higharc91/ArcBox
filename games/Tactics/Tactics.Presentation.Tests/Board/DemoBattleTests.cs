using Tactics.Presentation.Board;

namespace Tactics.Presentation.Tests.Board;

/// <summary><see cref="DemoBattle"/>: the dev-only <c>--tactics-demo</c> views.</summary>
public sealed class DemoBattleTests
{
    [Fact]
    public void A_plain_demo_is_the_untouched_map()
    {
        var battle = DemoBattle.View("crypt_hall").Battle!;

        Assert.Equal("crypt_hall", battle.Map);
        Assert.Empty(battle.Board!.Zones);
        Assert.Empty(battle.Board.Heights);
        Assert.Empty(battle.Board.Sunk);
    }

    [Fact]
    public void The_changed_demo_has_zones_a_raised_tile_and_the_collapsed_bridge()
    {
        var battle = DemoBattle.View("warlord_bridge:changed").Battle!;

        Assert.Equal("warlord_bridge", battle.Map);
        Assert.NotEmpty(battle.Board!.Zones);
        Assert.Contains(battle.Board.Heights, h => h.Delta > 0);
        Assert.Equal(8, battle.Board.Sunk.Length);
    }

    [Fact]
    public void The_view_goes_to_the_board()
    {
        var view = DemoBattle.View("river_crossing");

        Assert.Equal("Battle", view.Phase);
        Assert.NotNull(PhaseScreen.Of(view).Board);
    }
}
