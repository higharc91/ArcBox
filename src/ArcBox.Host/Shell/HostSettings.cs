using System.Text.Json;
using ArcBox.Sdk;
using Godot;

namespace ArcBox.Host.Shell;

/// <summary><c>user://settings.json</c> = <c>{ "serverUrl": "…" }</c>: the server picked on the Connect screen.</summary>
public sealed record HostSettings(string? ServerUrl)
{
    public const string DefaultServer = "http://localhost:5080";
    private const string FilePath = "user://settings.json";

    public static HostSettings Load()
    {
        try
        {
            var path = ProjectSettings.GlobalizePath(FilePath);
            return System.IO.File.Exists(path)
                ? JsonSerializer.Deserialize<HostSettings>(System.IO.File.ReadAllText(path), ArcJson.Options) ?? new HostSettings((string?)null)
                : new HostSettings((string?)null);
        }
        catch (Exception ex) when (ex is JsonException or System.IO.IOException)
        {
            GD.PushWarning($"Ignoring unreadable settings: {ex.Message}");
            return new HostSettings((string?)null);
        }
    }

    public void Save()
    {
        try
        {
            var path = ProjectSettings.GlobalizePath(FilePath);
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path, JsonSerializer.Serialize(this, ArcJson.Options));
        }
        catch (System.IO.IOException ex)
        {
            GD.PushWarning($"Could not save settings: {ex.Message}");
        }
    }
}
