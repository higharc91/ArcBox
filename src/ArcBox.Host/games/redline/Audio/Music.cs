using ArcBox.Host.Audio;
using Godot;
using Redline.Presentation.Audio.Synth;

namespace ArcBox.Host.Games.Redline.Audio;

/// <summary>Which music is playing.</summary>
public enum MusicTrack { None, Lobby, Race, Anthem, Theme }

/// <summary>
/// REDLINE's music on the Music bus (ducked under the voice): the lobby loop while REDLINE waits in the lobby and through
/// Driver Select, the low race bed from the grid to the flag, then the podium's anthem (once through) and the REDLINE
/// victory theme (looping). Loops crossfade over a second; the anthem starts at once, on its timpani roll.
/// </summary>
public partial class RaceMusic : Node
{
    private const float FadeSeconds = 1.0f;
    /// <summary>The race bed sits this far under the lobby loop: it is a bed, not a tune.</summary>
    private const float RaceBedDb = -7;
    /// <summary>The podium music is the show: it plays at full level.</summary>
    private const float PodiumDb = 0;

    private readonly AudioStreamPlayer _lobby = new() { Name = "LobbyLoop", Bus = AudioBuses.Music, VolumeDb = -80 };
    private readonly AudioStreamPlayer _race = new() { Name = "RaceBed", Bus = AudioBuses.Music, VolumeDb = -80 };
    private readonly AudioStreamPlayer _anthem = new() { Name = "Anthem", Bus = AudioBuses.Music, VolumeDb = -80 };
    private readonly AudioStreamPlayer _theme = new() { Name = "VictoryTheme", Bus = AudioBuses.Music, VolumeDb = -80 };
    private readonly Dictionary<AudioStreamPlayer, Tween> _fades = [];
    private MusicTrack _want;
    private MusicTrack _playing;
    private bool _loaded;
    private string _anthemId = "";

    public override void _Ready()
    {
        Name = "Music";
        AddChild(_lobby);
        AddChild(_race);
        AddChild(_anthem);
        AddChild(_theme);
    }

    /// <summary>The track asked for last.</summary>
    public MusicTrack Wanted => _want;

    /// <summary>True while the anthem is still sounding.</summary>
    public bool AnthemPlaying => _playing == MusicTrack.Anthem && _anthem.Playing;

    /// <summary>The anthem to play for <see cref="MusicTrack.Anthem"/> (its id for the log).</summary>
    public void SetAnthem(Pcm pcm, string id)
    {
        if (_anthemId == id && _anthem.Stream is not null)
        {
            return;
        }
        _anthemId = id;
        _anthem.Stop();
        _anthem.Stream = SfxBank.Wav(pcm);
    }

    /// <summary>The victory theme, looping.</summary>
    public void SetTheme(Pcm pcm)
    {
        if (_theme.Stream is null)
        {
            _theme.Stream = SfxBank.Wav(pcm, loop: true);
        }
    }

    public bool HasTheme => _theme.Stream is not null;

    /// <summary>Asks for <paramref name="track"/>; the loops start as soon as they are synthesised.</summary>
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
        if (_want == MusicTrack.Anthem && _anthem.Stream is null || _want == MusicTrack.Theme && _theme.Stream is null)
        {
            return;   // asked for before it was rendered: the director sets the stream and asks again
        }
        _playing = _want;
        Fade(_lobby, _want == MusicTrack.Lobby, 0);
        Fade(_race, _want == MusicTrack.Race, RaceBedDb);
        Fade(_theme, _want == MusicTrack.Theme, PodiumDb);
        if (_want == MusicTrack.Anthem)
        {
            Kill(_anthem);
            _anthem.VolumeDb = PodiumDb;
            _anthem.Play();
        }
        else
        {
            Fade(_anthem, false, 0);
        }
        var name = _want switch
        {
            MusicTrack.Lobby => "lobby-loop",
            MusicTrack.Race => "race-bed",
            MusicTrack.Anthem => $"anthem {_anthemId}",
            MusicTrack.Theme => "victory-theme",
            _ => null,
        };
        if (name is not null)
        {
            AudioLog.Play(AudioBuses.Music, name);
        }
    }

    private void Kill(AudioStreamPlayer player)
    {
        if (_fades.Remove(player, out var running))
        {
            running.Kill();
        }
    }

    private void Fade(AudioStreamPlayer player, bool on, float levelDb)
    {
        if (!on && !player.Playing)
        {
            return;
        }
        if (on && !player.Playing)
        {
            player.VolumeDb = -40;
            player.Play();
        }
        Kill(player);   // a fade still running the other way
        var tween = CreateTween();
        _fades[player] = tween;
        tween.TweenProperty(player, "volume_db", on ? levelDb : -60f, FadeSeconds);
        if (!on)
        {
            tween.TweenCallback(Callable.From(() =>
            {
                if (_playing != TrackOf(player))
                {
                    player.Stop();
                }
            }));
        }
    }

    private MusicTrack TrackOf(AudioStreamPlayer player) =>
        player == _lobby ? MusicTrack.Lobby : player == _race ? MusicTrack.Race : player == _anthem ? MusicTrack.Anthem : MusicTrack.Theme;
}
