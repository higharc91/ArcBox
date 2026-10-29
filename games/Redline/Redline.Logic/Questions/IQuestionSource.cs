namespace Redline.Logic;

public interface IQuestionSource
{
    /// <summary>Returns exactly <paramref name="count"/> questions. Synchronous; never blocks on I/O and never throws.</summary>
    IReadOnlyList<TriviaQuestion> Take(int count);

    /// <summary>
    /// Returns exactly <paramref name="count"/> questions of <paramref name="difficulty"/> where the source can supply them.
    /// The default ignores the difficulty and returns <see cref="Take(int)"/>; sources that can filter override it.
    /// </summary>
    IReadOnlyList<TriviaQuestion> Take(int count, Difficulty difficulty) => Take(count);
}
