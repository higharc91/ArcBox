using System.Numerics;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Beats;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.World;

/// <summary>
/// The party as the TV shows it, from a stream of views that can come late, bunched or not at all (overworld 5b §3). A view only ever
/// adds to a path. The leader is a forward-only playhead on that path, in the server's own time, so a diagonal takes what the server
/// gave it and nothing rubber-bands. Followers are points further back on the same path, and fan out when the party stops.
/// <para>
/// Each frame: <see cref="Receive"/> any new view, <see cref="Advance"/>, then <see cref="Places"/>, then read <see cref="Poses"/>,
/// <see cref="Focus"/> and <see cref="Heading"/> for <see cref="WorldCamera.Follow"/>. When <see cref="Cuts"/> changes, the camera wants
/// <see cref="WorldCamera.Snap"/> rather than a glide. Walkers are keyed by <see cref="WalkerView.Id"/>, and
/// <see cref="UnitPlan.Walkers"/> turns the two dictionaries into units to draw. Both dictionaries are reused from frame to frame.
/// </para>
/// <para>
/// Position is a pure function of how far along the path a walker is, so it doesn't depend on the frame rate and can't jitter with it.
/// The one thing that needs the ground is left for <see cref="Places"/>, which is why that takes the terrain.
/// </para>
/// </summary>
public sealed class WalkTrack
{
    private const string WalkAnim = BeatScript.WalkAnim;
    private const string IdleAnim = "idle";
    private const string DropAnim = "jump";
    private const double DropAnimSeconds = 0.5;     // the sheet's five frames at 10 fps
    private const double WalkCycle = 0.667;         // eight frames at 12 fps: only to spread the walkers across a stride
    private const double WalkPace = 0.3;            // seconds a tile at the server's speed
    private const float Nearly = 0.004f;            // a spring this close has arrived
    private const float OffTrail = 0.25f;           // this far from the trail, a walker takes its height from the ground under it
    private const float Rejoined = 0.2f;            // this close to the trail, a follower sets off along it
    private const float Body = 0.12f;               // half a body's width, in tiles
    private const float LineStep = 0.1f;            // how finely a line across the ground is checked
    private const float FlatCentre = 0.25f;         // the share of a tile, each way from its middle, that stays at its height off the trail

    private static readonly (float Back, float Side)[] Formation = [(1, 1), (1, -1), (2, 0), (2.2f, 2), (2.2f, -2)];

    private readonly List<Node> _path = [];
    private readonly List<Walker> _walkers = [];
    private readonly Dictionary<string, UnitPlace> _places = [];
    private readonly Dictionary<string, UnitPose> _poses = [];

    private int _epoch;
    private int _seq;
    private bool _started;
    private bool _held;
    private double _now;
    private double _play;              // the playhead, in the path's seconds
    private double _speed;             // how fast it's running, as a share of the server's speed
    private double _newestStart;       // the path's second the newest leg started at
    private double _newestAt;          // when that leg reached this TV
    private double _starved;           // how long a direction has been held with no path to play
    private double _restFrom;          // where on the path the party last stood
    private Vector2 _restFace = Vector2.UnitX;
    private bool _slotted;             // this rest's formation has been chosen
    private bool _seeded;              // someone else has said where this rest's followers stand

    /// <summary>How far the leader has walked, in tiles, ever. It only goes up. A floor of this ticking over is a footstep.</summary>
    public double Walked { get; private set; }

    /// <summary>The path still to play, in seconds of the server's time.</summary>
    public double Queued => _path.Count == 0 ? 0 : _path[^1].Time - _play;

    /// <summary>How many times the party has been put somewhere rather than walked there: a first view, a new epoch or a reconnect.</summary>
    public int Cuts { get; private set; }

    /// <summary>How many times a long stall has jumped the playhead along the path.</summary>
    public int Skips { get; private set; }

    /// <summary>
    /// Which way the party is really going, for the camera's look-ahead, on the map: a unit vector on a sustained walk, shrinking to nothing
    /// at rest and on a tap. It's taken over a stretch of path, so a stairstep road reads as the line it runs along.
    /// </summary>
    public Vector2 Heading { get; private set; }

