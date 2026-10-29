using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArcBox.Host.Core.Tests.Support;

namespace ArcBox.Host.Core.Tests;

public sealed class ContentCacheTests : IDisposable
{
    private const string Game = SignalEchoGame.Id;
    private const string Pack = HostServerFactory.PackId;

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("arcbox-cache-");
    private readonly HostServerFactory _factory = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _factory.Dispose();
        _root.Delete(true);
    }

    private string PackDir(string hash) => Path.Combine(_root.FullName, Game, Pack, hash);

    private static string FileSha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>The spec's pack hash, computed independently of the server and the cache.</summary>
    private static string PackHash(IReadOnlyDictionary<string, byte[]> files)
    {
        var text = new StringBuilder();
        foreach (var (path, bytes) in files.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            text.Append(path).Append('\n').Append(FileSha(bytes)).Append('\n');
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private static readonly string ServerHash = PackHash(HostServerFactory.PackFiles);

    private static IEnumerable<string> RootEntries(DirectoryInfo root) =>
        root.EnumerateFileSystemInfos().Select(e => e.Name);

    [Fact]
    public async Task Downloads_verifies_and_installs()
    {
        var cache = new ContentCache(_root.FullName, _factory.CreateContentClient());
        Assert.False(cache.IsCached(Game, Pack, ServerHash));

        var dir = await cache.EnsureAsync(Game, Pack, ServerHash, null, Ct);

        Assert.Equal(PackDir(ServerHash), dir);
        Assert.True(cache.IsCached(Game, Pack, ServerHash));
        foreach (var (path, bytes) in HostServerFactory.PackFiles)
        {
            Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(dir, path), Ct));
        }
        Assert.Equal(HostServerFactory.PackFiles.Count, Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Count());
        Assert.Equal([Game], RootEntries(_root)); // no .tmp-* left behind
    }

    [Fact]
    public async Task Cached_pack_makes_no_requests()
    {
        var counter = new CountingHandler();
        var cache = new ContentCache(_root.FullName, _factory.CreateContentClient(inner => counter.Wrap(inner)));

        await cache.EnsureAsync(Game, Pack, ServerHash, null, Ct);
        Assert.NotEmpty(counter.Requests);
        counter.Requests.Clear();

        var dir = await cache.EnsureAsync(Game, Pack, ServerHash, null, Ct);

        Assert.Equal(PackDir(ServerHash), dir);
        Assert.Empty(counter.Requests);
    }

    [Fact]
    public async Task Requests_encode_each_path_segment()
    {
        var counter = new CountingHandler();
        var cache = new ContentCache(_root.FullName, _factory.CreateContentClient(inner => counter.Wrap(inner)));

        await cache.EnsureAsync(Game, Pack, ServerHash, null, Ct);

        var prefix = $"/content/{Game}/{Pack}/{ServerHash}/";
        Assert.Contains(prefix + "maps/level%20one.json", counter.Requests);
        Assert.Contains(prefix + "notes%20%231%20100%25.txt", counter.Requests);
        Assert.Contains(prefix + "art/caf%C3%A9.bin", counter.Requests);
    }

    [Fact]
    public async Task Tampered_file_throws_and_leaves_nothing()
    {
        var counter = new CountingHandler
        {
            Intercept = async (request, send, ct) =>
            {
                var response = await send(request, ct);
                if (request.RequestUri!.AbsolutePath.EndsWith("/manifest.json", StringComparison.Ordinal))
                {
                    response.Content = new ByteArrayContent("{\"name\":\"evil\"}"u8.ToArray());
                }
                return response;
            },
        };
        var cache = new ContentCache(_root.FullName, _factory.CreateContentClient(inner => counter.Wrap(inner)));

        await Assert.ThrowsAsync<ContentIntegrityException>(() => cache.EnsureAsync(Game, Pack, ServerHash, null, Ct));

        Assert.False(cache.IsCached(Game, Pack, ServerHash));
        Assert.Empty(RootEntries(_root));
    }

    [Fact]
    public async Task Interrupted_download_leaves_nothing_and_retry_succeeds()
    {
        var fileRequests = 0;
        var counter = new CountingHandler
        {
            Intercept = (request, send, ct) =>
            {
                // Fail the third file request once, as if the network dropped mid-download.
                if (request.RequestUri!.AbsolutePath.StartsWith($"/content/{Game}/{Pack}/", StringComparison.Ordinal)
                    && Interlocked.Increment(ref fileRequests) == 3)
                {
                    throw new HttpRequestException("Simulated network drop.");
                }
                return send(request, ct);
            },
        };
        var cache = new ContentCache(_root.FullName, _factory.CreateContentClient(inner => counter.Wrap(inner)));

        await Assert.ThrowsAsync<HttpRequestException>(() => cache.EnsureAsync(Game, Pack, ServerHash, null, Ct));
        Assert.False(cache.IsCached(Game, Pack, ServerHash));
        Assert.Empty(RootEntries(_root));

        var dir = await cache.EnsureAsync(Game, Pack, ServerHash, null, Ct);

        Assert.True(cache.IsCached(Game, Pack, ServerHash));
        foreach (var (path, bytes) in HostServerFactory.PackFiles)
        {
            Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(dir, path), Ct));
        }
        Assert.Equal([Game], RootEntries(_root));
    }

    [Fact]
    public async Task Cancelled_download_leaves_nothing()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var cache = new ContentCache(_root.FullName, _factory.CreateContentClient());
        var progress = new SyncProgress(_ => cts.Cancel());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => cache.EnsureAsync(Game, Pack, ServerHash, progress, cts.Token));

        Assert.Empty(RootEntries(_root));
    }

    [Fact]
    public async Task Unknown_pack_or_hash_throws_integrity_exception_with_a_clear_message()
    {
        var cache = new ContentCache(_root.FullName, _factory.CreateContentClient());
        var stale = new string('0', 64);

        var staleHash = await Assert.ThrowsAsync<ContentIntegrityException>(() => cache.EnsureAsync(Game, Pack, stale, null, Ct));
        Assert.Contains(Pack, staleHash.Message);
        Assert.Contains(stale, staleHash.Message);
        await Assert.ThrowsAsync<ContentIntegrityException>(() => cache.EnsureAsync(Game, "nope", ServerHash, null, Ct));
        await Assert.ThrowsAsync<ContentIntegrityException>(() => cache.EnsureAsync("no-such-game", Pack, ServerHash, null, Ct));
        Assert.Empty(RootEntries(_root));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("")]
    public async Task Unsafe_ids_are_refused_before_touching_the_disk_or_network(string bad)
    {
        var counter = new CountingHandler();
        var cache = new ContentCache(_root.FullName, _factory.CreateContentClient(inner => counter.Wrap(inner)));

        Assert.False(cache.IsCached(Game, bad, ServerHash));
        await Assert.ThrowsAsync<ContentIntegrityException>(() => cache.EnsureAsync(Game, bad, ServerHash, null, Ct));
        await Assert.ThrowsAsync<ContentIntegrityException>(() => cache.EnsureAsync(bad, Pack, ServerHash, null, Ct));
        await Assert.ThrowsAsync<ContentIntegrityException>(() => cache.EnsureAsync(Game, Pack, bad, null, Ct));

        Assert.Empty(counter.Requests);
        Assert.Empty(RootEntries(_root));
    }

    [Fact]
    public async Task A_listed_path_that_escapes_the_pack_is_refused()
    {
        var evil = new FakeContent(Game, Pack).Add(new Dictionary<string, byte[]> { ["../escape.txt"] = [1] });
        var cache = new ContentCache(_root.FullName, evil.Client());

        await Assert.ThrowsAsync<ContentIntegrityException>(() => cache.EnsureAsync(Game, Pack, evil.Hashes[0], null, Ct));

        Assert.Empty(RootEntries(_root));
        Assert.False(File.Exists(Path.Combine(_root.FullName, "escape.txt")));
    }

    [Fact]
    public async Task Pack_hash_that_does_not_match_the_files_is_refused()
    {
        // The index claims a hash that isn't the hash of the files it lists, though every file's own sha is right.
        var files = new Dictionary<string, byte[]> { ["a.txt"] = [1, 2, 3] };
        var lie = new string('a', 64);
        var fake = new FakeContent(Game, Pack).Add(files, claimedHash: lie);
        var cache = new ContentCache(_root.FullName, fake.Client());

        var ex = await Assert.ThrowsAsync<ContentIntegrityException>(() => cache.EnsureAsync(Game, Pack, lie, null, Ct));

        Assert.Contains("hash", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(RootEntries(_root));
    }

    [Fact]
    public async Task Concurrent_ensures_of_one_pack_both_succeed()
    {
        var cache = new ContentCache(_root.FullName, _factory.CreateContentClient());

        var dirs = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => cache.EnsureAsync(Game, Pack, ServerHash, null, Ct)));

        Assert.All(dirs, d => Assert.Equal(PackDir(ServerHash), d));
        Assert.Equal(HostServerFactory.PackFiles.Count, Directory.EnumerateFiles(PackDir(ServerHash), "*", SearchOption.AllDirectories).Count());
        Assert.Equal([Game], RootEntries(_root));
    }

    [Fact]
    public async Task Keeps_newest_3_hashes()
    {
        var fake = new FakeContent(Game, Pack);
        for (var i = 1; i <= 4; i++)
        {
            fake.Add(new Dictionary<string, byte[]> { ["v.txt"] = [(byte)i] });
        }
        var cache = new ContentCache(_root.FullName, fake.Client());
        var (h1, h2, h3, h4) = (fake.Hashes[0], fake.Hashes[1], fake.Hashes[2], fake.Hashes[3]);

        await cache.EnsureAsync(Game, Pack, h1, null, Ct);
        await cache.EnsureAsync(Game, Pack, h2, null, Ct);
        await cache.EnsureAsync(Game, Pack, h3, null, Ct);
        Assert.True(cache.IsCached(Game, Pack, h1));

        // Write time, not install order, decides: h2 is the newest of the old three, h1 the oldest.
        var now = DateTime.UtcNow;
        Directory.SetLastWriteTimeUtc(PackDir(h1), now.AddHours(-3));
        Directory.SetLastWriteTimeUtc(PackDir(h3), now.AddHours(-2));
        Directory.SetLastWriteTimeUtc(PackDir(h2), now.AddHours(-1));

        await cache.EnsureAsync(Game, Pack, h4, null, Ct);

        Assert.False(cache.IsCached(Game, Pack, h1));
        Assert.True(cache.IsCached(Game, Pack, h2));
        Assert.True(cache.IsCached(Game, Pack, h3));
        Assert.True(cache.IsCached(Game, Pack, h4));
    }

    [Fact]
    public async Task Pruning_leaves_other_packs_and_games_alone()
    {
        var other = new FakeContent(Game, "other").Add(new Dictionary<string, byte[]> { ["o.txt"] = [9] });
        var mine = new FakeContent(Game, Pack);
        for (var i = 1; i <= 5; i++)
        {
            mine.Add(new Dictionary<string, byte[]> { ["v.txt"] = [(byte)i] });
        }
        var otherCache = new ContentCache(_root.FullName, other.Client());
        var cache = new ContentCache(_root.FullName, mine.Client());

        await otherCache.EnsureAsync(Game, "other", other.Hashes[0], null, Ct);
        foreach (var hash in mine.Hashes)
        {
            await cache.EnsureAsync(Game, Pack, hash, null, Ct);
        }

        Assert.True(otherCache.IsCached(Game, "other", other.Hashes[0]));
        Assert.Equal(3, Directory.GetDirectories(Path.Combine(_root.FullName, Game, Pack)).Length);
        Assert.True(cache.IsCached(Game, Pack, mine.Hashes[4]));
    }

    [Fact]
    public async Task A_tmp_directory_never_counts_as_installed()
    {
        Directory.CreateDirectory(Path.Combine(_root.FullName, ".tmp-" + Guid.NewGuid().ToString("N"), Game, Pack, ServerHash));
        var cache = new ContentCache(_root.FullName, _factory.CreateContentClient());

        Assert.False(cache.IsCached(Game, Pack, ServerHash));
        await cache.EnsureAsync(Game, Pack, ServerHash, null, Ct);
        Assert.True(cache.IsCached(Game, Pack, ServerHash));
    }

    [Fact]
    public async Task Progress_reaches_1()
    {
        var cache = new ContentCache(_root.FullName, _factory.CreateContentClient());
        var reports = new List<double>();

        await cache.EnsureAsync(Game, Pack, ServerHash, new SyncProgress(reports.Add), Ct);

        Assert.NotEmpty(reports);
        Assert.All(reports, r => Assert.InRange(r, 0.0, 1.0));
        Assert.Equal(reports.Order().ToList(), reports); // never goes backwards
        Assert.Equal(1.0, reports[^1]);
        Assert.True(reports.Count > 2, "a 200 KB file should report in steps");
    }

    [Fact]
    public async Task Progress_for_an_empty_pack_is_1()
    {
        var fake = new FakeContent(Game, Pack).Add(new Dictionary<string, byte[]> { ["empty.txt"] = [] });
        var cache = new ContentCache(_root.FullName, fake.Client());
        var reports = new List<double>();

        await cache.EnsureAsync(Game, Pack, fake.Hashes[0], new SyncProgress(reports.Add), Ct);

        Assert.Equal(1.0, reports[^1]);
    }

    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }

    /// <summary>Records request paths (escaped) and optionally intercepts them.</summary>
    private sealed class CountingHandler
    {
        public List<string> Requests { get; } = [];

        public Func<HttpRequestMessage, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>, CancellationToken, Task<HttpResponseMessage>>? Intercept { get; init; }

        public HttpMessageHandler Wrap(HttpMessageHandler inner) => new Hook(this, inner);

        private sealed class Hook(CountingHandler owner, HttpMessageHandler inner) : DelegatingHandler(inner)
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                lock (owner.Requests)
                {
                    owner.Requests.Add(request.RequestUri!.AbsolutePath);
                }
                return owner.Intercept is { } intercept
                    ? intercept(request, base.SendAsync, ct)
                    : base.SendAsync(request, ct);
            }
        }
    }

    /// <summary>An in-memory content server for one game and pack, holding any number of versions (hashes).</summary>
    private sealed class FakeContent(string gameId, string packId)
    {
        private readonly string _gameId = gameId;
        private readonly string _packId = packId;
        private readonly List<(string Hash, Dictionary<string, byte[]> Files)> _versions = [];

        public List<string> Hashes => [.. _versions.Select(v => v.Hash)];

        public FakeContent Add(Dictionary<string, byte[]> files, string? claimedHash = null)
        {
            _versions.Add((claimedHash ?? PackHash(files), files));
            return this;
        }

        public HttpClient Client() => new(new Handler(this)) { BaseAddress = new Uri("http://content.test/") };

        private sealed class Handler(FakeContent owner) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                var path = request.RequestUri!.AbsolutePath;
                var indexPath = $"/content/{owner._gameId}/index.json";
                if (path == indexPath)
                {
                    var index = owner._versions.Select(v => new
                    {
                        packId = owner._packId,
                        hash = v.Hash,
                        files = v.Files.OrderBy(f => f.Key, StringComparer.Ordinal)
                            .Select(f => new { path = f.Key, sha256 = FileSha(f.Value), bytes = f.Value.LongLength }),
                    });
                    return Task.FromResult(new HttpResponseMessage { Content = new StringContent(JsonSerializer.Serialize(index), Encoding.UTF8, new System.Net.Http.Headers.MediaTypeHeaderValue("application/json")) });
                }

                foreach (var (hash, files) in owner._versions)
                {
                    var prefix = $"/content/{owner._gameId}/{owner._packId}/{hash}/";
                    if (path.StartsWith(prefix, StringComparison.Ordinal)
                        && files.TryGetValue(Uri.UnescapeDataString(path[prefix.Length..]), out var bytes))
                    {
                        return Task.FromResult(new HttpResponseMessage { Content = new ByteArrayContent(bytes) });
                    }
                }
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
            }
        }
    }
}
