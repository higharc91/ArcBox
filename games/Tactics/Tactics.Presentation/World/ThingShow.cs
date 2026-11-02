using System.Collections.Immutable;
using System.Numerics;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Audio;
using Tactics.Presentation.Beats;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.World;

/// <summary>A frame of the region's things, ready to draw.</summary>
/// <param name="Units">Each thing, as a figure sorted among the walkers and the props (<see cref="BoardUnit.Thing"/>).</param>
/// <param name="Effects">The pack's effects playing over them: a puff as a door swings, smoke as a trap goes off.</param>
/// <param name="Sounds">The sounds that start this frame, by the sfx manifest's ids, to play at <see cref="ThingSounds.Db"/>.</param>
/// <param name="Plugs">
/// Where wall is drawn over a gap (<see cref="ExploreView.Plugs"/>): the server's, and one it has just taken away while its secret door is
/// still waiting to pop in, so the gap never shows open first.
/// </param>
public sealed record ThingFrame(ImmutableArray<BoardUnit> Units, ImmutableArray<FxDraw> Effects, ImmutableArray<string> Sounds,
    ImmutableArray<WallPlug> Plugs = default)
{
    /// <summary>The plugs; never a default array.</summary>
    public ImmutableArray<WallPlug> Plugs { get => field.IsDefault ? [] : field; init; } = Plugs;
}

/// <summary>
/// The things to interact with on the TV (interactables design §2.8, §2.9, §4.3): drawn where <see cref="ExploreView.Things"/> says, in their
/// state, and animated from <see cref="ExploreView.ThingEvents"/>, never from a change of state, so a TV that joins late (or comes back) draws
/// what's there and plays nothing it missed. A thing that has just gone (a pickup taken, a cache emptied) fades out where it was; one just found
/// pops in. Its picture is its own art once the set exists, else its stand-in, else the plain marker (<see cref="ThingArt"/>). Pure, and cheap
/// enough for every frame: it does nothing with a region that has no things. Godot only draws what it gives.
/// </summary>
public sealed class ThingShow
{
    /// <summary>The gap between two events of one view, so a lever moves before its gate, and an unlock before the opening.</summary>
    public const double Stagger = 0.2;

    /// <summary>How long a found thing takes to pop in.</summary>
    public const double PopSeconds = 0.35;

    /// <summary>How long a state's change takes when there's no strip to play: a squash, or a marker's slab sinking.</summary>
    public const double ChangeSeconds = 0.4;

    /// <summary>How long a thing that's gone takes to fade.</summary>
    public const double VanishSeconds = 0.45;

    /// <summary>How long the "!" of a find stays over it.</summary>
    public const double FoundSeconds = 1.6;

    private const double FlashSeconds = 0.3;
    private const double ShakeSeconds = 0.35;
    private const double HopSeconds = 0.25;
    private const float SlabHeight = 44;
    private const float SlabReach = 0.42f;      // of a tile, each way along the wall from its middle
    private const float DiamondShare = 0.7f;
    private const float LabelGap = 18;          // below the feet: just under the tile's front corner
    private const string Skull = "skull";
    private const string Found = "surprise";

    /// <summary>The marker's gold, as the story's: the palette's.</summary>
    public static readonly Rgb Gold = UnitPlan.ActiveRing;

    /// <summary>An armed trap's marker.</summary>
    public static readonly Rgb Danger = UnitPlan.EnemyRing;

    /// <summary>A disarmed or used-up thing's marker.</summary>
    public static readonly Rgb Spent = Rgb.Hex("#9aa0a6");

    private static readonly Rgb White = new(1.8f, 1.8f, 1.8f);
    private static readonly Rgb Red = new(1.6f, 0.65f, 0.6f);
    private static readonly Rgb Glow = new(1.7f, 1.5f, 0.9f);

    private sealed record Moment(string Id, string What, double Start, ThingView? From, ThingView To, ThingStrip? Strip, string? Fx, string? Sound)
    {
        public bool Sounded { get; set; }

