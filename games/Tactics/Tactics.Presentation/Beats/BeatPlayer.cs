using System.Collections.Immutable;
using System.Numerics;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Audio;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.Beats;

/// <summary>A line to say: <paramref name="Speaker"/> is who it's by, or null for the storyteller, in voice theme <paramref name="Theme"/>.</summary>
public sealed record SpokenLine(string? Speaker, string Text, string Theme, string? Clip = null);

/// <summary>One image to draw over the board, in art pixels.</summary>
/// <param name="Path">Relative to the pack's root.</param>
/// <param name="Source">The rectangle of the image.</param>
/// <param name="TopLeft">Where it goes.</param>
/// <param name="Size">How big.</param>
/// <param name="Rotation">Radians around its centre.</param>
/// <param name="Additive">Blended by adding light.</param>
/// <param name="Alpha">How opaque.</param>
public sealed record FxDraw(string Path, ArtRect Source, Vector2 TopLeft, Vector2 Size, float Rotation, bool Additive, float Alpha)
{
    /// <summary>It's a mark for the people watching, a number or a die badge, rather than part of the scene: it goes when the units' marks do (<see cref="MarkRule"/>).</summary>
    public bool Mark { get; init; }
    public bool White { get; init; }
    public ImmutableArray<SpellLight> Lights { get; init; } = [];
    /// <summary>An optional tint for recovery effects; ordinary effects retain their existing white modulation.</summary>
    public Rgb? Tint { get; init; }

    /// <summary>A transient surface sorts into the board here; other effects draw over the scene.</summary>
    public long? Order { get; init; }

    /// <summary>Stage art stays in battle pixels, but sorts beside figures and scenery on the region's tiles.</summary>
    public FxDraw AtWorldOrigin(Tile origin) => Order is { } order
        ? this with { Order = order + BoardOrder.Key(origin, BoardLayer.Under) }
        : this;
}

/// <summary>Where a number that pops up over a unit is drawn this moment, in art pixels.</summary>
/// <param name="Unit">The unit it's about.</param>
/// <param name="TopLeft">Its top left.</param>
/// <param name="Size">How big, all its glyphs together.</param>
public sealed record PopBox(UnitId Unit, Vector2 TopLeft, Vector2 Size);

/// <summary>What one moment of a beat looks like, for the units and over them.</summary>
/// <param name="Places">Units not standing squarely on a tile.</param>
/// <param name="Poses">Units doing something.</param>
/// <param name="Draws">Effects, numbers and badges.</param>
/// <param name="Shake">How far the board is off its place, in art pixels.</param>
public sealed record BeatFrame(ImmutableDictionary<UnitId, UnitPlace> Places, ImmutableDictionary<UnitId, UnitPose> Poses, ImmutableArray<FxDraw> Draws, Vector2 Shake)
{
    /// <summary>The numbers popping up now, for <see cref="MarkRule.ClearOfPops"/>: a Downed count beside one would read as part of it.</summary>
    public ImmutableArray<PopBox> Pops { get; init; } = [];
    public ImmutableHashSet<UnitId> Flashes { get; init; } = [];
    public ImmutableArray<SpellLight> Lights { get; init; } = [];
}

/// <summary>The TV dropped back to the newest beat.</summary>
/// <param name="Seq">The beat it went to.</param>
/// <param name="Skipped">The beats since the last one this TV played, which it won't play. A fresh scene has played none.</param>
public sealed record BeatCatchUp(int Seq, int Skipped);

/// <summary>What one <see cref="BeatPlayer.Tick"/> did.</summary>
/// <param name="Shown">Beats that finished, for the <c>shown</c> signal: at most one.</param>
/// <param name="Lines">Lines to say now.</param>
/// <param name="Moments">Moments worth a screenshot.</param>
/// <param name="BoardChanged">The displayed board may not be what it was: its plan must be checked.</param>
/// <param name="Busy">Something on screen is moving, so the units need redrawing.</param>
/// <param name="Started">A beat that began, with how long it's scripted to take.</param>
/// <param name="CaughtUp">A catch-up since the last tick, if there was one.</param>
public sealed record BeatTick(ImmutableArray<int> Shown, ImmutableArray<SpokenLine> Lines, ImmutableArray<string> Moments, bool BoardChanged, bool Busy, BeatScript? Started, BeatCatchUp? CaughtUp = null)
{
    /// <summary>Sounds this tick reached, by id.</summary>
    public ImmutableArray<string> Sounds { get; init; } = [];
}

