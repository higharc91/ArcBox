namespace ArcBox.Host.Core.Tests;

public sealed class SessionStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "arcbox-session-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_dir, "nested", "session.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Round_trip()
    {
        var store = new SessionStore(FilePath);
        var session = new HostSession("http://localhost:5080", "BCDF", "token-123");

        store.Save(session);

        Assert.Equal(session, new SessionStore(FilePath).Load());
    }

    [Fact]
    public void Saved_file_uses_platform_field_names()
    {
        new SessionStore(FilePath).Save(new HostSession("http://x", "BCDF", "t"));

        var json = File.ReadAllText(FilePath);
        Assert.Contains("\"serverUrl\"", json);
        Assert.Contains("\"code\"", json);
        Assert.Contains("\"hostToken\"", json);
    }

    [Fact]
    public void Missing_file_loads_null()
    {
        Assert.Null(new SessionStore(FilePath).Load());
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("null")]
    [InlineData("{\"serverUrl\":\"http://x\"}")]
    [InlineData("[]")]
    public void Corrupt_file_loads_null_and_is_deleted(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, content);

        Assert.Null(new SessionStore(FilePath).Load());
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void Clear_deletes_the_file_and_tolerates_missing()
    {
        var store = new SessionStore(FilePath);
        store.Save(new HostSession("http://x", "BCDF", "t"));

        store.Clear();
        store.Clear();

        Assert.False(File.Exists(FilePath));
        Assert.Null(store.Load());
    }
}
