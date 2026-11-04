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
/// A small pure scheduler that picks ambient life on screen from the terrain, materials and props near the camera: a few butterflies over grass, or at dusk fireflies over the
/// fields, fish jumping in deep water, the river's own steam off warm water, leaves falling from trees, embers over fires, motes in a lamp's light and now and then a bird or a
/// flock across the sky. What's where is settled once, by hash, so a frame only asks what's in view. Chimney smoke isn't here: the town kit's buildings carry their own
/// (<see cref="BuildingPlan"/>), sorted with the roof it rises from. Pure and seeded per region: runs on the TV only, bounded strictly to the camera view.
/// </summary>
public sealed class AmbientPlan
{
    private const float Margin = 64;
    private const int ButterflyEvery = 40;      // of the grass tiles
    private const int FireflyEvery = 60;        // of the field tiles, at dusk
    private const int FishEvery = 10;           // of the deep water tiles
    private const int SteamEvery = 2;           // of the water tiles over the ember; it thins away from there
    private const float SteamReach = 34;        // tiles from the stamp's water by which it's down to almost nothing
    private const float SteamAlpha = 0.5f;      // the smoke strip's steam, which is a plume; the river's own art is a faint mist already, drawn whole
    private const float FireflyAlpha = 0.7f;
    private const int LeafEvery = 2;            // of the leafy trees
    private const int MotesEvery = 2;           // of the lamps, lit at dusk
    private const float MotesAlpha = 0.55f;
    private const double BirdEvery = 26;        // seconds between birds across the sky
    private const float LeafDrop = 44;          // px a leaf falls, from the canopy to about the ground

    private readonly record struct Spot(Tile Tile, Vector2 Base, int Hash, string Variant = "");

    private readonly AmbientManifest? _manifest;
    private readonly ImmutableArray<Spot> _flitting;
    private readonly ImmutableArray<Spot> _fish;
    private readonly ImmutableArray<Spot> _steam;
    private readonly ImmutableArray<Spot> _leaves;
    private readonly ImmutableArray<Spot> _embers;
    private readonly ImmutableArray<Spot> _motes;
    private readonly int _seed;
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
        _seed = seed;

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

