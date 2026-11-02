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
    private readonly Dictionary<(string Preset, string Mood), string> _moods = [];
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
        var figure = _units.Sprite(sprite) ?? _units.StandIn(tone);
        if (figure.PixelScale < 1)
            return new HudPortrait(figure.Path, new ArtRect(figure.AnchorX - 20, Math.Max(0, figure.HeadTop - 4), 40, 40), _backdrops[tone]);
        if (parts is [CharacterSet, var preset, ..] && _portraits.TryGetValue(preset, out var file))
            return new HudPortrait(file, new ArtRect(0, 0, _portraitSize, _portraitSize), _backdrops[tone]);
        return new HudPortrait(figure.Path, figure.Source(figure.Idle.Frames[0], 0), _backdrops[tone]);
    }

    /// <summary>
    /// The face of whoever speaks a scene's line (overworld 5d §10): their portrait preset (<see cref="Tactics.Game.StoryLineView.Portrait"/>)
    /// in the line's mood, on a backdrop by their role in the lore (<see cref="ToneOf"/>). A mood the preset wasn't drawn in falls back to
    /// its idle face. Null for the narrator, who has no portrait (mockup 02), and for a preset the pack hasn't drawn: the caption then goes
    /// without a face rather than show a stranger's.
    /// </summary>
    public HudPortrait? Speaker(string? preset, string mood, string? role)
    {
        if (preset is null || !_portraits.TryGetValue(preset, out var idle))
            return null;
        var file = _moods.GetValueOrDefault((preset, mood), idle);
        return new HudPortrait(file, new ArtRect(0, 0, _portraitSize, _portraitSize), _backdrops[ToneOf(role)]);
    }

    /// <summary>
    /// The backdrop's side for a speaker's role in the lore (<c>characters.json</c>): an ally's own, an enemy's for a villain, and the
    /// neutral one for anyone else, a rival or an npc, who may yet be either.
    /// </summary>
    public static UnitTone ToneOf(string? role) => role switch
    {
        "ally" => UnitTone.Party,
        "villain" => UnitTone.Enemy,
        _ => UnitTone.Neutral,
    };

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
        {
            _portraits[preset.Name] = $"portraits/{preset.Value.GetProperty(Mood).GetString()}";
            foreach (var mood in preset.Value.EnumerateObject())
                _moods[(preset.Name, mood.Name)] = $"portraits/{mood.Value.GetString()}";
        }
    }

    private static HudPiece Piece(JsonElement e)
    {
        var r = e.GetProperty("rect");
        return new HudPiece(new ArtRect(r[0].GetInt32(), r[1].GetInt32(), r[2].GetInt32(), r[3].GetInt32()), Edges(e, "slice"), Edges(e, "outset"), Edges(e, "padding"));
    }

    private static ArtEdges Edges(JsonElement piece, string name)
        => piece.TryGetProperty(name, out var e) ? new ArtEdges(e[0].GetInt32(), e[1].GetInt32(), e[2].GetInt32(), e[3].GetInt32()) : default;
}