        /// <summary>How long it has anything to show.</summary>
        public double Seconds => What switch
        {
            ThingEvents.Revealed => FoundSeconds,
            _ when Appears => Math.Max(PopSeconds, 1.0),
            ThingEvents.Taken or ThingEvents.Opened when Gone => VanishSeconds,
            _ => Math.Max(Strip?.Seconds ?? 0, 1.0),
        };

        /// <summary>The thing isn't there any more: it fades out as it was.</summary>
        public bool Gone { get; init; }

        /// <summary>The thing wasn't there before: it pops in (a find, a trap seen or set off where nothing showed).</summary>
        public bool Appears { get; init; }
    }

    private readonly BoardTerrain _terrain;
    private readonly UnitArt _art;
    private readonly ThingArt _things;
    private readonly FxArt? _fx;
    private readonly SoundBank? _sounds;
    private readonly List<ThingView> _shown = [];
    private readonly List<Moment> _moments = [];
    private readonly Dictionary<ThingView, ThingPick> _picks = [];
    private readonly Dictionary<(ThingView, Vector2), ThingMarker> _markers = [];
    private readonly Dictionary<(ThingView, Vector2, bool), ThingLabel?> _labels = [];
    private readonly List<(WallPlug Plug, Moment For)> _held = [];
    private ImmutableArray<WallPlug> _plugs = [];
    private bool _started;
    private int _seen;
    private double _clock;

    /// <summary>A cache this big is dropped and started again: things don't change often, but a session's worth mustn't pile up.</summary>
    private const int CacheLimit = 512;

    /// <summary>How far past the view, in art pixels, a thing is still drawn: a tall door or its emote comes in from the edge.</summary>
    public const float ViewMargin = 96;

    /// <summary>The events that change what a thing is drawn as. The rest (an unlock, a failure, a look) leave it as it stands.</summary>
    private static readonly ImmutableHashSet<string> Changes = [ThingEvents.Opened, ThingEvents.Toggled, ThingEvents.Sprung, ThingEvents.Disarmed,
        ThingEvents.Used, ThingEvents.Read, ThingEvents.Taken, ThingEvents.Revealed, ThingEvents.Spotted];

    /// <summary>
    /// A show on <paramref name="terrain"/>, drawing with <paramref name="things"/>. With <paramref name="captions"/> (the art check), every
    /// thing is labelled with its key and where its picture came from.
    /// </summary>
    public ThingShow(BoardTerrain terrain, UnitArt art, ThingArt things, FxArt? fx = null, SoundBank? sounds = null, bool captions = false)
        => (_terrain, _art, _things, _fx, _sounds, Captions) = (terrain, art, things, fx, sounds, captions);

    /// <summary>
    /// Every thing is labelled with its art key (without <c>interactables.</c>, which every kind's has) and where its picture came from (the art
    /// check), from the next frame.
    /// </summary>
    public bool Captions { get; set; }

    /// <summary>The things as the newest view has them, in its order.</summary>
    public IReadOnlyList<ThingView> Shown => _shown;

    /// <summary>The numbers of the events still playing, oldest first: for tests and the log.</summary>
    public IEnumerable<string> Playing => _moments.Select(m => $"{m.What} {m.Id}");

