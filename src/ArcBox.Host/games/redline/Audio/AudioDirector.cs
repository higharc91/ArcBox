using ArcBox.Host.Audio;
using ArcBox.Host.Core.Audio;
using Godot;
using Redline.Logic;
using Redline.Presentation;
using Redline.Presentation.Audio;
using Redline.Presentation.Audio.Synth;
using Redline.Presentation.Ceremony;
using Redline.Presentation.Hud;

namespace ArcBox.Host.Games.Redline.Audio;

/// <summary>
/// REDLINE's sound (audio spec §3.3). <see cref="RedlineScene"/> hands it every view (<see cref="OnView"/>), each start
/// light as it comes on (<see cref="OnLight"/>), each spin-out the camera shows (<see cref="OnSpin"/>), and a per-frame
/// <see cref="Tick"/>. Which effect a transition plays is decided in <see cref="AudioCues"/>; the commentator's lines
/// come from <see cref="Commentary"/>. Questions and commentary go to the shell's <see cref="Narrator"/>
/// (<see cref="RedlineVoices"/> themes); effects, the crowd bed and the engines go to the Sfx bus, music to Music.
/// <para>
/// The podium (podium spec §5): the music is rendered in the background from the Chequered flag
/// (<see cref="PodiumMusicBank"/>). In the Anthem the commentator names the anthem first and the band follows
/// (<see cref="PodiumMusic.Start"/>; the voice ducks the music where they overlap); a country without a melody gets the
/// victory theme instead. The Champagne gets the theme, after the anthem's last chord. Each spray the scene reports
/// (<see cref="OnSpray"/>) pops a cork and hisses at its strength, the first with the victory sting.
/// </para>
/// </summary>
public partial class AudioDirector : Node
{
    /// <summary>The crunch lands this long after the screech, as the spinning car stops.</summary>
    private const double CrunchDelay = 0.9;
    private const float CrowdBedDb = -14;

    private readonly Dictionary<string, AudioStreamPlayer> _players = new(StringComparer.Ordinal);
    private readonly AudioStreamPlayer?[] _hiss = new AudioStreamPlayer?[3];
    private readonly List<(double At, string Cue)> _later = [];
    private readonly Grid _grid = Grid2026.Load();
    private RaceMusic _music = null!;
    private EnginePlayer _engines = null!;
    private AudioStreamPlayer _crowd = null!;
    private Narrator? _narrator;
    private bool _loaded;
    private bool _crowdOn;

    private RedlineHostView? _view;
    private double _time;
    private double? _remaining;
    private int _biggestGain;
    private int _lit;

    // The podium music: what plays, when it starts (scene clock) and when the anthem's last chord has rung.
    private AnthemInfo? _anthem;
    private double _anthemPhaseStart;
    private double _anthemLine;
    private double? _musicAt;
    private double _anthemEnds = double.NegativeInfinity;
    private bool _themeWanted;

    public override void _Ready()
    {
        Name = "AudioDirector";
        _music = new RaceMusic();
        AddChild(_music);
        _engines = new EnginePlayer();
        AddChild(_engines);
        _crowd = new AudioStreamPlayer { Name = "CrowdBed", Bus = AudioBuses.Sfx, VolumeDb = CrowdBedDb };
        AddChild(_crowd);
        SfxBank.Prewarm();
    }

    public void SetNarrator(Narrator narrator) => _narrator = narrator;

    /// <summary>REDLINE is waiting behind the lobby (warming up, or parked after a race): the lobby loop, nothing else.</summary>
    public void Lobby()
    {
        _view = null;
        _musicAt = null;
        _themeWanted = false;
        _later.Clear();
        _engines.StopAll();
        SetCrowd(false);
        _music.Play(MusicTrack.Lobby);
    }

