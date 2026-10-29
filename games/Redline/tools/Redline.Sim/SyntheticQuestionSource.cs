using Redline.Logic;

namespace Redline.Sim;

/// <summary>Relative weights of easy / medium / hard questions. The default is a flat 1/3 each.</summary>
public sealed record DifficultyMix(double Easy, double Medium, double Hard)
{
    public static readonly DifficultyMix Even = new(1, 1, 1);

    private double Total => Easy + Medium + Hard;

    public double Weight(Difficulty d) => d switch
    {
        Difficulty.Easy => Easy / Total,
        Difficulty.Medium => Medium / Total,
        Difficulty.Hard => Hard / Total,
        _ => throw new ArgumentOutOfRangeException(nameof(d)),
    };

    public Difficulty Draw(Random random)
    {
        var x = random.NextDouble() * Total;
        if (x < Easy) return Difficulty.Easy;
        return x < Easy + Medium ? Difficulty.Medium : Difficulty.Hard;
    }

    public override string ToString() =>
        $"easy {Weight(Difficulty.Easy):P0} / medium {Weight(Difficulty.Medium):P0} / hard {Weight(Difficulty.Hard):P0}";
}

/// <summary>
/// A seeded question source whose questions only matter for their difficulty and, for the Reading phase, their length:
/// question n is padded to the word count of fallback-pack question n (6–15 words, mean 9.4), without extra random draws,
/// so the balance numbers do not move. It also keeps the answer key
/// (question text → correct answer and difficulty) so a bot can tell which option on its phone is right before
/// rolling its own accuracy, the way a person who knows the answer would.
/// </summary>
public sealed class SyntheticQuestionSource(Random random, DifficultyMix mix) : IQuestionSource
{
    private static readonly string[] Categories =
        ["General Knowledge", "Science & Nature", "Geography", "History", "Film", "Music", "Sports", "Video Games"];

    private readonly Dictionary<string, (string Correct, Difficulty Difficulty)> _key = new(StringComparer.Ordinal);
    private int _next;

    public IReadOnlyList<TriviaQuestion> Take(int count) => Generate(count, null);

    /// <summary>Easy-only takes (qualifying): the same stream of questions, with the difficulty fixed instead of drawn from the mix.</summary>
    public IReadOnlyList<TriviaQuestion> Take(int count, Difficulty difficulty) => Generate(count, difficulty);

    private List<TriviaQuestion> Generate(int count, Difficulty? fixedDifficulty)
    {
        var list = new List<TriviaQuestion>(count);
        for (var i = 0; i < count; i++)
        {
            var n = ++_next;
            var difficulty = fixedDifficulty ?? mix.Draw(random);
            var category = Categories[random.Next(Categories.Length)];
            var text = Pad($"Synthetic question {n} ({category}, {difficulty})", WordsLike(n));
            var correct = $"right-{n}";
            _key[text] = (correct, difficulty);
            list.Add(new TriviaQuestion(category, difficulty, text, correct,
                [$"wrong-{n}-a", $"wrong-{n}-b", $"wrong-{n}-c"], QuestionOrigin.OpenTdb));
        }
        return list;
    }

    public (string Correct, Difficulty Difficulty) Lookup(string questionText) => _key[questionText];

    private static int WordsLike(int n)
    {
        var pack = FallbackQuestions.All;
        return pack[(n - 1) % pack.Count].Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private static string Pad(string text, int words)
    {
        var have = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        return have >= words ? text : text + string.Concat(Enumerable.Repeat(" word", words - have));
    }
}
