using ArcBox.Host.Core.Audio;
using Godot;

namespace ArcBox.Host.Audio;

/// <summary>
/// The TV's mixer (audio spec §3.3): <c>Master → Music, Sfx, Voice</c>, made in code at start-up. Each bus's volume is
/// its setting (<see cref="AudioSettings"/>, from <c>user://settings.json</c>) plus the voice ducking on Music and Sfx;
/// mute silences Master (and the narrator's built-in voice, which bypasses the buses).
/// </summary>
public static class AudioBuses
{
    public const string Master = "Master";
    public const string Music = "Music";
    public const string Sfx = "Sfx";
    public const string Voice = "Voice";

    private static double _duckMusicDb;
    private static double _duckSfxDb;

    public static AudioSettings Levels { get; private set; } = new(1, AudioSettings.DefaultMusic, AudioSettings.DefaultSfx, 1, false);

    /// <summary>The built-in TTS voice's volume (0–1): it does not go through the buses, so master, voice and mute apply here.</summary>
    public static double BuiltInVoiceVolume => Levels.Muted ? 0 : Levels.Master * Levels.Voice;

    /// <summary>Adds the Music, Sfx and Voice buses (sending to Master) if they are missing.</summary>
    public static void Ensure()
    {
        foreach (var name in new[] { Music, Sfx, Voice })
        {
            if (AudioServer.GetBusIndex(name) >= 0)
            {
                continue;
            }
            AudioServer.AddBus();
            var index = AudioServer.BusCount - 1;
            AudioServer.SetBusName(index, name);
            AudioServer.SetBusSend(index, Master);
        }
        Refresh();
    }

    public static void Apply(AudioSettings levels)
    {
        Levels = levels;
        Refresh();
    }

    /// <summary>The ducking offsets (≤ 0 dB) for Music and Sfx, set every frame by <see cref="Ducking"/>.</summary>
    public static void SetDuck(double musicDb, double sfxDb)
    {
        if (Math.Abs(musicDb - _duckMusicDb) < 0.01 && Math.Abs(sfxDb - _duckSfxDb) < 0.01)
        {
            return;
        }
        _duckMusicDb = musicDb;
        _duckSfxDb = sfxDb;
        Refresh();
    }

    private static void Refresh()
    {
        Set(Master, AudioLevels.Db(Levels.Master));
        AudioServer.SetBusMute(AudioServer.GetBusIndex(Master), Levels.Muted);
        Set(Music, AudioLevels.Db(Levels.Music) + _duckMusicDb);
        Set(Sfx, AudioLevels.Db(Levels.Sfx) + _duckSfxDb);
        Set(Voice, AudioLevels.Db(Levels.Voice));
    }

    private static void Set(string bus, double db)
    {
        var index = AudioServer.GetBusIndex(bus);
        if (index >= 0)
        {
            AudioServer.SetBusVolumeDb(index, (float)Math.Max(AudioLevels.SilentDb, db));
        }
    }
}
