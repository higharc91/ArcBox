using Godot;
using Meridian.Presentation.Art;

namespace ArcBox.Host.Games.Meridian;

/// <summary>The pack's art as textures, loaded once each, shared by the scene's layers; null for a file that won't load.</summary>
public sealed class ArtTextures
{
    private readonly Dictionary<string, Texture2D?> _cache = new(StringComparer.Ordinal);

    public MeridianArt Art { get; private set; } = MeridianArt.None;

    /// <summary>A crew member's id by their name, as the host view names them.</summary>
    public Func<string, string?> CrewId { get; private set; } = _ => null;

    public void Use(MeridianArt art, Func<string, string?> crewId)
    {
        (Art, CrewId) = (art, crewId);
        _cache.Clear();
    }

    public Texture2D? Get(string path)
    {
        if (!_cache.TryGetValue(path, out var texture))
        {
            texture = Image.LoadFromFile(path) is { } image ? ImageTexture.CreateFromImage(image) : null;
            _cache[path] = texture;
        }
        return texture;
    }

    /// <summary>The portrait of the crew member called <paramref name="crewName"/>, if the pack has one.</summary>
    public Texture2D? Portrait(string? crewName)
        => crewName is not null && CrewId(crewName) is { } id && Art.Portrait(id) is { } entry ? Get(entry.Path) : null;
}
