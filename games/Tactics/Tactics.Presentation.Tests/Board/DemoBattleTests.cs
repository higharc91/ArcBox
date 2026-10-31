using Tactics.Logic;
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
    public void The_fight_demo_has_every_kind_of_unit_and_they_all_go_on_the_board()
    {
        var battle = DemoBattle.View("river_crossing:fight").Battle!;
        var terrain = new BoardTerrain(Pack.Map("river_crossing"), battle.Board);

        var units = UnitPlan.Build(battle, terrain, UnitArt.Load(Pack.Root), BoardFit.For(terrain, Pack.Art));

        Assert.Equal(battle.Units.Count(u => u.Life is "Up" or "Downed"), units.Length);
        Assert.Contains(battle.Units, u => u.Life == "Downed" && u.Countdown > 0);
        Assert.Contains(battle.Units, u => u.Life == "Out");
        Assert.Contains(battle.Units, u => u.Life == "Dead");
        Assert.Contains(units, u => u.Active);
        Assert.Contains(units, u => !u.Icons.IsEmpty);
        Assert.Contains(units, u => u.Sprite.Width > 64);
        Assert.Empty(battle.Units.Where(u => terrain.Map[u.At].Flags.HasFlag(TileFlags.Impassable)));
    }

    [Fact]
    public void The_view_goes_to_the_board()
    {
        var view = DemoBattle.View("river_crossing");

        Assert.Equal("Battle", view.Phase);
        Assert.NotNull(PhaseScreen.Of(view).Board);
    }
}
