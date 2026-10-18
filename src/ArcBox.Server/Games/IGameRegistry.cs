using ArcBox.Sdk;

namespace ArcBox.Server.Games;

public interface IGameRegistry
{
    IReadOnlyList<GameManifest> Manifests { get; }
    bool Contains(string gameId);
    IGame Create(string gameId);
}