    /// <summary>The point the camera follows, as of the last <see cref="Places"/>: the leader, without the stride's hop.</summary>
    public UnitPlace Focus { get; private set; }

    /// <summary>
    /// What each walker is doing: <c>walk</c>, with <see cref="UnitPose.Seconds"/> tied to the distance it has walked, <c>idle</c>, or
    /// <c>jump</c> in the air on a long drop. <see cref="UnitPose.Facing"/> is always set, along one of the map's axes, which are what
    /// the art's four facings look along.
    /// </summary>
    public IReadOnlyDictionary<string, UnitPose> Poses => _poses;

    private bool Resting => _path.Count > 0 && _play >= _path[^1].Time && (!_held || _starved > WorldTuning.HeldPatience);

    /// <summary>Takes a new view. A stale one is ignored, and one that can't be joined to the path starts it again.</summary>
    public void Receive(ExploreView view)
    {
        if (_started && view.Epoch == _epoch && view.Seq < _seq)
            return;

        var way = new Vector2(view.Facing.X - view.At.X, view.Facing.Y - view.At.Y);
        if (way != Vector2.Zero)
            _restFace = AxisOf(way, _restFace);
        _held = view.Held;
        if (!_started || view.Epoch != _epoch || !Extend(view))
            Cut(view);
        if (!_held)
            _starved = 0;
        Muster(view);
        BuildPoses();
    }

    /// <summary>
    /// Puts walkers somewhere for this rest, in place of a formation: where a fight left them, after a new epoch. They stay there until
    /// the party moves off, and then fall in. Walkers the view doesn't have are ignored, so this comes after the <see cref="Receive"/>.
    /// </summary>
    public void Seed(IReadOnlyDictionary<string, UnitPlace> places)
    {
        foreach (var walker in _walkers)
            if (places.TryGetValue(walker.Id, out var place))
            {
                walker.Slot = place.Map;
                walker.Offset = place.Map - PointAt(walker.Along);
                (walker.Drift, walker.Placed, walker.At) = (Vector2.Zero, true, place.Map);
            }
        _seeded = true;
    }

    /// <summary>Moves everything on by <paramref name="seconds"/>.</summary>
    public void Advance(double seconds)
    {
        if (seconds <= 0 || _path.Count == 0)
            return;
        _now += seconds;
        var skipped = MoveHead(seconds);
        Walked = AlongAt(_play);
        var moving = !Resting;
        _slotted &= !moving;
        Trail(seconds, skipped, moving);
        Look(moving);
        Pose(seconds, moving);
    }

    /// <summary>Where each walker is now, on <paramref name="terrain"/>.</summary>
    public IReadOnlyDictionary<string, UnitPlace> Places(BoardTerrain terrain)
    {
        if (_path.Count == 0)
            return _places;
        if (Resting && !_slotted)
            Fan(terrain);

        var speed = (float)Math.Min(_speed, 1);
        foreach (var walker in _walkers)
        {
            var trail = HeightAt(terrain, walker.Along, speed, out walker.Fall);
            var off = Math.Clamp(walker.Offset.Length() / OffTrail, 0, 1);
            if (off > 0)
                walker.Fall = -1;
            var height = off >= 1 ? GroundAt(terrain, walker.At) : off > 0 ? float.Lerp(trail, GroundAt(terrain, walker.At), off) : trail;
            _places[walker.Id] = new UnitPlace(walker.At, height);
            walker.Placed = true;
        }
        if (_walkers.Count > 0)
            Focus = new UnitPlace(_walkers[0].At, HeightAt(terrain, _walkers[0].Along, 0, out _));
        else
            Focus = new UnitPlace(_path[^1].At, terrain.Top(_path[^1].Tile));
        return _places;
    }

    // ---- The path -------------------------------------------------------------------------------------------------------

