namespace ArcBox.Sdk;

public interface IGameContext
{
    DateTimeOffset Now { get; }
    Random Random { get; }                              // seeded per game; deterministic in tests
    void SetTimer(string key, TimeSpan after);          // replaces an existing timer with the same key
    void CancelTimer(string key);
    DateTimeOffset? TimerDeadline(string key);          // so views can include countdowns
    void EndGame(GameResults results);                  // ordered scores + optional message
}
