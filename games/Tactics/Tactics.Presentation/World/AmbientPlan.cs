using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json;
using Tactics.Logic;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.World;

/// <summary>One particle of ambient life active on the screen.</summary>
public readonly record struct AmbientItem(
    string Kind,
    string Variant,
    string Path,
    Tile Tile,
    Vector2 Position,
    int Frame,
    int TotalFrames,
    Vector2 FrameSize,
    Vector2 Anchor,
    bool Mirror,
    long Order,
    float Alpha = 1f,
    bool Add = false);

/// <summary>Metadata for one ambient asset in <c>ambient/manifest.json</c>.</summary>
public sealed record AmbientAsset(
    string Name,
    string Group,
    string Kind,
    int FrameWidth,
    int FrameHeight,
    int Frames,
    int Fps,
    bool Loop,
    int AnchorX,
    int AnchorY,
    string Layer,
    string Blend,
    ImmutableDictionary<string, string> Variants,
    string DefaultVariant,
    float Vx = 0,
    float Vy = 0);

/// <summary>The pack's ambient manifest.</summary>
public sealed class AmbientManifest
{
    public ImmutableDictionary<string, AmbientAsset> Assets { get; }

    public AmbientManifest(ImmutableDictionary<string, AmbientAsset> assets)
    {
        Assets = assets;
    }

    /// <summary>Loads <c>ambient/manifest.json</c> from <paramref name="packRoot"/>.</summary>
    public static AmbientManifest? Load(string packRoot)
    {
        var path = Path.Combine(packRoot, "ambient", "manifest.json");
        if (!File.Exists(path))
        {
            return null;
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var assets = ImmutableDictionary.CreateBuilder<string, AmbientAsset>();
        if (doc.RootElement.TryGetProperty("assets", out var assetsElem))
        {
            foreach (var prop in assetsElem.EnumerateObject())
            {
                var el = prop.Value;
                var name = el.TryGetProperty("name", out var n) ? n.GetString() ?? prop.Name : prop.Name;
                var group = el.TryGetProperty("group", out var g) ? g.GetString() ?? "" : "";
                var kind = el.TryGetProperty("kind", out var k) ? k.GetString() ?? "" : "";
                var frameElem = el.GetProperty("frame");
                var frameW = frameElem[0].GetInt32();
                var frameH = frameElem[1].GetInt32();
                var frames = el.GetProperty("frames").GetInt32();
                var fps = el.GetProperty("fps").GetInt32();
                var loop = el.TryGetProperty("loop", out var l) && l.GetBoolean();
                var anchorElem = el.GetProperty("anchor");
                var anchorX = anchorElem[0].GetInt32();
                var anchorY = anchorElem[1].GetInt32();
                var layer = el.TryGetProperty("layer", out var lay) ? lay.GetString() ?? "over" : "over";
                var blend = el.TryGetProperty("blend", out var bl) ? bl.GetString() ?? "normal" : "normal";

                var variants = ImmutableDictionary.CreateBuilder<string, string>();
                if (el.TryGetProperty("variants", out var varElem))
                {
                    foreach (var v in varElem.EnumerateObject())
                    {
                        variants[v.Name] = v.Value.GetString() ?? "";
                    }
                }
                var defaultVariant = el.TryGetProperty("default", out var d) ? d.GetString() ?? "" : "";

                var vx = 0f;
                var vy = 0f;
                if (el.TryGetProperty("motion", out var mot))
                {
                    if (mot.TryGetProperty("vx", out var vxe)) vx = vxe.GetSingle();
                    if (mot.TryGetProperty("vy", out var vye)) vy = vye.GetSingle();
                }

                assets[prop.Name] = new AmbientAsset(name, group, kind, frameW, frameH, frames, fps, loop,
                    anchorX, anchorY, layer, blend, variants.ToImmutable(), defaultVariant, vx, vy);
            }
        }
        return new AmbientManifest(assets.ToImmutable());
    }
}

/// <summary>
/// A small pure scheduler that picks ambient life on screen from the terrain and materials near the camera: a few butterflies over grass, or at dusk fireflies over the fields, fish
/// jumping in deep water, chimney smoke rising from hearths, and steam off warm water. What's where is settled once, by hash, so a frame only asks what's in view.
/// Pure and seeded per region: runs on the TV only, bounded strictly to the camera view.
/// </summary>
public sealed class AmbientPlan
{
    private const float Margin = 64;
    private const int ButterflyEvery = 40;      // of the grass tiles
    private const int FireflyEvery = 60;        // of the field tiles, at dusk
    private const int FishEvery = 10;           // of the deep water tiles
    private const int SteamEvery = 2;           // of the water tiles over the ember; it thins away from there
    private const float SteamReach = 34;        // tiles from the stamp's water by which it's down to almost nothing
    private const float SteamAlpha = 0.5f;
    private const float FireflyAlpha = 0.7f;

