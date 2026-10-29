using ArcBox.Host.Core.Audio;

namespace ArcBox.Host.Core.Tests;

public sealed class HostSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "arcbox-settings-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_dir, "nested", "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Missing_file_loads_defaults()
    {
        Assert.Equal(new HostSettings(), new SettingsStore(FilePath).Load());
    }

    [Fact]
    public void Round_trip_with_platform_field_names()
    {
        var store = new SettingsStore(FilePath);

        store.Save(new HostSettings("http://10.0.0.2:5080", "high"));

        Assert.Equal(new HostSettings("http://10.0.0.2:5080", "high"), new SettingsStore(FilePath).Load());
        var json = File.ReadAllText(FilePath);
        Assert.Contains("\"serverUrl\"", json);
        Assert.Contains("\"preset\"", json);
    }

    [Fact]
    public void Update_keeps_the_other_fields()
    {
        var store = new SettingsStore(FilePath);
        store.Save(new HostSettings("http://10.0.0.2:5080", null));

        store.Update(s => s with { Preset = "low" });
        store.Update(s => s with { ServerUrl = "http://10.0.0.3:5080" });

        Assert.Equal(new HostSettings("http://10.0.0.3:5080", "low"), store.Load());
    }

    [Fact]
    public void Old_file_without_a_preset_still_loads()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, """{ "serverUrl": "http://10.0.0.2:5080" }""");

        Assert.Equal(new HostSettings("http://10.0.0.2:5080", null), new SettingsStore(FilePath).Load());
    }

    [Fact]
    public void Corrupt_file_loads_defaults_and_is_overwritten_by_the_next_save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, "{ not json");
        var store = new SettingsStore(FilePath);

        Assert.Equal(new HostSettings(), store.Load());
        store.Update(s => s with { Preset = "ultra" });
        Assert.Equal(new HostSettings(null, "ultra"), store.Load());
    }

    [Fact]
    public void Volumes_mute_and_narration_voice_round_trip()
    {
        var store = new SettingsStore(FilePath);
        store.Save(new HostSettings("http://10.0.0.2:5080", "high")
        {
            MasterVolume = 0.8, MusicVolume = 0.3, SfxVolume = 0.9, VoiceVolume = 1, Muted = true, NarrationVoice = "Zira",
        });

        var loaded = new SettingsStore(FilePath).Load();
        Assert.Equal(0.3, loaded.MusicVolume);
        Assert.True(loaded.Muted);
        Assert.Equal("Zira", loaded.NarrationVoice);
        var json = File.ReadAllText(FilePath);
        Assert.Contains("\"musicVolume\"", json);
        Assert.Contains("\"narrationVoice\"", json);
    }

    [Fact]
    public void Audio_levels_default_and_clamp()
    {
        var levels = AudioSettings.From(new HostSettings());
        Assert.Equal(new AudioSettings(1, AudioSettings.DefaultMusic, AudioSettings.DefaultSfx, 1, false), levels);

        var wild = AudioSettings.From(new HostSettings { MasterVolume = 7, MusicVolume = -2, SfxVolume = double.NaN });
        Assert.Equal(1, wild.Master);
        Assert.Equal(0, wild.Music);
        Assert.Equal(AudioSettings.DefaultSfx, wild.Sfx);
    }
}
