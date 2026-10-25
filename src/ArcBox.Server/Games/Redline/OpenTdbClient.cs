using System.Globalization;
using System.Net;
using System.Text.Json;
using Redline.Logic;

namespace ArcBox.Server.Games.Redline;

/// <summary>Outcome of one OpenTDB question request. The pool owns retry policy.</summary>
public abstract record OpenTdbResult
{
    public sealed record Ok(IReadOnlyList<TriviaQuestion> Questions) : OpenTdbResult;

    /// <summary>response_code 3 or 4: the token is unknown or exhausted; request a new one.</summary>
    public sealed record TokenEmpty : OpenTdbResult;

    /// <summary>response_code 5 or HTTP 429.</summary>
    public sealed record RateLimited : OpenTdbResult;

    public sealed record Failed(string Reason) : OpenTdbResult;
}

/// <summary>
/// Typed HttpClient for Open Trivia DB. Deliberately has no retry handler: rate limits and token exhaustion
/// are reported in the response body and the pool decides what to do about them.
/// </summary>
public sealed class OpenTdbClient(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private static readonly string[] CategoryPrefixes = ["Entertainment: ", "Science: "];

    /// <summary>Requests a session token; null if OpenTDB refuses or the request fails.</summary>
    public async Task<string?> RequestTokenAsync(CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync("api_token.php?command=request", ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            await using var body = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var dto = await JsonSerializer.DeserializeAsync<TokenDto>(body, Json, ct).ConfigureAwait(false);
            return dto is { ResponseCode: 0, Token: { Length: > 0 } token } ? token : null;
        }
        catch (Exception e) when (IsTransient(e, ct))
        {
            return null;
        }
    }

    public async Task<OpenTdbResult> FetchAsync(string? token, int amount, CancellationToken ct)
    {
        var url = $"api.php?amount={amount.ToString(CultureInfo.InvariantCulture)}&type=multiple&encode=url3986";
        if (!string.IsNullOrEmpty(token))
            url += $"&token={Uri.EscapeDataString(token)}";

        try
        {
            using var response = await http.GetAsync(url, ct).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                return new OpenTdbResult.RateLimited();
            if (!response.IsSuccessStatusCode)
                return new OpenTdbResult.Failed($"HTTP {(int)response.StatusCode}");

            await using var body = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var dto = await JsonSerializer.DeserializeAsync<QuestionsDto>(body, Json, ct).ConfigureAwait(false);
            if (dto is null)
                return new OpenTdbResult.Failed("Empty response body");

            switch (dto.ResponseCode)
            {
                case 0:
                    break;
                case 3 or 4:
                    return new OpenTdbResult.TokenEmpty();
                // "No results" with a token can mean the token has fewer than `amount` questions left; a fresh
                // token is the remedy (worst case: one extra token request). Without a token it is a plain failure.
                case 1 when !string.IsNullOrEmpty(token):
                    return new OpenTdbResult.TokenEmpty();
                case 5:
                    return new OpenTdbResult.RateLimited();
                default:
                    return new OpenTdbResult.Failed($"response_code {dto.ResponseCode}");
            }

            if (dto.Results is null)
                return new OpenTdbResult.Failed("Missing results");

            var questions = new List<TriviaQuestion>(dto.Results.Count);
            foreach (var item in dto.Results)
            {
                if (Decode(item) is { } question)
                    questions.Add(question);
            }

            return new OpenTdbResult.Ok(questions);
        }
        catch (JsonException e)
        {
            return new OpenTdbResult.Failed($"Malformed response: {e.Message}");
        }
        catch (Exception e) when (IsTransient(e, ct))
        {
            return new OpenTdbResult.Failed(e is HttpRequestException ? $"HTTP error: {e.Message}" : "Request timed out");
        }
    }

    // A timeout surfaces as OperationCanceledException (TaskCanceledException) while the caller's token is untouched.
    private static bool IsTransient(Exception e, CancellationToken ct) =>
        e is HttpRequestException or JsonException || (e is OperationCanceledException && !ct.IsCancellationRequested);

    private static TriviaQuestion? Decode(QuestionDto? dto)
    {
        if (dto?.IncorrectAnswers is not { Count: 3 } rawIncorrect)
            return null;

        var type = Unescape(dto.Type);
        var text = Unescape(dto.Question);
        var correct = Unescape(dto.CorrectAnswer);
        var category = Unescape(dto.Category);
        if (type != "multiple" || string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(correct)
            || string.IsNullOrWhiteSpace(category))
            return null;

        var difficulty = Unescape(dto.Difficulty)?.ToLowerInvariant() switch
        {
            "easy" => Difficulty.Easy,
            "medium" => Difficulty.Medium,
            "hard" => Difficulty.Hard,
            _ => (Difficulty?)null,
        };
        if (difficulty is null)
            return null;

        var incorrect = new List<string>(3);
        foreach (var raw in rawIncorrect)
        {
            var answer = Unescape(raw);
            if (string.IsNullOrWhiteSpace(answer))
                return null;
            incorrect.Add(answer);
        }

        var all = incorrect.Append(correct);
        if (all.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 4)
            return null;

        foreach (var prefix in CategoryPrefixes)
        {
            if (category.StartsWith(prefix, StringComparison.Ordinal))
            {
                category = category[prefix.Length..];
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(category))
            return null;

        return new TriviaQuestion(category, difficulty.Value, text, correct, incorrect, QuestionOrigin.OpenTdb);
    }

    private static string? Unescape(string? s) => s is null ? null : Uri.UnescapeDataString(s);

    private sealed record TokenDto(int ResponseCode, string? Token);

    private sealed record QuestionsDto(int ResponseCode, List<QuestionDto?>? Results);

    private sealed record QuestionDto(
        string? Type,
        string? Difficulty,
        string? Category,
        string? Question,
        string? CorrectAnswer,
        List<string?>? IncorrectAnswers);
}
