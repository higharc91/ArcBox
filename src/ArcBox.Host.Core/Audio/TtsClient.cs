using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Speech;

namespace ArcBox.Host.Core.Audio;

/// <summary>What the TV needs to use the server's speech endpoints: the server and the room's host credentials.</summary>
public sealed record TtsSession(Uri Server, string Code, string HostToken);

/// <summary>The <c>POST /api/tts</c> body.</summary>
public sealed record TtsRequestBody(string Code, string HostToken, string Text, string Voice);

/// <summary>The outcome of one <c>/api/tts</c> request.</summary>
public abstract record TtsResult;

/// <summary>An MP3 clip (wrap it in an <c>AudioStreamMP3</c>).</summary>
public sealed record TtsAudio(byte[] Mp3) : TtsResult;

/// <summary>No audio: speak the line with the built-in voice (and tell <see cref="NarratorFallbackPolicy"/>).</summary>
public sealed record TtsFailed(TtsFailure Failure, int? Status = null, TimeSpan? RetryAfter = null) : TtsResult;

/// <summary>
/// The TV side of the platform speech service (<c>docs/speech-setup.md</c>): <c>POST /api/tts</c> with the room's code
/// and host token, giving up after <see cref="Timeout"/>, and <c>GET /api/tts/voices</c> for each theme's built-in voice
/// hints. Never throws for server or network problems: they come back as <see cref="TtsFailed"/> (or a null voice list).
/// Thread-safe; call it off the main thread.
/// </summary>
public sealed class TtsClient(HttpClient http)
{
    /// <summary>The server's limit on text length (<c>Speech:MaxChars</c> default).</summary>
    public const int MaxChars = 300;

    private static readonly JsonSerializerOptions Json = ArcJson.Options;

    /// <summary>How long to wait for a clip before falling back (default <see cref="NarratorFallbackPolicy.Timeout"/>).</summary>
    public TimeSpan Timeout { get; init; } = NarratorFallbackPolicy.Timeout;

    /// <summary>The request body for <paramref name="text"/>, trimmed; null when the server would refuse it (empty or too long).</summary>
    public static TtsRequestBody? Request(TtsSession session, string text, string theme)
    {
        ArgumentNullException.ThrowIfNull(session);
        var trimmed = text?.Trim() ?? "";
        return trimmed.Length is 0 or > MaxChars ? null : new TtsRequestBody(session.Code, session.HostToken, trimmed, theme);
    }

    public static TtsFailure Classify(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => TtsFailure.Unauthorized,
        HttpStatusCode.BadRequest => TtsFailure.BadRequest,
        HttpStatusCode.TooManyRequests => TtsFailure.RateLimited,
        _ => TtsFailure.Unavailable,
    };

    public async Task<TtsResult> SynthesizeAsync(TtsSession session, string text, string theme, CancellationToken ct)
    {
        if (Request(session, text, theme) is not { } body)
        {
            return new TtsFailed(TtsFailure.BadRequest);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        try
        {
            using var response = await http.PostAsJsonAsync(new Uri(session.Server, "api/tts"), body, Json, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                return new TtsFailed(Classify(response.StatusCode), (int)response.StatusCode, RetryAfter(response));
            }
            var mp3 = await response.Content.ReadAsByteArrayAsync(timeout.Token);
            return mp3.Length > 0 ? new TtsAudio(mp3) : new TtsFailed(TtsFailure.Unavailable, (int)response.StatusCode);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new TtsFailed(TtsFailure.Timeout);
        }
        catch (HttpRequestException)
        {
            return new TtsFailed(TtsFailure.Network);
        }
    }

    /// <summary>Each voice theme's built-in voice hints by theme id; null when the list could not be read.</summary>
    public async Task<IReadOnlyDictionary<string, FallbackVoice>?> VoicesAsync(TtsSession session, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(session);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(session.Server, "api/tts/voices"));
        request.Headers.Add("X-ArcBox-Room", session.Code);
        request.Headers.Add("X-ArcBox-Host-Token", session.HostToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            using var response = await http.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }
            var voices = await response.Content.ReadFromJsonAsync<List<VoiceInfo>>(Json, timeout.Token);
            return voices?
                .Where(v => !string.IsNullOrEmpty(v.Id))
                .GroupBy(v => v.Id)
                .ToDictionary(g => g.Key, g => g.First().Fallback ?? new FallbackVoice());
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or NotSupportedException
                                       || (e is OperationCanceledException && !ct.IsCancellationRequested))
        {
            return null;
        }
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage response) => response.Headers.RetryAfter switch
    {
        { Delta: { } delta } => delta,
        { Date: { } date } => date - DateTimeOffset.UtcNow,
        _ => null,
    };

    private sealed record VoiceInfo(string Id, string? Description, FallbackVoice? Fallback);
}