    /// <summary>Starts a new path where the view says the party is, with the view's legs as the trail behind it, and nobody walking.</summary>
    private void Cut(ExploreView view)
    {
        var (time, along) = (_path.Count == 0 ? 0 : _play, Walked);
        _path.Clear();
        var legs = view.Legs.IsDefaultOrEmpty ? [] : view.Legs;
        var (first, face) = (legs.IsEmpty ? view.At : legs[0].To, _restFace);
        _path.Add(new Node(first, time, along, face));
        foreach (var leg in legs.Skip(1))
            AddLeg(leg);
        if (_path[^1].Tile != view.At)
        {
            _path.Clear();
            _path.Add(new Node(view.At, time, along, face));
        }

        (_epoch, _seq, _started) = (view.Epoch, view.Seq, true);
        (_play, _speed, _starved) = (_path[^1].Time, 0, 0);
        (_newestStart, _newestAt, _restFrom) = (_play, _now, _path[^1].Along);
        Walked = _path[^1].Along;
        (_slotted, _seeded) = (false, false);
        Cuts++;

        var ahead = Walked;
        foreach (var walker in _walkers)
        {
            ahead = walker.Along = walker == _walkers[0] ? Walked : Behind(ahead, out walker.Waiting);
            (walker.Offset, walker.Drift, walker.Slot, walker.Placed, walker.Stride) = (Vector2.Zero, Vector2.Zero, null, false, 0);
            (walker.At, walker.Moving, walker.Fall, walker.Face) = (PointAt(walker.Along), false, -1, _restFace);
        }
    }

    /// <summary>Adds the view's new legs to the path. False if there's a gap they don't cover, or they don't end where the view says.</summary>
    private bool Extend(ExploreView view)
    {
        if (view.Seq == _seq)
            return view.At == _path[^1].Tile;
        var news = view.Legs.Where(l => l.Seq > _seq).ToArray();
        if (news.Length == 0 || news[0].Seq != _seq + 1 || news[^1].To != view.At)
            return false;

        foreach (var leg in news)
            AddLeg(leg);
        _seq = view.Seq;
        (_newestStart, _newestAt, _seeded) = (_path[^2].Time, _now, false);
        Trim();
        return true;
    }

    private void AddLeg(WalkLeg leg)
    {
        var last = _path[^1];
        var to = new Vector2(leg.To.X + 0.5f, leg.To.Y + 0.5f);
        var way = to - last.At;
        if (way == Vector2.Zero)
            return;
        last.Radius = (float)Math.Clamp(last.Along - Walked, 0, WorldTuning.CornerRadius);    // from where the leader is, if it's already in the corner
        var raw = AxisOf(way, last.Face);
        var face = _path.Count > 1 && _path[^2].Raw == raw && last.Raw != raw ? last.Face : raw;     // a stairstep: don't flip every tile
        _path.Add(new Node(leg.To, last.Time + leg.Ms / 1000.0, last.Along + way.Length(), face) { Raw = raw });
    }

    /// <summary>Forgets path nobody needs: the last walker's, and what the heading looks back over.</summary>
    private void Trim()
    {
        var keep = _walkers.Aggregate(Walked - WorldTuning.HeadingChord, (least, w) => Math.Min(least, w.Along)) - WorldTuning.SpacingReach;
        var old = 0;
        while (old + 1 < _path.Count && _path[old + 1].Along < keep)
            old++;
        _path.RemoveRange(0, old);
    }

    /// <summary>The leg whose end is at or past <paramref name="along"/>; 0 only for a path of one node.</summary>
    private int LegAt(double along)
    {
        var i = _path.Count - 1;
        while (i > 1 && _path[i - 1].Along >= along)
            i--;
        return i;
    }

    private int LegAtTime(double time)
    {
        var i = _path.Count - 1;
        while (i > 1 && _path[i - 1].Time >= time)
            i--;
        return i;
    }

    private double AlongAt(double time)
    {
        var i = LegAtTime(time);
        if (i == 0)
            return _path[0].Along;
        var (a, b) = (_path[i - 1], _path[i]);
        return a.Along + (b.Along - a.Along) * Math.Clamp((time - a.Time) / (b.Time - a.Time), 0, 1);
    }

    /// <summary>The point of the path itself, corners and all.</summary>
    private Vector2 LineAt(double along)
    {
        var i = LegAt(along);
        if (i == 0)
            return _path[0].At;
        var (a, b) = (_path[i - 1], _path[i]);
        return Vector2.Lerp(a.At, b.At, (float)Math.Clamp((along - a.Along) / (b.Along - a.Along), 0, 1));
    }