/// <summary>
/// Plays a battle's beats one at a time, from their own events, and keeps the battle as the player has seen it
/// (<see cref="Shown"/>). The newest view is the truth only when nothing's left to play: then <see cref="Shown"/> takes it
/// whole, so any error heals. The TV sends <c>shown</c> for what comes out of <see cref="Tick"/>, but nothing here waits for
/// the game, and the game's fallback timer doesn't wait for this: a beat that runs long only makes the picture lag.
/// <para>
/// When that lag can't be played off, the TV catches up (<see cref="BeatQueue.Late"/>): it takes the view as it stands, drops every
/// beat but the newest, and plays that one from just before it, so what's on the board still leads into the beat. That covers a
/// scene joining a fight, a reconnect that missed beats, and a TV too slow to keep up. A beat on stage finishes first. A
/// dropped beat is never reported, so the game only hears about the newest, which is what it's waiting on.
/// </para>
/// </summary>
public sealed class BeatPlayer(FxArt fx, UnitArt art, SoundBank? sounds = null, VoiceClips? voices = null)
{
    private const double LongestFrame = 0.1;         // a hitch slows a beat down, rather than skipping it
    private const double SecondsPerWord = 0.36;      // the narrator's own reading pace
    private const double CaptionExtra = 1.2;
    private const float NumberScale = 2;
    private const float PopRise = 16;
    private const float PopFloat = 12;
    private const float PopRow = 22;
    private const float BadgeRise = 56;
    private const float LungeReach = 0.45f;

    /// <summary>How high a stride lifts a walking unit, in height steps.</summary>
    public const float StepHop = 0.15f;

    private const float ShakeSize = 2.5f;
    private const float BadgeScale = 1;

    /// <summary>The moment, for <c>--shots</c>, that a beat the AI finished starts.</summary>
    public const string AiFinishedMoment = "ai-finished";

    /// <summary>The moment, for <c>--shots</c>, that a reaction offer reaches the screen.</summary>
    public const string ReactionMoment = "reaction";

    private readonly BeatQueue _queue = new();
    private readonly List<(Cue Cue, double Start)> _fading = [];
    private readonly Queue<SpokenLine> _unread = [];     // lines waiting their turn, as the voice speaks one at a time
    private BattleView? _live;
    private BeatScript? _script;
    private readonly List<AbilityUsed> _waitingSpells = [];
    private BeatCatchUp? _caughtUp;
    private int _next;
    private int _played;        // the last beat begun
    private double _cinematicClock;
    private CutInPlayback? _cutIn;
    private readonly CinematicBudget _cinematicBudget = new();
    private CutInFrame? _carriedCamera;
    private double _clock;      // into the current beat
    private double _now;        // since the start, as beats see it: a hitch doesn't count past LongestFrame
    private double _real;       // since the start, as the game sees it
    private double _rest;
    private double _captionUntil;
    private double _captionRead;      // when the caption's line has been said, so the next can come up
    private double _aiFinishedUntil;
    private bool _dirty;
    private bool _aiBegan;
    private int? _opening;

    /// <summary>The battle as the player has seen it, or null before the first view.</summary>
    public BattleView? Shown { get; private set; }

    /// <summary>The line to show, while it's still being read.</summary>
    public SpokenLine? Caption { get; private set; }

    /// <summary>The newest view, which runs ahead of <see cref="Shown"/>.</summary>
    public BattleView? Live => _live;

    /// <summary>The unit targeted by the currently displayed action; never taken from a future queued beat.</summary>
    public UnitId? Target { get; private set; }


    /// <summary>Optional TV framing factory. Without it the original playback timing is byte-for-byte unchanged.</summary>
    public Func<Beat, BattleView, BeatScript, CutInPlayback?>? CutIn { get; set; }

