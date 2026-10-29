namespace ArcBox.Host.Shell;

/// <summary>
/// A scene that draws content packs the server delivers (spec §3.6): maps, sprite sheets, dialogue. Before its first
/// <see cref="IGameScene.Render"/> it gets each required pack's local directory by pack id, e.g.
/// <c>roots["maps"] + "/level one.json"</c>. The shell shows the "Loading content…" card until every pack is downloaded
/// and verified, so the directories are complete; a game without packs gets an empty dictionary. A scene kept for a
/// rematch (<see cref="IPreloadScene.Park"/>) gets it again before the next game's first view.
/// </summary>
public interface IContentScene
{
    void SetContentRoots(IReadOnlyDictionary<string, string> rootsByPackId);
}
