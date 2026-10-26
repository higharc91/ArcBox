using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace ArcBox.Server.Speech;

/// <summary>
/// Disk cache of synthesised MP3s with single-flight synthesis (spec §3.1).
/// </summary>
/// <remarks>
/// <para>Key: SHA-256 of <see cref="ResolvedVoice.Fingerprint"/> + "\n" + text, so a change to a theme's voice, language,
/// rate, pitch or gain never serves stale audio. Files are <c>&lt;key&gt;.mp3</c> in <see cref="Directory"/>; an
/// in-memory index of the keys on disk is loaded at startup, so other server instances' files are reused.</para>
/// <para>Concurrent callers for the same key share one synthesis (a <see cref="Lazy{T}"/> task per key). The shared
/// synthesis runs on the cache's own lifetime token, not on any caller's, so one caller giving up does not fail the
/// others; each caller waits with its own token. Failures are not cached: the in-flight entry is removed either way.</para>
/// </remarks>
public sealed class SpeechCache : IDisposable
{
    private readonly ConcurrentDictionary<string, Lazy<Task<byte[]>>> _inflight = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _index = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ILogger<SpeechCache> _log;

    public SpeechCache(
        IOptions<SpeechOptions> options,
        ILogger<SpeechCache> log,
        IHostEnvironment? environment = null,
        IHostApplicationLifetime? lifetime = null)
    {
        _log = log;
        // In-flight syntheses stop when shutdown begins, so none outlives the host's shutdown timeout.
        lifetime?.ApplicationStopping.Register(Dispose);
        var root = environment?.ContentRootPath ?? AppContext.BaseDirectory;
        Directory = Path.GetFullPath(Path.Combine(root, options.Value.CacheDir));
        LoadIndex();
    }

    public string Directory { get; }

    /// <summary>Number of clips on disk that this instance knows about.</summary>
    public int Count => _index.Count;

    public static string Key(ResolvedVoice voice, string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(voice.Fingerprint + "\n" + text)));

    /// <summary>
    /// Returns the cached clip for <paramref name="key"/>, or runs <paramref name="synthesize"/> once (shared by every
    /// concurrent caller), stores the result and returns it. <paramref name="synthesize"/> exceptions propagate.
    /// </summary>
    public async Task<byte[]> GetOrCreateAsync(string key, Func<CancellationToken, Task<byte[]>> synthesize, CancellationToken ct)
    {
        if (await TryReadAsync(key, ct).ConfigureAwait(false) is { } hit)
            return hit;

        Lazy<Task<byte[]>>? created = null;
        created = new Lazy<Task<byte[]>>(() => CreateAsync(key, synthesize, created!));
        var shared = _inflight.GetOrAdd(key, created);
        return await shared.Value.WaitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Stops in-flight syntheses. Callers waiting on them see cancellation; nothing waits for them to end.</summary>
    public void Dispose()
    {
        try
        {
            _shutdown.Cancel();
        }
        catch (AggregateException e)
        {
            _log.LogDebug(e, "Speech cache shutdown callback failed");
        }
    }

    private async Task<byte[]> CreateAsync(string key, Func<CancellationToken, Task<byte[]>> synthesize, Lazy<Task<byte[]>> self)
    {
        try
        {
            // Yield so the synthesis never runs on the caller's stack (a prewarm must not block its caller).
            await Task.Yield();
            var token = _shutdown.Token;

            // Another caller may have finished between our miss and taking the in-flight slot.
            if (await TryReadAsync(key, token).ConfigureAwait(false) is { } hit)
                return hit;

            var audio = await synthesize(token).ConfigureAwait(false);
            await WriteAsync(key, audio).ConfigureAwait(false);
            return audio;
        }
        finally
        {
            _inflight.TryRemove(new KeyValuePair<string, Lazy<Task<byte[]>>>(key, self));
        }
    }

    private async Task<byte[]?> TryReadAsync(string key, CancellationToken ct)
    {
        if (!_index.ContainsKey(key))
            return null;

        try
        {
            var bytes = await File.ReadAllBytesAsync(PathFor(key), ct).ConfigureAwait(false);
            if (bytes.Length > 0)
                return bytes;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _log.LogWarning("Speech cache entry {Key} unreadable ({Error}); synthesising again", key, e.Message);
        }

        _index.TryRemove(key, out _);
        return null;
    }

    // Write to a temporary file and move it into place, so a reader never sees half a clip.
    private async Task WriteAsync(string key, byte[] audio)
    {
        var path = PathFor(key);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            await File.WriteAllBytesAsync(temp, audio).ConfigureAwait(false);
            File.Move(temp, path, overwrite: true);
            _index[key] = 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The clip is still returned; it just is not cached.
            _log.LogWarning("Could not write speech cache entry in {Directory}: {Error}", Directory, e.Message);
            TryDelete(temp);
        }
    }

    private void LoadIndex()
    {
        try
        {
            if (!System.IO.Directory.Exists(Directory))
                return;
            foreach (var file in System.IO.Directory.EnumerateFiles(Directory, "*.mp3"))
                _index[Path.GetFileNameWithoutExtension(file)] = 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _log.LogWarning("Could not read speech cache directory {Directory}: {Error}", Directory, e.Message);
        }
    }

    private string PathFor(string key) => Path.Combine(Directory, key + ".mp3");

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Best effort.
        }
    }
}