        // What props give: leaves from a leafy tree, in its own colours; embers over a fire; motes in a lamp's light once it's lit.
        var props = map.Props.Where(p => map.InBounds(p.At)).ToArray();
        _leaves = [.. props.Where(p => p.Id is "tree" or "tree_fruit" or "swamp_tree").Select(p => At(p.At, 5) with { Variant = LeafOf(p.Variant) })
            .Where(s => s.Hash % LeafEvery == 0)];
        _embers = [.. props.Where(p => p.Id is "campfire" or "brazier" or "torch" or "bonfire").Select(p => At(p.At, 6))];
        _motes = dusk ? [.. props.Where(p => p.Id.StartsWith("lamp", StringComparison.Ordinal) || p.Id.StartsWith("lantern", StringComparison.Ordinal)).Select(p => At(p.At, 7))
            .Where(s => s.Hash % MotesEvery == 0)] : [];
    }

    /// <summary>The leaf colour that falls from a tree of <paramref name="variant"/>: the ambient manifest has green, autumn, red and blossom.</summary>
    private static string LeafOf(string? variant) => variant switch
    {
        "green" or "light" or "cypress" => "green",
        "red" => "red",
        "blossom" or "apple" => "blossom",
        _ => "autumn",
    };

    /// <summary>How many steam plumes it has, for tests.</summary>
    public int SteamCount => _steam.Length;

    /// <summary>How many butterflies or fireflies it has, for tests.</summary>
    public int FlittingCount => _flitting.Length;

    /// <summary>How many trees drop leaves, fires throw embers and lamps hold motes, for tests.</summary>
    public (int Leaves, int Embers, int Motes) PropLife => (_leaves.Length, _embers.Length, _motes.Length);

    /// <summary>Every image <see cref="ActiveAt"/> can draw, relative to the ambient folder, once each: the TV reads them as the world opens, not as they first show.</summary>
    public ImmutableArray<string> Paths
    {
        get
        {
            if (_manifest is null)
                return [];
            IEnumerable<string> Of(string asset, IEnumerable<string>? variants = null)
                => _manifest.Assets.TryGetValue(asset, out var a) ? (variants ?? a.Variants.Keys).Select(v => a.Variants.GetValueOrDefault(v, "")) : [];
            return [.. new[]
            {
                _flitting.IsEmpty ? [] : Of(_dusk ? "fireflies" : "butterfly", _dusk ? ["gold"] : null),
                _fish.IsEmpty ? [] : Of("fish"),
                _steam.IsEmpty ? [] : _manifest.Assets.ContainsKey("river_steam") ? Of("river_steam") : Of("chimney_smoke", ["steam"]),
                Of("leaf", _leaves.Select(l => l.Variant).Distinct()),
                _embers.IsEmpty ? [] : Of("embers"),
                _motes.IsEmpty ? [] : Of("motes", ["sun"]),
                Of("bird", [BirdVariant]),
                Of("flock", [BirdVariant]),
            }.SelectMany(p => p).Where(p => p.Length > 0).Distinct(StringComparer.Ordinal)];
        }
    }

    /// <summary>Crows over the ashen ford's dusk, swallows by day.</summary>
    private string BirdVariant => _dusk ? "crow" : "swallow";

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
            foreach (var (tile, spot, h, _) in _flitting)
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
                var frame = (int)(((long)(clock * flitterAsset.Fps) + h) % flitterAsset.Frames);
                var mirror = MathF.Cos((float)(clock * 1.7 + phase)) < 0;
                result.Add(new AmbientItem(flitter, variant, path, tile, spot + new Vector2(wx, wy), frame, flitterAsset.Frames,
                    new Vector2(flitterAsset.FrameWidth, flitterAsset.FrameHeight), new Vector2(flitterAsset.AnchorX, flitterAsset.AnchorY), mirror,
                    BoardOrder.Key(tile, BoardLayer.Unit), _dusk ? FireflyAlpha : 1, flitterAsset.Blend == "add"));
            }
        }

        // 2. Fish jumping in deep water
        if (_manifest.Assets.TryGetValue("fish", out var fishAsset))
        {
            foreach (var (tile, spot, h, _) in _fish)
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

        // 3. The river's steam (the lore has it steam at the ford), each plume on its own beat: its own art, or failing that the smoke strip's steam.
        var (steamAsset, steamKind, steamVariant) = _manifest.Assets.TryGetValue("river_steam", out var river) ? (river, "river_steam", river.DefaultVariant is { Length: > 0 } d ? d : "mist")
            : _manifest.Assets.TryGetValue("chimney_smoke", out var smoke) && smoke.Variants.ContainsKey("steam") ? (smoke, "chimney_smoke", "steam") : (null, "", "");
        if (steamAsset is not null)
        {
            foreach (var (tile, spot, h, _) in _steam)
            {
                if (!InView(spot, 48))
                    continue;
                var plume = Strip(steamAsset, steamKind, steamVariant, tile, spot, (int)(clock * steamAsset.Fps) + h, steamKind == "river_steam" ? 1 : SteamAlpha, (h & 2) == 0);
                // The river's mist is faint by design (alpha at most 60 of 255): in the dusk's light it was lost on the water, so it catches the light as a glow does.
                result.Add(steamKind == "river_steam" ? plume with { Add = true } : plume);
            }
        }

        // 4. Leaves falling from trees, one at a time each, from the canopy to about the ground, drifting as they go.
        if (_manifest.Assets.TryGetValue("leaf", out var leaf))
        {
            foreach (var (tile, spot, h, variant) in _leaves)
            {
                if (!InView(spot, 64))
                    continue;
                var period = 6.0 + h % 5;
                var falling = (clock + h % 997 * 0.013) % period;
                var fall = LeafDrop / MathF.Max(1, leaf.Vy);
                if (falling >= fall)
                    continue;
                var t = (float)falling;
                var start = spot + new Vector2(h % 17 - 8, -LeafDrop - 6);
                var at = start + new Vector2(leaf.Vx * t + MathF.Sin(t * 2.2f + h % 7) * 7, leaf.Vy * t);
                var alpha = Math.Clamp((fall - t) / 0.6f, 0, 1);
                result.Add(Strip(leaf, "leaf", variant, tile, at, (int)(clock * leaf.Fps) + h, alpha, (h & 1) == 0));
            }
        }

        // 5. Embers off a fire, which glow by themselves.
        if (_manifest.Assets.TryGetValue("embers", out var embers))
        {
            foreach (var (tile, spot, h, _) in _embers)
            {
                if (InView(spot, 48))
                    result.Add(Strip(embers, "embers", embers.DefaultVariant is { Length: > 0 } v ? v : "fire", tile, spot + new Vector2(0, -4), (int)(clock * embers.Fps) + h, 1, false));
            }
        }

        // 6. Motes in the light of a lamp at dusk.
        if (_manifest.Assets.TryGetValue("motes", out var motes))
        {
            foreach (var (tile, spot, h, _) in _motes)
            {
                if (InView(spot, 48))
                    result.Add(Strip(motes, "motes", "sun", tile, spot + new Vector2(0, -26), (int)(clock * motes.Fps) + h, MotesAlpha, (h & 1) == 0));
            }
        }

        // 7. Now and then a bird, or a flock, across the sky: high on the screen, wherever the camera is, as what's that high doesn't move with the ground.
        var beat = (long)Math.Floor(clock / BirdEvery);
        var birdHash = Hash(_seed, (int)beat, 0, 8);
        var flies = (birdHash & 1) == 0 ? "bird" : "flock";
        if (_manifest.Assets.TryGetValue(flies, out var bird))
        {
            var speed = MathF.Max(8, MathF.Abs(bird.Vx));
            var across = view.Max.X - view.Min.X + 2 * bird.FrameWidth;
            var flown = (float)(clock - beat * BirdEvery) * speed;
            if (flown < across)
            {
                var leftward = (birdHash & 2) != 0;
                var height = view.Min.Y + (view.Max.Y - view.Min.Y) * (0.08f + birdHash % 100 / 100f * 0.3f) + bird.Vy * flown / speed;
                var x = leftward ? view.Max.X + bird.FrameWidth - flown : view.Min.X - bird.FrameWidth + flown;
                var sway = MathF.Sin(flown / speed * 1.5f) * 2;
                result.Add(Strip(bird, flies, BirdVariant, default, new Vector2(x, height + sway), (int)(clock * bird.Fps), 1, leftward));
            }
        }

        return [.. result];
    }

    private static AmbientItem Strip(AmbientAsset asset, string kind, string variant, Tile tile, Vector2 at, int frame, float alpha, bool mirror)
        => new(kind, variant, asset.Variants.GetValueOrDefault(variant, ""), tile, at, (frame % asset.Frames + asset.Frames) % asset.Frames, asset.Frames,
            new Vector2(asset.FrameWidth, asset.FrameHeight), new Vector2(asset.AnchorX, asset.AnchorY), mirror, BoardOrder.Key(tile, BoardLayer.Over), alpha, asset.Blend == "add");

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
