namespace ArcBox.Sdk.Content;

/// <summary>Read-only content input supplied to loaders. Battle rules never acquire filesystem handles.</summary>
public interface IFileSource
{
    bool Exists(string path);
    byte[] ReadAllBytes(string path);
    string ReadAllText(string path);
    string[] GetDirectories(string path);
    Stream OpenRead(string path);
}

/// <summary>The platform's physical-file adapter; callers may instead provide an in-memory or packaged content source.</summary>
public sealed class PhysicalFileSource : IFileSource
{
    public static IFileSource Instance { get; } = new PhysicalFileSource();
    private PhysicalFileSource() { }
    public bool Exists(string path) => File.Exists(path);
    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);
    public string ReadAllText(string path) => File.ReadAllText(path);
    public string[] GetDirectories(string path) => Directory.GetDirectories(path);
    public Stream OpenRead(string path) => File.OpenRead(path);
}