    private readonly record struct Spot(Tile Tile, Vector2 Base, int Hash);

    private readonly AmbientManifest? _manifest;
    private readonly ImmutableArray<Spot> _flitting;
    private readonly ImmutableArray<Spot> _fish;
    private readonly ImmutableArray<Spot> _steam;
    private readonly ImmutableArray<Spot> _chimneys;
    private readonly bool _dusk;

    /// <param name="terrain">The ground.</param>
    /// <param name="map">Its map.</param>
    /// <param name="manifest">The pack's ambient art.</param>
    /// <param name="seed">What makes two regions differ.</param>
    /// <param name="dusk">Whether it's dusk here, when butterflies are gone and fireflies are out.</param>
    public AmbientPlan(BoardTerrain terrain, BattleMap map, AmbientManifest? manifest = null, int seed = 0, bool dusk = false)
    {
        _manifest = manifest;
        _dusk = dusk;

        Spot At(Tile tile, int salt) => new(tile, BoardProjection.Corner(tile.X, tile.Y, map[tile].Height) + new Vector2(16, 8), Hash(seed, tile.X, tile.Y, salt));
        var warm = map.StampedTiles.Where(t => (map[t].Flags & TileFlags.Water) != 0).ToArray();
        float FromWarm(Tile tile) => warm.Length == 0 ? float.MaxValue : warm.Min(w => MathF.Abs(w.X - tile.X) + MathF.Abs(w.Y - tile.Y));

        var (flitting, fish, steam) = (ImmutableArray.CreateBuilder<Spot>(), ImmutableArray.CreateBuilder<Spot>(), ImmutableArray.CreateBuilder<Spot>());
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                var tile = new Tile(x, y);
                var t = map[tile];
                var material = t.Material < map.Materials.Length ? map.Materials[t.Material] : "";
                if ((t.Flags & TileFlags.Water) != 0)
                {
                    if ((t.Flags & TileFlags.Deep) != 0 && At(tile, 2).Hash % FishEvery == 0)
                        fish.Add(At(tile, 2));
                    if (warm.Length > 0)
                    {
                        // Thickest at the ford, as the lore has it, and thinning up and down the river.
                        var every = SteamEvery * (1 + FromWarm(tile) / SteamReach * 12);
                        if (At(tile, 3).Hash % (int)every == 0)
                            steam.Add(At(tile, 3));
                    }
                }
                else if (dusk ? IsDuskField(material) : material.Contains("grass", StringComparison.OrdinalIgnoreCase))
                {
                    if (At(tile, 1).Hash % (dusk ? FireflyEvery : ButterflyEvery) == 0)
                        flitting.Add(At(tile, 1));
                }
            }
        }
        (_flitting, _fish, _steam) = (flitting.ToImmutable(), fish.ToImmutable(), steam.ToImmutable());
        _chimneys = [.. map.Props.Where(p => p.Id.Contains("chimney", StringComparison.OrdinalIgnoreCase)).Select(p => At(p.At, 4))];
    }

    /// <summary>How many steam plumes it has, for tests.</summary>
    public int SteamCount => _steam.Length;

    /// <summary>How many butterflies or fireflies it has, for tests.</summary>
    public int FlittingCount => _flitting.Length;

    /// <summary>The ambient creatures and particles active within <paramref name="view"/> at <paramref name="clock"/> seconds.</summary>
    public ImmutableArray<AmbientItem> ActiveAt(double clock, ArtArea view)
    {
        if (_manifest is null || _manifest.Assets.IsEmpty)
        {
            return [];
        }

        var result = new List<AmbientItem>();
        var seen = new ArtArea(view.Min - new Vector2(Margin), view.Max + new Vector2(Margin));
        bool InView(Vector2 at, float reach) => seen.Overlaps(new ArtArea(at - new Vector2(reach), at + new Vector2(reach)));

        // 1. Butterflies over grass, or fireflies over the fields at dusk
        var flitter = _dusk ? "fireflies" : "butterfly";
        if (_manifest.Assets.TryGetValue(flitter, out var flitterAsset))
        {
            foreach (var (tile, spot, h) in _flitting)
            {
                if (!InView(spot, 32))
                {
                    continue;
                }

                // Wander within a small radius (6 px) so it strictly stays over its tile
                var phase = (h % 100) * 0.1f;
                var wx = MathF.Sin((float)(clock * 1.7 + phase)) * 6f;
                var wy = MathF.Cos((float)(clock * 1.3 + phase)) * 3f - 2f;
                var variant = _dusk ? "gold" : Pick(flitterAsset, h);
                var path = flitterAsset.Variants.GetValueOrDefault(variant, "");
                var frame = ((int)(clock * flitterAsset.Fps) + h) % flitterAsset.Frames;
                var mirror = MathF.Cos((float)(clock * 1.7 + phase)) < 0;
                result.Add(new AmbientItem(flitter, variant, path, tile, spot + new Vector2(wx, wy), frame, flitterAsset.Frames,
                    new Vector2(flitterAsset.FrameWidth, flitterAsset.FrameHeight), new Vector2(flitterAsset.AnchorX, flitterAsset.AnchorY), mirror,
                    BoardOrder.Key(tile, BoardLayer.Unit), _dusk ? FireflyAlpha : 1, flitterAsset.Blend == "add"));
            }
        }

        // 2. Fish jumping in deep water
        if (_manifest.Assets.TryGetValue("fish", out var fishAsset))
        {
            foreach (var (tile, spot, h) in _fish)
            {
                if (!InView(spot, 16))
                {
                    continue;
                }

                var period = 6.0 + (h % 4);
                var offset = (h % 1000) * 0.01;
                var cycle = (clock + offset) % period;
                var duration = fishAsset.Frames / (double)fishAsset.Fps; // 1.0s
                if (cycle < duration)
                {
                    var frame = Math.Clamp((int)(cycle * fishAsset.Fps), 0, fishAsset.Frames - 1);
                    var variant = Pick(fishAsset, h);
                    result.Add(new AmbientItem("fish", variant, fishAsset.Variants.GetValueOrDefault(variant, ""), tile, spot, frame, fishAsset.Frames,
                        new Vector2(fishAsset.FrameWidth, fishAsset.FrameHeight), new Vector2(fishAsset.AnchorX, fishAsset.AnchorY), (h & 1) == 0,
                        BoardOrder.Key(tile, BoardLayer.Unit)));
                }
            }
        }

        // 3. Chimney smoke, and steam off the water, each on its own beat.
        if (_manifest.Assets.TryGetValue("chimney_smoke", out var smokeAsset))
        {
            foreach (var (tile, spot, h) in _chimneys)
            {
                if (InView(spot, 32))
                    result.Add(Smoke(smokeAsset, smokeAsset.DefaultVariant, tile, spot + new Vector2(0, -8), (int)(clock * smokeAsset.Fps) + h, 1));
            }
            if (smokeAsset.Variants.ContainsKey("steam"))
            {
                foreach (var (tile, spot, h) in _steam)
                {
                    if (InView(spot, 48))
                        result.Add(Smoke(smokeAsset, "steam", tile, spot, (int)(clock * smokeAsset.Fps) + h, SteamAlpha));
                }
            }
        }

        return [.. result];
    }

    private static AmbientItem Smoke(AmbientAsset asset, string variant, Tile tile, Vector2 at, int frame, float alpha)
        => new("chimney_smoke", variant, asset.Variants.GetValueOrDefault(variant, ""), tile, at, frame % asset.Frames, asset.Frames,
            new Vector2(asset.FrameWidth, asset.FrameHeight), new Vector2(asset.AnchorX, asset.AnchorY), false, BoardOrder.Key(tile, BoardLayer.Over), alpha);

    private static string Pick(AmbientAsset asset, int hash)
    {
        var keys = asset.Variants.Keys.Order(StringComparer.Ordinal).ToArray();
        return keys.Length > 0 ? keys[hash % keys.Length] : asset.DefaultVariant;
    }

    private static int Hash(int seed, int x, int y, int salt)
    {
        var h = seed ^ (x * 73856093) ^ (y * 19349663) ^ (salt * 83492791);
        h ^= h >> 13;
        h *= 1274126177;
        h ^= h >> 16;
        return h & int.MaxValue;
    }

    private static bool IsDuskField(string material)
        => material is "grass_ford/autumn" or "grass_ford/dry" or "dirt_ford/wet" or "dirt_ford/dry" or "grass/autumn" or "dirt/brown";
}