    /// <summary>
    /// Takes a view. The first one only says what's there: its events happened before this TV was watching. After that, each event newer than
    /// the last starts its moment, a <see cref="Stagger"/> after the one before it in the same view. The server's numbers go on across a
    /// restart (<c>WorldSave.ThingSeq</c>), so a view this TV missed costs nothing: what's newer than the last it played still plays, and
    /// nothing older plays again. Numbers that go back (a server from before they were kept) are taken as the new count, with nothing played.
    /// </summary>
    public void Receive(ExploreView view)
    {
        var things = view.Things is { IsDefault: false } list ? list : [];
        var events = view.ThingEvents is { IsDefault: false } e ? e : [];
        var newest = events.IsEmpty ? 0 : events.Max(x => x.Seq);
        var before = _shown.ToDictionary(t => t.Id, StringComparer.Ordinal);
        var plugsBefore = _plugs;
        _plugs = view.Plugs is { IsDefault: false } plugs ? plugs : [];
        _shown.Clear();
        _shown.AddRange(things);
        if (!_started || newest < _seen)
        {
            (_started, _seen) = (true, newest);
            return;
        }
        var now = _shown.ToDictionary(t => t.Id, StringComparer.Ordinal);
        var start = _clock;
        foreach (var happened in events.Where(x => x.Seq > _seen).OrderBy(x => x.Seq))
        {
            var was = before.GetValueOrDefault(happened.Id) ?? _moments.LastOrDefault(m => m.Id == happened.Id)?.To;
            var after = now.GetValueOrDefault(happened.Id);
            if (after is null && was is null)
                continue;       // nothing this TV was ever shown: a passage that isn't there, moved by a lever
            var moment = MomentOf(happened, start, was, after);
            _moments.Add(moment);
            // What it's drawn as from here: an unlock before an opening leaves it shut, so the opening is the one that changes it, once.
            before[happened.Id] = moment.To;
            if (moment.Appears && plugsBefore.FirstOrDefault(p => p.At == moment.To.At) is { } plug && !_plugs.Contains(plug))
                _held.Add((plug, moment));
            start += Stagger;
        }
        _seen = Math.Max(_seen, newest);
    }

    private Moment MomentOf(ThingEventView happened, double start, ThingView? was, ThingView? after)
    {
        var changes = Changes.Contains(happened.What) || was is null;
        var to = changes ? after ?? was! : was ?? after!;
        var from = happened.What == ThingEvents.Revealed ? null : was;
        var changed = changes && from is not null && after is not null && from.State != after.State;
        var strip = changed ? _things.Strip(after!, from!.State, after!.State) : null;
        var fx = happened.What switch
        {
            ThingEvents.Revealed => "fx.poof",
            ThingEvents.Sprung => "fx.smoke",
            ThingEvents.Disarmed => "fx.dust",
            ThingEvents.Opened or ThingEvents.Toggled when changed && KindOf(to)?.Family == InteractableFamily.Passage => "fx.dust",
            ThingEvents.Used when to.Kind == "shrine" || to.Kind == "altar" => "fx.bless",
            _ => null,
        };
        return new Moment(happened.Id, happened.What, start, from, to, strip, fx, ThingSounds.For(happened.What, to.Kind, to.State, _sounds))
        {
            Gone = changes && after is null,
            Appears = was is null && after is not null,
        };
    }

    private static InteractableKind? KindOf(ThingView thing) => InteractableKinds.Of(thing.Kind);

