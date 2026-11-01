using ArcBox.Sdk;
using Tactics.Logic;

namespace Tactics.Game;

/// <summary>
/// The party in the world (overworld 5b): which region it's in, the tile it stands on and who leads it. All of it is ready from
/// the moment a session starts, whatever phase that lands in, so a TV joining mid-fight still learns the region. The live tile
/// is kept here and reaches the document only through <see cref="SaveWorld"/>.
/// </summary>
public sealed partial class TacticsGame
{
    /// <summary>The region the party is in; null for a pack with no region, which plays as it did before the world.</summary>
    private WorldRegion? _region;

    private Tile _at;
    private Tile _facing;

    /// <summary>What <see cref="ExploreView.Epoch"/> says. See <see cref="EnterWorld"/>.</summary>
    private int _epoch;

    /// <summary>Whoever leads now, by member id; null with nobody to lead.</summary>
    private string? _lead;

    /// <summary>
    /// The member whose session this is. <see cref="WorldSave.LastLeader"/> keeps this one, not whoever holds the lead at the
    /// moment, so a restart mid-handover still hands the lead back to them and the next session's rotation doesn't skip anyone.
    /// Null until someone is elected, which is what makes a session's rotation happen once.
    /// </summary>
    private string? _sessionLead;

    /// <summary>Who has the pad, if anyone.</summary>
    public Player? Leader => _lead is { } lead ? _players.FirstOrDefault(p => p.Profile?.Value == lead) : null;

    private static bool IsWorldAction(string action) => action is "battles" or "backToWalk" or "endSession";

    // ---- Coming into the world ----------------------------------------------------------------------------------------

    /// <summary>
    /// Puts the party where the document says, or at the region's start. A place that no longer makes sense goes to the start
    /// with a notice: the region was removed, or a repaint left the tile unstandable or cut off from the start.
    /// </summary>
    private void EnterWorld()
    {
        var saved = _doc.World;
        var region = saved?.Region is { } id && Rules.RegionFor(id) is { } named ? named : Rules.Regions.Values.MinBy(r => r.Def.Chapter);
        if (region is null)
            return;
        _region = region;
        // Not 0: a server restart restarts Seq under a TV that may have stayed up, and to it only a new Epoch means "snap".
        _epoch = (int)(_ctx.Now.ToUnixTimeSeconds() % int.MaxValue);
        var start = region.Def.Spawns.Start;
        if (saved is not null && saved.Region == region.Def.Id && CanStandOn(saved.At, start))
            (_at, _facing) = (saved.At, saved.Facing);
        else
        {
            (_at, _facing) = (start, new Tile(start.X + 1, start.Y));
            if (saved is not null)
                Note("The land has changed since you were last here, so the party starts again from the road's beginning.");
        }
    }

    private bool CanStandOn(Tile at, Tile start)
    {
        var terrain = Rules.GroundFor(_region!.Def.Id)!.Terrain;
        return terrain.Map.InBounds(at) && terrain.Walkable(at) && WorldWalk.Reach(terrain, start, ClassDef.DefaultJump).Contains(at);
    }

    private void Note(string text) => _notice = _notice is null ? text : $"{_notice} {text}";

    /// <summary>Into the world, from Muster, the battle list or Results.</summary>
    private void ToExploration()
    {
        _toasts.Clear();
        _phase = TacticsPhase.Exploration;
        ReconsiderLeader();
    }

    /// <summary>Out of the world, to anywhere: everything walking runs on ends here, and the place is saved at once.</summary>
    private void LeaveExploration() => SaveWorld();

    private void EndSession()
    {
        if (_phase == TacticsPhase.Exploration)
            LeaveExploration();
        _ctx.EndGame(new GameResults([], $"The party is level {_doc.PartyLevel}; {_doc.Completed.Length} battles won."));
    }

    // ---- The lead -----------------------------------------------------------------------------------------------------

    /// <summary>Whether a member can take the lead: here, with a character.</summary>
    private bool CanLead(string member)
        => _doc.Members.ContainsKey(member) && _players.Any(p => p.Profile?.Value == member && _online.Contains(p.Id));

    /// <summary>
    /// The next member after <paramref name="last"/> who can lead, in member order, which is ordinal by profile because seat
    /// order changes between nights. <paramref name="last"/> needn't be a member any more.
    /// </summary>
    private string? NextAfter(string? last)
    {
        var free = _doc.Members.Keys.Where(CanLead).ToArray();
        return free.FirstOrDefault(m => string.CompareOrdinal(m, last) > 0) ?? free.FirstOrDefault();
    }

    /// <summary>
    /// Settles who leads: this session's leader if they're here (electing one if the session has none yet, which is its one
    /// rotation), else whoever already leads, else the next who can.
    /// </summary>
    private void ReconsiderLeader()
    {
        if (_region is null)
            return;
        _sessionLead ??= NextAfter(_doc.World?.LastLeader);
        var wanted = _sessionLead is { } own && CanLead(own) ? own
            : _lead is { } still && CanLead(still) ? still
            : NextAfter(_lead ?? _sessionLead);
        if (wanted == _lead)
            return;
        _lead = wanted;
        SaveWorld();
    }

    // ---- Saving -------------------------------------------------------------------------------------------------------

    /// <summary>Whether the document is behind the party.</summary>
    private bool WorldChanged => _region is not null
        && (_doc.World is not { } saved || saved.Region != _region.Def.Id || saved.At != _at || saved.Facing != _facing
            || _sessionLead is not null && saved.LastLeader != _sessionLead);

    /// <summary>Brings the document up to the party, without saving it.</summary>
    private void KeepWorld()
    {
        if (!WorldChanged)
            return;
        _doc = _doc with
        {
            World = (_doc.World ?? new WorldSave(_region!.Def.Id, _at, _facing, null, [], [])) with
            {
                Region = _region!.Def.Id, At = _at, Facing = _facing, LastLeader = _sessionLead ?? _doc.World?.LastLeader,
            },
        };
    }

    /// <summary>Saves the party's place now, if it has changed.</summary>
    private void SaveWorld()
    {
        if (!WorldChanged)
            return;
        KeepWorld();
        Save();
    }
}
