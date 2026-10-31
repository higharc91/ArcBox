using System.Collections.Immutable;
using Tactics.Game;
using Tactics.Logic;

namespace Tactics.Presentation.Beats;

/// <summary>How long things take on the TV, in seconds. An attack stays under about 1 s, a walk a little under the game's patience.</summary>
public static class BeatTiming
{
    /// <summary>One tile of a walk.</summary>
    public const double Step = 0.2;

    /// <summary>A pause between one beat and the next.</summary>
    public const double Rest = 0.1;

    /// <summary>The swing up to the moment of the blow.</summary>
    public const double Windup = 0.22;

    /// <summary>A melee lunge, there and back.</summary>
    public const double Lunge = 0.38;

    /// <summary>A self-targeted cast.</summary>
    public const double Cast = 0.3;

    /// <summary>A shot's shortest flight.</summary>
    public const double FlightBase = 0.1;

    /// <summary>A shot's flight, per tile.</summary>
    public const double FlightPerTile = 0.03;

    /// <summary>A shot's longest flight, wherever the target is.</summary>
    public const double FlightMost = 0.3;

    /// <summary>How long a blow lands for before anything else happens.</summary>
    public const double Impact = 0.24;

    /// <summary>Between the targets of one area effect, whose whole spread stays inside an attack's second.</summary>
    public const double Stagger = 0.06;

    /// <summary>A number rising off a unit. It overruns the beat, and fades over whatever comes next.</summary>
    public const double Pop = 0.75;

    /// <summary>A save's badge, which also overruns.</summary>
    public const double Badge = 0.8;

    /// <summary>What a save holds the beat up for.</summary>
    public const double BadgeHold = 0.2;

    /// <summary>A condition arriving.</summary>
    public const double Condition = 0.12;

    /// <summary>A unit knocked one tile.</summary>
    public const double Slide = 0.18;

    /// <summary>A unit going over an edge.</summary>
    public const double Fall = 0.4;

    /// <summary>A figure falling down.</summary>
    public const double Down = 0.3;

    /// <summary>A unit appearing or leaving.</summary>
    public const double Puff = 0.25;

    /// <summary>A zone being laid.</summary>
    public const double Zone = 0.3;

    /// <summary>A said line's hold on the beat. The voice and the caption run on past it.</summary>
    public const double Said = 0.9;

    /// <summary>The ground shaking before it gives way, for something big.</summary>
    public const double Rumble = 0.4;

    /// <summary>Time to take in what the ground did, for something big.</summary>
    public const double Collapse = 0.7;

    /// <summary>The same, for a spell's one or two tiles.</summary>
    public const double SmallRumble = 0.15;

    /// <summary>The same, for a spell's one or two tiles.</summary>
    public const double SmallCollapse = 0.2;

    /// <summary>Between the tiles of something big: a collapse alters its tiles one event at a time.</summary>
    public const double Ripple = 0.06;

    /// <summary>A boss changing phase.</summary>
    public const double Phase = 0.8;

    /// <summary>Time to see whose turn it is.</summary>
    public const double Turn = 0.2;

    /// <summary>The last beat of a battle, before Results replaces it.</summary>
    public const double Finale = 0.9;

    /// <summary>The least the "AI finished" badge stays up: a timed-out turn can be a single end of turn, over in a moment.</summary>
    public const double AiFinished = 1.5;

    /// <summary>The shortest a beat can be.</summary>
    public const double Least = 0.12;

    /// <summary>The longest an effect blocks nothing but still shows; loops have no end of their own.</summary>
    public const double BurstMost = 0.6;
}

/// <summary>Something that happens at <paramref name="At"/> seconds into a beat, for <paramref name="Length"/> seconds.</summary>
public abstract record Cue(double At, double Length);

/// <summary>The displayed battle takes this event.</summary>
public sealed record ApplyCue(double At, BattleEvent Event) : Cue(At, 0);

/// <summary>One tile of a walk.</summary>
public sealed record WalkCue(double At, double Length, UnitId Unit, Tile From, Tile To) : Cue(At, Length);