    /// <summary>
    /// Moves on by <paramref name="seconds"/> and gives the things to draw. <paramref name="pixels"/> is the walk's (<see cref="WalkShow.Pixels"/>):
    /// where feet go on the screen's pixels, so a thing stands still against the ground as the camera moves.
    /// </summary>
    /// <remarks>
    /// With <paramref name="view"/> (what the screen shows, in art pixels), only what's in it or near it is built: a region thick with things
    /// costs a frame no more than the few on screen.
    /// </remarks>
    public ThingFrame Frame(double seconds, Func<Vector2, bool, Vector2>? pixels = null, ArtArea? view = null)
    {
        _clock += seconds;
        _moments.RemoveAll(m => _clock - m.Start > m.Seconds);
        _held.RemoveAll(h => !_moments.Contains(h.For) || _clock - h.For.Start > PopSeconds / 2);
        var plugs = _held.Count == 0 ? _plugs : [.. _plugs, .. _held.Select(h => h.Plug).Distinct()];
        if (_shown.Count == 0 && _moments.Count == 0)
            return new ThingFrame([], [], [], plugs);
        var near = view is { } v ? new ArtArea(v.Min - new Vector2(ViewMargin), v.Max + new Vector2(ViewMargin)) : (ArtArea?)null;
        bool Seen(Tile at)
        {
            if (!_terrain.Map.InBounds(at))
                return false;
            if (near is not { } area)
                return true;
            var feet = Feet(at, pixels);
            return feet.X >= area.Min.X && feet.X <= area.Max.X && feet.Y >= area.Min.Y && feet.Y <= area.Max.Y;
        }

        var units = ImmutableArray.CreateBuilder<BoardUnit>();
        foreach (var thing in _shown)
        {
            if (!Seen(thing.At))
                continue;
            // A moment still to come holds the thing as it was: a gate waits for its lever, and a find stays unseen until its turn.
            var next = _moments.FirstOrDefault(m => m.Id == thing.Id && m.Start > _clock);
            var now = _moments.LastOrDefault(m => m.Id == thing.Id && !m.Gone && m.Start <= _clock);
            units.Add(Unit(next?.From ?? thing, now, pixels, waiting: next is { Appears: true } && now is null));
        }
        foreach (var gone in _moments.Where(m => m.Gone && m.Start <= _clock && _shown.All(t => t.Id != m.Id)).GroupBy(m => m.Id).Select(g => g.First()))
            if (Seen(gone.To.At))
                units.Add(Unit(gone.To, gone, pixels, waiting: false));

        var effects = ImmutableArray.CreateBuilder<FxDraw>();
        var sounds = ImmutableArray.CreateBuilder<string>();
        foreach (var moment in _moments.Where(m => m.Start <= _clock))
        {
            if (!moment.Sounded)
            {
                moment.Sounded = true;
                if (moment.Sound is { } sound)
                    sounds.Add(sound);
            }
            if (moment.Fx is { } key && _fx?.Clip(key) is { } clip && _clock - moment.Start < clip.Seconds && Seen(moment.To.At))
                effects.Add(Effect(clip, Feet(moment.To.At, pixels), _clock - moment.Start));
        }
        if (units.Count > 1)
            units.Sort((a, b) => a.Order.CompareTo(b.Order));
        return new ThingFrame(units.ToImmutable(), effects.ToImmutable(), sounds.ToImmutable(), plugs);
    }

    /// <summary>
    /// Two lists already in painter's order as one, <paramref name="first"/>'s first on a tie: the walkers and the things, each frame, without
    /// sorting them all again.
    /// </summary>
    public static ImmutableArray<BoardUnit> Merge(ImmutableArray<BoardUnit> first, ImmutableArray<BoardUnit> second)
    {
        if (second.IsDefaultOrEmpty)
            return first;
        if (first.IsDefaultOrEmpty)
            return second;
        var merged = ImmutableArray.CreateBuilder<BoardUnit>(first.Length + second.Length);
        var (i, j) = (0, 0);
        while (i < first.Length && j < second.Length)
            merged.Add(second[j].Order < first[i].Order ? second[j++] : first[i++]);
        while (i < first.Length)
            merged.Add(first[i++]);
        while (j < second.Length)
            merged.Add(second[j++]);
        return merged.MoveToImmutable();
    }

    private ThingPick PickOf(ThingView thing)
    {
        if (_picks.TryGetValue(thing, out var pick))
            return pick;
        if (_picks.Count >= CacheLimit)
            _picks.Clear();
        return _picks[thing] = _things.Pick(thing);
    }

    private Vector2 Feet(Tile at, Func<Vector2, bool, Vector2>? pixels)
    {
        var feet = UnitPlace.On(_terrain, at).Feet;
        return pixels?.Invoke(feet, true) ?? feet;
    }

    private static FxDraw Effect(FxClip clip, Vector2 feet, double t)
    {
        var frame = clip.FrameAt(t);
        return new FxDraw(clip.Path, new ArtRect(frame * clip.Width, 0, clip.Width, clip.Height),
            new Vector2(feet.X - clip.AnchorX, feet.Y - clip.Lift - clip.AnchorY), new Vector2(clip.Width, clip.Height), 0, clip.Additive, 1);
    }