    public void OnView(RedlineHostView view, RedlineHostView? previous)
    {
        _view = view;
        var newPhase = previous is null || previous.Phase != view.Phase || previous.Round != view.Round;
        if (newPhase)
        {
            _later.Clear();
            _lit = 0;
            _biggestGain = view.Phase == RacePhase.Reveal && view.Drivers.Count > 0
                ? Math.Max(0, view.Drivers.Max(d => d.Distance - d.PreviousDistance))
                : 0;
        }
        _remaining = HudClock.Remaining(view.DeadlineUtc, DateTimeOffset.UtcNow, HudClock.PhaseSeconds(view));

        foreach (var cue in AudioCues.ForView(view, previous))
        {
            Play(cue);
        }

        // The commentator first, so a question read at the same moment cuts straight in over a round intro. Qualifying's
        // opener is the exception (spec §6): lap 1's question waits for it, and lap 1's Reading is longer to make room
        // (RedlineRules.QualifyingIntro).
        if (_narrator is { } narrator)
        {
            foreach (var line in Commentary.Lines(view, previous, _grid))
            {
                var opener = line.Kind == CommentaryKind.Intro && QualifyingText.IsQualifying(view.Phase);
                narrator.Say(line.Text, RedlineVoices.Announcer, NarrationPriority.Commentary, opener);
            }
            if (AudioCues.ReadsQuestion(view, previous))
            {
                narrator.Say(view.Question!.Text!, RedlineVoices.Questions, NarrationPriority.Question);
            }
        }

        if (view.Phase is RacePhase.Anthem or RacePhase.Champagne)
        {
            Podium(view, newPhase);
        }
        else
        {
            _musicAt = null;
            _themeWanted = false;
            _music.Play(view.Phase switch
            {
                RacePhase.DriverSelect => MusicTrack.Lobby,
                RacePhase.Chequered => MusicTrack.None,   // the fanfare and the crowd take over
                _ => MusicTrack.Race,
            });
        }
        if (view.Phase == RacePhase.Chequered && newPhase)
        {
            PodiumMusicBank.Prepare(PodiumMusic.PredictAnthemId(view, _grid));
        }
        SetCrowd(view.Phase is not RacePhase.DriverSelect);
    }

    /// <summary>The podium's music for a new Anthem or Champagne view.</summary>
    private void Podium(RedlineHostView view, bool newPhase)
    {
        if (!newPhase)
        {
            return;
        }
        if (view.Phase == RacePhase.Anthem)
        {
            _anthem = PodiumMusic.Anthem(view.Podium?.AnthemId);
            PodiumMusicBank.Prepare(_anthem?.Id);
            _anthemPhaseStart = _time;
            _anthemLine = PodiumMusic.LineSeconds(Commentary.Lines(view, null, _grid).FirstOrDefault()?.Text);
            _anthemEnds = double.NegativeInfinity;
            _themeWanted = false;
            _musicAt = null;   // set once the music is rendered and its length known (PodiumTick)
            _music.Play(MusicTrack.None);
            return;
        }
        // Champagne: the theme, after the anthem has finished (it may run on for a moment).
        _musicAt = null;
        _themeWanted = true;
        PodiumMusicBank.Prepare(null);
    }

    /// <summary>Per frame in the ceremony: starts the anthem (or the theme) on time, and the theme once the anthem is done.</summary>
    private void PodiumTick(RedlineHostView view)
    {
        if (view.Phase == RacePhase.Anthem && !_themeWanted)
        {
            var phase = HudClock.PhaseSeconds(view);
            var loops = _anthem is null;
            var clip = _anthem is { } info ? PodiumMusicBank.Anthem(info) : PodiumMusicBank.Theme();
            if (!clip.IsCompletedSuccessfully)
            {
                return;
            }
            if (_musicAt is null)
            {
                _musicAt = _anthemPhaseStart + PodiumMusic.Start(_anthemLine, clip.Result.Seconds, phase, loops);
                if (loops)
                {
                    _music.SetTheme(clip.Result);
                }
                else
                {
                    _music.SetAnthem(clip.Result, _anthem!.Id);
                }
            }
            if (_time >= _musicAt && _music.Wanted == MusicTrack.None && _time - _anthemPhaseStart < phase)
            {
                _anthemEnds = loops ? double.NegativeInfinity : _time + clip.Result.Seconds;
                _music.Play(loops ? MusicTrack.Theme : MusicTrack.Anthem);
            }
            return;
        }
        if (_themeWanted && _time >= _anthemEnds - 0.25 && PodiumMusicBank.Theme() is { IsCompletedSuccessfully: true } theme)
        {
            _music.SetTheme(theme.Result);
            if (_music.Wanted != MusicTrack.Theme)
            {
                _music.Play(MusicTrack.Theme);
            }
            _themeWanted = false;
        }
    }