    /// <summary>
    /// The point a walker stands at. A corner is a curve inside its own tile, which keeps its speed through a gentle turn, slows a little
    /// for a sharp one, and comes to a stop and back for a U-turn. A corner's size is settled when the leg after it arrives, and it starts no nearer than the leader is, so nobody moves when it does.
    /// </summary>
    private Vector2 PointAt(double along)
    {
        var i = LegAt(along);
        if (i == 0)
            return _path[0].At;
        if (i + 1 < _path.Count && _path[i].Along - along < _path[i].Radius)
            return Corner(i, along);
        if (i >= 2 && along - _path[i - 1].Along < _path[i - 1].Radius)
            return Corner(i - 1, along);
        return LineAt(along);
    }

    private Vector2 Corner(int node, double along)
    {
        var (before, middle, after) = (_path[node - 1].At, _path[node], _path[node + 1].At);
        var radius = middle.Radius;
        var share = (float)Math.Clamp((along - middle.Along + radius) / (2 * radius), 0, 1);
        var enter = middle.At + Vector2.Normalize(before - middle.At) * radius;
        var leave = middle.At + Vector2.Normalize(after - middle.At) * radius;
        var (s, t) = (1 - share, share);
        return s * s * enter + 2 * s * t * middle.At + t * t * leave;
    }

    /// <summary>
    /// The place one spacing behind <paramref name="along"/>. It's measured as the crow flies, not along the path, or walkers would bunch
    /// on each corner, but it never takes more path than <see cref="WorldTuning.SpacingReach"/>. <paramref name="ranOut"/> says the
    /// path doesn't go back that far.
    /// </summary>
    private double Behind(double along, out bool ranOut)
    {
        var (spacing, from) = (WorldTuning.Spacing, LineAt(along));
        var least = along - WorldTuning.SpacingReach;
        var at = along;
        ranOut = false;
        for (var i = LegAt(along); i >= 1; i--)
        {
            var (start, here) = (_path[i - 1], LineAt(at));
            if (Vector2.Distance(start.At, from) >= spacing)
            {
                var way = Vector2.Normalize(start.At - here);
                var gap = here - from;
                var across = Vector2.Dot(gap, way);
                var back = -across + MathF.Sqrt(Math.Max(0, across * across - gap.LengthSquared() + spacing * spacing));
                return Math.Max(at - back, least);
            }
            if (start.Along <= least)
                return least;
            at = start.Along;
        }
        ranOut = true;
        return _path[0].Along;
    }

    // ---- The playhead ---------------------------------------------------------------------------------------------------

    /// <summary>Runs the playhead on. True if it jumped.</summary>
    private bool MoveHead(double seconds)
    {
        var end = _path[^1].Time;
        var head = Math.Min(_newestStart + (_now - _newestAt), end);    // where the server has got to, as near as this TV can tell
        var lag = head - _play;
        var skipped = lag > WorldTuning.SnapLag;
        if (skipped)
        {
            _play = head - WorldTuning.TargetLag;
            lag = WorldTuning.TargetLag;
            Skips++;
        }

        var left = end - _play;
        if (left <= 0)
        {
            _speed = 0;
            _starved = _held ? _starved + seconds : 0;
            return skipped;
        }

        _starved = 0;
        var rate = Math.Clamp(1 + WorldTuning.RateGain * (lag - WorldTuning.TargetLag), WorldTuning.RateLeast, WorldTuning.RateMost);
        if (!_held)
            rate = Math.Max(rate, 1);     // nothing more is coming, so there's no buffer to build
        var settle = _held ? WorldTuning.HeldSettle : WorldTuning.StopSettle;
        var brake = Math.Max(Math.Sqrt(2 * left / settle), WorldTuning.BrakeLeast);
        var was = Math.Max(_speed, Math.Min(WorldTuning.StartSpeed, brake));
        var ramp = seconds / WorldTuning.RateRamp;
        _speed = Math.Min(Math.Clamp(rate, was - ramp, was + ramp), brake);
        _play = Math.Min(end, _play + (was + _speed) / 2 * seconds);
        if (_play >= end)
            _speed = _held ? _speed : 0;
        return skipped;
    }

