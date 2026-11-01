using System.Collections.Immutable;
using System.Numerics;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Beats;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.World;

/// <summary>Where the TV's world has got, as the scene sees it, for <see cref="StageLayout.WorldOf"/>.</summary>
/// <param name="Exists">A world is open, or opening, for this scene.</param>
/// <param name="Open">It's showing.</param>
/// <param name="Built">Its plan and ground are built.</param>
/// <param name="Failed">It can't be built.</param>
/// <param name="GivenUp">The scene gave a world up (it failed, or didn't open in time) and has none now.</param>
public readonly record struct WorldSeen(bool Exists, bool Open, bool Built, bool Failed, bool GivenUp);

/// <summary>
/// How the TV lays a screen out for a fight in the world (overworld 5c): what <c>TacticsScene.ShowBoard</c> decides, so Godot only applies it.
/// </summary>
/// <param name="InPlace">The fight's board goes on the world.</param>
/// <param name="PlainBoard">The plain board shows the fight.</param>
/// <param name="Hud">The battle's HUD shows.</param>
/// <param name="Battle">For <see cref="WorldCover.Look"/>: a board has the screen, so there's no header or table.</param>
/// <param name="FeedBoard">The screen's battle goes to the beat player. Not when a screen is only shown again: its battle is older than the player's.</param>
public sealed record StageLayout(bool InPlace, bool PlainBoard, bool Hud, bool Battle, bool FeedBoard)
{
    /// <summary>
    /// The layout of <paramref name="screen"/>: its world drawn or not (<paramref name="walking"/>), the world's plan built or not, and whether
    /// the beat player still has a battle shown. <paramref name="again"/> is a screen shown again for the world's sake, which mustn't feed the
    /// player the older battle it carries (5c review, item 2).
    /// </summary>
    public static StageLayout Of(PhaseScreen screen, bool walking, bool worldBuilt, bool playerShows, bool again = false)
    {
        var inPlace = screen.InPlace && walking && worldBuilt && (screen.Board is not null || playerShows);
        if (inPlace)
            return new StageLayout(true, false, screen.Board is not null, !screen.OverWorld, screen.Board is not null && !again);
        return new StageLayout(false, screen.Board is not null, screen.Board is not null, screen.Board is not null, screen.Board is not null && !again);
    }

    /// <summary>What the director is told of the world (<see cref="StageDirector.SetWorld"/>).</summary>
    public static WorldState WorldOf(WorldSeen world)
        => world.Exists && world.Open && world.Built ? WorldState.Ready
            : (world.Exists && world.Failed) || (!world.Exists && world.GivenUp) ? WorldState.Failed
            : WorldState.Loading;
}

/// <summary>
/// One frame of a fight drawn on the world: the beat's own places and poses with the director's walk-in over them, the units it hides, and
/// where the board's pixels land. The camera has the beat's shake, so the units are laid out from the steady camera and the HUD follows the
/// shaken one. Pure: <c>TacticsScene.RedrawStage</c> applies it.
/// </summary>
/// <param name="Places">Units not standing on their tiles, in the battle map's tiles.</param>
/// <param name="Poses">Units doing something.</param>
/// <param name="Hidden">Units not on the stage yet.</param>
/// <param name="Fit">The board's fit on the layout, without the shake: for laying units out.</param>
/// <param name="HudFit">The board's fit as it's drawn this frame, shake and all: for the HUD's marks.</param>
public sealed record StageFrame(ImmutableDictionary<UnitId, UnitPlace> Places, ImmutableDictionary<UnitId, UnitPose> Poses, ImmutableHashSet<UnitId> Hidden,
    BoardFit Fit, BoardFit HudFit)
{
    /// <summary>The frame of <paramref name="beat"/> on <paramref name="where"/>, with the camera at <paramref name="camera"/>, shaken by <paramref name="shake"/> art pixels.</summary>
    public static StageFrame Of(BeatFrame beat, StageDirector director, BattleStage where, BoardFit camera, Vector2 shake)
    {
        var steady = camera.Origin - shake * camera.Scale;
        return new StageFrame(
            director.Places.IsEmpty ? beat.Places : beat.Places.SetItems(director.Places),
            director.Poses.IsEmpty ? beat.Poses : beat.Poses.SetItems(director.Poses),
            director.Hidden,
            BoardFit.Stage(where, steady, camera.Scale),
            BoardFit.Stage(where, camera.Origin, camera.Scale));
    }

    /// <summary>The units to draw: those not hidden.</summary>
    public ImmutableArray<BoardUnit> Shown(ImmutableArray<BoardUnit> units) => Hidden.IsEmpty ? units : [.. units.Where(u => !Hidden.Contains(u.Id))];
}

/// <summary>A rebuild of the world's blocks to run: the tiles, and the ground to build them on (<see cref="StagePlan.WorldBlocks"/>).</summary>
public sealed record RepaintJob(StagePlan Plan, ImmutableArray<Tile> Tiles, BoardChanges? Changes);

/// <summary>
/// The world's block rebuilds waiting while one runs. Asks made meanwhile are merged, their tiles together, on the newest ground, so a heal
/// asked for behind a change is never dropped (5c review, item 3): building every waiting tile on the ground as it is now is right for all of them.
/// </summary>
public sealed class RepaintQueue
{
    private readonly SortedSet<Tile> _tiles = [];
    private StagePlan? _plan;
    private BoardChanges? _changes;

    /// <summary>True when something is waiting.</summary>
    public bool Waiting => _tiles.Count > 0;

    /// <summary>Asks for the blocks round <paramref name="tiles"/> on <paramref name="plan"/>'s stage, with <paramref name="changes"/> (null: healed).</summary>
    public void Ask(StagePlan plan, IEnumerable<Tile> tiles, BoardChanges? changes)
    {
        _tiles.UnionWith(tiles);
        (_plan, _changes) = (plan, changes);
    }

    /// <summary>Everything waiting, as one job, or null; the queue is empty afterwards.</summary>
    public RepaintJob? Take()
    {
        if (_tiles.Count == 0 || _plan is null)
            return null;
        var job = new RepaintJob(_plan, [.. _tiles], _changes);
        _tiles.Clear();
        (_plan, _changes) = (null, null);
        return job;
    }
}
