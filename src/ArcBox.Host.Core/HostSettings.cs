using System.Text.Json;
using ArcBox.Sdk;

namespace ArcBox.Host.Core;

/// <summary>
/// <c>user://settings.json</c> = <c>{ "serverUrl": "…", "preset": "ultra|high|low", "masterVolume": 1, "musicVolume": 0.55,
/// "sfxVolume": 0.85, "voiceVolume": 1, "muted": false, "narrationVoice": "…" }</c>: the server picked on the Connect
/// screen, the render preset last chosen with F2, the audio mixer (linear 0–1; <c>M</c> toggles <c>muted</c>) and,
/// optionally, the built-in TTS voice the narrator falls back to (part of a voice id or name; default: the first
/// English voice). Any of them may be missing; see <see cref="Audio.AudioSettings"/> for the audio defaults.
/// </summary>
public sealed record HostSettings(string? ServerUrl = null, string? Preset = null)
{
    public const string DefaultServer = "http://localhost:5080";

    public double? MasterVolume { get; init; }
    public double? MusicVolume { get; init; }
    public double? SfxVolume { get; init; }
    public double? VoiceVolume { get; init; }
    public bool? Muted { get; init; }

    /// <summary>Part of the id or name of the built-in voice to use when the server's voice is unavailable.</summary>
    public string? NarrationVoice { get; init; }
}

/// <summary>Reads and writes the settings file (given as a real file path). Never throws for a bad or missing file.</summary>
public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions Json = new(ArcJson.Options) { WriteIndented = true };

    public string Path { get; } = path;

    /// <summary>The saved settings, or defaults when the file is missing or unreadable.</summary>
    public HostSettings Load()
    {
        try
        {
            return File.Exists(Path)
                ? JsonSerializer.Deserialize<HostSettings>(File.ReadAllText(Path), Json) ?? new HostSettings()
                : new HostSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new HostSettings();
        }
    }

    /// <summary>Writes <paramref name="settings"/>; false when the file could not be written.</summary>
    public bool Save(HostSettings settings)
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path));
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            var temp = Path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, Json));
            File.Move(temp, Path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Loads, changes one thing and saves, so the other fields survive.</summary>
    public HostSettings Update(Func<HostSettings, HostSettings> change)
    {
        var next = change(Load());
        Save(next);
        return next;
    }
}