    // ---- Followers -----------------------------------------------------------------------------------------------------

    /// <summary>Moves everyone along the trail, then eases each off it to its place in the formation, or back onto it.</summary>
    private void Trail(double seconds, bool skipped, bool moving)
    {
        var ahead = Walked;
        for (var i = 0; i < _walkers.Count; i++)
        {
            var walker = _walkers[i];
            if (i == 0)
                walker.Along = Walked;
            else
            {
                ahead = Behind(ahead, out walker.Waiting);
                walker.Pace = walker.Offset.LengthSquared() < Rejoined * Rejoined ? Math.Min(walker.Pace + WorldTuning.FollowerPick * seconds, WorldTuning.FollowerMost) : 0;
                var most = skipped ? double.MaxValue : moving ? walker.Pace * seconds : 0;
                walker.Along = Math.Min(Math.Max(ahead, walker.Along), walker.Along + most);
            }
            var walked = walker.Along - walker.Before;
            walker.Before = walker.Along;

            if (skipped)
                (walker.Offset, walker.Drift) = (Vector2.Zero, Vector2.Zero);
            var trail = PointAt(walker.Along);          // a follower goes back to the trail the way it left it, which is known to be clear
            if (!moving)
            {
                if (_slotted)
                    Spring(walker, walker.Slot is { } slot ? slot - trail : Vector2.Zero, WorldTuning.FanRate, seconds);
            }
            else if (walker.Waiting)
                walker.Drift = Vector2.Zero;
            else
                Spring(walker, Vector2.Zero, WorldTuning.RegroupRate, seconds);

            var was = walker.At;
            walker.At = trail + walker.Offset;
            walker.Stepped = walker.Offset.LengthSquared() < Nearly * Nearly ? walked : Vector2.Distance(was, walker.At);
        }
    }

    /// <summary>A critically damped spring, solved exactly, so that it moves the same at any frame rate and can be interrupted at any moment.</summary>
    private static void Spring(Walker walker, Vector2 target, float rate, double seconds)
    {
        var gap = walker.Offset - target;
        if (gap.LengthSquared() < Nearly * Nearly && walker.Drift.LengthSquared() < Nearly)
        {
            (walker.Offset, walker.Drift) = (target, Vector2.Zero);
            return;
        }
        var t = (float)seconds;
        var decay = MathF.Exp(-rate * t);
        var push = walker.Drift + gap * rate;
        var next = target + (gap + push * t) * decay;
        var drift = (walker.Drift - push * rate * t) * decay;
        var fastest = WorldTuning.FanMost * t;
        var step = next - walker.Offset;
        if (step.LengthSquared() > fastest * fastest)
        {
            next = walker.Offset + step * (fastest / step.Length());
            drift = drift.LengthSquared() > 0 ? Vector2.Normalize(drift) * WorldTuning.FanMost : drift;
        }
        (walker.Offset, walker.Drift) = (next, drift);
    }

    /// <summary>
    /// Chooses where each follower stands for this rest: tiles behind the leader in a loose wedge, on ground they could walk to from the
    /// leader's tile without leaving their feet. Whoever is closest takes each spot, so they don't cross. In a corridor that comes out as a line.
    /// </summary>
    private void Fan(BoardTerrain terrain)
    {
        _slotted = true;
        Choose(terrain);

        foreach (var walker in _walkers.Skip(1))
            if (!walker.Placed && walker.Waiting && walker.Slot is { } slot)      // no trail to have come along: it was just there
                (walker.Offset, walker.Drift, walker.At) = (slot - PointAt(walker.Along), Vector2.Zero, slot);
    }

