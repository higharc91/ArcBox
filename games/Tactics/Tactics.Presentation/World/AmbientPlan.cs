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
    float Alpha = 1f);

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
/// A small pure scheduler that picks ambient life on screen from the terrain and materials near the camera:
/// butterflies over grass, fish jumping in deep water, chimney smoke rising from hearths.
/// Pure and seeded per region: runs on the TV only, bounded strictly to the camera view.
/// </summary>
public sealed class AmbientPlan
{
    private readonly BoardTerrain _terrain;
    private readonly BattleMap _map;
    private readonly AmbientManifest? _manifest;
    private readonly int _seed;
    private readonly ImmutableArray<Tile> _grassTiles;
    private readonly ImmutableArray<Tile> _deepWaterTiles;
    private readonly ImmutableArray<PropPlacement> _chimneys;

    public AmbientPlan(BoardTerrain terrain, BattleMap map, AmbientManifest? manifest = null, int seed = 0)
    {
        _terrain = terrain;
        _map = map;
        _manifest = manifest;
        _seed = seed;

        var grass = ImmutableArray.CreateBuilder<Tile>();
        var water = ImmutableArray.CreateBuilder<Tile>();
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                var tile = new Tile(x, y);
                var t = map[tile];
                if ((t.Flags & (TileFlags.Water | TileFlags.Deep)) == (TileFlags.Water | TileFlags.Deep))
                {
                    water.Add(tile);
                }
                else if (t.Material < map.Materials.Length && map.Materials[t.Material].Contains("grass", StringComparison.OrdinalIgnoreCase))
                {
                    grass.Add(tile);
                }
            }
        }
        _grassTiles = grass.ToImmutable();
        _deepWaterTiles = water.ToImmutable();
        _chimneys = [.. map.Props.Where(p => p.Id.Contains("chimney", StringComparison.OrdinalIgnoreCase))];
    }

    /// <summary>The ambient creatures and particles active within <paramref name="view"/> at <paramref name="clock"/> seconds.</summary>
    public ImmutableArray<AmbientItem> ActiveAt(double clock, ArtArea view)
    {
        if (_manifest is null || _manifest.Assets.IsEmpty)
        {
            return [];
        }

        var result = new List<AmbientItem>();
        var margin = 64f;
        var paddedView = new ArtArea(view.Min - new Vector2(margin), view.Max + new Vector2(margin));

        // 1. Butterflies over grass
        if (_manifest.Assets.TryGetValue("butterfly", out var butterflyAsset) && !_grassTiles.IsEmpty)
        {
            foreach (var tile in _grassTiles)
            {
                var h = Hash(_seed, tile.X, tile.Y, 1);
                if (h % 12 != 0)
                {
                    continue;
                }

                var height = _map[tile].Height;
                var basePos = BoardProjection.Corner(tile.X, tile.Y, height) + new Vector2(16, 8);
                if (!paddedView.Overlaps(new ArtArea(basePos - new Vector2(16), basePos + new Vector2(16))))
                {
                    continue;
                }

                // Wander within a small radius (6 px) so it strictly stays over its grass tile
                var phase = (h % 100) * 0.1f;
                var wx = MathF.Sin((float)(clock * 1.7 + phase)) * 6f;
                var wy = MathF.Cos((float)(clock * 1.3 + phase)) * 3f - 2f;
                var pos = basePos + new Vector2(wx, wy);

                var variantKeys = butterflyAsset.Variants.Keys.ToArray();
                var variant = variantKeys.Length > 0 ? variantKeys[h % variantKeys.Length] : butterflyAsset.DefaultVariant;
                var path = butterflyAsset.Variants.TryGetValue(variant, out var vp) ? vp : "";
                var frame = (int)(clock * butterflyAsset.Fps) % butterflyAsset.Frames;
                var mirror = MathF.Cos((float)(clock * 1.7 + phase)) < 0;
                var order = BoardOrder.Key(tile, BoardLayer.Unit);

                result.Add(new AmbientItem("butterfly", variant, path, tile, pos, frame, butterflyAsset.Frames,
                    new Vector2(butterflyAsset.FrameWidth, butterflyAsset.FrameHeight),
                    new Vector2(butterflyAsset.AnchorX, butterflyAsset.AnchorY), mirror, order));
            }
        }

        // 2. Fish jumping in deep water
        if (_manifest.Assets.TryGetValue("fish", out var fishAsset) && !_deepWaterTiles.IsEmpty)
        {
            foreach (var tile in _deepWaterTiles)
            {
                var h = Hash(_seed, tile.X, tile.Y, 2);
                if (h % 10 != 0)
                {
                    continue;
                }

                var height = _map[tile].Height;
                var basePos = BoardProjection.Corner(tile.X, tile.Y, height) + new Vector2(16, 8);
                if (!paddedView.Overlaps(new ArtArea(basePos - new Vector2(16), basePos + new Vector2(16))))
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
                    var variantKeys = fishAsset.Variants.Keys.ToArray();
                    var variant = variantKeys.Length > 0 ? variantKeys[h % variantKeys.Length] : fishAsset.DefaultVariant;
                    var path = fishAsset.Variants.TryGetValue(variant, out var vp) ? vp : "";
                    var mirror = (h & 1) == 0;
                    var order = BoardOrder.Key(tile, BoardLayer.Unit);

                    result.Add(new AmbientItem("fish", variant, path, tile, basePos, frame, fishAsset.Frames,
                        new Vector2(fishAsset.FrameWidth, fishAsset.FrameHeight),
                        new Vector2(fishAsset.AnchorX, fishAsset.AnchorY), mirror, order));
                }
            }
        }

        // 3. Chimney smoke
        if (_manifest.Assets.TryGetValue("chimney_smoke", out var smokeAsset))
        {
            foreach (var prop in _chimneys)
            {
                var height = _map[prop.At].Height;
                var basePos = BoardProjection.Corner(prop.At.X, prop.At.Y, height) + new Vector2(16, 0);
                if (!paddedView.Overlaps(new ArtArea(basePos - new Vector2(32), basePos + new Vector2(32))))
                {
                    continue;
                }

                var frame = (int)(clock * smokeAsset.Fps) % smokeAsset.Frames;
                var variant = smokeAsset.DefaultVariant;
                var path = smokeAsset.Variants.TryGetValue(variant, out var vp) ? vp : "";
                var order = BoardOrder.Key(prop.At, BoardLayer.Over);

                result.Add(new AmbientItem("chimney_smoke", variant, path, prop.At, basePos, frame, smokeAsset.Frames,
                    new Vector2(smokeAsset.FrameWidth, smokeAsset.FrameHeight),
                    new Vector2(smokeAsset.AnchorX, smokeAsset.AnchorY), false, order));
            }
        }

        return [.. result];
    }

    private static int Hash(int seed, int x, int y, int salt)
    {
        var h = seed ^ (x * 73856093) ^ (y * 19349663) ^ (salt * 83492791);
        return Math.Abs(h);
    }
}
