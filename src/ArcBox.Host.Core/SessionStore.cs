using System.Text.Json;
using ArcBox.Sdk;

namespace ArcBox.Host.Core;

/// <summary>What the host needs to resume its room after a restart (platform §5.4).</summary>
public sealed record HostSession(string ServerUrl, string Code, string HostToken);

/// <summary>Reads and writes <c>user://session.json</c> (given as a real file path).</summary>
public sealed class SessionStore(string path)
{
    private static readonly JsonSerializerOptions Json = new(ArcJson.Options) { WriteIndented = true };

    public string Path { get; } = path;

    /// <summary>The saved session, or null if there is none. A corrupt file is deleted.</summary>
    public HostSession? Load()
    {
        if (!File.Exists(Path))
        {
            return null;
        }

        try
        {
            var session = JsonSerializer.Deserialize<HostSession>(File.ReadAllText(Path), Json);
            if (session is not null
                && !string.IsNullOrWhiteSpace(session.ServerUrl)
                && !string.IsNullOrWhiteSpace(session.Code)
                && !string.IsNullOrWhiteSpace(session.HostToken))
            {
                return session;
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Fall through: treat as corrupt.
        }

        Clear();
        return null;
    }

    public void Save(HostSession session)
    {
        var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path));
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // Write then move, so a crash mid-write never leaves half a file.
        var temp = Path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(session, Json));
        File.Move(temp, Path, overwrite: true);
    }

    public void Clear()
    {
        try
        {
            File.Delete(Path);
        }
        catch (DirectoryNotFoundException)
        {
            // Nothing to clear.
        }
    }
}