    public CutInFrame? Camera => _cutIn?.Frame(_cinematicClock) ?? _carriedCamera;
    public CutInFrame? CarriedCamera => _carriedCamera;
    public void ClearCinematics() => (_cutIn, _carriedCamera) = (null, null);
    public ImmutableArray<Tile> Telegraph => _cutIn?.Telegraph(_cinematicClock) ?? [];
    public string? AbilityBanner => _cutIn?.Banner(_cinematicClock);
    public bool EnhancedEffects { get; set; }
    public bool ReducedEffects { get; set; }
    private double FadeLength(Cue cue) => cue.Length * (ReducedEffects && cue is BurstCue or HazardCue ? 0.6 : 1);

    /// <summary>The unit to badge: its turn timed out, or its player left, and the AI is finishing it on screen.</summary>
    public UnitId? AiFinished { get; private set; }

    /// <summary>True when nothing's left to play.</summary>
    public bool Idle => _script is null && _queue.Count == 0;

    /// <summary>The beats waiting behind the one playing.</summary>
    public int Waiting => _queue.Count;

    /// <summary>
    /// A new view: its unseen beats are queued, and if nothing's playing it's simply what's on screen. The first view starts
    /// from before what it holds, as it's already past those beats.
    /// </summary>
    public void Receive(BattleView live)
    {
        if (_queue.Offer(live.Beats, _real))
        {
            (_script, Shown, _cutIn, _carriedCamera) = (null, null, null, null);
            _cinematicBudget.Reset();
            _waitingSpells.Clear();
        }
        _live = live;
        if (_script is null && _queue.Late(_real))
            CatchUp(live);
        Shown ??= Unplayed(live);
        if (Idle)
            Shown = live;
    }

    private void CatchUp(BattleView live)
    {
        _waitingSpells.Clear();
        _queue.KeepNewest();
        ClearCinematics();
        _fading.Clear();
        (AiFinished, _aiBegan, _rest, _dirty, Shown) = (null, false, 0, true, Unplayed(live));
        if (_queue.Waiting.LastOrDefault() is { } newest)
            _caughtUp = new BeatCatchUp(newest.Seq, Math.Max(0, newest.Seq - _played - 1));
    }

    private BattleView Unplayed(BattleView live) => _queue.Waiting.Reverse().Aggregate(live, DisplayedBattle.Before);

    /// <summary>
    /// Forgets the battle, as when it's over, or for a spell without a board. The next view counts as a first one, so if the
    /// battle's still on, it's a catch-up: this can't bring old beats back.
    /// </summary>
    public void Reset()
    {
        _waitingSpells.Clear();
        _queue.Clear();
        _cinematicBudget.Reset();
        _fading.Clear();
        (_script, _live, Shown, Caption, _rest, AiFinished, _caughtUp, _cutIn) = (null, null, null, null, 0, null, null, null);
        _unread.Clear();
        ClearCinematics();
    }

    /// <summary>Moves on by <paramref name="delta"/> seconds.</summary>
    public BeatTick Tick(double delta)
    {
        var seconds = Math.Min(delta, LongestFrame);
        _now += seconds;
        _real += delta;
        var faded = _fading.RemoveAll(f => _now >= f.Start + FadeLength(f.Cue)) > 0;
        if (_unread.Count > 0 && _now >= _captionRead)
            Caption = Captioned(_unread.Dequeue());
        else if (Caption is not null && _now >= _captionUntil)
            Caption = null;

        var redraw = _dirty || faded;     // whatever just went is still in the last frame drawn
        _dirty = false;
        if (AiFinished is not null && _now >= _aiFinishedUntil)
            (AiFinished, redraw) = (null, true);

        var board = Shown?.Board;
        var shown = ImmutableArray<int>.Empty;
        var lines = ImmutableArray.CreateBuilder<SpokenLine>();
        var moments = ImmutableArray.CreateBuilder<string>();
        var heard = ImmutableArray.CreateBuilder<string>();
        BeatScript? started = null;

        if (_rest > 0)
        {
            _rest -= seconds;
        }
        else if (Begin() is { } script)
        {
            started = script;
            if (_aiBegan)
                moments.Add(AiFinishedMoment);
            _aiBegan = false;
        }

        if (_script is { } playing)
        {
            _cinematicClock += seconds;
            _clock = _cutIn?.ScriptTime(_cinematicClock) ?? _clock + seconds;
            for (; _next < playing.Cues.Length && playing.Cues[_next].At <= _clock
                && (_cutIn is null || _cutIn.CanReach(_cinematicClock, playing.Cues[_next].At)); _next++)
                Reach(playing.Cues[_next], lines, moments, heard);
            if (_cutIn is { } cinematic ? cinematic.Finished(_cinematicClock) : _clock >= playing.Length)
            {
                shown = [playing.Seq];
                _carriedCamera = _cutIn is { HoldsForReaction: true } ? _cutIn.Frame(_cinematicClock) : null;
                _script = null;
                _cutIn = null;
                _rest = BeatTiming.Rest;
                if (_queue.Count == 0 && _live is not null)
                    Shown = _live;     // the view's units are the truth now: whatever drifted heals here
            }
        }

        var caughtUp = _caughtUp;
        _caughtUp = null;
        return new BeatTick(shown, lines.ToImmutable(), moments.ToImmutable(), caughtUp is not null || !DisplayedBattle.SameBoard(board, Shown?.Board),
            _script is not null || _fading.Count > 0 || !shown.IsEmpty || redraw || _dirty || AiFinished is not null, started, caughtUp) { Sounds = heard.ToImmutable() };
    }

