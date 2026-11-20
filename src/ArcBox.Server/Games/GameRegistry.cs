using ArcBox.Sdk;
using ArcBox.Server.Content;

namespace ArcBox.Server.Games;

public sealed record GameRegistration(Func<IGame> Factory);

public sealed class GameRegistry : IGameRegistry
{
    private readonly Dictionary<string, (GameManifest Manifest, Func<IGame> Factory)> _registrations = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="content">The content packs, to resolve each game's lobby card art to a URL (#405); without them, no art.</param>
    public GameRegistry(IEnumerable<GameRegistration> registrations, ContentPackRegistry? content = null)
    {
        var manifests = new List<GameManifest>();
        foreach (var reg in registrations)
        {
            var game = reg.Factory();
            var manifest = WithCardArtUrl(game.Manifest, content);
            _registrations[manifest.Id] = (manifest, reg.Factory);
            manifests.Add(manifest);
        }
        Manifests = manifests;
    }

    public IReadOnlyList<GameManifest> Manifests { get; }

    /// <summary>
    /// The lobby card art as the TV fetches it: <c>/content/{game}/base/{hash}/{path}</c>, an immutable URL the content
    /// endpoint serves. Art the game's base pack doesn't hold is dropped, so the TV never asks for a missing file and
    /// shows the text card.
    /// </summary>
    public static GameManifest WithCardArtUrl(GameManifest manifest, ContentPackRegistry? content)
    {
        if (manifest.CardArt is not { Length: > 0 } path)
        {
            return manifest with { CardArt = null };
        }
        var pack = content?.For(manifest.Id).FirstOrDefault(p => p.PackId == "base");
        return pack is not null && pack.Files.Any(f => f.Path == path)
            ? manifest with { CardArt = $"/content/{Uri.EscapeDataString(manifest.Id)}/{pack.PackId}/{pack.Hash}/{path}" }
            : manifest with { CardArt = null };
    }

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
