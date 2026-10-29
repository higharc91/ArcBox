using ArcBox.Server.Speech;
using Redline.Logic;

namespace ArcBox.Server.Games.Redline;

/// <summary>
/// Decorates REDLINE's question source: returns exactly what the inner source returns and, in the background, has the
/// speech service synthesise each question text in the <see cref="RedlineVoices.Questions"/> theme, so the TV's
/// <c>/api/tts</c> request during Reading is a cache hit.
/// </summary>
/// <remarks>
/// <see cref="IQuestionSource.Take"/> stays synchronous and never throws: <see cref="ISpeechService.Prewarm"/> only
/// schedules work (bounded concurrency, errors logged and dropped), and even a failure to schedule is caught here.
/// </remarks>
public sealed class NarratedQuestionSource(IQuestionSource inner, ISpeechService speech, ILogger<NarratedQuestionSource> log)
    : IQuestionSource
{
    public IReadOnlyList<TriviaQuestion> Take(int count) => Prewarmed(inner.Take(count));

    public IReadOnlyList<TriviaQuestion> Take(int count, Difficulty difficulty) => Prewarmed(inner.Take(count, difficulty));

    private IReadOnlyList<TriviaQuestion> Prewarmed(IReadOnlyList<TriviaQuestion> questions)
    {
        try
        {
            speech.Prewarm(RedlineVoices.Questions, questions.Select(q => q.Text).ToArray());
        }
        catch (Exception e)
        {
            log.LogWarning("Question prewarm failed to start: {Error}", e.Message);
        }

        return questions;
    }
}