    private BeatScript? Begin()
    {
        if (_script is not null || Shown is null || _live is null)
            return null;
        if (_queue.Late(_real))
            CatchUp(_live);
        if (_queue.Next() is not { } beat)
            return null;
        beat = ResumeSpell(beat);
        (_opening, _played) = (beat.Seq, beat.Seq);     // if working out its script throws, it's already off the queue
        (_script, _next, _clock, _cinematicClock) = (BeatScript.Of(beat, Shown, _live, fx, sounds), 0, 0, 0);
        _cutIn = _cinematicBudget.Apply(Shown.Round, Shown.Units.Count(u => u.Side == nameof(Side.Party)), CutIn?.Invoke(beat, Shown, _script));
        _opening = null;
        Target = null;
        if (beat.Actor is { } actor && beat.Events.Any(e => e.AiFinished))
            (AiFinished, _aiFinishedUntil, _aiBegan) = (actor, _now + Math.Max(_script.Length, BeatTiming.AiFinished), true);
        return _script;
    }

    // The engine already paid for and announced this spell in the parent batch. Only its presentation resumes here.
    private Beat ResumeSpell(Beat beat)
    {
        var open = BeatScript.OpenSpellOffer(beat);
        if (_waitingSpells.Count == 0 && open is null) return beat;
        var events = beat.Events;
        _waitingSpells.RemoveAll(a => events.OfType<Countered>().Any(c => c.Caster == a.Source && c.Ability == a.Ability));
        var additions = new Dictionary<int, List<AbilityUsed>>();
        foreach (var held in _waitingSpells.AsEnumerable().Reverse().ToArray())
        {
            if (events.OfType<AbilityUsed>().Any(a => a.Source == held.Source && a.Ability == held.Ability))
            {
                _waitingSpells.Remove(held);
                continue;
            }
            // One wizard can counter a counter to their own spell: source alone cannot identify that spell's result.
            var anotherCast = events.OfType<AbilityUsed>().Any(a => a.Source == held.Source && a.Ability != held.Ability);
            var index = -1;
            if (!anotherCast)
                for (var i = 0; i < events.Length && index < 0; i++)
                    if (events[i] is Countered c && c.By == held.Source
                        || events[i] is Rolled { Kind: RollKind.Check } r && r.Roller == held.Source) index = i;
            if (index < 0)
            {
                // A new nested window keeps earlier incantations suspended, rather than replacing them.
                if (open is not null || !events.OfType<Reacted>().Any()) continue;
                index = 0;
                for (var i = 0; i < events.Length; i++)
                    if (events[i] is Reacted or Countered or Rolled { Kind: RollKind.Check }) index = i + 1;
            }
            if (!additions.TryGetValue(index, out var here)) additions[index] = here = [];
            here.Add(held);
            _waitingSpells.Remove(held);
        }
        if (additions.Count > 0)
        {
            var resumed = ImmutableArray.CreateBuilder<BattleEvent>();
            for (var i = 0; i <= events.Length; i++)
            {
                if (additions.TryGetValue(i, out var here)) resumed.AddRange(here);
                if (i < events.Length) resumed.Add(events[i]);
            }
            beat = beat with { Events = resumed.ToImmutable() };
        }
        if (open is not null)
        {
            var announced = events.OfType<AbilityUsed>().LastOrDefault(a => a.Source == open.Actor && a.Ability == open.Ability);
            if (announced is not null && !_waitingSpells.Any(a => a.Source == announced.Source && a.Ability == announced.Ability))
                _waitingSpells.Add(announced);
        }
        return beat;
    }

