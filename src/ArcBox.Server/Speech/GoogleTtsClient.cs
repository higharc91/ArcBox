using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace ArcBox.Server.Speech;

/// <summary>
/// Typed HttpClient for Google Cloud Text-to-Speech REST v1 <c>text:synthesize</c>, MP3 output.
/// </summary>
/// <remarks>
/// <para>The API key travels as <c>?key=</c>. The factory's request loggers are removed for this client
/// (<c>RemoveAllLoggers</c>) and every message this class produces passes through <see cref="Redact"/>, so the key
/// never reaches a log. Exceptions are rethrown as <see cref="SpeechSynthesisException"/> without the original as
/// inner exception, because an inner exception's message could carry the request URL.</para>
/// <para>Request body: <c>{input:{text}, voice:{languageCode, name}, audioConfig:{audioEncoding:"MP3", ...}}</c>.
/// The optional <c>audioConfig</c> fields come from <see cref="ResolvedVoice"/>, which has already dropped those the
/// voice family rejects (see <see cref="VoiceCapabilities"/> for the sources).</para>
/// </remarks>
public sealed class GoogleTtsClient(HttpClient http, IOptions<SpeechOptions> options) : ISpeechSynthesizer
{
    private const string Path = "v1/text:synthesize";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private string ApiKey => options.Value.Google.ApiKey ?? "";

    public async Task<byte[]> SynthesizeAsync(string text, ResolvedVoice voice, CancellationToken ct)
    {
        var key = ApiKey;
        if (string.IsNullOrWhiteSpace(key))
            throw new SpeechSynthesisException("No Google API key is configured.");

        var body = new SynthesizeRequest(
            new InputDto(text),
            new VoiceDto(voice.LanguageCode, voice.Voice),
            new AudioConfigDto("MP3", voice.SpeakingRate, voice.Pitch, voice.VolumeGainDb));

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Path}?key={Uri.EscapeDataString(key)}")
        {
            Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
            var payload = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new SpeechSynthesisException(Redact($"Google TTS HTTP {(int)response.StatusCode}: {ErrorMessage(payload)}"));

            var dto = JsonSerializer.Deserialize<SynthesizeResponse>(payload, Json);
            if (string.IsNullOrEmpty(dto?.AudioContent))
                throw new SpeechSynthesisException("Google TTS returned no audioContent.");

            return Convert.FromBase64String(dto.AudioContent);
        }
        catch (SpeechSynthesisException)
        {
            throw;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw new SpeechSynthesisException("Google TTS timed out.");
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or FormatException or IOException)
        {
            throw new SpeechSynthesisException(Redact($"Google TTS request failed ({e.GetType().Name}): {e.Message}"));
        }
    }

    /// <summary>Replaces the API key (raw or URL-escaped) wherever it appears.</summary>
    public string Redact(string message)
    {
        var key = ApiKey;
        if (string.IsNullOrEmpty(key))
            return message;
        message = message.Replace(key, "REDACTED", StringComparison.Ordinal);
        var escaped = Uri.EscapeDataString(key);
        return escaped == key ? message : message.Replace(escaped, "REDACTED", StringComparison.Ordinal);
    }

    // Google errors look like {"error":{"code":400,"message":"...","status":"INVALID_ARGUMENT"}}.
    private static string ErrorMessage(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("error", out var error))
            {
                var status = error.TryGetProperty("status", out var s) ? s.GetString() : null;
                var message = error.TryGetProperty("message", out var m) ? m.GetString() : null;
                return $"{status} {message}".Trim();
            }
        }
        catch (JsonException)
        {
            // Not JSON; fall through to a short excerpt.
        }

        return payload.Length <= 200 ? payload : payload[..200];
    }

    private sealed record SynthesizeRequest(InputDto Input, VoiceDto Voice, AudioConfigDto AudioConfig);

    private sealed record InputDto(string Text);

    private sealed record VoiceDto(string LanguageCode, string Name);

    private sealed record AudioConfigDto(string AudioEncoding, double? SpeakingRate, double? Pitch, double? VolumeGainDb);

    private sealed record SynthesizeResponse(string? AudioContent);
}