/// <summary>Knocked one tile.</summary>
public sealed record SlideCue(double At, double Length, UnitId Unit, Tile From, Tile To) : Cue(At, Length);

/// <summary>A melee swing: out toward a tile and back, playing an animation.</summary>
public sealed record LungeCue(double At, double Length, UnitId Unit, Tile From, Tile Toward, string Anim) : Cue(At, Length);

/// <summary>A unit plays an animation where it stands, optionally looking at a tile.</summary>
public sealed record PoseCue(double At, double Length, UnitId Unit, string Anim, Tile? From = null, Tile? Toward = null) : Cue(At, Length);

/// <summary>A shot in the air.</summary>
public sealed record FlightCue(double At, double Length, ShotArt Art, Tile From, Tile To) : Cue(At, Length);

/// <summary>An effect on a tile.</summary>
public sealed record BurstCue(double At, double Length, FxClip Clip, Tile Tile) : Cue(At, Length);

/// <summary>How a number pops.</summary>
public enum PopStyle { Damage, Heal, Crit }

/// <summary>A number or word rising off a tile; <paramref name="Row"/> stacks it above another.</summary>
public sealed record PopCue(double At, double Length, UnitId Unit, Tile Tile, string Text, PopStyle Style, int Row = 0) : Cue(At, Length);

/// <summary>A d20 over a unit: the die kept, in gold for a success.</summary>
public sealed record BadgeCue(double At, double Length, UnitId Unit, Tile Tile, int Die, bool Success) : Cue(At, Length);

/// <summary>A unit drops down a level or more: a fall into a chasm starts at the height it left.</summary>
public sealed record FallCue(double At, double Length, UnitId Unit, Tile Tile, int Levels) : Cue(At, Length);

/// <summary>The board shakes.</summary>
public sealed record ShakeCue(double At, double Length) : Cue(At, Length);

/// <summary>A dialogue line.</summary>
public sealed record LineCue(double At, string Speaker, string Line) : Cue(At, 0);

/// <summary>A moment worth a screenshot with <c>--shots</c>.</summary>
public sealed record MomentCue(double At, string Label) : Cue(At, 0);

/// <summary>
/// One beat as a timed list of what to show, worked out from its own events and the battle as it stood before them. Things
/// happen in the order the engine emitted them, so nothing gets ahead of what caused it: a bridge can't sink before the walk that triggers it.
/// </summary>
/// <param name="Seq">The beat.</param>
/// <param name="Cues">In time order.</param>
/// <param name="Length">How long the beat holds the stage. A number or a badge may fade on past it.</param>
public sealed record BeatScript(int Seq, ImmutableArray<Cue> Cues, double Length)
{
    private const string Attack = "attack";
    private const string Hurt = "hurt";
    private const string Dodge = "dodge";
    private const string Down = "down";
    private const string Walk = "walk";
    private const string Special = "special";
    private const string CastKey = "characters.cast";
    private const string Shoot = "shoot";
    private const string Arrow = "projectiles.arrow";
    private const string Spark = "fx.hit.spark";
    private const string Magic = "fx.hit.magic";
    private const string Puff = "fx.poof.white";
    private const string Dust = "fx.dust.earth";
    private const string Heal = "fx.heal.holy";
    private const string Crit = "fx.crit.gold";
    private const string Pillar = "fx.pillar.shadow";
    private const string Miss = "MISS";
    private const string CritWord = "CRIT";

    private static readonly ImmutableHashSet<string> Physical = ["slashing", "piercing", "bludgeoning"];

    /// <summary>The script for <paramref name="beat"/>. <paramref name="shown"/> is the battle as the player last saw it; <paramref name="live"/> is the newest view.</summary>
    public static BeatScript Of(Beat beat, BattleView shown, BattleView live, FxArt fx) => new Builder(beat, shown, live, fx).Build();

