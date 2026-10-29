using System.Numerics;

namespace Redline.Presentation.Hud;

/// <summary>
/// Fits a circuit's centre line (metres, x east, y south) into a map box, keeping the aspect ratio and centring it. World
/// positions use the same axes (Godot X = east, Z = south), so a car's (X, Z) maps straight through <see cref="Map"/>.
/// </summary>
public sealed class TrackMapLayout
{
    private readonly float _minX, _minY, _scale, _offsetX, _offsetY;

    public TrackMapLayout(IReadOnlyList<Vector2> points, float width, float height, float padding)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count < 2) throw new ArgumentException("A map needs at least two points.", nameof(points));
        _minX = points.Min(p => p.X);
        _minY = points.Min(p => p.Y);
        var spanX = Math.Max(1e-3f, points.Max(p => p.X) - _minX);
        var spanY = Math.Max(1e-3f, points.Max(p => p.Y) - _minY);
        _scale = Math.Min((width - 2 * padding) / spanX, (height - 2 * padding) / spanY);
        _offsetX = (width - spanX * _scale) / 2;
        _offsetY = (height - spanY * _scale) / 2;
        Line = [.. points.Select(Map)];
    }

    /// <summary>The centre line in map pixels.</summary>
    public IReadOnlyList<Vector2> Line { get; }

    /// <summary>Pixels per metre.</summary>
    public float Scale => _scale;

    public Vector2 Map(Vector2 world) => new(_offsetX + (world.X - _minX) * _scale, _offsetY + (world.Y - _minY) * _scale);

    /// <summary>The start/finish marker: a short line across the track at the first point, <paramref name="halfLength"/> px each side.</summary>
    public (Vector2 A, Vector2 B) StartLine(float halfLength)
    {
        var p = Line[0];
        var t = Vector2.Normalize(Line[1] - Line[^1]);
        var n = new Vector2(-t.Y, t.X) * halfLength;
        return (p - n, p + n);
    }
}