    private void Choose(BoardTerrain terrain)
    {
        var followers = _walkers.Skip(1).Where(w => !_seeded || w.Slot is null).ToList();
        var rules = terrain.Rules;
        var open = Reachable(terrain, _path[^1].Tile);
        if (_seeded)
            open.RemoveAll(t => _walkers.Any(w => w.Slot is { } slot && TileUnder(slot) == t));
        var (centre, way) = (_path[^1].At, _path.Count > 1 ? Vector2.Normalize(_path[^1].At - _path[^2].At) : _restFace);
        var side = new Vector2(-way.Y, way.X);
        for (var i = 0; followers.Count > 0 && open.Count > 0; i++)
        {
            var (back, across) = Formation[i % Formation.Length];
            var ideal = centre - way * (back + 2 * (i / Formation.Length)) + side * across;
            var walker = followers.MinBy(w => Vector2.DistanceSquared(w.At, ideal))!;
            followers.Remove(walker);
            walker.Drift = Vector2.Zero;    // what was checked is a straight line
            var scatter = Scatter(walker.Id);
            var home = PointAt(walker.Along);    // there and back: the party will want it on the trail again
            var tiles = open.Where(t => Clear(rules, walker.At, Middle(t) + scatter) && Clear(rules, home, Middle(t) + scatter)).ToArray();
            if (tiles.Length == 0)
            {
                walker.Slot = walker.At;    // hemmed in: it stays where it stopped
                continue;
            }
            var tile = tiles.MinBy(t => Fit(t, ideal, centre, way));
            open.Remove(tile);
            walker.Slot = Middle(tile) + scatter;
        }
        foreach (var walker in followers)
            walker.Slot = walker.At;
    }

    private static Vector2 Middle(Tile tile) => new(tile.X + 0.5f, tile.Y + 0.5f);

    /// <summary>Whether a straight walk from one point to another keeps a body's width on ground that can be walked on.</summary>
    private static bool Clear(Terrain rules, Vector2 from, Vector2 to)
    {
        var samples = (int)MathF.Ceiling(Vector2.Distance(from, to) / LineStep);
        for (var i = 1; i <= samples; i++)
        {
            var at = Vector2.Lerp(from, to, (float)i / samples);
            foreach (var (x, y) in (ReadOnlySpan<(float, float)>)[(-Body, -Body), (Body, -Body), (-Body, Body), (Body, Body)])
                if (!rules.Walkable(TileUnder(at + new Vector2(x, y))))
                    return false;
        }
        return true;
    }

    private static Tile TileUnder(Vector2 map) => new((int)MathF.Floor(map.X), (int)MathF.Floor(map.Y));

    private static float Fit(Tile tile, Vector2 ideal, Vector2 leader, Vector2 way)
    {
        var at = Middle(tile);
        var ahead = Vector2.Dot(at - leader, way) > 0 ? 6 : 0;
        return Vector2.DistanceSquared(at, ideal) + ahead;
    }

    /// <summary>A little way off the tile's middle, always the same for one walker.</summary>
    private static Vector2 Scatter(string id)
    {
        var hash = Hash(id);
        return new Vector2((hash % 13 / 6f - 1) * WorldTuning.FanScatter, (hash / 13 % 13 / 6f - 1) * WorldTuning.FanScatter);
    }

    private static uint Hash(string id)
    {
        var hash = 2166136261u;
        foreach (var c in id)
            hash = (hash ^ c) * 16777619u;
        return hash;
    }

    /// <summary>The tiles a few legal steps from <paramref name="start"/>, no higher or lower than a step can be, and not <paramref name="start"/>.</summary>
    private static List<Tile> Reachable(BoardTerrain terrain, Tile start)
    {
        var rules = terrain.Rules;
        var seen = new HashSet<Tile> { start };
        var edge = new List<Tile> { start };
        if (!rules.Map.InBounds(start))
            return [];
        var top = rules.HeightAt(start);
        for (var step = 0; step < WorldTuning.FanReach; step++)
        {
            var next = new List<Tile>();
            foreach (var tile in edge)
                foreach (var to in WorldWalk.Steps(rules, tile, ClassDef.DefaultJump))
                    if (Math.Abs(rules.HeightAt(to) - top) <= ClassDef.DefaultJump && seen.Add(to))
                        next.Add(to);
            edge = next;
        }
        seen.Remove(start);
        return [.. seen];
    }

