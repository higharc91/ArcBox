using System.Collections.Immutable;
using System.Text.Json;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.World;

/// <summary>Where a thing's picture comes from (interactables design §4.3).</summary>
public enum ThingSource
{
    /// <summary>Its own art: <c>interactables.&lt;kind&gt;.&lt;state&gt;</c>, once the art set exists.</summary>
    Art,

    /// <summary>A prop the pack has now, which its kind names for that state (<see cref="ThingView.StandIn"/>).</summary>
    StandIn,

    /// <summary>Nothing to draw it with yet: the plain marker on its tile.</summary>
    Marker,
}

/// <summary>What a thing is drawn with in one state.</summary>
/// <param name="Source">Where it came from.</param>
/// <param name="Key">The art key it was found under; null for a marker.</param>
/// <param name="Sprite">The picture, as a one-row strip of frames; null for a marker.</param>
/// <param name="Flat">It lies on the ground, under the walkers on its tile (the manifest's <c>layer: ground</c>).</param>
public sealed record ThingPick(ThingSource Source, string? Key, UnitSprite? Sprite, bool Flat);

/// <summary>A transition's strip (<c>interactables.chest_opening</c>): played once, from its first frame or, going back, from its last.</summary>
/// <param name="Key">Its art key.</param>
/// <param name="Sprite">The strip.</param>
/// <param name="Frames">How many frames it has.</param>
/// <param name="Fps">How fast it plays.</param>
/// <param name="Reverse">It plays backwards: the move is the other way from the one it was drawn for (a gate shutting, a lever going off).</param>
public sealed record ThingStrip(string Key, UnitSprite Sprite, int Frames, double Fps, bool Reverse)
{
    /// <summary>How long it plays.</summary>
    public double Seconds => Fps <= 0 ? 0 : Frames / Fps;

    /// <summary>The frame <paramref name="seconds"/> in, held on the last once it's played.</summary>
    public int FrameAt(double seconds)
    {
        var step = Math.Clamp((int)(seconds * Fps), 0, Frames - 1);
        return Reverse ? Frames - 1 - step : step;
    }
}

/// <summary>
/// The art things are drawn with, by key (interactables design §4): every props-shaped set the pack has (<c>props</c>, <c>dungeon</c> and,
/// once the user's art AI has made it, <c>interactables</c>) and the pickups' extras. A thing's own art wins whenever its set has the very
/// state (<c>interactables.chest.open</c> = <see cref="ThingView.Art"/> + "." + <see cref="ThingView.State"/>), then its stand-in, then
/// the plain marker. So the art drops in by key alone: copy the manifest and its files into <c>content/tactics/base/interactables/</c>, and the
/// TV draws them, with no code change. Pure: Godot only draws what this picks.
/// </summary>
public sealed class ThingArt
{
    /// <summary>The set the art brief's manifest goes in.</summary>
    public const string Set = "interactables";

    /// <summary>Props-shaped sets, read when they're there.</summary>
    private static readonly string[] PropSets = ["props", "dungeon", Set];

    /// <summary>What lies flat when its art doesn't say (art brief §4.2: "ground layer").</summary>
    private static readonly ImmutableHashSet<string> FlatKinds = ["sack", "cache", "note", "trap"];

    private const string Transition = "transition";

    private sealed record Asset(ImmutableArray<(string Name, string File)> Variants, int Width, int Height, int AnchorX, int AnchorY, int Frames,
        double Fps, bool Flat, ImmutableArray<string> Tags);

    private readonly Dictionary<string, Asset> _assets = new(StringComparer.Ordinal);    // "<set>.<asset>"

    private ThingArt()
    {
    }

