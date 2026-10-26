using ArcBox.Host.Audio;
using Godot;
using Redline.Logic;
using Redline.Presentation.Audio;
using Redline.Presentation.Audio.Synth;

namespace ArcBox.Host.Games.Redline.Audio;

/// <summary>
/// The engine bed: up to <see cref="EngineMix.MaxVoices"/> streaming <see cref="EngineVoice"/>s on the Sfx bus, one per
/// car the camera is on, each fed through an <see cref="AudioStreamGenerator"/> every frame with the rpm, load and level
/// <see cref="EngineMix.Target"/> gives for that car's speed. About 4 % of a core per voice while audible; stopped
/// otherwise.
/// </summary>
public partial class EnginePlayer : Node
{
    private const int Rate = Sfx.SampleRate;
    private const float BufferSeconds = 0.2f;
    /// <summary>How fast a voice's level follows its target (dB per second, up and down).</summary>
    private const double RiseDbPerSecond = 40, FallDbPerSecond = 25;
    private const double SilentDb = -60;

    private sealed class Voice(int seed)
    {
        public readonly EngineVoice Synth = new(Rate, seed);
        public AudioStreamPlayer Player = null!;
        public AudioStreamGeneratorPlayback? Playback;
        public string? Code;
        public double Db = SilentDb;
        public EngineTarget Last = new(EngineMix.IdleRpm, 0.1, SilentDb);
    }

    private readonly Voice[] _voices = [new(1), new(2), new(3)];
    private readonly float[] _mono = new float[(int)(Rate * BufferSeconds) + 1];
    private readonly Vector2[] _frames = new Vector2[(int)(Rate * BufferSeconds) + 1];

    public override void _Ready()
    {
        Name = "Engines";
        for (var i = 0; i < _voices.Length; i++)
        {
            var player = new AudioStreamPlayer
            {
                Name = $"Engine{i}",
                Bus = AudioBuses.Sfx,
                Stream = new AudioStreamGenerator { MixRate = Rate, BufferLength = BufferSeconds },
                VolumeDb = (float)SilentDb,
            };
            AddChild(player);
            _voices[i].Player = player;
        }
    }

    /// <summary>
    /// One frame: <paramref name="subjects"/> are the cars on camera (best first), <paramref name="speed"/> a car's
    /// current speed in m/s, <paramref name="lit"/> the start lights on, <paramref name="biggestGain"/> the round's largest gain.
    /// </summary>
    public void Update(double delta, RacePhase phase, IReadOnlyList<string> subjects, Func<string, double> speed, int lit, int biggestGain)
    {
        for (var i = 0; i < _voices.Length; i++)
        {
            var v = _voices[i];
            var code = i < subjects.Count ? subjects[i] : null;
            var target = code is null ? null : EngineMix.Target(phase, speed(code), lit, biggestGain);

            if (target is { } t)
            {
                if (code != v.Code)
                {
                    v.Code = code;
                    AudioLog.Play(AudioBuses.Sfx, $"engine {code}");
                }
                v.Last = t;
                Approach(v, t.GainDb, delta);
                if (!v.Player.Playing)
                {
                    v.Player.Play();
                    v.Playback = v.Player.GetStreamPlayback() as AudioStreamGeneratorPlayback;
                }
            }
            else
            {
                Approach(v, SilentDb, delta);
                if (v.Db <= SilentDb + 0.5 && v.Player.Playing)
                {
                    v.Player.Stop();
                    v.Playback = null;
                    v.Code = null;
                }
            }

            if (v.Player.Playing && v.Playback is { } playback)
            {
                Feed(v, playback);
            }
        }
    }

    /// <summary>Silences every engine at once (the scene is parked).</summary>
    public void StopAll()
    {
        foreach (var v in _voices)
        {
            v.Player.Stop();
            v.Playback = null;
            v.Code = null;
            v.Db = SilentDb;
            v.Player.VolumeDb = (float)v.Db;
        }
    }

    private static void Approach(Voice v, double targetDb, double delta)
    {
        var step = (targetDb > v.Db ? RiseDbPerSecond : FallDbPerSecond) * delta;
        v.Db = Math.Abs(targetDb - v.Db) <= step ? targetDb : v.Db + Math.Sign(targetDb - v.Db) * step;
        v.Player.VolumeDb = (float)v.Db;
    }

    private void Feed(Voice v, AudioStreamGeneratorPlayback playback)
    {
        var n = Math.Min(playback.GetFramesAvailable(), _mono.Length);
        if (n <= 0)
        {
            return;
        }
        var mono = _mono.AsSpan(0, n);
        v.Synth.Fill(mono, v.Last.Rpm, v.Last.Load);
        for (var i = 0; i < n; i++)
        {
            _frames[i] = new Vector2(mono[i], mono[i]);
        }
        playback.PushBuffer(new ReadOnlySpan<Vector2>(_frames, 0, n));
    }
}
