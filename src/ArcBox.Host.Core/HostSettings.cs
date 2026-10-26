using System.Text.Json;
using ArcBox.Sdk;

namespace ArcBox.Host.Core;

/// <summary>
/// <c>user://settings.json</c> = <c>{ "serverUrl": "…", "preset": "ultra|high|low" }</c>: the server picked on the
/// Connect screen and the render preset last chosen with F2. Either may be missing.
/// </summary>
public sealed record HostSettings(string? ServerUrl = null, string? Preset = null)
{
    public const string DefaultServer = "http://localhost:5080";
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
