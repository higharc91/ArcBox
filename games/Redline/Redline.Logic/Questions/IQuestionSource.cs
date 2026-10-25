namespace Redline.Logic;

public interface IQuestionSource
{
    /// <summary>Returns exactly <paramref name="count"/> questions. Synchronous; never blocks on I/O and never throws.</summary>
    IReadOnlyList<TriviaQuestion> Take(int count);
}