    private void Reach(Cue cue, ImmutableArray<SpokenLine>.Builder lines, ImmutableArray<string>.Builder moments, ImmutableArray<string>.Builder heard)
    {
        switch (cue)
        {
            case ApplyCue apply when Shown is not null && _live is not null:
                Target = apply.Event switch
                {
                    AbilityUsed used => used.Target.Unit,
                    AttackResolved hit => hit.Target,
                    Damaged hit => hit.Unit,
                    Healed healed => healed.Unit,
                    ConditionApplied condition => condition.Unit,
                    ConditionRemoved condition => condition.Unit,
                    TurnStarted or TurnEnded or BattleEnded => null,
                    _ => Target,
                };
                Shown = DisplayedBattle.Apply(Shown, apply.Event, _live);
                break;
            case FocusCue focus:
                Target = focus.Unit;
                break;
            case LineCue line:
                lines.Add(Say(line));
                break;
            case MomentCue moment:
                moments.Add(moment.Label);
                break;
            case SoundCue sound:
                heard.Add(sound.Id);
                break;
            case BurstCue or PopCue or BadgeCue or FlightCue or HazardCue:
                _fading.Add((cue, _now - (_clock - cue.At)));
                break;
        }
    }

    private SpokenLine Say(LineCue line)
    {
        var narrator = line.Speaker == TacticsVoices.NarratorSpeaker;
        var clip = Shown is { } battle ? voices?.For(battle.Encounter, line.Speaker, line.Line) : null;
        var said = new SpokenLine(narrator ? null : Shown?.Units.FirstOrDefault(u => u.Id.Value == line.Speaker)?.Name ?? Words.Title(line.Speaker), line.Line,
            TacticsVoices.ForSpeaker(line.Speaker), clip?.Path);
        if (Caption is null || _now >= _captionRead)
            Caption = Captioned(said);
        else
            _unread.Enqueue(said);
        return said;
    }

    private SpokenLine Captioned(SpokenLine line)
    {
        _captionRead = _now + line.Text.Split(' ').Length * SecondsPerWord;
        _captionUntil = _captionRead + CaptionExtra;
        return line;
    }

