namespace Redline.Logic;

public enum Difficulty { Easy, Medium, Hard }

public enum QuestionOrigin { OpenTdb, Fallback }

/// <summary>One multiple-choice trivia question: a correct answer and exactly three incorrect ones.</summary>
public sealed record TriviaQuestion(
    string Category,
    Difficulty Difficulty,
    string Text,
    string Correct,
    IReadOnlyList<string> Incorrect,
    QuestionOrigin Origin);