    private BoardUnit Unit(ThingView thing, Moment? moment, Func<Vector2, bool, Vector2>? pixels, bool waiting)
    {
        var pick = PickOf(thing);
        var feet = Feet(thing.At, pixels);
        var t = moment is null ? double.MaxValue : _clock - moment.Start;
        var motion = waiting ? Motion.Hidden : moment is null ? Motion.Still : MotionOf(moment, t);
        var mirror = thing.Facing is "se" or "nw";

        var sprite = pick.Sprite ?? MarkerSprite;
        var anim = sprite.Idle;
        if (moment?.Strip is { } strip && t < strip.Seconds)
            (sprite, anim) = (strip.Sprite, new UnitAnim([strip.FrameAt(t)], 1, false));

        var drawn = feet + motion.Offset;
        var marker = pick.Sprite is null ? (moment is null ? StillMarker(thing, drawn) : MarkerOf(thing, moment, t, drawn)) : null;
        var flat = pick.Flat;
        var tile = OrderTile(thing, sprite);
        var look = new ThingLook(marker, LabelFor(thing, pick, feet), motion.Scale, motion.Tint, motion.Alpha);

        var emote = waiting ? null
            : moment is { What: ThingEvents.Revealed } && t < FoundSeconds ? _art.Emote(Found)
            : Warns(thing) ? _art.Emote(Skull)
            : null;
        var head = marker?.Slab is { } slab ? new Vector2(drawn.X, slab.Min(p => p.Y) - 2)
            : pick.Sprite is null ? new Vector2(drawn.X, drawn.Y - 14)
            : UnitPlan.Overhead(drawn, sprite);
        return new BoardUnit(BoardOrder.Key(tile, flat && !Shut(thing) ? BoardLayer.Ground : BoardLayer.Prop), new UnitId($"thing:{thing.Id}"), UnitTone.Neutral, drawn,
            sprite, anim, 0, 0, mirror, 0, false, [], [], null, null, [], drawn, null)
        {
            Thing = look,
            Emote = emote,
            EmoteAt = emote is null ? null : head,
        };
    }

    /// <summary>A passage shut in its wall: it stands up out of its gap.</summary>
    private static bool Shut(ThingView thing) => thing.Blocks && KindOf(thing)?.Family == InteractableFamily.Passage;

    /// <summary>
    /// The tile whose slot it sorts in. A shut passage stands in a gap of its wall, so it goes after the next tile of the wall along its run
    /// (x for sw and ne, y for se and nw), whose raised top would otherwise cut it; nobody stands there, and whoever stands in front of the
    /// gap is still drawn over it. A sprite wider than a tile goes after its whole diagonal, as a figure's does.
    /// </summary>
    public static Tile OrderTile(ThingView thing, UnitSprite sprite)
    {
        if (Shut(thing))
            return thing.Facing is "se" or "nw" ? new Tile(thing.At.X, thing.At.Y + 1) : new Tile(thing.At.X + 1, thing.At.Y);
        return sprite.Width > BoardProjection.HalfWidth * 2 ? new Tile(thing.At.X + thing.At.Y, 0) : thing.At;
    }

    /// <summary>True while it warns the party: its trap is set and seen, or it's a seen floor trap still armed.</summary>
    public static bool Warns(ThingView thing) => thing.Trapped || (thing.Kind == "trap" && thing.State == "armed");

    /// <summary>Stands in for a marker's sprite: its size is what the marker covers, for what's painted over it.</summary>
    private static readonly UnitSprite MarkerSprite = new("", 48, 64, 24, 52, 0, new UnitAnim([0], 1, true), new UnitAnim([0], 1, true), 0,
        ImmutableDictionary<string, UnitAnim>.Empty);

    private readonly record struct Motion(Vector2 Scale, Vector2 Offset, Rgb Tint, float Alpha)
    {
        public static readonly Motion Still = new(Vector2.One, Vector2.Zero, ThingLook.Plain, 1);
        public static readonly Motion Hidden = Still with { Alpha = 0 };
    }

    private static float Ease(double share) => (float)(1 - Math.Pow(1 - Math.Clamp(share, 0, 1), 3));

    private static float Fading(double t, double seconds) => (float)Math.Clamp(1 - t / seconds, 0, 1);

