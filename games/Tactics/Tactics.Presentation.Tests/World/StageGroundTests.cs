using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.World;
using Xunit;

namespace Tactics.Presentation.Tests.World;

/// <summary><see cref="StageGround"/>: the stage is built again for each change of the board, and the world's blocks once for each change of its ground.</summary>
public sealed class StageGroundTests
{
    private static readonly BattleStage Stage = new("map", new Tile(16, 10), 0, 8, 6);

    [Fact]
    public void World_blocks_are_asked_for_once_per_ground_changing_beat()
    {
        var ground = new StageGround();
        var fire = BoardChanges.None with { Zones = [new ZoneView(1, "fire", [new Tile(2, 2)])] };
        var raised = fire with { Heights = [new HeightChange(new Tile(3, 3), 2)] };
        var sunk = raised with { Sunk = [new Tile(4, 4)] };
        // One beat after another, each shown many frames running, as the scene's redraw shows them.
        BoardChanges?[] beats = [null, null, fire, fire, fire, raised, raised, raised with { Zones = [] }, sunk, sunk, sunk];

        var asks = beats.Select(b => ground.Show(b, Stage)).ToArray();

        Assert.Equal(4, asks.Count(a => a.Restage));     // fire, raised, the fire going out, sunk with the fire back
        Assert.Equal([new Tile(19, 13)], asks[5].Blocks);
        Assert.Equal([new Tile(20, 14)], asks[8].Blocks);
        Assert.Equal(2, asks.Count(a => !a.Blocks.IsEmpty));
        Assert.Equal(sunk, ground.World);

        Assert.Equal([new Tile(19, 13), new Tile(20, 14)], ground.Heal(Stage));
        Assert.Empty(ground.Heal(Stage));
        Assert.False(ground.Show(null, Stage).Restage);
    }

    [Fact]
    public void A_fight_that_never_changed_the_ground_heals_nothing()
    {
        var ground = new StageGround();
        ground.Show(BoardChanges.None with { Zones = [new ZoneView(1, "fire", [new Tile(2, 2)])] }, Stage);

        Assert.Empty(ground.Heal(Stage));
    }
}