    private sealed class Builder(Beat beat, BattleView shown, BattleView live, FxArt fx)
    {
        private readonly List<Cue> _cues = [];
        private BattleView _view = shown;
        private double _t;
        private double _hold;          // time the next event waits for, kept back so a fall can start with the ground going
        private AbilityUsed? _ability;
        private int _swings;
        private int _hits;
        private int _rolls;
        private bool _rolled;          // the last thing to advance the clock was a roll, which already spaced this target from the last
        private int _areaEnd = -1;     // the last event of the area effect being played: before it, its targets resolve together
        private int _at;
        private bool _big;             // this beat alters more than a spell's tile or two: all of it is one collapse
        private bool _collapsing;

        public BeatScript Build()
        {
            _big = beat.Events.OfType<TerrainAltered>().Sum(a => a.Tiles.Length) > 2;
            for (_at = 0; _at < beat.Events.Length; _at++)
            {
                var e = beat.Events[_at];
                if (e is AbilityUsed)
                    _areaEnd = AreaEnd(_at);
                if (!PartOfTheFall(e))
                    Flush();
                Play(e);
                _view = DisplayedBattle.Apply(_view, e, live);
                if (_at == _areaEnd)
                    _t += BeatTiming.Impact;   // the stage holds once, after the last target, as it would for one
            }
            Flush();
            var cues = _cues.OrderBy(c => c.At).ToImmutableArray();
            var last = cues.IsEmpty ? 0 : cues[^1].At;
            return new BeatScript(beat.Seq, cues, Math.Max(Math.Max(_t, last), BeatTiming.Least));
        }

        // What the ground going takes with it happens together with it, not after the dust has settled.
        private bool PartOfTheFall(BattleEvent e)
            => e is Fell || (_collapsing && e is TerrainAltered or Downed or Died or UnitOut or ConditionApplied or ConditionRemoved or PassiveTriggered);

        private bool InArea => _at <= _areaEnd;

        /// <summary>
        /// The index of the last event of the ability at <paramref name="start"/> on one of its targets if it's an area effect, or
        /// -1. The events don't say which of their units the ability was aimed at, as a passive's effects run inline with nothing
        /// to mark their end, so only what can't be a side effect counts: the units other than the caster that a roll was made
        /// against. A passive's save lands on the target it was triggered by, so it can't add one. An ability with no rolls at all
        /// (a mass heal) is judged by whom it touched before anything else could join in: a passive, or a collision.
        /// </summary>
        private int AreaEnd(int start)
        {
            var source = ((AbilityUsed)beat.Events[start]).Source;
            var window = beat.Events.Skip(start + 1).TakeWhile(e => e is not (AbilityUsed or TurnEnded)).ToArray();
            var rolled = window.Select(RolledAgainst).OfType<UnitId>().Where(u => u != source).ToHashSet();
            var targets = rolled.Count > 0
                ? rolled
                : window.TakeWhile(e => e is not (PassiveTriggered or Collided)).Select(Touched).OfType<UnitId>().Where(u => u != source).ToHashSet();
            if (targets.Count < 2)
                return -1;
            var last = Array.FindLastIndex(window, e => (RolledAgainst(e) ?? Touched(e)) is { } unit && targets.Contains(unit));
            return start + 1 + last;
        }

        private static UnitId? RolledAgainst(BattleEvent e) => e switch
        {
            Rolled { Kind: RollKind.Save } save => save.Roller,
            AttackResolved attack => attack.Target,
            _ => null,
        };

        private static UnitId? Touched(BattleEvent e) => e switch
        {
            Damaged hit => hit.Unit,
            Healed healed => healed.Unit,
            ConditionApplied gained => gained.Unit,
            Downed downed => downed.Unit,
            Died died => died.Unit,
            UnitOut gone => gone.Unit,
            _ => null,
        };

        private void Flush()
        {
            if (_collapsing)
                Moment("collapsed", _hold * 0.7);
            _t += _hold;
            _hold = 0;
            _collapsing = false;
        }

