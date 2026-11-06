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

    // ---- Per-file reuse (the user's decision 7) ----------------------------------------------------------------

    /// <summary>A pack like an export: a few art files and two region files.</summary>
    private static Dictionary<string, byte[]> Export(string region = "{\"tiles\":[1,2,3]}") => new()
    {
        ["manifest.json"] = "{\"name\":\"base\"}"u8.ToArray(),
        ["terrain/grass.png"] = [.. Enumerable.Range(0, 5000).Select(i => (byte)i)],
        ["props/crate.png"] = [.. Enumerable.Range(0, 3000).Select(i => (byte)(i * 7))],
        ["rules/world/ashen_ford.json"] = Encoding.UTF8.GetBytes(region),
        ["rules/world/rime_pass.json"] = "{\"tiles\":[9]}"u8.ToArray(),
    };

    private string FileRequest(string hash, string path) => $"/content/{Game}/{Pack}/{hash}/{path}";

    [Fact]
    public async Task A_new_export_fetches_only_the_changed_file_and_the_index()
    {
        var fake = new FakeContent(Game, Pack).Add(Export()).Add(Export("{\"tiles\":[1,2,4,5]}"));
        var installs = new List<ContentInstall>();
        var cache = new ContentCache(_root.FullName, fake.Client(), installed: installs.Add);
        var (h1, h2) = (fake.Hashes[0], fake.Hashes[1]);

        await cache.EnsureAsync(Game, Pack, h1, null, Ct);
        Assert.Equal(6, fake.Requests.Count);   // cold: the index and all 5 files
        fake.Requests.Clear();

        var dir = await cache.EnsureAsync(Game, Pack, h2, null, Ct);

        Assert.Equal([$"/content/{Game}/index.json", FileRequest(h2, "rules/world/ashen_ford.json")], fake.Requests);
        foreach (var (path, bytes) in Export("{\"tiles\":[1,2,4,5]}"))
        {
            Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(dir, path), Ct));
        }
        Assert.True(cache.IsCached(Game, Pack, h1));   // the old version stays whole: a running game may read it

        var second = installs[1];
        Assert.Equal((5, 1, 4), (second.Files, second.FetchedFiles, second.ReusedFiles));
        Assert.Equal("{\"tiles\":[1,2,4,5]}".Length, second.FetchedBytes);
        Assert.Equal(second.Bytes - second.FetchedBytes, second.ReusedBytes);
        Assert.True(second.IndexBytes > 0);
        Assert.Equal((0, 5), (installs[0].ReusedFiles, installs[0].FetchedFiles));
    }

    [Fact]
    public async Task A_changed_file_of_the_same_length_is_fetched()
    {
        var fake = new FakeContent(Game, Pack).Add(Export("{\"tiles\":[1,2,3]}")).Add(Export("{\"tiles\":[1,2,8]}"));
        var cache = new ContentCache(_root.FullName, fake.Client());

        await cache.EnsureAsync(Game, Pack, fake.Hashes[0], null, Ct);
        fake.Requests.Clear();
        var dir = await cache.EnsureAsync(Game, Pack, fake.Hashes[1], null, Ct);

        Assert.Equal([$"/content/{Game}/index.json", FileRequest(fake.Hashes[1], "rules/world/ashen_ford.json")], fake.Requests);
        Assert.Equal("{\"tiles\":[1,2,8]}", await File.ReadAllTextAsync(Path.Combine(dir, "rules/world/ashen_ford.json"), Ct));
    }

    [Fact]
    public async Task A_damaged_local_copy_is_fetched_not_copied()
    {
        var fake = new FakeContent(Game, Pack).Add(Export()).Add(Export("{\"tiles\":[7]}"));
        var cache = new ContentCache(_root.FullName, fake.Client());
        await cache.EnsureAsync(Game, Pack, fake.Hashes[0], null, Ct);

        // Flip a byte of an unchanged file in the old version, keeping its length.
        var grass = Path.Combine(PackDir(fake.Hashes[0]), "terrain", "grass.png");
        var bytes = await File.ReadAllBytesAsync(grass, Ct);
        bytes[100] ^= 0xFF;
        await File.WriteAllBytesAsync(grass, bytes, Ct);
        fake.Requests.Clear();

        var dir = await cache.EnsureAsync(Game, Pack, fake.Hashes[1], null, Ct);

        Assert.Contains(FileRequest(fake.Hashes[1], "terrain/grass.png"), fake.Requests);
        Assert.Equal(Export()["terrain/grass.png"], await File.ReadAllBytesAsync(Path.Combine(dir, "terrain", "grass.png"), Ct));
        Assert.Equal(3, fake.Requests.Count);   // the index, the region and the damaged file
    }

    [Fact]
    public async Task Copies_come_from_whichever_installed_version_has_the_file()
    {
        // v3 brings back v1's region; v2 is the newest installed but has a different one.
        var fake = new FakeContent(Game, Pack).Add(Export("{\"a\":1}")).Add(Export("{\"b\":22}")).Add(Rename(Export("{\"a\":1}")));
        var cache = new ContentCache(_root.FullName, fake.Client());
        await cache.EnsureAsync(Game, Pack, fake.Hashes[0], null, Ct);
        await cache.EnsureAsync(Game, Pack, fake.Hashes[1], null, Ct);
        fake.Requests.Clear();

        await cache.EnsureAsync(Game, Pack, fake.Hashes[2], null, Ct);

        Assert.Equal([$"/content/{Game}/index.json", FileRequest(fake.Hashes[2], "extra.txt")], fake.Requests);

        static Dictionary<string, byte[]> Rename(Dictionary<string, byte[]> files)
        {
            files["extra.txt"] = [1];   // a different pack hash than v1's
            return files;
        }
    }

    [Fact]
    public async Task A_removed_file_is_not_carried_over()
    {
        var smaller = Export();
        smaller.Remove("rules/world/rime_pass.json");
        var fake = new FakeContent(Game, Pack).Add(Export()).Add(smaller);
        var cache = new ContentCache(_root.FullName, fake.Client());

        await cache.EnsureAsync(Game, Pack, fake.Hashes[0], null, Ct);
        fake.Requests.Clear();
        var dir = await cache.EnsureAsync(Game, Pack, fake.Hashes[1], null, Ct);

        Assert.Equal([$"/content/{Game}/index.json"], fake.Requests);
        Assert.False(File.Exists(Path.Combine(dir, "rules", "world", "rime_pass.json")));
        Assert.Equal(4, Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Count());
    }

    [Fact]
    public async Task Progress_with_reused_files_still_rises_to_1()
    {
        var fake = new FakeContent(Game, Pack).Add(Export()).Add(Export("{}"));
        var cache = new ContentCache(_root.FullName, fake.Client());
        await cache.EnsureAsync(Game, Pack, fake.Hashes[0], null, Ct);
        var reports = new List<double>();

        await cache.EnsureAsync(Game, Pack, fake.Hashes[1], new SyncProgress(reports.Add), Ct);

        Assert.Equal(reports.Order().ToList(), reports);
        Assert.All(reports, r => Assert.InRange(r, 0.0, 1.0));
        Assert.Equal(1.0, reports[^1]);
    }

    [Fact]
    public async Task An_interrupted_build_that_reused_files_leaves_nothing_and_the_old_version_whole()
    {
        var fake = new FakeContent(Game, Pack).Add(Export()).Add(Export("{\"x\":1}"));
        var cache = new ContentCache(_root.FullName, fake.Client());
        await cache.EnsureAsync(Game, Pack, fake.Hashes[0], null, Ct);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var reports = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.EnsureAsync(
            Game, Pack, fake.Hashes[1], new SyncProgress(_ => { if (++reports == 2) cts.Cancel(); }), cts.Token));

        Assert.False(cache.IsCached(Game, Pack, fake.Hashes[1]));
        Assert.Equal([Game], RootEntries(_root));
        foreach (var (path, bytes) in Export())
        {
            Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(PackDir(fake.Hashes[0]), path), Ct));
        }
    }

    [Fact]
    public async Task A_cache_hit_counts_as_use_for_eviction()
    {
        var fake = new FakeContent(Game, Pack);
        for (var i = 1; i <= 4; i++)
        {
            fake.Add(new Dictionary<string, byte[]> { ["v.txt"] = [(byte)i] });
        }
        var cache = new ContentCache(_root.FullName, fake.Client());
        var h = fake.Hashes;
        foreach (var hash in h.Take(3))
        {
            await cache.EnsureAsync(Game, Pack, hash, null, Ct);
        }
        var now = DateTime.UtcNow;
        Directory.SetLastWriteTimeUtc(PackDir(h[0]), now.AddHours(-3));
        Directory.SetLastWriteTimeUtc(PackDir(h[1]), now.AddHours(-2));
        Directory.SetLastWriteTimeUtc(PackDir(h[2]), now.AddHours(-1));

        await cache.EnsureAsync(Game, Pack, h[0], null, Ct);   // the oldest, used again
        await cache.EnsureAsync(Game, Pack, h[3], null, Ct);

        Assert.True(cache.IsCached(Game, Pack, h[0]));
        Assert.False(cache.IsCached(Game, Pack, h[1]));
        Assert.True(cache.IsCached(Game, Pack, h[2]));
        Assert.True(cache.IsCached(Game, Pack, h[3]));
    }

    [Fact]
    public async Task The_size_limit_evicts_old_versions_but_keeps_the_previous_one()
    {
        var fake = new FakeContent(Game, Pack);
        for (var i = 1; i <= 3; i++)
        {
            fake.Add(new Dictionary<string, byte[]> { ["big.bin"] = [.. Enumerable.Repeat((byte)i, 1000)] });
        }
        var installs = new List<ContentInstall>();
        var cache = new ContentCache(_root.FullName, fake.Client(), maxBytes: 1500, installed: installs.Add);
        var h = fake.Hashes;

        await cache.EnsureAsync(Game, Pack, h[0], null, Ct);
        await Task.Delay(20, Ct);   // distinct write times
        await cache.EnsureAsync(Game, Pack, h[1], null, Ct);
        Assert.True(cache.IsCached(Game, Pack, h[0]));   // 2000 bytes, over the limit, but h[0] is the one before h[1]
        await Task.Delay(20, Ct);
        await cache.EnsureAsync(Game, Pack, h[2], null, Ct);

        Assert.False(cache.IsCached(Game, Pack, h[0]));
        Assert.True(cache.IsCached(Game, Pack, h[1]));
        Assert.True(cache.IsCached(Game, Pack, h[2]));
        Assert.Equal([$"{Game}/{Pack}/{h[0]}"], installs[2].Evicted);
    }

    [Fact]
    public async Task The_real_server_index_drives_per_file_reuse()
    {
        // Same files under the real ContentEndpoints: a second cache root with a stale copy of the pack fetches nothing
        // but the index, because every file matches by path and SHA-256.
        var cache = new ContentCache(_root.FullName, _factory.CreateContentClient());
        await cache.EnsureAsync(Game, Pack, ServerHash, null, Ct);
        Directory.Move(PackDir(ServerHash), PackDir(new string('0', 64)));
        var counter = new CountingHandler();
        var installs = new List<ContentInstall>();
        var again = new ContentCache(_root.FullName, _factory.CreateContentClient(inner => counter.Wrap(inner)), installed: installs.Add);

        await again.EnsureAsync(Game, Pack, ServerHash, null, Ct);

        Assert.Equal([$"/content/{Game}/index.json"], counter.Requests);
        Assert.Equal((0, 0L, HostServerFactory.PackFiles.Count), (installs[0].FetchedFiles, installs[0].FetchedBytes, installs[0].ReusedFiles));
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

        /// <summary>Every request path, unescaped, in order.</summary>
        public List<string> Requests { get; } = [];

        public HttpClient Client() => new(new Handler(this)) { BaseAddress = new Uri("http://content.test/") };

        private sealed class Handler(FakeContent owner) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                var path = request.RequestUri!.AbsolutePath;
                lock (owner.Requests)
                {
                    owner.Requests.Add(Uri.UnescapeDataString(path));
                }
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
