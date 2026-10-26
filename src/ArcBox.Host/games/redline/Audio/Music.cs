using ArcBox.Host.Audio;
using Godot;
using Redline.Presentation.Audio.Synth;

namespace ArcBox.Host.Games.Redline.Audio;

/// <summary>Which loop is playing.</summary>
public enum MusicTrack { None, Lobby, Race }

/// <summary>
/// REDLINE's music on the Music bus (ducked under the voice): the lobby loop while REDLINE waits in the lobby and through
/// Driver Select, the low race bed from the grid to the flag. Changes crossfade over a second.
/// </summary>
public partial class RaceMusic : Node
{
    private const float FadeSeconds = 1.0f;
    /// <summary>The race bed sits this far under the lobby loop: it is a bed, not a tune.</summary>
    private const float RaceBedDb = -7;

    private readonly AudioStreamPlayer _lobby = new() { Name = "LobbyLoop", Bus = AudioBuses.Music, VolumeDb = -80 };
    private readonly AudioStreamPlayer _race = new() { Name = "RaceBed", Bus = AudioBuses.Music, VolumeDb = -80 };
    private readonly Dictionary<AudioStreamPlayer, Tween> _fades = [];
    private MusicTrack _want;
    private MusicTrack _playing;
    private bool _loaded;

    public override void _Ready()
    {
        Name = "Music";
        AddChild(_lobby);
        AddChild(_race);
    }

    /// <summary>Asks for <paramref name="track"/>; it starts as soon as the loops are synthesised.</summary>
    public void Play(MusicTrack track)
    {
        _want = track;
        Apply();
    }

    public override void _Process(double delta)
    {
        if (!_loaded && SfxBank.Prewarm() is { IsCompletedSuccessfully: true } build)
        {
            _lobby.Stream = SfxBank.Wav(build.Result.Lobby.Pcm, loop: true);
            _race.Stream = SfxBank.Wav(build.Result.Race.Pcm, loop: true);
            _loaded = true;
            Apply();
        }
    }

    private void Apply()
    {
        if (!_loaded || _want == _playing || !IsInsideTree())
        {
            return;
        }
        _playing = _want;
        Fade(_lobby, _want == MusicTrack.Lobby, 0);
        Fade(_race, _want == MusicTrack.Race, RaceBedDb);
        if (_want != MusicTrack.None)
        {
            AudioLog.Play(AudioBuses.Music, _want == MusicTrack.Lobby ? "lobby-loop" : "race-bed");
        }
    }

    private void Fade(AudioStreamPlayer player, bool on, float levelDb)
    {
        if (on && !player.Playing)
        {
            player.VolumeDb = -40;
            player.Play();
        }
        if (_fades.TryGetValue(player, out var running))
        {
            running.Kill();   // a fade still running the other way
        }
        var tween = CreateTween();
        _fades[player] = tween;
        tween.TweenProperty(player, "volume_db", on ? levelDb : -60f, FadeSeconds);
        if (!on)
        {
            tween.TweenCallback(Callable.From(() =>
            {
                if (_playing != (player == _lobby ? MusicTrack.Lobby : MusicTrack.Race))
                {
                    player.Stop();
                }
            }));
        }
    }
}
