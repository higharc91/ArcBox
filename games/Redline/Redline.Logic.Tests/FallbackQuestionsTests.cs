using Redline.Logic;

namespace Redline.Logic.Tests;

public class FallbackQuestionsTests
{
    [Fact]
    public void Pack_has_at_least_60_questions_across_8_categories()
    {
        Assert.True(FallbackQuestions.All.Count >= 60);
        Assert.Equal(8, FallbackQuestions.All.Select(q => q.Category).Distinct().Count());
        Assert.All(FallbackQuestions.All, q => Assert.Equal(QuestionOrigin.Fallback, q.Origin));
    }

    [Fact]
    public void Every_question_has_three_distinct_wrong_answers_and_no_blanks()
        => FallbackQuestions.Validate(FallbackQuestions.All);

    [Fact]
    public void Validate_rejects_duplicate_texts_and_two_incorrect_answers()
    {
        var q = FallbackQuestions.All[0];
        Assert.Throws<InvalidDataException>(() => FallbackQuestions.Validate([q, q]));
        Assert.Throws<InvalidDataException>(() => FallbackQuestions.Validate([q with { Incorrect = ["x", "y"] }]));
    }

    [Fact]
    public void Default_interface_Take_by_difficulty_returns_plain_Take()
    {
        var source = new PlainSource();

        var questions = ((IQuestionSource)source).Take(2, Difficulty.Easy);

        Assert.Equal(2, source.LastCount);
        Assert.Equal(source.Result, questions);
    }

    private sealed class PlainSource : IQuestionSource
    {
        public int LastCount { get; private set; }

        public IReadOnlyList<TriviaQuestion> Result { get; } = FallbackQuestions.All.Where(q => q.Difficulty == Difficulty.Hard).Take(2).ToList();

        public IReadOnlyList<TriviaQuestion> Take(int count)
        {
            LastCount = count;
            return Result;
        }
    }
}
