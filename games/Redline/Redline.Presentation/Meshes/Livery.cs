using System.Globalization;
using System.Numerics;

namespace Redline.Presentation.Meshes;

/// <summary>T-camera colour: black on a team's first car, yellow on its second.</summary>
public enum TcamColour
{
    Black,
    Yellow,
}

/// <summary>
/// Turns the livery strings of <see cref="Redline.Logic.Livery"/> (from grid2026.json, the single source) into vertex colours.
/// Colours stay sRGB-encoded, as written in the JSON; the Godot material sets <c>vertex_color_is_srgb</c>.
/// </summary>
public static class LiveryColours
{
    /// <summary>Parses <c>#rrggbb</c> into sRGB components 0..1 with alpha 1.</summary>
    public static Vector4 Parse(string hex)
    {
        ArgumentNullException.ThrowIfNull(hex);
        if (hex.Length != 7 || hex[0] != '#'
            || !int.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            throw new FormatException($"'{hex}' is not a #rrggbb colour.");
        return new Vector4(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f, 1f);
    }

    /// <summary>Parses a driver's <c>tcam</c> field ("black" or "yellow", any case).</summary>
    public static TcamColour ParseTcam(string tcam) =>
        tcam?.ToLowerInvariant() switch
        {
            "black" => TcamColour.Black,
            "yellow" => TcamColour.Yellow,
            _ => throw new ArgumentException($"Unknown T-cam colour '{tcam}'.", nameof(tcam)),
        };
}
