using System.Collections.Concurrent;
using System.Threading.Channels;
using ArcBox.Sdk.Protocol;

namespace ArcBox.Host.Core.Tests;

public class ContentLoaderTests
{
    private static readonly PackRef Maps = new("maps", "aaa");
    private static readonly PackRef Art = new("art", "bbb");

    private readonly Channel<HostEvent> _posted = Channel.CreateUnbounded<HostEvent>();
    private readonly ConcurrentDictionary<string, Call> _calls = new();
    private int _ensureCount;

    private sealed record Call(PackRef Pack, IProgress<double> Progress, TaskCompletionSource<string> Done, CancellationToken Ct);

    /// <summary>A loader whose EnsureAsync waits for the test, except for the pack ids in <paramref name="cached"/> (a cache hit: done at once).</summary>
    private ContentLoader Loader(params string[] cached) => new(
        (gameId, pack, progress, ct) =>
        {
            Interlocked.Increment(ref _ensureCount);
            if (cached.Contains(pack.PackId))
            {
                return Task.FromResult($"/cache/{gameId}/{pack.PackId}/{pack.Hash}");
            }
            var call = new Call(pack, progress, new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously), ct);
            _calls[pack.PackId] = call;
            return call.Done.Task;
        },
        e => _posted.Writer.TryWrite(e));

    /// <summary>Waits for the loader's next posted event and applies it, as Main does on the next frame.</summary>
    private async Task<(T Event, ContentLoad? Changed)> ApplyNextAsync<T>(ContentLoader loader) where T : ContentLoadEvent
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var e = await _posted.Reader.ReadAsync(cts.Token);
        var typed = Assert.IsType<T>(e);
        return (typed, loader.Apply(typed));
    }

    [Fact]
    public void Game_without_packs_needs_no_load()
    {
        var loader = Loader();

        Assert.Null(loader.Require("redline", []));

        Assert.Null(loader.For("redline"));
        Assert.Equal(0, _ensureCount);
    }

    [Fact]
    public void Cached_packs_are_ready_at_once_without_any_progress_report()
    {
        var loader = Loader("maps", "art");

        var load = loader.Require("tactics", [Maps, Art]);

        Assert.NotNull(load);
        Assert.True(load.Ready);
        Assert.Equal(100, load.Percent);
        Assert.Equal("/cache/tactics/maps/aaa", load.Roots["maps"]);
        Assert.Equal("/cache/tactics/art/bbb", load.Roots["art"]);
        Assert.False(_posted.Reader.TryRead(out _));
    }

    [Fact]
    public async Task Percent_is_the_rounded_average_across_packs()
    {
        var loader = Loader();
        var load = loader.Require("tactics", [Maps, Art])!;
        Assert.Equal(0, load.Percent);
        Assert.False(load.Ready);

        _calls["maps"].Progress.Report(0.5);
        await ApplyNextAsync<ContentProgressEvent>(loader);
        Assert.Equal(25, load.Percent);

        _calls["art"].Progress.Report(0.25);
        await ApplyNextAsync<ContentProgressEvent>(loader);
        Assert.Equal(38, load.Percent);   // (0.5 + 0.25) / 2 = 37.5 %, rounded half away from zero
    }

    [Fact]
    public async Task A_finished_pack_counts_as_done_whatever_progress_it_reported()
    {
        var loader = Loader();
        var load = loader.Require("tactics", [Maps, Art])!;

        _calls["maps"].Progress.Report(0.4);
        await ApplyNextAsync<ContentProgressEvent>(loader);
        _calls["maps"].Done.SetResult("/cache/maps");   // no final Report(1)
        var (_, changed) = await ApplyNextAsync<ContentPackReadyEvent>(loader);

        Assert.Same(load, changed);
        Assert.Equal(50, load.Percent);
        Assert.False(load.Ready);

        _calls["art"].Done.SetResult("/cache/art");
        await ApplyNextAsync<ContentPackReadyEvent>(loader);
        Assert.True(load.Ready);
        Assert.Equal(100, load.Percent);
        Assert.Equal(new Dictionary<string, string> { ["maps"] = "/cache/maps", ["art"] = "/cache/art" }, load.Roots);
    }

    [Fact]
    public async Task Percent_stays_below_100_until_every_pack_is_ready()
    {
        var loader = Loader();
        var load = loader.Require("tactics", [Maps])!;

        _calls["maps"].Progress.Report(0.999);
        await ApplyNextAsync<ContentProgressEvent>(loader);

        Assert.Equal(99, load.Percent);
    }

    [Fact]
    public async Task Progress_is_posted_once_per_whole_percent()
    {
        var loader = Loader();
        loader.Require("tactics", [Maps]);

        _calls["maps"].Progress.Report(0.101);
        _calls["maps"].Progress.Report(0.104);
        _calls["maps"].Progress.Report(0.109);
        _calls["maps"].Progress.Report(0.11);

        var (first, _) = await ApplyNextAsync<ContentProgressEvent>(loader);
        var (second, _) = await ApplyNextAsync<ContentProgressEvent>(loader);
        Assert.Equal(0.101, first.Fraction);
        Assert.Equal(0.11, second.Fraction);
        Assert.False(_posted.Reader.TryRead(out _));
    }

    [Fact]
    public async Task Failure_is_reported_and_retry_reruns_only_the_unfinished_packs()
    {
        var loader = Loader();
        var load = loader.Require("tactics", [Maps, Art])!;
        _calls["art"].Done.SetResult("/cache/art");
        await ApplyNextAsync<ContentPackReadyEvent>(loader);

        _calls["maps"].Done.SetException(new ContentIntegrityException("bad hash"));
        var (failed, changed) = await ApplyNextAsync<ContentFailedEvent>(loader);

        Assert.Same(load, changed);
        Assert.Equal("maps", failed.PackId);
        Assert.IsType<ContentIntegrityException>(load.Error);
        Assert.False(load.Ready);

        _calls.Clear();
        loader.Retry("tactics");

        Assert.Null(load.Error);
        Assert.Equal(["maps"], _calls.Keys);
        _calls["maps"].Done.SetResult("/cache/maps");
        await ApplyNextAsync<ContentPackReadyEvent>(loader);
        Assert.True(load.Ready);
        Assert.Equal(3, _ensureCount);
    }

    [Fact]
    public void Ensure_that_throws_synchronously_fails_the_load()
    {
        var loader = new ContentLoader((_, _, _, _) => throw new InvalidOperationException("no disk"), _ => { });

        var load = loader.Require("tactics", [Maps])!;

        Assert.IsType<InvalidOperationException>(load.Error);
    }

    [Fact]
    public void The_same_packs_again_reuse_the_running_load()
    {
        var loader = Loader();
        var load = loader.Require("tactics", [Maps, Art]);

        var again = loader.Require("tactics", [Art, Maps]);

        Assert.Same(load, again);
        Assert.Equal(2, _ensureCount);
    }

    [Fact]
    public async Task The_same_packs_after_a_failure_try_again()
    {
        var loader = Loader();
        var load = loader.Require("tactics", [Maps])!;
        _calls["maps"].Done.SetException(new HttpRequestException("offline"));
        await ApplyNextAsync<ContentFailedEvent>(loader);

        var again = loader.Require("tactics", [Maps]);

        Assert.Same(load, again);
        Assert.Null(load.Error);
        Assert.Equal(2, _ensureCount);
    }

    [Fact]
    public async Task A_new_hash_replaces_the_load_and_cancels_the_old_one_whose_events_are_then_ignored()
    {
        var loader = Loader();
        var old = loader.Require("tactics", [Maps])!;
        var oldCall = _calls["maps"];

        var fresh = loader.Require("tactics", [Maps with { Hash = "ccc" }])!;

        Assert.NotSame(old, fresh);
        Assert.Same(fresh, loader.For("tactics"));
        Assert.True(oldCall.Ct.IsCancellationRequested);
        oldCall.Progress.Report(0.5);
        var (_, changed) = await ApplyNextAsync<ContentProgressEvent>(loader);
        Assert.Null(changed);
        Assert.Equal(0, fresh.Percent);
    }

    [Fact]
    public void Each_game_has_its_own_load()
    {
        var loader = Loader("maps");

        var a = loader.Require("tactics", [Maps]);
        var b = loader.Require("other", [Maps]);

        Assert.NotSame(a, b);
        Assert.Same(a, loader.For("tactics"));
        Assert.Same(b, loader.For("other"));
    }

    [Fact]
    public async Task Clear_forgets_and_cancels_every_load_and_later_events_are_ignored()
    {
        var loader = Loader();
        loader.Require("tactics", [Maps]);
        var call = _calls["maps"];

        loader.Clear();

        Assert.Null(loader.For("tactics"));
        Assert.True(call.Ct.IsCancellationRequested);
        // The same game again after a new session: the old download's events don't touch the new load.
        var fresh = loader.Require("tactics", [Maps])!;
        call.Done.SetResult("/old-server/maps");
        var (_, changed) = await ApplyNextAsync<ContentPackReadyEvent>(loader);
        Assert.Null(changed);
        Assert.False(fresh.Ready);
    }

    [Fact]
    public void An_empty_list_forgets_the_games_earlier_load()
    {
        var loader = Loader();
        loader.Require("tactics", [Maps]);
        var call = _calls["maps"];

        loader.Require("tactics", []);

        Assert.Null(loader.For("tactics"));
        Assert.True(call.Ct.IsCancellationRequested);
    }
}
