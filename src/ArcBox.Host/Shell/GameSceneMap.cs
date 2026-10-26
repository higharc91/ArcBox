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

    /// <summary>
    /// Starts any slow, thread-safe preparation a game's scene needs (REDLINE builds its circuit and car geometry), so
    /// loading the scene when the game starts is quick. Safe to call repeatedly; returns at once.
    /// </summary>
    public static void Prewarm(string? gameId)
    {
        if (gameId == "redline")
        {
            _ = Games.Redline.World.RedlineAssets.Prewarm(new global::Redline.Logic.RedlineRules().CircuitId);
        }
    }
}
