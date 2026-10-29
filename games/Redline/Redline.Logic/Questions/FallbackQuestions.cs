using System.Text.Json;
using ArcBox.Sdk;

namespace Redline.Logic;

public static class FallbackQuestions
{
    public const string ResourceName = "Redline.Logic.Data.fallback-questions.json";
    public const int IncorrectCount = 3;

    private static readonly Lazy<IReadOnlyList<TriviaQuestion>> Cached = new(LoadCore);

    /// <summary>The embedded fallback pack (Origin = Fallback). Parsed and validated once, then cached.</summary>
    public static IReadOnlyList<TriviaQuestion> All => Cached.Value;

    /// <summary>Throws <see cref="InvalidDataException"/> on blank fields, an incorrect-answer count other than three, duplicate answers within a question, or duplicate question texts.</summary>
    public static void Validate(IReadOnlyList<TriviaQuestion> questions)
    {
        if (questions is null)
            throw new InvalidDataException("Question list is null.");

        var texts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var q in questions)
        {
            if (q is null)
                throw new InvalidDataException("Question list contains a null entry.");
            if (string.IsNullOrWhiteSpace(q.Category))
                throw new InvalidDataException($"Question '{q.Text}' has a blank category.");
            if (string.IsNullOrWhiteSpace(q.Text))
                throw new InvalidDataException("A question has blank text.");
            if (string.IsNullOrWhiteSpace(q.Correct))
                throw new InvalidDataException($"Question '{q.Text}' has a blank correct answer.");
            if (q.Incorrect is null || q.Incorrect.Count != IncorrectCount)
                throw new InvalidDataException($"Question '{q.Text}' must have {IncorrectCount} incorrect answers, found {q.Incorrect?.Count ?? 0}.");

            var answers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!answers.Add(q.Correct.Trim()))
                throw new InvalidDataException($"Question '{q.Text}' has a duplicate answer.");
            foreach (var wrong in q.Incorrect)
            {
                if (string.IsNullOrWhiteSpace(wrong))
                    throw new InvalidDataException($"Question '{q.Text}' has a blank incorrect answer.");
                if (!answers.Add(wrong.Trim()))
                    throw new InvalidDataException($"Question '{q.Text}' has duplicate answer '{wrong}'.");
            }

            if (!texts.Add(q.Text.Trim()))
                throw new InvalidDataException($"Duplicate question text '{q.Text}'.");
        }
    }

    private static IReadOnlyList<TriviaQuestion> LoadCore()
    {
        using var stream = typeof(FallbackQuestions).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidDataException($"Embedded resource '{ResourceName}' not found.");
        var rows = JsonSerializer.Deserialize<List<Row>>(stream, ArcJson.Options)
            ?? throw new InvalidDataException("fallback-questions.json is empty.");

        var questions = rows
            .Select(r => new TriviaQuestion(r.Category, r.Difficulty, r.Text, r.Correct, r.Incorrect ?? [], QuestionOrigin.Fallback))
            .ToList();
        Validate(questions);
        return questions;
    }

    private sealed record Row(string Category, Difficulty Difficulty, string Text, string Correct, List<string>? Incorrect);
}
