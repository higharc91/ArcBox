using ArcBox.Host.Audio;
using Godot;
using Meridian.Presentation.Art;
using Meridian.Presentation.Audio;
using Meridian.Presentation.Beats;
using Meridian.Presentation.Board;

namespace ArcBox.Host.Games.Meridian;

/// <summary>
/// Meridian's music and sound (#338; M5 plan, Task 7) on the mixer's Music and Sfx buses, so the volume settings, mute and
/// the ducking under Tiller's voice (the shell's <c>Ducking</c>) all apply. What to play is <see cref="SoundPlan"/>'s
/// call; this only plays it. Music crossfades, and a sting runs into the track after it. A sound with no file is skipped:
/// nothing here can hold up a beat.
/// </summary>
public partial class MeridianAudio : Node
{
    private const float FadeSeconds = 1.5f;
    private const float StingFadeSeconds = 0.25f;
    private const float QuietDb = -60;
    private const float FootstepDb = -10;   // footsteps sit under everything else
    private const float TickDb = -8;
    private const int EffectVoices = 8;

    private readonly AudioStreamPlayer[] _music =
    [
        new() { Name = "MusicA", Bus = AudioBuses.Music, VolumeDb = QuietDb },
        new() { Name = "MusicB", Bus = AudioBuses.Music, VolumeDb = QuietDb },
    ];
    private readonly List<AudioStreamPlayer> _effects = [];
    private readonly Dictionary<string, AudioStreamOggVorbis?> _streams = new(StringComparer.Ordinal);
    private readonly Dictionary<AudioStreamPlayer, Tween> _fades = [];
    private readonly Dictionary<AudioStreamPlayer, long> _startedAt = [];
    private readonly Godot.Timer _stingEnds = new() { Name = "StingEnds", OneShot = true };
    private MeridianArt _art = MeridianArt.None;
    private MusicCue? _cue;
    private string? _playing;
    private string? _then;
    private int _front;
    private long _started;

    // Where the last frame left off: the beat and its timeline, the view and the time since, and the walk.
    private int _beatSeq;
    private double _beatAt;
    private object? _view;
    private double _sinceView;
    private double _walked;

    public override void _Ready()
    {
        Name = "MeridianAudio";
        foreach (var player in _music)
        {
            AddChild(player);
        }
        // A timer of its own, not Finished, which never comes on a machine with no sound device.
        AddChild(_stingEnds);
        _stingEnds.Timeout += () =>
        {
            if (_then is { } then)
            {
                Start(then, FadeSeconds, null);
            }
        };
        for (var i = 0; i < EffectVoices; i++)
        {
            var player = new AudioStreamPlayer { Name = $"Effect{i}", Bus = AudioBuses.Sfx };
            _effects.Add(player);
            AddChild(player);
        }
    }

    /// <summary>The pack's sounds (its <c>audio</c> set).</summary>
    public void Use(MeridianArt art)
    {
        _art = art;
        _streams.Clear();
    }

    /// <summary>Plays what this frame calls for: the music for the view, the beat's sounds as its timeline passes them,
    /// footsteps while anyone walks, and the tick in a public decision's last ten seconds.</summary>
    public void Update(Director director, CrewMotion motion, double delta)
    {
        if (director.Shown is not { } view)
        {
            return;
        }
        Music(SoundPlan.Music(view, director.Current));

        if (director.Current is { } beat)
        {
            if (beat.Seq != _beatSeq)
            {
                (_beatSeq, _beatAt) = (beat.Seq, -1);   // a new beat: everything from its start is still to play
            }
            // Only what the timeline has newly passed, up to and including now; a timeline held for the voice repeats nothing.
            var upTo = director.Elapsed + 1e-6;
            foreach (var cue in SoundPlan.Due(beat, _beatAt, upTo))
            {
                Play(cue.Id);
            }
            _beatAt = Math.Max(_beatAt, upTo);
        }

        if (!ReferenceEquals(view, _view))
        {
            (_view, _sinceView) = (view, 0);   // every view says the seconds left as of when it was made
        }
        var before = _sinceView;
        _sinceView += delta;
        if (SoundPlan.Tick(view.TurnEndsIn, before, _sinceView))
        {
            Play("sfx.tick", TickDb);
        }

        if (motion.Poses.Any(p => p.Walking))
        {
            _walked += delta;
            if (_walked >= SoundPlan.FootstepEvery)
            {
                _walked -= SoundPlan.FootstepEvery;
                Play("sfx.footstep", FootstepDb);
            }
        }
        else
        {
            _walked = SoundPlan.FootstepEvery;   // the first step of a walk sounds at once
        }
    }

    public override void _ExitTree()
    {
        _stingEnds.Stop();
        foreach (var fade in _fades.Values)
        {
            fade.Kill();
        }
        _fades.Clear();
        foreach (var player in _music.Concat(_effects))
        {
            player.Stop();
            player.Stream = null;
        }
        _streams.Clear();
    }

    private void Music(MusicCue? cue)
    {
        if (cue is null || cue == _cue)
        {
            return;
        }
        _cue = cue;
        _stingEnds.Stop();
        Start(cue.Id, cue.Then is null ? FadeSeconds : StingFadeSeconds, cue.Then);
    }

    private void Play(string id, float db = 0)
    {
        if (SoundPlan.File(_art, id) is not { } entry || Stream(entry.Path, loop: false) is not { } stream)
        {
            return;
        }
        var voice = _effects.FirstOrDefault(p => !p.Playing) ?? _effects.MinBy(p => _startedAt.GetValueOrDefault(p))!;
        _startedAt[voice] = ++_started;
        voice.Stream = stream;
        voice.VolumeDb = db;
        voice.Play();
        AudioLog.Play(AudioBuses.Sfx, id);
    }

    private void Start(string id, float fade, string? then)
    {
        if (_playing == id && _music[_front].Playing)
        {
            _then = then;   // already on (the sting ran into it): nothing restarts
            return;
        }
        if (SoundPlan.File(_art, id) is not { } entry || Stream(entry.Path, entry.Loop) is not { } stream)
        {
            if (then is not null)
            {
                Start(then, FadeSeconds, null);   // no sting: straight on to what follows it
            }
            return;
        }
        var (old, next) = (_music[_front], _music[1 - _front]);
        _front = 1 - _front;
        Fade(old, QuietDb, fade, stopAfter: true);
        next.Stream = stream;
        next.VolumeDb = QuietDb;
        next.Play();
        _playing = id;
        Fade(next, 0, fade, stopAfter: false);
        AudioLog.Play(AudioBuses.Music, id);
        _then = then;
        if (then is not null)
        {
            _stingEnds.Start(Math.Max(0.1, stream.GetLength()));
        }
    }

    private void Fade(AudioStreamPlayer player, float db, float seconds, bool stopAfter)
    {
        if (_fades.Remove(player, out var running))
        {
            running.Kill();
        }
        if (stopAfter && !player.Playing)
        {
            return;
        }
        var tween = CreateTween();
        _fades[player] = tween;
        tween.TweenProperty(player, "volume_db", db, seconds);
        if (stopAfter)
        {
            tween.TweenCallback(Callable.From(player.Stop));
        }
    }

    private AudioStreamOggVorbis? Stream(string path, bool loop)
    {
        if (!_streams.TryGetValue(path, out var stream))
        {
            stream = System.IO.File.Exists(path) ? AudioStreamOggVorbis.LoadFromFile(path) : null;
            _streams[path] = stream;
        }
        if (stream is not null)
        {
            stream.Loop = loop;
        }
        return stream;
    }
}