    /// <summary>This moment of the beat, on <paramref name="terrain"/> as it's drawn.</summary>
    public BeatFrame Frame(BoardTerrain terrain)
    {
        var places = ImmutableDictionary.CreateBuilder<UnitId, UnitPlace>();
        var poses = ImmutableDictionary.CreateBuilder<UnitId, UnitPose>();
        var shake = Vector2.Zero;
        var flashes = ImmutableHashSet.CreateBuilder<UnitId>();
        var sampled = _clock;
        if (EnhancedEffects && !ReducedEffects && _script is { } active)
        {
            var impact = active.Cues.OfType<ImpactCue>().LastOrDefault(c => c.At <= _clock && _clock < c.At + c.Length && c.Amount >= 12);
            if (impact is not null)
            {
                var into = _clock - impact.At;
                var freeze = Math.Clamp(impact.Amount / 10, 2, 4) / 60.0;
                sampled = impact.At + Math.Max(0, into - freeze) * impact.Length / (impact.Length - freeze);
            }
        }
        if (_script is { } script)
            foreach (var cue in script.Cues.Where(c => c.At <= sampled && sampled < c.At + c.Length))
            {
                var (into, share) = (sampled - cue.At, (float)((sampled - cue.At) / cue.Length));
                switch (cue)
                {
                    case WalkCue walk:
                        places[walk.Unit] = Between(terrain, walk.From, walk.To, share, StepHop * MathF.Sin(share * MathF.PI));
                        poses[walk.Unit] = new UnitPose(BeatScript.WalkAnim, _clock, BeatScript.Way(walk.From, walk.To));   // a stride outlasts a tile
                        break;
                    case SlideCue slide:
                        places[slide.Unit] = Between(terrain, slide.From, slide.To, share, 0);
                        poses[slide.Unit] = new UnitPose(BeatScript.HurtAnim, into);
                        break;
                    case LungeCue lunge:
                        places[lunge.Unit] = Between(terrain, lunge.From, lunge.Toward, LungeReach * MathF.Sin(share * MathF.PI), 0);
                        poses[lunge.Unit] = new UnitPose(lunge.Anim, into, BeatScript.Way(lunge.From, lunge.Toward));
                        break;
                    case PoseCue pose:
                        poses[pose.Unit] = new UnitPose(pose.Anim, into + (_cutIn?.PoseLead(_cinematicClock, pose.At) ?? 0),
                            pose is { From: { } from, Toward: { } toward } ? BeatScript.Way(from, toward) : null);
                        break;
                    case FallCue fall:
                        var ground = UnitPlace.On(terrain, fall.Tile);
                        var top = fall.Levels + (terrain.Look(fall.Tile).Void ? BoardTerrain.VoidSink : 0);
                        places[fall.Unit] = ground with { Height = ground.Height + top * (1 - share * share) };
                        break;
                    case ImpactCue impact when EnhancedEffects && !ReducedEffects:
                        if (_clock - cue.At < 2.0 / 60) flashes.Add(impact.Unit);
                        var kick = Math.Clamp(impact.Amount / 12f, 0.5f, 3f) * (1 - share);
                        shake += new Vector2(kick * MathF.Sin((float)into * 75), kick * .4f * MathF.Cos((float)into * 51));
                        break;
                    case ShakeCue:
                        var fade = ShakeSize * (1 - share);
                        shake = new Vector2(fade * MathF.Sin((float)into * 70), fade * 0.5f * MathF.Cos((float)into * 53));
                        break;
                }
            }

        var draws = ImmutableArray.CreateBuilder<FxDraw>();
        var pops = ImmutableArray.CreateBuilder<PopBox>();
        foreach (var (cue, start) in _fading)
        {
            var age = _now - start - (_clock - sampled);
            if (age >= 0) Draw(cue, age * (ReducedEffects && cue is BurstCue or HazardCue ? 1 / 0.6 : 1), terrain, draws, pops);
        }
        return new BeatFrame(places.ToImmutable(), poses.ToImmutable(), draws.ToImmutable(), ReducedEffects ? Vector2.Zero : shake)
        {
            Pops = pops.ToImmutable(), Flashes = flashes.ToImmutable(),
            Lights = EnhancedEffects ? [.. draws.SelectMany(d => d.Lights).OrderByDescending(l => l.Power).Take(12)
                .Select(l => ReducedEffects ? l with { Power = l.Power * .75f } : l), .. SpellLighting.ForZones(terrain, _now, ReducedEffects)] : []
        };
    }

    private static UnitPlace Between(BoardTerrain terrain, Tile from, Tile to, float share, float hop)
    {
        var (a, b) = (UnitPlace.On(terrain, from), UnitPlace.On(terrain, to));
        var at = Vector2.Lerp(a.Map, b.Map, share);
        return new UnitPlace(at, RoadSurface.TryHeight(terrain, at, out var height)
            ? height : a.Height + (b.Height - a.Height) * share + hop);
    }