        private Tile? TileOf(UnitId id) => _view.Units.FirstOrDefault(u => u.Id == id)?.At;

        private void Apply(BattleEvent e) => _cues.Add(new ApplyCue(_t, e));

        private void Burst(string? key, Tile? tile, double? at = null)
        {
            if (key is not null && tile is { } place && fx.Clip(key) is { } clip)
                _cues.Add(new BurstCue(at ?? _t, Math.Min(clip.Loop ? BeatTiming.BurstMost : clip.Seconds, BeatTiming.BurstMost), clip, place));
        }

        private void Moment(string label, double after = 0) => _cues.Add(new MomentCue(_t + after, label));

        private void Play(BattleEvent e)
        {
            switch (e)
            {
                case Moved moved:
                    PlayWalk(moved);
                    break;
                case Pushed pushed:
                    _cues.Add(new SlideCue(_t, BeatTiming.Slide, pushed.Unit, pushed.From, pushed.To));
                    _t += BeatTiming.Slide;
                    Apply(e);
                    break;
                case Fell fell:
                    PlayFall(fell);
                    break;
                case Collided collided:
                    Burst(Spark, TileOf(collided.Unit));
                    _cues.Add(new ShakeCue(_t, BeatTiming.Slide));
                    _t += BeatTiming.Slide;
                    break;
                case Teleported port:
                    Burst(Puff, port.From);
                    Burst(Puff, port.To);
                    _t += BeatTiming.Puff;
                    Apply(e);
                    break;
                case AbilityUsed used:
                    (_ability, _swings, _hits, _rolls) = (used, 0, 0, 0);
                    Swing(used);
                    break;
                case Rolled { Kind: RollKind.Attack } when _ability is { } again:
                    if (_swings > 0 && !InArea)
                    {
                        _hits = 0;
                        Swing(again);   // the next of several attacks in one action; an area's rolls are all one swing
                    }
                    _swings++;
                    break;
                case Rolled { Kind: RollKind.Save or RollKind.Contest or RollKind.Check } rolled:
                    Badge(rolled.Roller, rolled.Result);
                    break;
                case ConditionSaved saved:
                    Badge(saved.Unit, saved.Roll);
                    Apply(e);
                    break;
                case AttackResolved resolved:
                    PlayResolved(resolved);
                    break;
                case Damaged hit:
                    PlayDamage(hit);
                    break;
                case Healed healed:
                    Burst(_ability?.Presentation?.Fx is null ? Heal : null, TileOf(healed.Unit));
                    Pop(healed.Unit, $"+{healed.Amount}", PopStyle.Heal);
                    Apply(e);
                    Land();
                    break;
                case ConditionApplied or ConditionRemoved:
                    Apply(e);
                    Moment(e is ConditionApplied ? "condition" : "condition-ends", BeatTiming.Condition * 0.8);
                    _t += InArea ? 0 : BeatTiming.Condition;
                    break;
                case ConcentrationBroken broken:
                    Burst(Puff, TileOf(broken.Caster));
                    break;
                case Countered countered:
                    Burst(Magic, TileOf(countered.Caster));
                    _t += BeatTiming.Impact;
                    break;
                case Downed downed:
                    Burst(Dust, TileOf(downed.Unit));
                    Apply(e);
                    Moment("downed", BeatTiming.Down * 0.8);
                    _t += InArea ? 0 : BeatTiming.Down;
                    break;
                case Died or UnitOut:
                    PlayLoss(e, e is Died d ? d.Unit : ((UnitOut)e).Unit);
                    break;
                case Revived revived:
                    Burst(Heal, TileOf(revived.Unit));
                    Apply(e);
                    _t += BeatTiming.Puff;
                    break;
                case CountdownTicked:
                    Apply(e);
                    _t += BeatTiming.Condition;
                    break;
                case Spawned spawned:
                    Burst(Puff, spawned.At);
                    Apply(e);
                    Moment("spawn", BeatTiming.Puff * 0.8);
                    _t += BeatTiming.Puff;
                    break;
                case Despawned or Transformed:
                    Burst(Puff, TileOf(e is Despawned gone ? gone.Unit : ((Transformed)e).Unit));
                    Apply(e);
                    _t += BeatTiming.Puff;
                    break;
                case ZoneCreated:
                    Apply(e);
                    Moment("zone", BeatTiming.Zone * 0.8);
                    _t += BeatTiming.Zone;
                    break;
                case ZoneEnded:
                    Apply(e);
                    _t += BeatTiming.Condition;
                    break;
                case TerrainAltered altered:
                    PlayTerrain(altered);
                    break;
                case TriggerFired fired:
                    Moment($"trigger-{fired.Id}");
                    break;
                case Said said:
                    _cues.Add(new LineCue(_t, said.Speaker, said.Line));
                    _t += BeatTiming.Said;
                    break;
                case PhaseEntered phase:
                    PlayPhase(phase);
                    break;
                case TurnStarted:
                    Apply(e);
                    _t += BeatTiming.Turn;
                    break;
                case BattleEnded:
                    Apply(e);
                    _t += BeatTiming.Finale;
                    break;
                default:
                    Apply(e);
                    break;
            }
        }