    private static Rgb Mix(Rgb to, float amount) => new(1 + (to.R - 1) * amount, 1 + (to.G - 1) * amount, 1 + (to.B - 1) * amount);

    private static Vector2 Shake(double t) => new((float)(2.5 * Math.Sin(t * 55) * Fading(t, ShakeSeconds)), 0);

    /// <summary>What a moment does to its thing, <paramref name="t"/> seconds in. Every one is over within a second, and ends as it stands.</summary>
    private static Motion MotionOf(Moment moment, double t)
    {
        var motion = EventMotion(moment, t);
        if (!moment.Appears)
            return motion;
        // Out of nothing, a touch too big, then settles (easeOutBack), with whatever the event does on top.
        var p = Math.Clamp(t / PopSeconds, 0, 1);
        const double Back = 1.7;
        var s = (float)(1 + (Back + 1) * Math.Pow(p - 1, 3) + Back * Math.Pow(p - 1, 2));
        return motion with { Scale = motion.Scale * s, Alpha = motion.Alpha * (float)Math.Clamp(t / (PopSeconds / 2), 0, 1) };
    }

    private static Motion EventMotion(Moment moment, double t)
    {
        switch (moment.What)
        {
            case ThingEvents.Taken:
            case ThingEvents.Opened when moment.Gone:
            {
                var p = Ease(t / VanishSeconds);
                return Motion.Still with { Offset = new Vector2(0, -12 * p), Alpha = 1 - p };
            }
            case ThingEvents.Unlocked:
                return Motion.Still with { Tint = Mix(White, Fading(t, FlashSeconds)) };
            case ThingEvents.Disarmed:
                return Motion.Still with { Tint = Mix(White, Fading(t, FlashSeconds)) };
            case ThingEvents.Failed:
                return Motion.Still with { Offset = Shake(t) };
            case ThingEvents.Spotted:
                return Motion.Still with { Tint = Mix(Red, Fading(t, FlashSeconds * 2)) };
            case ThingEvents.Sprung:
                return Motion.Still with { Offset = Shake(t), Tint = Mix(Red, Fading(t, FlashSeconds * 1.5)) };
            case ThingEvents.Used:
                return Squash(moment, t) with { Tint = Mix(Glow, Fading(t, FlashSeconds * 2)) };
            case ThingEvents.Opened:
                return Squash(moment, t) with { Tint = Mix(Glow, Fading(t, FlashSeconds)) };
            case ThingEvents.Toggled:
                return Squash(moment, t);
            case ThingEvents.Read:
            case ThingEvents.Looked:
                return Motion.Still with { Offset = new Vector2(0, (float)(-3 * Math.Sin(Math.PI * Math.Clamp(t / HopSeconds, 0, 1)))) };
            default:
                return Motion.Still;
        }
    }

    /// <summary>A change of state with no strip to show it: a squash and spring back, as a lid drops. A strip plays as it's drawn instead.</summary>
    private static Motion Squash(Moment moment, double t)
    {
        if (moment.Strip is not null || moment.From is null || moment.From.State == moment.To.State)
            return Motion.Still;
        var k = (float)(0.12 * Math.Sin(Math.PI * Math.Clamp(t / ChangeSeconds, 0, 1)));
        return Motion.Still with { Scale = new Vector2(1 + k, 1 - k) };
    }

    // ---- The plain marker -------------------------------------------------------------------------------------------------------

    /// <summary>How far a passage's slab stands: 1 shut, 0 open, and on its way between during a change.</summary>
    private static float Rise(ThingView thing) => thing.State == InteractableKinds.Closed ? 1 : 0;

