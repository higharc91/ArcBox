using ArcBox.Sdk;

namespace ArcBox.Server.Games;

public sealed record GameRegistration(Func<IGame> Factory);

public sealed class GameRegistry : IGameRegistry
{
    private readonly Dictionary<string, (GameManifest Manifest, Func<IGame> Factory)> _registrations = new(StringComparer.OrdinalIgnoreCase);

    public GameRegistry(IEnumerable<GameRegistration> registrations)
    {
        var manifests = new List<GameManifest>();
        foreach (var reg in registrations)
        {
            var game = reg.Factory();
            var manifest = game.Manifest;
            _registrations[manifest.Id] = (manifest, reg.Factory);
            manifests.Add(manifest);
        }
        Manifests = manifests;
    }

    public IReadOnlyList<GameManifest> Manifests { get; }

    public bool Contains(string gameId)
    {
        return _registrations.ContainsKey(gameId);
    }

    public IGame Create(string gameId)
    {
        if (!_registrations.TryGetValue(gameId, out var entry))
        {
            throw new KeyNotFoundException($"Game with ID '{gameId}' was not found in registry.");
        }
        return entry.Factory();
    }
}