    /// <summary>Lines the walkers up with the view's party, leader first, keeping everyone where they are on screen.</summary>
    private void Muster(ExploreView view)
    {
        var party = view.Party.IsDefault ? [] : view.Party;
        var order = party.OrderBy(w => w.Leads ? 0 : 1).Select(w => w.Id).ToArray();     // a stable sort: the rest stay in member order
        if (order.SequenceEqual(_walkers.Select(w => w.Id)))
            return;

        var known = _walkers.ToDictionary(w => w.Id);
        _walkers.Clear();
        _places.Clear();
        _poses.Clear();
        var ahead = Walked;
        foreach (var id in order)
        {
            var isLeader = _walkers.Count == 0;
            var fresh = !known.TryGetValue(id, out var walker);
            walker ??= new Walker(id) { Face = _restFace, Phase = Hash(id) % 16 / 16.0 * WalkCycle };
            walker.Waiting = false;
            ahead = walker.Along = isLeader ? Walked : Behind(ahead, out walker.Waiting);
            walker.Before = walker.Along;
            var trail = PointAt(walker.Along);
            walker.At = fresh ? trail : walker.At;
            (walker.Offset, walker.Slot, walker.Placed) = (walker.At - trail, null, !fresh);
            _walkers.Add(walker);
        }
        _slotted = false;
        _seeded = false;
        if (_walkers.Count > 0 && _walkers[0].Offset != Vector2.Zero)
            _walkers[0].Slot = _path[^1].At;     // the new leader walks to the front
    }

    // ---- Where it's all going -------------------------------------------------------------------------------------------

    private void Look(bool moving)
    {
        if (!moving)
        {
            _restFrom = Walked;
            Heading = Vector2.Zero;
            return;
        }
        var chord = LineAt(Walked) - LineAt(Math.Max(Walked - WorldTuning.HeadingChord, _restFrom));
        var length = chord.Length();
        if (length < 1e-3f)
        {
            Heading = Vector2.Zero;
            return;
        }
        var walked = (float)(Walked - _restFrom);
        var share = Math.Clamp((walked - WorldTuning.HeadingFrom) / (WorldTuning.HeadingFull - WorldTuning.HeadingFrom), 0, 1);
        Heading = chord / length * (share * share * (3 - 2 * share)) * Math.Min(1, length / WorldTuning.HeadingFrom);
    }

    private void Pose(double seconds, bool moving)
    {
        var starving = moving && Queued <= 0;
        for (var i = 0; i < _walkers.Count; i++)
        {
            var walker = _walkers[i];
            var slow = walker.Stepped / seconds < WorldTuning.StandingSpeed;
            if (slow && (!moving || walker.Waiting))
            {
                (walker.Moving, walker.Stride) = (false, 0);
                walker.Face = moving ? walker.Face : _restFace;
                continue;
            }

            walker.Moving = true;
            if (starving)
                walker.Stride += seconds / WalkPace;
            else if (walker.Fall < 0)
                walker.Stride += walker.Stepped;

            var going = walker.Drift;
            walker.Face = walker.Offset.LengthSquared() > OffTrail * OffTrail / 4 && going != Vector2.Zero
                ? AxisOf(going, walker.Face, sticky: 0.5f)
                : _path[LegAt(walker.Along)].Face;
        }
        BuildPoses();
    }

    private void BuildPoses()
    {
        foreach (var walker in _walkers)
            _poses[walker.Id] = walker.Fall >= 0 ? new UnitPose(DropAnim, walker.Fall * DropAnimSeconds, walker.Face)
                : walker.Moving ? new UnitPose(WalkAnim, walker.Phase + walker.Stride * WorldTuning.StrideSeconds, walker.Face)
                : new UnitPose(IdleAnim, _now, walker.Face);
    }

    /// <summary>
    /// The axis of the map a way looks along, which is what the art's four facings are. A way between two axes keeps
    /// <paramref name="current"/> if that's one of them; <paramref name="sticky"/> widens that, for a way that wobbles.
    /// </summary>
    private static Vector2 AxisOf(Vector2 way, Vector2 current, float sticky = 0.7f)
    {
        var unit = Vector2.Normalize(way);
        if (Vector2.Dot(unit, current) >= sticky - 1e-3f)
            return current;
        return MathF.Abs(way.X) >= MathF.Abs(way.Y) ? new Vector2(MathF.Sign(way.X), 0) : new Vector2(0, MathF.Sign(way.Y));
    }

