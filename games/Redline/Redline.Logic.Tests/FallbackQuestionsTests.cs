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
}
