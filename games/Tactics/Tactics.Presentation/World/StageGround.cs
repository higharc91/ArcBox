using System.Collections.Immutable;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Beats;

namespace Tactics.Presentation.World;

/// <summary>What a board, as shown, asks of the stage and of the world's blocks.</summary>
/// <param name="Restage">The stage's plan must be built again (<see cref="StagePlan.With"/>): the board changed, zones and all.</param>
/// <param name="Blocks">
/// The world tiles whose blocks must be built again (<see cref="StagePlan.WorldBlocks"/>), or empty: only a height or a sinking reaches the
/// world, and that work takes a block's whole bake, so it's asked for once per change and never on a frame of its own.
/// </param>
public readonly record struct GroundAsk(bool Restage, ImmutableArray<Tile> Blocks);

/// <summary>
/// Follows the board a fight in the world shows, beat by beat, and says what each change needs rebuilt (overworld 5c §4). A fight's ground
/// heals when it ends (plan 5c, default 9): <see cref="Heal"/> asks once for every block the fight changed to go back as it was. Pure.
/// </summary>
public sealed class StageGround
{
    private BoardChanges _shown = BoardChanges.None;    // what the stage draws
    private BoardChanges _world = BoardChanges.None;    // what the world's blocks draw

    /// <summary>The board <paramref name="shown"/> on <paramref name="stage"/>: what has to be built again for it, if anything.</summary>
    public GroundAsk Show(BoardChanges? shown, BattleStage stage)
    {
        var next = shown ?? BoardChanges.None;
        if (DisplayedBattle.SameBoard(_shown, next))
            return new GroundAsk(false, []);
        _shown = next;
        var touched = BoardChangesOnWorld.Touched(BoardChangesOnWorld.Of(_world, stage), BoardChangesOnWorld.Of(next, stage));
        if (!touched.IsEmpty)
            _world = next;
        return new GroundAsk(true, touched);
    }

    /// <summary>The fight is over: the world tiles to build again as the region has them, or empty if the fight never changed its ground.</summary>
    public ImmutableArray<Tile> Heal(BattleStage stage)
    {
        var touched = BoardChangesOnWorld.Touched(BoardChangesOnWorld.Of(_world, stage), BoardChangesOnWorld.Of(null, stage));
        (_shown, _world) = (BoardChanges.None, BoardChanges.None);
        return touched;
    }

    /// <summary>What the world's blocks are drawing now, in battle tiles: what <see cref="StagePlan.WorldBlocks"/> is to build them with.</summary>
    public BoardChanges World => _world;
}
