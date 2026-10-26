namespace ArcBox.Host.Shell;

/// <summary>Game ID → scene path. A missing or unknown scene shows <see cref="MissingScene"/> instead.</summary>
public static class GameSceneMap
{
    private static readonly Dictionary<string, string> Paths = new(StringComparer.Ordinal)
    {
        ["counter"] = "res://games/counter/CounterScene.tscn",
        ["redline"] = "res://games/redline/RedlineScene.tscn",
    };

    public static string? ScenePath(string gameId) => Paths.GetValueOrDefault(gameId);
}