    /// <summary>The art under the pack's root. A set that isn't there is left out; one that's there must read.</summary>
    public static ThingArt Load(string packRoot)
    {
        var art = new ThingArt();
        foreach (var set in PropSets)
        {
            var path = Path.Combine(packRoot, set, "manifest.json");
            if (!File.Exists(path))
                continue;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("props", out var props))
                art.AddProps(set, props);
        }
        var pickups = Path.Combine(packRoot, "pickups", "manifest.json");
        if (File.Exists(pickups))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(pickups));
            if (doc.RootElement.TryGetProperty("extras", out var extras))
                art.AddExtras("pickups", extras);
        }
        return art;
    }

    /// <summary>True once the art brief's set is in the pack.</summary>
    public bool HasOwnArt => _assets.Keys.Any(k => k.StartsWith($"{Set}.", StringComparison.Ordinal));

    /// <summary>The picture for an art key (<c>set.asset</c> for its first variant, <c>set.asset.variant</c> for that one), or null.</summary>
    public UnitSprite? Sprite(string key) => Find(key, exact: false) is { } found ? SpriteOf(found.Asset, found.File) : null;

    /// <summary>What <paramref name="thing"/> is drawn with in its state: its own art, else its stand-in, else the marker.</summary>
    public ThingPick Pick(ThingView thing)
    {
        var own = $"{thing.Art}.{thing.State}";
        if (Find(own, exact: true) is { } art)
            return new ThingPick(ThingSource.Art, own, SpriteOf(art.Asset, art.File), art.Asset.Flat);
        if (thing.StandIn is { } standIn && Find(standIn, exact: false) is { } stand)
            return new ThingPick(ThingSource.StandIn, standIn, SpriteOf(stand.Asset, stand.File), FlatKinds.Contains(thing.Kind) || stand.Asset.Flat);
        return new ThingPick(ThingSource.Marker, null, null, FlatKinds.Contains(thing.Kind));
    }

    /// <summary>
    /// The strip that plays as <paramref name="thing"/> goes from <paramref name="from"/> to <paramref name="to"/>, from its own set: an asset
    /// named after its art (<c>chest_opening</c> for <c>interactables.chest</c>) tagged <c>transition</c>, <c>from:</c> and <c>to:</c>. One
    /// drawn the other way plays backwards. Null without one, or unless both states are drawn with its own art.
    /// </summary>
    public ThingStrip? Strip(ThingView thing, string from, string to)
    {
        var parts = thing.Art.Split('.');
        if (parts.Length != 2 || parts[0] != Set || from == to)
            return null;
        // Only between two states drawn with its own art: a strip that ends on a stand-in would snap to another picture.
        if (Find($"{thing.Art}.{from}", exact: true) is null || Find($"{thing.Art}.{to}", exact: true) is null)
            return null;
        foreach (var (key, asset) in _assets)
        {
            if (!key.StartsWith($"{Set}.{parts[1]}_", StringComparison.Ordinal) || !asset.Tags.Contains(Transition) || asset.Variants.IsEmpty)
                continue;
            var forward = asset.Tags.Contains($"from:{from}") && asset.Tags.Contains($"to:{to}");
            var back = asset.Tags.Contains($"from:{to}") && asset.Tags.Contains($"to:{from}");
            if (forward || back)
                return new ThingStrip(key, SpriteOf(asset, asset.Variants[0].File), Math.Max(1, asset.Frames), asset.Fps, !forward);
        }
        return null;
    }

    private (Asset Asset, string File)? Find(string key, bool exact)
    {
        var parts = key.Split('.');
        if (parts.Length is < 2 or > 3 || !_assets.TryGetValue($"{parts[0]}.{parts[1]}", out var asset) || asset.Variants.IsEmpty)
            return null;
        if (parts.Length == 2)
            return exact ? null : (asset, asset.Variants[0].File);
        foreach (var (name, file) in asset.Variants)
            if (name == parts[2])
                return (asset, file);
        return null;
    }

    private static UnitSprite SpriteOf(Asset asset, string file)
    {
        var frames = Math.Max(1, asset.Frames);
        var idle = new UnitAnim([.. Enumerable.Range(0, frames)], frames > 1 && asset.Fps > 0 ? asset.Fps : 1, true);
        return new UnitSprite(file, asset.Width, asset.Height, asset.AnchorX, asset.AnchorY, 0, idle, idle, 0, ImmutableDictionary<string, UnitAnim>.Empty);
    }

    private void AddProps(string set, JsonElement props)
    {
        foreach (var entry in props.EnumerateObject())
        {
            var e = entry.Value;
            if (!e.TryGetProperty("variants", out var variants) || variants.ValueKind != JsonValueKind.Object)
                continue;
            var (size, anchor) = (e.GetProperty("size"), e.GetProperty("anchor"));
            _assets[$"{set}.{entry.Name}"] = new Asset([.. variants.EnumerateObject().Select(v => (v.Name, $"{set}/{v.Value.GetString()}"))],
                size[0].GetInt32(), size[1].GetInt32(), anchor[0].GetInt32(), anchor[1].GetInt32(),
                e.TryGetProperty("frames", out var f) ? f.GetInt32() : 1, e.TryGetProperty("fps", out var fps) ? fps.GetDouble() : 0,
                e.TryGetProperty("layer", out var layer) && layer.GetString() == "ground",
                e.TryGetProperty("tags", out var tags) ? [.. tags.EnumerateArray().Select(t => t.GetString() ?? "")] : []);
        }
    }

    /// <summary>pickups/manifest.json's extras (<c>loot_sack</c>…): one strip each, with a frame size rather than a size.</summary>
    private void AddExtras(string set, JsonElement extras)
    {
        foreach (var entry in extras.EnumerateObject())
        {
            var e = entry.Value;
            if (!e.TryGetProperty("file", out var file) || !e.TryGetProperty("frame", out var frame) || !e.TryGetProperty("anchor", out var anchor))
                continue;
            _assets[$"{set}.{entry.Name}"] = new Asset([("strip", $"{set}/{file.GetString()}")], frame[0].GetInt32(), frame[1].GetInt32(),
                anchor[0].GetInt32(), anchor[1].GetInt32(), e.TryGetProperty("frames", out var f) ? f.GetInt32() : 1,
                e.TryGetProperty("fps", out var fps) ? fps.GetDouble() : 0, false, []);
        }
    }
}
