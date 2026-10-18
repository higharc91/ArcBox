namespace ArcBox.Sdk;

public sealed record PlayerScore(PlayerId PlayerId, string Name, int Score);
public sealed record GameResults(IReadOnlyList<PlayerScore> Scores, string? Message = null);
