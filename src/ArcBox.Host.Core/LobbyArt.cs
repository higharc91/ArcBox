namespace ArcBox.Host.Core;

/// <summary>
/// The lobby cards' title images (#405): one PNG per game, fetched by itself from the URL the server put in the game's
/// manifest, never with the game's whole content pack. Nothing waits on it: a card shows its text until the image
/// arrives, and keeps its text if the image is missing, slow, or not a PNG. The URLs are immutable (hash-named), so a
/// fetched image is cached for good.
/// </summary>
/// <param name="http">Its <see cref="HttpClient.BaseAddress"/> is the server.</param>
public sealed class LobbyArt(HttpClient http, TimeSpan? timeout = null)
{
    /// <summary>How long an image may take before its card settles for text.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(3);

    private static readonly byte[] PngSignature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    private readonly Dictionary<string, Task<byte[]?>> _fetched = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();
    private readonly TimeSpan _timeout = timeout ?? DefaultTimeout;

    /// <summary>The image's PNG bytes, or null: no URL, a failed or slow request, or something that isn't a PNG.</summary>
    public Task<byte[]?> Get(string? url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return Task.FromResult<byte[]?>(null);
        }
        lock (_gate)
        {
            if (!_fetched.TryGetValue(url, out var task))
            {
                task = Fetch(url);
                _fetched[url] = task;
            }
            return task;
        }
    }

    /// <summary>
    /// The fetch, under a deadline of its own: whatever the transport does with cancellation, the card gets its answer by
    /// then (the image, or null for text).
    /// </summary>
    private async Task<byte[]?> Fetch(string url)
    {
        var fetch = Download(url);
        var finished = await Task.WhenAny(fetch, Task.Delay(_timeout)).ConfigureAwait(false);
        return finished == fetch ? await fetch.ConfigureAwait(false) : null;
    }

    private async Task<byte[]?> Download(string url)
    {
        try
        {
            using var cancel = new CancellationTokenSource(_timeout);
            using var response = await http.GetAsync(url.TrimStart('/'), cancel.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }
            var bytes = await response.Content.ReadAsByteArrayAsync(cancel.Token).ConfigureAwait(false);
            return bytes.AsSpan().StartsWith(PngSignature) ? bytes : null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or OperationCanceledException or InvalidOperationException)
        {
            return null;   // the card keeps its text
        }
    }
}
