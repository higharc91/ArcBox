using System.Text.RegularExpressions;
using ArcBox.Server.Profiles;
using ArcBox.Server.Rooms;
using ArcBox.Server.Storage;
using ArcBox.Server.Tests.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace ArcBox.Server.Tests.Profiles;

public sealed class ProfileServiceTests : IDisposable
{
    private readonly ArcDb _db = StorageTestSupport.NewMemoryDb();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly StorageWriter _writer;
    private readonly ProfileService _profiles;

    public ProfileServiceTests()
    {
        _writer = new StorageWriter(_db, _time, NullLogger<StorageWriter>.Instance);
        _profiles = new ProfileService(_db, _writer, _time);
    }

    public void Dispose()
    {
        _writer.Dispose();
        _db.Dispose();
    }

    private Task FlushAsync() => _writer.FlushAsync(TestContext.Current.CancellationToken);

    [Fact]
    public void Create_ids_match_pr_prefix()
    {
        var profile = _profiles.Create("Dave");

        Assert.Matches(new Regex("^pr_[A-Za-z0-9_-]{16}$"), profile.Id.Value);
        Assert.Equal("Dave", profile.DisplayName);
        Assert.Equal(profile, _profiles.Find(profile.Id));
        Assert.NotEqual(profile.Id, _profiles.Create("Dave").Id);
    }

    [Fact]
    public async Task IssueKey_stores_only_the_hash()
    {
        var profile = _profiles.Create("Dave");
        var key = _profiles.IssueKey(profile.Id);
        await FlushAsync();

        Assert.Equal(profile.Id, key.Id);
        Assert.Equal("Dave", key.Name);
        Assert.Equal(22, key.Key.Length); // 128 bits, base64url
        Assert.Equal(0, _db.Scalar<int>($"SELECT COUNT(*) FROM profile_keys WHERE key_hash = '{key.Key}'"));
        Assert.Equal(1, _db.Scalar<int>($"SELECT COUNT(*) FROM profile_keys WHERE key_hash = '{Secrets.Hash(key.Key)}'"));
    }

    [Fact]
    public void Resolve_finds_profile_by_key()
    {
        var dave = _profiles.Create("Dave");
        var key = _profiles.IssueKey(dave.Id);

        Assert.Equal(dave, _profiles.Resolve(key.Key));
        Assert.Null(_profiles.Resolve("not-a-key"));
        Assert.Null(_profiles.Resolve(""));
    }

    [Fact]
    public async Task IssueKey_keeps_newest_10()
    {
        var profile = _profiles.Create("Dave");
        var other = _profiles.Create("Erin");
        var otherKey = _profiles.IssueKey(other.Id);
        var keys = Enumerable.Range(0, 12).Select(_ => _profiles.IssueKey(profile.Id).Key).ToList();
        await FlushAsync();

        Assert.All(keys.Take(2), k => Assert.Null(_profiles.Resolve(k)));
        Assert.All(keys.Skip(2), k => Assert.Equal(profile, _profiles.Resolve(k)));
        Assert.Equal(other, _profiles.Resolve(otherKey.Key));
        Assert.Equal(10, _db.Scalar<int>($"SELECT COUNT(*) FROM profile_keys WHERE profile_id = '{profile.Id}'"));
        Assert.Equal(1, _db.Scalar<int>($"SELECT COUNT(*) FROM profile_keys WHERE profile_id = '{other.Id}'"));
    }

    [Fact]
    public async Task ForgetKeys_revokes_all()
    {
        var dave = _profiles.Create("Dave");
        var a = _profiles.IssueKey(dave.Id);
        var b = _profiles.IssueKey(dave.Id);
        var erin = _profiles.Create("Erin");
        var c = _profiles.IssueKey(erin.Id);

        _profiles.ForgetKeys(dave.Id);
        await FlushAsync();

        Assert.Null(_profiles.Resolve(a.Key));
        Assert.Null(_profiles.Resolve(b.Key));
        Assert.Equal(erin, _profiles.Resolve(c.Key));
        Assert.Equal(dave, _profiles.Find(dave.Id));
        Assert.Equal(0, _db.Scalar<int>($"SELECT COUNT(*) FROM profile_keys WHERE profile_id = '{dave.Id}'"));
        Assert.Equal(1, _db.Scalar<int>($"SELECT COUNT(*) FROM profile_keys WHERE profile_id = '{erin.Id}'"));
    }

    [Fact]
    public void ResolveAll_orders_by_last_used_desc()
    {
        var a = _profiles.Create("Ann");
        var b = _profiles.Create("Bob");
        var c = _profiles.Create("Cy");
        var ka = _profiles.IssueKey(a.Id).Key;
        var kb = _profiles.IssueKey(b.Id).Key;
        var kc = _profiles.IssueKey(c.Id).Key;

        _time.Advance(TimeSpan.FromMinutes(1));
        _profiles.Resolve(kb);
        _time.Advance(TimeSpan.FromMinutes(1));
        _profiles.Resolve(ka);

        Assert.Equal([a, b, c], _profiles.ResolveAll([kc, kb, "unknown", ka, ka]));
        Assert.Empty(_profiles.ResolveAll([]));
    }

    [Fact]
    public async Task Survives_restart()
    {
        var dave = _profiles.Create("Dave");
        var key = _profiles.IssueKey(dave.Id);
        await FlushAsync();

        var restarted = new ProfileService(_db, _writer, _time);

        Assert.Equal(dave, restarted.Resolve(key.Key));
        Assert.Equal(dave, restarted.Find(dave.Id));
    }

    [Fact]
    public async Task Resolve_persists_last_used()
    {
        var dave = _profiles.Create("Dave");
        var key = _profiles.IssueKey(dave.Id);
        await FlushAsync();

        _time.Advance(TimeSpan.FromHours(1));
        _profiles.Resolve(key.Key);
        await FlushAsync();

        Assert.Equal(_time.GetUtcNow().UtcDateTime.ToString("O"),
            _db.Scalar<string>($"SELECT last_used_utc FROM profile_keys WHERE key_hash = '{Secrets.Hash(key.Key)}'"));
    }
}