    private void Draw(Cue cue, double into, BoardTerrain terrain, ImmutableArray<FxDraw>.Builder draws, ImmutableArray<PopBox>.Builder pops)
    {
        var share = (float)(into / cue.Length);
        switch (cue)
        {
            case HazardCue hazard when fx.Hazard(hazard.Hazard) is { } art:
                var centre = terrain.Centre(hazard.Tile);
                // A quick arrival and a soft release avoid the whole water patch blinking off together.
                var alpha = (float)Math.Min(1, Math.Min(into / 0.04, (hazard.Length - into) / 0.18));
                void Layer(ArtImage image, Vector2 size, Vector2 origin, BoardLayer layer)
                {
                    var frame = (int)(into * image.Fps) % image.Frames;
                    draws.Add(new FxDraw(image.Path, new ArtRect(frame * (int)size.X, 0, (int)size.X, (int)size.Y),
                        centre - origin, size, 0, false, Math.Clamp(alpha, 0, 1))
                    {
                        Order = BoardOrder.Key(hazard.Tile, layer),
                        Lights = layer == BoardLayer.Ground && hazard.Hazard == "electrified"
                            ? [SpellEmission.ForAsset("fx.shock.storm", true)!.At(centre - new Vector2(0, 5), centre, Math.Clamp(alpha, 0, 1))] : []
                    });
                }
                Layer(art.Ground, art.Frame, art.Origin, BoardLayer.Ground);
                if (art.Over is { } over)
                    Layer(over, art.OverFrame, art.OverOrigin, BoardLayer.Over);
                break;
            case BurstCue burst:
                DrawBurst(burst, into, terrain, draws);
                break;
            case FlightCue flight:
                DrawFlight(flight, into, share, terrain, draws);
                break;
            case PopCue pop:
                DrawPop(pop, share, terrain, draws, pops);
                break;
            case BadgeCue badge:
                DrawBadge(badge, share, terrain, draws);
                break;
        }
    }

    private static void DrawBurst(BurstCue burst, double into, BoardTerrain terrain, ImmutableArray<FxDraw>.Builder draws)
    {
        var clip = burst.Clip;
        var from = terrain.Centre(burst.Tile);
        var way = burst.Toward is { } toward ? terrain.Centre(toward) - from : Vector2.UnitX;
        var angle = MathF.Atan2(way.Y, way.X);

        var progress = (float)Math.Clamp(into / burst.Length, 0, 1);
        var scale = burst.Scale * (burst.Steam ? 1 + 0.18f * progress : 1);
        var fade = Math.Clamp((progress - 0.15f) / 0.85f, 0, 1);
        var alpha = burst.Alpha * (burst.Steam ? 1 - fade * fade * (3 - 2 * fade) : 1);
        var rise = burst.Steam ? 12 * progress : 0;
        var seconds = burst.Steam ? into * clip.Seconds / burst.Length : into;

        // Drawing turns around the frame's middle, so its anchor is put where it should land after the turn.
        var size = new Vector2(clip.Width, clip.Height) * scale;
        var anchor = Vector2.Transform(new Vector2(clip.AnchorX, clip.AnchorY) * scale - size / 2, Matrix3x2.CreateRotation(angle));
        var at = from - new Vector2(0, clip.Lift + rise) - anchor - size / 2;
        draws.Add(new FxDraw(clip.Path, new ArtRect(clip.FrameAt(seconds) * clip.Width, 0, clip.Width, clip.Height), at, size, angle, clip.Additive, alpha)
        {
            Lights = BurstLights(clip, from, angle, scale, (float)Math.Clamp(into / .08, 0, 1) * (1 - progress) * alpha)
        });
    }

    private static ImmutableArray<SpellLight> BurstLights(FxClip clip, Vector2 from, float angle, float scale, float envelope)
    {
        if (clip.Emission is not { } emission) return [];
        var lift = new Vector2(0, clip.Lift);
        if (!clip.Aimed) return [emission.At(from - lift, from, envelope)];
        // The luminous line/cone runs along the actual rotated art, rather than pooling only at the caster's feet.
        var span = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (clip.Width - clip.AnchorX) * scale;
        return [.. new[] { .15f, .5f, .85f }.Select(share =>
            (emission with { Power = emission.Power * .55f }).At(from - lift + span * share, from + span * share, envelope))];
    }

