using System.Collections.Immutable;
using System.Text.Json;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.Hud;

/// <summary>Insets from a rectangle's four sides, in ui/manifest.json's order.</summary>
public readonly record struct ArtEdges(int Left, int Top, int Right, int Bottom);

/// <summary>A piece of the HUD's art (ui/manifest.json: <c>nineSlice</c>).</summary>
/// <param name="Rect">Where it is in its image.</param>
/// <param name="Slice">The corners that don't stretch.</param>
/// <param name="Outset">How far it reaches outside the element's box: studs and glows.</param>
/// <param name="Padding">The content's inset inside the box.</param>
public sealed record HudPiece(ArtRect Rect, ArtEdges Slice, ArtEdges Outset, ArtEdges Padding);

/// <summary>A unit's face on the strip.</summary>
/// <param name="Path">The image, relative to the pack's root.</param>
/// <param name="Source">The rectangle of it.</param>
/// <param name="Backdrop">The side's backdrop, to draw behind it.</param>
public sealed record HudPortrait(string Path, ArtRect Source, string Backdrop);

/// <summary>What the pack says about the HUD: the panels' and turn order's pieces (ui/manifest.json) and each character's portrait.</summary>
public sealed class HudArt
{
    private const string CharacterSet = "characters";
    private const string Mood = "idle";

    private readonly UnitArt _units;
    private readonly Dictionary<string, HudPiece> _panels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HudPiece> _turn = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _portraits = new(StringComparer.Ordinal);
    private readonly Dictionary<UnitTone, string> _backdrops = [];
    private int _portraitSize;

    private HudArt(UnitArt units) => _units = units;

    /// <summary>The panels' image.</summary>
    public string PanelsPath { get; private set; } = "";

    /// <summary>The turn order's image.</summary>
    public string TurnPath { get; private set; } = "";

    /// <summary>Reads the manifests under the pack's root. Throws if one is missing or malformed.</summary>
    public static HudArt Load(string packRoot, UnitArt units)
    {
        var art = new HudArt(units);
        using var ui = JsonDocument.Parse(File.ReadAllText(Path.Combine(packRoot, "ui", "manifest.json")));
        using var portraits = JsonDocument.Parse(File.ReadAllText(Path.Combine(packRoot, "portraits", "manifest.json")));
        art.ReadUi(ui.RootElement);
        art.ReadPortraits(portraits.RootElement);
        return art;
    }

    /// <summary>A piece of <see cref="PanelsPath"/>, by its name in the manifest.</summary>
    public HudPiece Panel(string name) => _panels[name];

    /// <summary>A piece of <see cref="TurnPath"/>, by its name in the manifest.</summary>
    public HudPiece Turn(string name) => _turn[name];

    /// <summary>The face for a sprite key: a character's portrait, else the first frame of its sprite, on its side's backdrop.</summary>
    public HudPortrait Portrait(string? sprite, UnitTone tone)
    {
        var parts = sprite?.Split('.') ?? [];
        if (parts is [CharacterSet, var preset, ..] && _portraits.TryGetValue(preset, out var file))
            return new HudPortrait(file, new ArtRect(0, 0, _portraitSize, _portraitSize), _backdrops[tone]);
        var figure = _units.Sprite(sprite) ?? _units.StandIn(tone);
        return new HudPortrait(figure.Path, figure.Source(figure.Idle.Frames[0], 0), _backdrops[tone]);
    }

    private void ReadUi(JsonElement ui)
    {
        var panels = ui.GetProperty("panels");
        PanelsPath = $"ui/{panels.GetProperty("file").GetString()}";
        foreach (var piece in panels.GetProperty("pieces").EnumerateObject())
            _panels[piece.Name] = Piece(piece.Value);

        var turn = ui.GetProperty("turnOrder");
        TurnPath = $"ui/{turn.GetProperty("file").GetString()}";
        foreach (var piece in turn.GetProperty("pieces").EnumerateObject())
            _turn[piece.Name] = Piece(piece.Value);
    }

    private void ReadPortraits(JsonElement root)
    {
        _portraitSize = root.GetProperty("size").GetInt32();
        var backdrops = root.GetProperty("backdrops");
        _backdrops[UnitTone.Party] = $"portraits/{backdrops.GetProperty("ally").GetString()}";
        _backdrops[UnitTone.Enemy] = $"portraits/{backdrops.GetProperty("enemy").GetString()}";
        _backdrops[UnitTone.Neutral] = $"portraits/{backdrops.GetProperty("neutral").GetString()}";
        foreach (var preset in root.GetProperty("presets").EnumerateObject())
            _portraits[preset.Name] = $"portraits/{preset.Value.GetProperty(Mood).GetString()}";
    }

    private static HudPiece Piece(JsonElement e)
    {
        var r = e.GetProperty("rect");
        return new HudPiece(new ArtRect(r[0].GetInt32(), r[1].GetInt32(), r[2].GetInt32(), r[3].GetInt32()), Edges(e, "slice"), Edges(e, "outset"), Edges(e, "padding"));
    }

    private static ArtEdges Edges(JsonElement piece, string name)
        => piece.TryGetProperty(name, out var e) ? new ArtEdges(e[0].GetInt32(), e[1].GetInt32(), e[2].GetInt32(), e[3].GetInt32()) : default;
}