    /// <summary>
    /// The height at a place on the trail, as a battle walks it: straight from one tile's top to the next, with a hop on top. A step down
    /// past <see cref="WorldTuning.LongDrop"/> is walked to the edge, and then it's a fall, as <see cref="FallCue"/> falls.
    /// </summary>
    private float HeightAt(BoardTerrain terrain, double along, float hop, out float fall)
    {
        fall = -1;
        var i = LegAt(along);
        if (i == 0)
            return terrain.Top(_path[0].Tile);
        var (a, b) = (_path[i - 1], _path[i]);
        var (top, end) = (terrain.Top(a.Tile), terrain.Top(b.Tile));
        var share = (float)Math.Clamp((along - a.Along) / (b.Along - a.Along), 0, 1);
        if (top - end <= WorldTuning.LongDrop)
            return float.Lerp(top, end, share) + hop * BeatPlayer.StepHop * MathF.Sin(share * MathF.PI);
        if (share <= WorldTuning.DropEdge)
            return top;
        var air = (share - WorldTuning.DropEdge) / (1 - WorldTuning.DropEdge);
        if (share < 1)
            fall = air;
        return float.Lerp(top, end, air * air);
    }

    /// <summary>The height of the ground at a point off the trail: flat around each tile's middle, and a ramp between tiles.</summary>
    private static float GroundAt(BoardTerrain terrain, Vector2 map)
    {
        var (x, y) = (map.X - 0.5f, map.Y - 0.5f);
        var (ix, iy) = ((int)MathF.Floor(x), (int)MathF.Floor(y));
        var (fx, fy) = (Flat(x - ix), Flat(y - iy));
        float Top(int tx, int ty) => terrain.Top(new Tile(Math.Clamp(tx, 0, terrain.Map.Width - 1), Math.Clamp(ty, 0, terrain.Map.Height - 1)));
        return float.Lerp(float.Lerp(Top(ix, iy), Top(ix + 1, iy), fx), float.Lerp(Top(ix, iy + 1), Top(ix + 1, iy + 1), fx), fy);
    }

    private static float Flat(float share) => Math.Clamp((share - FlatCentre) / (1 - 2 * FlatCentre), 0, 1);

    /// <summary>A point on the path: the tile, where the middle of it is, and when and how far along the party reaches it.</summary>
    private sealed class Node(Tile tile, double time, double along, Vector2 face)
    {
        public Tile Tile { get; } = tile;

        public Vector2 At { get; } = new(tile.X + 0.5f, tile.Y + 0.5f);

        public double Time { get; } = time;

        public double Along { get; } = along;

        /// <summary>The way a walker faces on the leg that ends here.</summary>
        public Vector2 Face { get; } = face;

        /// <summary>The way the leg itself looks, before the stairstep rule.</summary>
        public Vector2 Raw { get; init; } = face;

        /// <summary>How far either side the corner here is rounded from, in tiles.</summary>
        public float Radius { get; set; }
    }

    /// <summary>One of the party, as this TV is showing them.</summary>
    private sealed class Walker(string id)
    {
        public string Id { get; } = id;

        public double Phase;              // where in a stride they start, so they don't march in step

        public double Along;              // how far along the path they are

        public Vector2 Offset;            // how far off the trail

        public Vector2 Drift;             // how fast that's changing

        public Vector2? Slot;             // where they stand at this rest, if anywhere

        public bool Placed;               // they've been on screen: from now on they walk places

        public bool Waiting;              // the trail doesn't go back far enough for them, so they stay where they are

        public Vector2 At;                // where they are

        public double Pace;               // how fast they may go along the trail, in tiles a second

        public double Before;             // how far along they were

        public double Stepped;            // how far that took them, this frame

        public Vector2 Face = Vector2.UnitX;

        public double Stride;             // tiles walked since they last stood

        public bool Moving;

        public float Fall = -1;           // how far through a drop, or below 0 on their feet
    }
}