        private void PlayWalk(Moved moved)
        {
            for (var i = 1; i < moved.Path.Length; i++)
            {
                _cues.Add(new WalkCue(_t, BeatTiming.Step, moved.Unit, moved.Path[i - 1], moved.Path[i]));
                _t += BeatTiming.Step;
                Apply(new Moved(moved.Unit, [moved.Path[i - 1], moved.Path[i]]));
            }
        }

        private void Swing(AbilityUsed used)
        {
            var from = TileOf(used.Source);
            var aim = used.Target.Unit is { } unit ? TileOf(unit) : used.Target.Tile;
            var anim = used.Presentation?.Anim ?? Attack;
            var effect = used.Presentation?.Fx;
            if (from is not { } source)
            {
                Burst(effect, aim);
                return;
            }
            if (aim is not { } target || target == source)
            {
                _cues.Add(new PoseCue(_t, BeatTiming.Cast, used.Source, anim));
                Burst(effect, source);
                _t += BeatTiming.Cast;
                return;
            }

            var reach = Math.Max(Math.Abs(target.X - source.X), Math.Abs(target.Y - source.Y));
            // The basic Attack names no projectile, whatever the weapon, and only a bow reaches that far with it.
            var plain = reach > 1 && used.Presentation?.Projectile is null && anim.EndsWith(Attack, StringComparison.Ordinal);
            if ((plain ? Arrow : used.Presentation?.Projectile) is { } key && fx.Shot(key) is { } shot)
            {
                anim = plain ? Shoot : anim;
                var flight = Math.Min(BeatTiming.FlightBase + BeatTiming.FlightPerTile * reach, BeatTiming.FlightMost);
                _cues.Add(new PoseCue(_t, BeatTiming.Windup + flight, used.Source, anim, source, target));
                _cues.Add(new FlightCue(_t + BeatTiming.Windup, flight, shot, source, target));
                _t += BeatTiming.Windup + flight;
            }
            else if (reach <= 1 && !anim.EndsWith(CastKey, StringComparison.Ordinal))
            {
                _cues.Add(new LungeCue(_t, BeatTiming.Lunge, used.Source, source, target, anim));
                _t += BeatTiming.Windup;
            }
            else
            {
                _cues.Add(new PoseCue(_t, BeatTiming.Windup, used.Source, anim, source, target));
                _t += BeatTiming.Windup;
            }
            Burst(effect, target);
            Moment("attack", -0.1);
        }

        private void Badge(UnitId unit, RollResult roll)
        {
            if (TileOf(unit) is not { } tile)
                return;
            _cues.Add(new BadgeCue(_t, BeatTiming.Badge, unit, tile, roll.Kept, roll.Success));
            Moment("save", 0.1);
            _t += _rolls++ == 0 ? BeatTiming.BadgeHold : BeatTiming.Stagger;   // a second roll is an area's: they resolve together
            _rolled = true;
        }

