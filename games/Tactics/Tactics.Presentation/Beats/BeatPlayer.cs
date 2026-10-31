using System.Collections.Immutable;
using System.Numerics;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.Beats;

/// <summary>A line to say: <paramref name="Speaker"/> is who it's by, or null for the storyteller, in voice theme <paramref name="Theme"/>.</summary>
public sealed record SpokenLine(string? Speaker, string Text, string Theme);

/// <summary>One image to draw over the board, in art pixels.</summary>
/// <param name="Path">Relative to the pack's root.</param>
/// <param name="Source">The rectangle of the image.</param>
/// <param name="TopLeft">Where it goes.</param>
/// <param name="Size">How big.</param>
/// <param name="Rotation">Radians around its centre.</param>
/// <param name="Additive">Blended by adding light.</param>
/// <param name="Alpha">How opaque.</param>
public sealed record FxDraw(string Path, ArtRect Source, Vector2 TopLeft, Vector2 Size, float Rotation, bool Additive, float Alpha);

/// <summary>What one moment of a beat looks like, for the units and over them.</summary>
/// <param name="Places">Units not standing squarely on a tile.</param>
/// <param name="Poses">Units doing something.</param>
/// <param name="Draws">Effects, numbers and badges.</param>
/// <param name="Shake">How far the board is off its place, in art pixels.</param>
public sealed record BeatFrame(ImmutableDictionary<UnitId, UnitPlace> Places, ImmutableDictionary<UnitId, UnitPose> Poses, ImmutableArray<FxDraw> Draws, Vector2 Shake);

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
public sealed record BeatTick(ImmutableArray<int> Shown, ImmutableArray<SpokenLine> Lines, ImmutableArray<string> Moments, bool BoardChanged, bool Busy, BeatScript? Started, BeatCatchUp? CaughtUp = null);

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
public sealed class BeatPlayer(FxArt fx, UnitArt art)
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
    private const float StepHop = 0.15f;
    private const float ShakeSize = 2.5f;
    private const float BadgeScale = 1;

    /// <summary>The moment, for <c>--shots</c>, that a beat the AI finished starts.</summary>
    public const string AiFinishedMoment = "ai-finished";

    /// <summary>The moment, for <c>--shots</c>, that a reaction offer reaches the screen.</summary>
    public const string ReactionMoment = "reaction";

    private readonly BeatQueue _queue = new();
    private readonly List<(Cue Cue, double Start)> _fading = [];
    private BattleView? _live;
    private BeatScript? _script;
    private BeatCatchUp? _caughtUp;
    private int _next;
    private int _played;        // the last beat begun
    private double _clock;      // into the current beat
    private double _now;        // since the start, as beats see it: a hitch doesn't count past LongestFrame
    private double _real;       // since the start, as the game sees it
    private double _rest;
    private double _captionUntil;
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
            (_script, Shown) = (null, null);
        _live = live;
        if (_script is null && _queue.Late(_real))
            CatchUp(live);
        Shown ??= Unplayed(live);
        if (Idle)
            Shown = live;
    }

    private void CatchUp(BattleView live)
    {
        _queue.KeepNewest();
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
        _queue.Clear();
        _fading.Clear();
        (_script, _live, Shown, Caption, _rest, AiFinished, _caughtUp) = (null, null, null, null, 0, null, null);
    }

    /// <summary>Moves on by <paramref name="delta"/> seconds.</summary>
    public BeatTick Tick(double delta)
    {
        var seconds = Math.Min(delta, LongestFrame);
        _now += seconds;
        _real += delta;
        var faded = _fading.RemoveAll(f => _now >= f.Start + f.Cue.Length) > 0;
        if (Caption is not null && _now >= _captionUntil)
            Caption = null;

        var redraw = _dirty || faded;     // whatever just went is still in the last frame drawn
        _dirty = false;
        if (AiFinished is not null && _now >= _aiFinishedUntil)
            (AiFinished, redraw) = (null, true);

        var board = Shown?.Board;
        var shown = ImmutableArray<int>.Empty;
        var lines = ImmutableArray.CreateBuilder<SpokenLine>();
        var moments = ImmutableArray.CreateBuilder<string>();
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
            _clock += seconds;
            for (; _next < playing.Cues.Length && playing.Cues[_next].At <= _clock; _next++)
                Reach(playing.Cues[_next], lines, moments);
            if (_clock >= playing.Length)
            {
                shown = [playing.Seq];
                _script = null;
                _rest = BeatTiming.Rest;
                if (_queue.Count == 0 && _live is not null)
                    Shown = _live;     // the view's units are the truth now: whatever drifted heals here
            }
        }

        var caughtUp = _caughtUp;
        _caughtUp = null;
        return new BeatTick(shown, lines.ToImmutable(), moments.ToImmutable(), caughtUp is not null || !DisplayedBattle.SameBoard(board, Shown?.Board),
            _script is not null || _fading.Count > 0 || !shown.IsEmpty || redraw || _dirty || AiFinished is not null, started, caughtUp);
    }

    private BeatScript? Begin()
    {
        if (_script is not null || Shown is null || _live is null)
            return null;
        if (_queue.Late(_real))
            CatchUp(_live);
        if (_queue.Next() is not { } beat)
            return null;
        (_opening, _played) = (beat.Seq, beat.Seq);     // if working out its script throws, it's already off the queue
        (_script, _next, _clock) = (BeatScript.Of(beat, Shown, _live, fx), 0, 0);
        _opening = null;
        if (beat.Actor is { } actor && beat.Events.Any(e => e.AiFinished))
            (AiFinished, _aiFinishedUntil, _aiBegan) = (actor, _now + Math.Max(_script.Length, BeatTiming.AiFinished), true);
        return _script;
    }

    private void Reach(Cue cue, ImmutableArray<SpokenLine>.Builder lines, ImmutableArray<string>.Builder moments)
    {
        switch (cue)
        {
            case ApplyCue apply when Shown is not null && _live is not null:
                Shown = DisplayedBattle.Apply(Shown, apply.Event, _live);
                break;
            case LineCue line:
                lines.Add(Say(line));
                break;
            case MomentCue moment:
                moments.Add(moment.Label);
                break;
            case BurstCue or PopCue or BadgeCue or FlightCue:
                _fading.Add((cue, _now - (_clock - cue.At)));
                break;
        }
    }

    private SpokenLine Say(LineCue line)
    {
        var narrator = line.Speaker == TacticsVoices.NarratorSpeaker;
        var said = new SpokenLine(narrator ? null : Shown?.Units.FirstOrDefault(u => u.Id.Value == line.Speaker)?.Name ?? Words.Title(line.Speaker), line.Line,
            narrator ? TacticsVoices.Narrator : TacticsVoices.Character);
        Caption = said;
        _captionUntil = _now + CaptionExtra + line.Line.Split(' ').Length * SecondsPerWord;
        return said;
    }

    /// <summary>This moment of the beat, on <paramref name="terrain"/> as it's drawn.</summary>
    public BeatFrame Frame(BoardTerrain terrain)
    {
        var places = ImmutableDictionary.CreateBuilder<UnitId, UnitPlace>();
        var poses = ImmutableDictionary.CreateBuilder<UnitId, UnitPose>();
        var shake = Vector2.Zero;
        if (_script is { } script)
            foreach (var cue in script.Cues.Where(c => c.At <= _clock && _clock < c.At + c.Length))
            {
                var (into, share) = (_clock - cue.At, (float)((_clock - cue.At) / cue.Length));
                switch (cue)
                {
                    case WalkCue walk:
                        places[walk.Unit] = Between(terrain, walk.From, walk.To, share, StepHop * MathF.Sin(share * MathF.PI));
                        poses[walk.Unit] = new UnitPose(BeatScript.WalkAnim, into, BeatScript.Way(walk.From, walk.To));
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
                        poses[pose.Unit] = new UnitPose(pose.Anim, into, pose is { From: { } from, Toward: { } toward } ? BeatScript.Way(from, toward) : null);
                        break;
                    case FallCue fall:
                        var ground = UnitPlace.On(terrain, fall.Tile);
                        var top = fall.Levels + (terrain.Look(fall.Tile).Void ? BoardTerrain.VoidSink : 0);
                        places[fall.Unit] = ground with { Height = ground.Height + top * (1 - share * share) };
                        break;
                    case ShakeCue:
                        var fade = ShakeSize * (1 - share);
                        shake = new Vector2(fade * MathF.Sin((float)into * 70), fade * 0.5f * MathF.Cos((float)into * 53));
                        break;
                }
            }

        var draws = ImmutableArray.CreateBuilder<FxDraw>();
        foreach (var (cue, start) in _fading)
            Draw(cue, _now - start, terrain, draws);
        return new BeatFrame(places.ToImmutable(), poses.ToImmutable(), draws.ToImmutable(), shake);
    }

    private static UnitPlace Between(BoardTerrain terrain, Tile from, Tile to, float share, float hop)
    {
        var (a, b) = (UnitPlace.On(terrain, from), UnitPlace.On(terrain, to));
        return new UnitPlace(Vector2.Lerp(a.Map, b.Map, share), a.Height + (b.Height - a.Height) * share + hop);
    }

    private void Draw(Cue cue, double into, BoardTerrain terrain, ImmutableArray<FxDraw>.Builder draws)
    {
        var share = (float)(into / cue.Length);
        switch (cue)
        {
            case BurstCue burst:
                var clip = burst.Clip;
                var at = terrain.Centre(burst.Tile) - new Vector2(clip.AnchorX, clip.AnchorY + clip.Lift);
                draws.Add(new FxDraw(clip.Path, new ArtRect(clip.FrameAt(into) * clip.Width, 0, clip.Width, clip.Height), at,
                    new Vector2(clip.Width, clip.Height), 0, clip.Additive, 1));
                break;
            case FlightCue flight:
                DrawFlight(flight, into, share, terrain, draws);
                break;
            case PopCue pop:
                DrawPop(pop, share, terrain, draws);
                break;
            case BadgeCue badge:
                DrawBadge(badge, share, terrain, draws);
                break;
        }
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
            shot.Headings > 1 ? 0 : angle, shot.Additive, 1));
    }

    private void DrawPop(PopCue pop, float share, BoardTerrain terrain, ImmutableArray<FxDraw>.Builder draws)
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
        foreach (var (rect, advance) in glyphs)
        {
            var size = new Vector2(rect.Width, rect.Height) * NumberScale;
            draws.Add(new FxDraw(UnitArt.NumbersPath, rect, new Vector2(pen, centre.Y - size.Y / 2), size, 0, false, alpha));
            pen += advance * NumberScale;
        }
    }

    private void DrawBadge(BadgeCue badge, float share, BoardTerrain terrain, ImmutableArray<FxDraw>.Builder draws)
    {
        var die = badge.Success ? art.DieGold : art.DieDark;
        var size = new Vector2(die.Width, die.Height) * BadgeScale;
        var alpha = share < 0.8f ? 1 : (1 - share) / 0.2f;
        var centre = terrain.Centre(badge.Tile) - new Vector2(0, BadgeRise);
        draws.Add(new FxDraw(UnitArt.DicePath, die, centre - size / 2, size, 0, false, alpha));

        var digits = badge.Die.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var pen = centre.X - digits.Length * 4;
        foreach (var digit in digits)
        {
            var (rect, advance) = art.Digit(digit);
            draws.Add(new FxDraw(UnitArt.NumbersPath, rect, new Vector2(pen, centre.Y - rect.Height / 2f + 1), new Vector2(rect.Width, rect.Height), 0, false, alpha));
            pen += advance;
        }
    }

    /// <summary>Drops the beat that's playing, for one that can't be played: returns its number, so it can still be reported, or null if none was.</summary>
    public int? Skip()
    {
        if ((_script?.Seq ?? _opening) is not { } seq)
            return null;     // nothing to drop, so nothing to put right
        (_script, _opening) = (null, null);
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
