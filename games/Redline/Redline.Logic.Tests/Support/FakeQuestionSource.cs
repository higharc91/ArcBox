using Redline.Logic;

namespace Redline.Logic.Tests;

/// <summary>
/// Deterministic question source: question i has Text "Q{i}" (or <paramref name="text"/>(i)), Correct "right{i}",
/// Incorrect w1..w3, numbered across calls.
/// </summary>
public sealed class FakeQuestionSource(Difficulty difficulty = Difficulty.Medium, Func<int, string>? text = null) : IQuestionSource
{
    private int _next;

    public int TakeCalls { get; private set; }

    public IReadOnlyList<TriviaQuestion> Take(int count)
    {
        TakeCalls++;
        var list = new List<TriviaQuestion>(count);
        for (var n = 0; n < count; n++)
        {
            var i = ++_next;
            list.Add(new TriviaQuestion("Science & Nature", difficulty, text?.Invoke(i) ?? $"Q{i}", $"right{i}", ["w1", "w2", "w3"], QuestionOrigin.OpenTdb));
        }
        return list;
    }
}
