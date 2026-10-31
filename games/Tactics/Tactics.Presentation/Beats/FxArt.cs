using System.Text.Json;

namespace Tactics.Presentation.Beats;

/// <summary>An effect: a strip of frames played at a point on the board.</summary>
/// <param name="Path">Relative to the pack's root.</param>
/// <param name="Width">One frame's width.</param>
/// <param name="Height">One frame's height.</param>
/// <param name="Frames">How many frames.</param>
/// <param name="Fps">Frames a second.</param>
/// <param name="Loop">It runs until it's stopped.</param>
/// <param name="AnchorX">The frame's anchor, across.</param>
/// <param name="AnchorY">The frame's anchor, down.</param>
/// <param name="Lift">How far above a unit's ground point the anchor goes, from the manifest's <c>attach</c>.</param>
/// <param name="Additive">Blended by adding light.</param>
public sealed record FxClip(string Path, int Width, int Height, int Frames, double Fps, bool Loop, int AnchorX, int AnchorY, int Lift, bool Additive)
{
    /// <summary>How long it runs once through.</summary>
    public double Seconds => Frames / Fps;

    /// <summary>The frame <paramref name="seconds"/> in.</summary>
    public int FrameAt(double seconds)
    {
        var frame = (int)(seconds * Fps);
        return Loop ? frame % Frames : Math.Min(frame, Frames - 1);
    }
}

/// <summary>What flies from a shooter to a target. A sheet has a row per heading; an effect strip has one row and is turned to face its way.</summary>
/// <param name="Path">Relative to the pack's root.</param>
/// <param name="Width">One frame's width.</param>
/// <param name="Height">One frame's height.</param>
/// <param name="Frames">Frames in a row.</param>
/// <param name="Fps">Frames a second.</param>
/// <param name="Headings">Rows, each a direction; 1 means it's turned instead.</param>
/// <param name="Additive">Blended by adding light.</param>
public sealed record ShotArt(string Path, int Width, int Height, int Frames, double Fps, int Headings, bool Additive)
{
    /// <summary>
    /// The row for a direction in radians, with y down the screen as it's drawn. Rows go counter-clockwise from screen-right
    /// (projectiles/manifest.json), so against the angle.
    /// </summary>
    public static int Row(float radians, int headings)
    {
        var turns = -radians / (2 * MathF.PI);
        return (int)MathF.Round((turns - MathF.Floor(turns)) * headings) % headings;
    }
}

/// <summary>
/// What the pack's fx, signature and projectiles manifests say. A key is what an ability's presentation names:
/// <c>fx.slash.fire</c>, <c>fx.slash</c> for the default variant, <c>signature.vanguard_shield_wall.gold</c>,
/// <c>projectiles.arrow</c>. An unknown key is null, and whatever wanted it just isn't drawn.
/// </summary>
public sealed class FxArt
{
    private const string Effects = "effects";
    private const string Projectiles = "projectiles";
    private const int BodyLift = 15;       // manifest: "attach"
    private const int OverheadLift = 36;

    private readonly Dictionary<string, Dictionary<string, FxClip>> _sets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FxClip> _defaults = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ShotArt> _shots = new(StringComparer.Ordinal);

    private FxArt()
    {
    }

    /// <summary>Reads the manifests under the pack's root. Throws if one is missing or malformed.</summary>
    public static FxArt Load(string packRoot)
    {
        var art = new FxArt();
        art.ReadEffects(packRoot, "fx");
        art.ReadEffects(packRoot, "signature");
        art.ReadShots(packRoot);
        return art;
    }

    /// <summary>The effect for a key, or null.</summary>
    public FxClip? Clip(string key)
    {
        var parts = key.Split('.');
        if (parts.Length < 2 || !_sets.TryGetValue($"{parts[0]}.{parts[1]}", out var variants))
            return null;
        return parts.Length > 2 && variants.TryGetValue(parts[2], out var clip) ? clip : _defaults[$"{parts[0]}.{parts[1]}"];
    }

    /// <summary>What flies for a key: a projectile, or an effect strip. Null if the pack has neither.</summary>
    public ShotArt? Shot(string key)
    {
        if (key.StartsWith($"{Projectiles}.", StringComparison.Ordinal))
            return _shots.GetValueOrDefault(key[(Projectiles.Length + 1)..]);
        return Clip(key) is { } clip ? new ShotArt(clip.Path, clip.Width, clip.Height, clip.Frames, clip.Fps, 1, clip.Additive) : null;
    }

    private void ReadEffects(string root, string set)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, set, "manifest.json")));
        foreach (var effect in doc.RootElement.GetProperty(Effects).EnumerateObject())
        {
            var e = effect.Value;
            var (frame, anchor) = (e.GetProperty("frame"), e.GetProperty("anchor"));
            var clips = new Dictionary<string, FxClip>(StringComparer.Ordinal);
            foreach (var variant in e.GetProperty("variants").EnumerateObject())
                clips[variant.Name] = new FxClip($"{set}/{variant.Value.GetString()}", frame[0].GetInt32(), frame[1].GetInt32(), e.GetProperty("frames").GetInt32(),
                    e.GetProperty("fps").GetDouble(), e.GetProperty("loop").GetBoolean(), anchor[0].GetInt32(), anchor[1].GetInt32(),
                    LiftOf(e.GetProperty("attach").GetString()), e.GetProperty("blend").GetString() == "add");
            var key = $"{set}.{effect.Name}";
            _sets[key] = clips;
            _defaults[key] = clips.GetValueOrDefault(e.TryGetProperty("default", out var first) ? first.GetString() ?? "" : "") ?? clips.Values.First();
        }
    }

    private void ReadShots(string root)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, Projectiles, "manifest.json")));
        var r = doc.RootElement;
        var frame = r.GetProperty("frame");
        foreach (var shot in r.GetProperty(Projectiles).EnumerateObject())
        {
            var e = shot.Value;
            _shots[shot.Name] = new ShotArt($"{Projectiles}/{e.GetProperty("file").GetString()}", frame[0].GetInt32(), frame[1].GetInt32(), e.GetProperty("frames").GetInt32(),
                e.GetProperty("fps").GetDouble(), r.GetProperty("headings").GetInt32(), e.GetProperty("blend").GetString() == "add");
        }
    }

    private static int LiftOf(string? attach) => attach switch
    {
        "body" or "link" => BodyLift,
        "overhead" => OverheadLift,
        _ => 0,
    };
}