        private void PlayResolved(AttackResolved resolved)
        {
            if (resolved.Crit)
            {
                Burst(Crit, TileOf(resolved.Target));
                Pop(resolved.Target, CritWord, PopStyle.Crit, 1);
            }
            else if (!resolved.Hit)
            {
                Pop(resolved.Target, Miss, PopStyle.Damage);
                _cues.Add(new PoseCue(_t, BeatTiming.Impact, resolved.Target, Dodge));
                _t += InArea ? BeatTiming.Stagger : BeatTiming.Impact;
            }
        }

        private void PlayDamage(Damaged hit)
        {
            Burst(hit.Type is { } type && !Physical.Contains(type) ? Magic : Spark, TileOf(hit.Unit));
            if (hit.Amount > 0)
                Pop(hit.Unit, $"-{hit.Amount}", PopStyle.Damage);
            _cues.Add(new PoseCue(_t, BeatTiming.Impact, hit.Unit, Hurt));
            Moment("hit", 0.08);
            Apply(hit);
            Land();
        }

        // A blow takes its moment; the rest of an area effect's targets follow close behind it.
        private void Land()
        {
            if (!InArea)
                _t += _hits++ == 0 ? BeatTiming.Impact : BeatTiming.Stagger;
            else if (!_rolled)
                _t += BeatTiming.Stagger;
            _rolled = false;
        }

        private void Pop(UnitId unit, string text, PopStyle style, int row = 0)
        {
            if (TileOf(unit) is { } tile)
                _cues.Add(new PopCue(_t, BeatTiming.Pop, unit, tile, text, style, row));
        }

        private void PlayFall(Fell fell)
        {
            if (TileOf(fell.Unit) is not { } tile)
                return;
            _cues.Add(new FallCue(_t, BeatTiming.Fall, fell.Unit, tile, fell.Levels));
            if (!fell.IntoDeep)
                Burst(Dust, tile, _t + BeatTiming.Fall);
            if (_hold < BeatTiming.Fall)
                _hold = BeatTiming.Fall;
        }

        private void PlayLoss(BattleEvent e, UnitId unit)
        {
            _cues.Add(new PoseCue(_t, BeatTiming.Down, unit, Down));
            var fallen = _t + BeatTiming.Down;
            Burst(Puff, TileOf(unit), fallen);
            _cues.Add(new ApplyCue(fallen, e));
            _t = InArea ? _t : fallen;     // in an area they all fall together, and the others carry on
        }

        private void PlayTerrain(TerrainAltered altered)
        {
            if (_collapsing)
            {
                _t += BeatTiming.Ripple;
            }
            else
            {
                var rumble = _big ? BeatTiming.Rumble : BeatTiming.SmallRumble;
                _cues.Add(new ShakeCue(_t, rumble + (_big ? BeatTiming.Collapse : BeatTiming.Slide)));
                _t += rumble;
                _collapsing = _big;
            }
            foreach (var tile in altered.Tiles)
                Burst(Dust, tile);
            Apply(altered);
            Moment(_big ? "collapse" : "terrain", 0.04);
            _hold = _big ? BeatTiming.Collapse : BeatTiming.SmallCollapse;
        }

        private void PlayPhase(PhaseEntered phase)
        {
            _cues.Add(new PoseCue(_t, BeatTiming.Phase, phase.Unit, Special));
            _cues.Add(new ShakeCue(_t, BeatTiming.Phase * 0.6));
            Burst(Pillar, TileOf(phase.Unit));
            Moment("phase", BeatTiming.Phase * 0.5);
            _t += BeatTiming.Phase;
        }
    }

    /// <summary>The way a step from one tile to another goes, for a unit's facing.</summary>
    internal static System.Numerics.Vector2 Way(Tile from, Tile to) => new(to.X - from.X, to.Y - from.Y);

    internal const string WalkAnim = Walk;
    internal const string HurtAnim = Hurt;
}
