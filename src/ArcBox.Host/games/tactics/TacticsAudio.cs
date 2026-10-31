using ArcBox.Host.Audio;
using Godot;
using Tactics.Presentation.Audio;

namespace ArcBox.Host.Games.Tactics;

/// <summary>
/// Wardenflame's music and effects, on the mixer's Music and Sfx buses, so their volumes, mute and the voice's ducking apply. What to
/// play is worked out in <c>Tactics.Presentation.Audio</c>; this class only plays it. Music crossfades as RaceMusic does, and a
/// sting then runs into what follows it. Files are read from the content pack as they're first asked for, and a file that can't be read
/// is skipped: nothing here can hold up a beat.
/// </summary>
public partial class TacticsAudio : Node
{
    private const float FadeSeconds = 1.2f;
    private const float StingFadeSeconds = 0.25f;
    private const float QuietDb = -60;
    private const float BedDb = -14;      // ambience sits under the effects and the music
    private const int EffectVoices = 12;

    private readonly AudioStreamPlayer[] _music = [
        new() { Name = "MusicA", Bus = AudioBuses.Music, VolumeDb = QuietDb },
        new() { Name = "MusicB", Bus = AudioBuses.Music, VolumeDb = QuietDb },
    ];
    private readonly AudioStreamPlayer _bed = new() { Name = "Ambience", Bus = AudioBuses.Sfx, VolumeDb = BedDb };
    private readonly List<AudioStreamPlayer> _effects = [];
    private readonly Dictionary<string, AudioStreamOggVorbis?> _streams = new(StringComparer.Ordinal);
    private readonly Dictionary<AudioStreamPlayer, Tween> _fades = [];
    private readonly Godot.Timer _stingEnds = new() { Name = "StingEnds", OneShot = true };
    private string _root = "";
    private SoundBank? _bank;
    private SoundPicker? _picker;
    private MusicCue? _cue;
    private string? _bedId;
    private int _front;
    private readonly Dictionary<AudioStreamPlayer, long> _startedAt = [];
    private long _started;
    private string? _then;

    public override void _Ready()
    {
        Name = "TacticsAudio";
        foreach (var player in _music)
        {
            AddChild(player);
        }
        AddChild(_bed);
        // A timer of its own, not Finished, which never comes on a machine with no sound device, and not one of the tree's,
        // which would outlive this scene.
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

    /// <summary>The pack to play from.</summary>
    public void Use(string root, SoundBank bank) => (_root, _bank, _picker) = (root, bank, new SoundPicker(bank));

    /// <summary>Asks for <paramref name="cue"/>, or leaves the music alone for null. Asking for what's already on changes nothing.</summary>
    public void Music(MusicCue? cue)
    {
        if (cue is null || cue == _cue || _bank is null)
        {
            return;
        }
        _cue = cue;
        _stingEnds.Stop();
        Start(cue.Id, cue.Then is null ? FadeSeconds : StingFadeSeconds, cue.Then);
    }

    /// <summary>Runs the ambience bed <paramref name="id"/>, or none for null.</summary>
    public void Ambience(string? id)
    {
        if (id == _bedId || _bank is null)
        {
            return;
        }
        _bedId = id;
        _bed.Stop();
        if (id is null || _picker?.Next(id) is not { } file || Stream(file, loop: true) is not { } stream)
        {
            return;
        }
        _bed.Stream = stream;
        _bed.Play();
        AudioLog.Play(AudioBuses.Sfx, $"ambience {id}");
    }

    /// <summary>Plays the sound <paramref name="id"/>, next of its files, at its group's level, on a free voice, or over the oldest if all twelve are busy.</summary>
    public void Play(string id)
    {
        if (_picker?.Next(id) is not { } file || Stream(file, loop: false) is not { } stream)
        {
            return;
        }
        var voice = _effects.FirstOrDefault(p => !p.Playing) ?? _effects.MinBy(p => _startedAt.GetValueOrDefault(p))!;
        _startedAt[voice] = ++_started;
        voice.Stream = stream;
        voice.VolumeDb = (float)SoundChoice.LevelDb(_bank?.Sound(id)?.Group ?? "");
        voice.Play();
        AudioLog.Play(AudioBuses.Sfx, id);
    }

    private void Start(string id, float fade, string? then)
    {
        if (_bank?.Track(id) is not { } track || Stream(track.File, track.Loop) is not { } stream)
        {
            if (then is not null)
            {
                Start(then, FadeSeconds, null);     // no sting: straight on to what follows it, not on with the fight's track
            }
            return;
        }
        var (old, next) = (_music[_front], _music[1 - _front]);
        _front = 1 - _front;
        Fade(old, QuietDb, fade, stopAfter: true);
        next.Stream = stream;
        next.VolumeDb = QuietDb;
        next.Play();
        Fade(next, 0, fade, stopAfter: false);
        AudioLog.Play(AudioBuses.Music, id);

        _then = then;
        if (then is not null)
        {
            _stingEnds.Start(stream.GetLength());
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

    private AudioStreamOggVorbis? Stream(string file, bool loop)
    {
        if (_streams.TryGetValue(file, out var known))
        {
            return known;
        }
        var path = System.IO.Path.Combine(_root, file);
        var stream = System.IO.File.Exists(path) ? AudioStreamOggVorbis.LoadFromFile(path) : null;
        if (stream is null)
        {
            GD.PushWarning($"Tactics sound: can't load {path}");
            return _streams[file] = null;
        }
        stream.Loop = loop;
        return _streams[file] = stream;
    }
}