    /// <summary>A champagne spray: the cork, the hiss at the spray's strength (on the step's own voice), the sting for the first.</summary>
    public void OnSpray(NewSpray spray)
    {
        var look = SprayLook.For(spray.Strength);
        foreach (var cue in AudioCues.ForSpray(spray.First))
        {
            if (cue != AudioCues.SprayHiss)
            {
                Play(cue, cue == AudioCues.CorkPop ? $"step {spray.Step}" : null);
                continue;
            }
            AudioLog.Play(AudioBuses.Sfx, $"{cue} step {spray.Step} {look.Strength:0.00}");
            if (_hiss[Math.Clamp(spray.Step, 1, 3) - 1] is { } hiss)
            {
                hiss.VolumeDb = (float)look.HissDb;
                hiss.PitchScale = (float)look.HissPitch;
                hiss.Play();
            }
        }
    }

    /// <summary>Start light <paramref name="index"/> (1–5) has just come on.</summary>
    public void OnLight(int index)
    {
        _lit = index;
        Play(AudioCues.StartBeep, $"#{index}");
    }

    /// <summary>The camera shows car <paramref name="code"/> spinning out.</summary>
    public void OnSpin(string code)
    {
        Play(AudioCues.Screech, code);
        _later.Add((_time + CrunchDelay, AudioCues.Crunch));
    }

    /// <summary>Every frame: countdown ticks, delayed cues and the engines on <paramref name="shot"/>'s cars.</summary>
    public void Tick(double delta, Shot? shot, Func<string, double> speed)
    {
        _time += delta;
        if (!_loaded && SfxBank.Prewarm() is { IsCompletedSuccessfully: true } build)
        {
            Load(build.Result);
        }
        for (var i = _later.Count - 1; i >= 0; i--)
        {
            if (_time >= _later[i].At)
            {
                Play(_later[i].Cue);
                _later.RemoveAt(i);
            }
        }
        if (_view is not { } view)
        {
            return;
        }
        if (view.Phase is RacePhase.Anthem or RacePhase.Champagne)
        {
            PodiumTick(view);
        }
        if (_remaining is { } before)
        {
            var after = Math.Max(0, before - delta);
            _remaining = after;
            if (AudioCues.CountdownTicks(view.Phase, before, after) > 0)
            {
                Play(AudioCues.Countdown, $"{Math.Ceiling(after - 1e-6):0}");
            }
        }
        _engines.Update(delta, view.Phase, EngineMix.Subjects(shot, view), speed, _lit, _biggestGain);
    }

    private void Load(SfxBank.Sounds sounds)
    {
        foreach (var (name, pcm) in sounds.Clips)
        {
            if (name == AudioCues.CrowdBed)
            {
                _crowd.Stream = SfxBank.Wav(pcm, loop: true);
                continue;
            }
            if (name == AudioCues.SprayHiss)
            {
                // One voice per step, so overlapping sprays keep their own strength.
                for (var i = 0; i < _hiss.Length; i++)
                {
                    _hiss[i] = new AudioStreamPlayer { Name = $"{name}-{i + 1}", Bus = AudioBuses.Sfx, Stream = SfxBank.Wav(pcm) };
                    AddChild(_hiss[i]);
                }
                continue;
            }
            var player = new AudioStreamPlayer { Name = name, Bus = AudioBuses.Sfx, Stream = SfxBank.Wav(pcm), MaxPolyphony = 4 };
            AddChild(player);
            _players[name] = player;
        }
        _loaded = true;
        GD.Print($"REDLINE sounds ready ({sounds.Clips.Count} effects and 2 music loops synthesised in {sounds.Milliseconds} ms)");
        if (_crowdOn)
        {
            _crowdOn = false;
            SetCrowd(true);
        }
    }

    private void Play(string cue, string? detail = null)
    {
        AudioLog.Play(AudioBuses.Sfx, detail is null ? cue : $"{cue} {detail}");
        if (_players.TryGetValue(cue, out var player))
        {
            player.Play();
        }
    }

    private void SetCrowd(bool on)
    {
        if (on == _crowdOn)
        {
            return;
        }
        _crowdOn = on;
        if (!on)
        {
            _crowd.Stop();
        }
        else if (_crowd.Stream is not null)
        {
            _crowd.Play();
            AudioLog.Play(AudioBuses.Sfx, AudioCues.CrowdBed);
        }
    }
}
