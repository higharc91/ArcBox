using System.Security.Cryptography;
using System.Text.Json;
using Meridian.Presentation.Board;

namespace Meridian.Presentation.Art;

/// <summary>A tile's art: a 64 × 64 frame, lit from overhead so the engine can turn it.</summary>
public sealed record TileArt(string Path);

/// <summary>A crew member's sprites: 24 × 24 frames anchored at the feet.</summary>
public sealed record CrewArt(string Idle, string Walk, int WalkFrames, int Fps, int AnchorX, int AnchorY);

/// <summary>
/// The art the content pack has (the contract in #331): per set, a <c>manifest.json</c> keyed by content ids, naming
/// hash-named files. Anything missing, or whose bytes don't match its manifest's sha256, has no art, and the scene draws
/// its placeholder at the same size (M4 plan, Task 6). <see cref="Problems"/> lists what was refused, for one log line each.
/// </summary>
public sealed class MeridianArt
{
    private readonly Dictionary<string, TileArt> _tiles = new(StringComparer.Ordinal);
    private readonly Dictionary<HatchState, string> _hatches = [];
    private readonly Dictionary<string, CrewArt> _crew = new(StringComparer.Ordinal);
    private readonly List<string> _problems = [];

    /// <summary>No art at all: everything is a placeholder.</summary>
    public static MeridianArt None { get; } = new();

    public IReadOnlyList<string> Problems => _problems;

    public TileArt? Tile(string tileId) => _tiles.GetValueOrDefault(tileId);

    /// <summary>A hatch overlay, drawn for the north edge. <see cref="HatchState.None"/> is a wall's overlay.</summary>
    public string? Hatch(HatchState state) => _hatches.GetValueOrDefault(state);

    public CrewArt? Crew(string crewId) => _crew.GetValueOrDefault(crewId);

    /// <param name="root">The content pack's directory (<c>content/meridian/base</c> as delivered).</param>
    public static MeridianArt Load(string root)
    {
        var art = new MeridianArt();
        if (Manifest(art, root, "tiles") is { } tiles && tiles.TryGetProperty("tiles", out var tileMap))
        {
            foreach (var t in tileMap.EnumerateObject())
            {
                if (art.Checked(root, "tiles", t.Value) is { } path)
                {
                    art._tiles[t.Name] = new TileArt(path);
                }
            }
        }
        if (Manifest(art, root, "hatches") is { } hatches && hatches.TryGetProperty("states", out var states))
        {
            foreach (var s in states.EnumerateObject())
            {
                if (Enum.TryParse<HatchState>(s.Name, ignoreCase: true, out var state) && art.Checked(root, "hatches", s.Value) is { } path)
                {
                    art._hatches[state] = path;
                }
            }
        }
        if (Manifest(art, root, "crew") is { } crew && crew.TryGetProperty("crew", out var members))
        {
            foreach (var m in members.EnumerateObject())
            {
                if (m.Value.TryGetProperty("idle", out var idle) && m.Value.TryGetProperty("walk", out var walk)
                    && art.Checked(root, "crew", idle) is { } idlePath && art.Checked(root, "crew", walk) is { } walkPath)
                {
                    var anchor = m.Value.TryGetProperty("anchor", out var a) ? a.EnumerateArray().Select(x => x.GetInt32()).ToArray() : [12, 22];
                    art._crew[m.Name] = new CrewArt(idlePath, walkPath, walk.GetProperty("frames").GetInt32(),
                                                    walk.GetProperty("fps").GetInt32(), anchor[0], anchor[1]);
                }
            }
        }
        return art;
    }

    private static JsonElement? Manifest(MeridianArt art, string root, string set)
    {
        var path = System.IO.Path.Combine(root, set, "manifest.json");
        if (!File.Exists(path))
        {
            return null;   // no art for this set yet: placeholders
        }
        try
        {
            return JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();
        }
        catch (JsonException e)
        {
            art._problems.Add($"{set}/manifest.json: {e.Message}");
            return null;
        }
    }

    /// <summary>The file an entry names, if it exists and its bytes match the entry's sha256.</summary>
    private string? Checked(string root, string set, JsonElement entry)
    {
        if (!entry.TryGetProperty("file", out var f) || f.GetString() is not { } file)
        {
            _problems.Add($"{set}: an entry names no file");
            return null;
        }
        var path = System.IO.Path.Combine(root, set, file);
        if (!File.Exists(path))
        {
            _problems.Add($"{set}/{file}: missing");
            return null;
        }
        if (entry.TryGetProperty("sha256", out var sha) && sha.GetString() is { } want
            && !string.Equals(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))), want, StringComparison.OrdinalIgnoreCase))
        {
            _problems.Add($"{set}/{file}: its bytes don't match the manifest's sha256");
            return null;
        }
        return path;
    }
}