    private static void DrawFlight(FlightCue flight, double into, float share, BoardTerrain terrain, ImmutableArray<FxDraw>.Builder draws)
    {
        if (share >= 1)
            return;
        var shot = flight.Art;
        var lift = new Vector2(0, 15);
        var (from, to) = (terrain.Centre(flight.From) - lift, terrain.Centre(flight.To) - lift);
        var way = to - from;
        var angle = MathF.Atan2(way.Y, way.X);
        var row = shot.Headings > 1 ? ShotArt.Row(angle, shot.Headings) : 0;
        var column = (int)(into * shot.Fps) % shot.Frames;
        var size = new Vector2(shot.Width, shot.Height);
        draws.Add(new FxDraw(shot.Path, new ArtRect(column * shot.Width, row * shot.Height, shot.Width, shot.Height), Vector2.Lerp(from, to, share) - size / 2, size,
            shot.Headings > 1 ? 0 : angle, shot.Additive, 1)
        {
            Lights = shot.Emission is { } emission ? [emission.At(Vector2.Lerp(from, to, share), Vector2.Lerp(from, to, share) + lift,
                Math.Min(1, Math.Min(share * 6, (1 - share) * 6)))] : []
        });
    }

    private void DrawPop(PopCue pop, float share, BoardTerrain terrain, ImmutableArray<FxDraw>.Builder draws, ImmutableArray<PopBox>.Builder pops)
    {
        var style = pop.Style switch
        {
            PopStyle.Heal => "heal",
            PopStyle.Crit => "crit",
            _ => "damage",
        };
        ImmutableArray<(ArtRect Rect, int Advance)> glyphs = [.. Pieces(pop.Text).Select(g => art.Glyph(style, g))];
        var width = glyphs.Sum(g => g.Advance) * NumberScale;
        var centre = terrain.Centre(pop.Tile) - new Vector2(0, UnitReach + PopRise + PopFloat * share + pop.Row * PopRow);
        var alpha = share < 0.7f ? 1 : (1 - share) / 0.3f;
        var pen = centre.X - width / 2;
        var (right, height) = (pen, 0f);
        foreach (var (rect, advance) in glyphs)
        {
            var size = new Vector2(rect.Width, rect.Height) * NumberScale;
            draws.Add(new FxDraw(UnitArt.NumbersPath, rect, new Vector2(pen, centre.Y - size.Y / 2), size, 0, false, alpha) { Mark = true });
            (right, height) = (MathF.Max(right, pen + size.X), MathF.Max(height, size.Y));
            pen += advance * NumberScale;
        }
        var left = centre.X - width / 2;
        pops.Add(new PopBox(pop.Unit, new Vector2(left, centre.Y - height / 2), new Vector2(right - left, height)));
    }

    private void DrawBadge(BadgeCue badge, float share, BoardTerrain terrain, ImmutableArray<FxDraw>.Builder draws)
    {
        var die = badge.Success ? art.DieGold : art.DieDark;
        var size = new Vector2(die.Width, die.Height) * BadgeScale;
        var alpha = share < 0.8f ? 1 : (1 - share) / 0.2f;
        var centre = terrain.Centre(badge.Tile) - new Vector2(0, BadgeRise);
        draws.Add(new FxDraw(UnitArt.DicePath, die, centre - size / 2, size, 0, false, alpha) { Mark = true });

        var digits = badge.Die.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var pen = centre.X - digits.Length * 4;
        foreach (var digit in digits)
        {
            var (rect, advance) = art.Digit(digit);
            draws.Add(new FxDraw(UnitArt.NumbersPath, rect, new Vector2(pen, centre.Y - rect.Height / 2f + 1), new Vector2(rect.Width, rect.Height), 0, false, alpha) { Mark = true });
            pen += advance;
        }
    }

    /// <summary>Drops the beat that's playing, for one that can't be played: returns its number, so it can still be reported, or null if none was.</summary>
    public int? Skip()
    {
        if ((_script?.Seq ?? _opening) is not { } seq)
            return null;     // nothing to drop, so nothing to put right
        (_script, _opening, _cutIn, _carriedCamera) = (null, null, null, null);
        _fading.Clear();
        _rest = BeatTiming.Rest;
        _dirty = true;
        if (_queue.Count == 0 && _live is not null)
            Shown = _live;
        return seq;
    }

    private const float UnitReach = 28;     // about a figure's height: a number starts at its head

    /// <summary>The glyphs of a pop: a word is one, a number one each.</summary>
    private static IEnumerable<string> Pieces(string text)
        => text.All(char.IsLetter) ? [text] : text.Select(c => c.ToString());
}