    private ThingMarker MarkerOf(ThingView thing, Moment? moment, double t, Vector2 feet)
    {
        var kind = KindOf(thing);
        var (half, quarter) = (BoardProjection.HalfWidth * DiamondShare, BoardProjection.HalfHeight * DiamondShare);
        ImmutableArray<Vector2> diamond = [feet + new Vector2(0, -quarter), feet + new Vector2(half, 0), feet + new Vector2(0, quarter), feet + new Vector2(-half, 0)];
        var (line, fill) = thing.Kind == "trap"
            ? thing.State switch { "armed" => (Danger, Danger), "sprung" => (Danger, Spent), _ => (Spent, Spent) }
            : thing.State is InteractableKinds.Used or InteractableKinds.Taken ? (Spent, Spent) : (Gold, Gold);
        var fillAlpha = thing.Blocks ? 0.4f : thing.Kind == "trap" && thing.State == "sprung" ? 0.3f : 0.12f;

        ImmutableArray<Vector2>? slab = null;
        if (kind?.Family == InteractableFamily.Passage)
        {
            var rise = Rise(thing);
            if (moment is { From: { } from } && moment.Strip is null && from.State != thing.State && t < ChangeSeconds)
                rise = Rise(from) + (rise - Rise(from)) * Ease(t / ChangeSeconds);
            if (rise > 0.01f)
            {
                // Along its wall: sw and ne walls run along x, se and nw along y (art brief §4.1).
                var along = thing.Facing is "se" or "nw"
                    ? new Vector2(-BoardProjection.HalfWidth, BoardProjection.HalfHeight)
                    : new Vector2(BoardProjection.HalfWidth, BoardProjection.HalfHeight);
                var (a, b) = (feet - along * SlabReach, feet + along * SlabReach);
                var up = new Vector2(0, -SlabHeight * rise);
                slab = [a, b, b + up, a + up];
            }
        }

        ImmutableArray<Vector2>? handle = null;
        if (kind?.Family == InteractableFamily.Switch)
        {
            var on = thing.State == InteractableKinds.On ? 1f : 0f;
            if (moment is { From: { } from } && from.State != thing.State && t < ChangeSeconds)
                on = (from.State == InteractableKinds.On ? 1 : 0) + (on - (from.State == InteractableKinds.On ? 1 : 0)) * Ease(t / ChangeSeconds);
            var (foot, top) = (feet, feet + new Vector2(0, -20));
            var pivot = feet + new Vector2(0, -12);
            var angle = (-50 + 100 * on) * MathF.PI / 180;      // from up and to the left (off) to up and to the right (on)
            handle = [foot, top, pivot, pivot + new Vector2(MathF.Sin(angle), -MathF.Cos(angle)) * 14];
        }
        return new ThingMarker(diamond, slab, handle, line, fill, fillAlpha);
    }

    /// <summary>The marker of a thing with nothing playing, kept: it's the same every frame it stands still.</summary>
    private ThingMarker StillMarker(ThingView thing, Vector2 feet)
    {
        if (_markers.TryGetValue((thing, feet), out var marker))
            return marker;
        if (_markers.Count >= CacheLimit)
            _markers.Clear();
        return _markers[(thing, feet)] = MarkerOf(thing, null, double.MaxValue, feet);
    }

    private ThingLabel? LabelFor(ThingView thing, ThingPick pick, Vector2 feet)
    {
        if (_labels.TryGetValue((thing, feet, Captions), out var label))
            return label;
        if (_labels.Count >= CacheLimit)
            _labels.Clear();
        return _labels[(thing, feet, Captions)] = LabelOf(thing, pick, feet);
    }

    private ThingLabel? LabelOf(ThingView thing, ThingPick pick, Vector2 feet)
    {
        var at = feet + new Vector2(0, LabelGap);
        if (Captions)
        {
            var from = pick.Source switch
            {
                ThingSource.Art => "art",
                ThingSource.StandIn => $"stand-in: {pick.Key}",
                _ => "marker",
            };
            var key = $"{thing.Art}.{thing.State}";
            var shown = key.StartsWith($"{ThingArt.Set}.", StringComparison.Ordinal) ? key[(ThingArt.Set.Length + 1)..] : key;
            var facing = thing.Facing == "sw" ? "" : $" ({thing.Facing})";
            return new ThingLabel([shown + facing, from], at, pick.Source == ThingSource.Art ? Gold : ThingLook.Plain);
        }
        return pick.Sprite is null ? new ThingLabel([thing.Name], at, Gold) : null;
    }
}
