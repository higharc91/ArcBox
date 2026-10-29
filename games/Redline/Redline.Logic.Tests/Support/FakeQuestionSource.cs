using Redline.Logic;

namespace Redline.Logic.Tests;

/// <summary>
/// Deterministic question source: question i has Text "Q{i}" (or <paramref name="text"/>(i)), Correct "right{i}",
/// Incorrect w1..w3, numbered across calls of both <c>Take</c> overloads.
/// </summary>
public sealed class FakeQuestionSource(Difficulty difficulty = Difficulty.Medium, Func<int, string>? text = null) : IQuestionSource
{
    private int _next;

    /// <summary>Calls of the plain <see cref="Take(int)"/> (the race's questions).</summary>
    public int TakeCalls { get; private set; }

    /// <summary>Every <see cref="Take(int, Difficulty)"/> call (qualifying's questions), in order.</summary>
    public List<(int Count, Difficulty Difficulty)> DifficultyTakes { get; } = [];

    public IReadOnlyList<TriviaQuestion> Take(int count)
    {
        TakeCalls++;
        return Make(count, difficulty);
    }

    /// <summary>Records the call; the questions have the difficulty asked for.</summary>
    public IReadOnlyList<TriviaQuestion> Take(int count, Difficulty difficulty)
    {
        DifficultyTakes.Add((count, difficulty));
        return Make(count, difficulty);
    }

    private List<TriviaQuestion> Make(int count, Difficulty d)
    {
        var list = new List<TriviaQuestion>(count);
        for (var n = 0; n < count; n++)
        {
            var i = ++_next;
            list.Add(new TriviaQuestion("Science & Nature", d, text?.Invoke(i) ?? $"Q{i}", $"right{i}", ["w1", "w2", "w3"], QuestionOrigin.OpenTdb));
        }
        return list;
    }
}
