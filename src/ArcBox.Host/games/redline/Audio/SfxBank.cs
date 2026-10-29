using System.Diagnostics;
using Godot;
using Redline.Presentation.Audio;
using Redline.Presentation.Audio.Synth;
using SynthMusic = Redline.Presentation.Audio.Synth.Music;

namespace ArcBox.Host.Games.Redline.Audio;

/// <summary>
/// REDLINE's sounds, synthesised once per run on a background thread (about 1.5 s of CPU) while the world builds, like
/// <c>RedlineAssets.Prewarm</c>: every <see cref="Sfx"/> clip by its <see cref="AudioCues"/> name (with the podium's cork pop,
/// spray hiss and victory sting), and the two music loops. The podium music is rendered later, by <see cref="PodiumMusicBank"/>.
/// </summary>
public static class SfxBank
{
    /// <summary>The crowd murmur loop's length.</summary>
    private const double CrowdBedSeconds = 8;

    private static readonly Lock Gate = new();
    private static Task<Sounds>? _build;

    public sealed record Sounds(IReadOnlyDictionary<string, Pcm> Clips, MusicLoop Lobby, MusicLoop Race, long Milliseconds);

    /// <summary>Starts the build (once) and returns it; safe to call from anywhere, any number of times.</summary>
    public static Task<Sounds> Prewarm()
    {
        lock (Gate)
        {
            return _build ??= Task.Run(Build);
        }
    }

    private static Sounds Build()
    {
        var sw = Stopwatch.StartNew();
        var makers = new (string Name, Func<Pcm> Make)[]
        {
            (AudioCues.StartBeep, Sfx.StartBeep),
            (AudioCues.LightsOut, Sfx.LightsOut),
            (AudioCues.Lock, Sfx.LockClick),
            (AudioCues.Answer, Sfx.Tick),
            (AudioCues.Countdown, Sfx.CountdownTick),
            (AudioCues.Whoosh, Sfx.Whoosh),
            (AudioCues.Correct, Sfx.CorrectSting),
            (AudioCues.Wrong, Sfx.WrongBuzz),
            (AudioCues.Purple, Sfx.PurpleChime),
            (AudioCues.Screech, Sfx.TyreScreech),
            (AudioCues.Crunch, Sfx.Crunch),
            (AudioCues.Cheer, Sfx.CrowdCheer),
            (AudioCues.Fanfare, Sfx.Fanfare),
            (AudioCues.CrowdBed, () => Sfx.CrowdBed(CrowdBedSeconds)),
            (AudioCues.CorkPop, ChampagneSfx.CorkPop),
            (AudioCues.SprayHiss, ChampagneSfx.SprayHiss),
            (AudioCues.Sting, VictoryTheme.Sting),
        };
        var clips = makers.AsParallel().Select(m => (m.Name, Pcm: m.Make())).ToDictionary(x => x.Name, x => x.Pcm);
        var lobby = Task.Run(SynthMusic.LobbyLoop);
        var race = Task.Run(SynthMusic.RaceBed);
        Task.WaitAll(lobby, race);
        return new Sounds(clips, lobby.Result, race.Result, sw.ElapsedMilliseconds);
    }

    /// <summary>A Godot stream for <paramref name="pcm"/> (16-bit mono), looping over the whole clip when <paramref name="loop"/>.</summary>
    public static AudioStreamWav Wav(Pcm pcm, bool loop = false) => new()
    {
        Format = AudioStreamWav.FormatEnum.Format16Bits,
        MixRate = pcm.SampleRate,
        Stereo = false,
        Data = pcm.ToBytes(),
        LoopMode = loop ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled,
        LoopBegin = 0,
        LoopEnd = loop ? pcm.Samples.Length : 0,
    };
}
