using System.Collections.Immutable;
using System.Text.Json;

namespace Tactics.Presentation.Board;

/// <summary>A rectangle of an image, in pixels.</summary>
public readonly record struct ArtRect(int X, int Y, int Width, int Height);

/// <summary>An animation: columns of a sprite sheet.</summary>
public sealed record UnitAnim(ImmutableArray<int> Frames, double Fps, bool Loop)
{
    /// <summary>The sheet column showing <paramref name="seconds"/> in. A one-shot stays on its last frame.</summary>
    public int FrameAt(double seconds)
    {
        var step = (int)(seconds * Fps);
        return Frames[Loop ? step % Frames.Length : Math.Min(step, Frames.Length - 1)];
    }
}

/// <summary>A sprite sheet: columns of frames, row 0 facing the viewer, row 1 facing away.</summary>
/// <param name="Path">Relative to the pack's root.</param>
/// <param name="Width">One frame's width.</param>
/// <param name="Height">One frame's height.</param>
/// <param name="AnchorX">The ground contact pixel, across.</param>
/// <param name="AnchorY">The ground contact pixel, down. It stands on the tile's centre.</param>
/// <param name="HeadTop">The first row of the frame the figure reaches.</param>
/// <param name="Idle">The standing animation.</param>
/// <param name="Down">The lying-down animation.</param>
/// <param name="EffectShift">How far overhead status effects move down the screen from where a standard figure has them.</param>
/// <param name="Anims">The sheet's animations by name, from its manifest.</param>
public sealed record UnitSprite(string Path, int Width, int Height, int AnchorX, int AnchorY, int HeadTop, UnitAnim Idle, UnitAnim Down, int EffectShift,
    ImmutableDictionary<string, UnitAnim> Anims)
{
    private const string Special = "special";
    private const string Attack = "attack";
    private const string Cast = "cast";
    private const string Dodge = "dodge";

    /// <summary>
    /// The animation for a manifest name or a presentation key (<c>walk</c>, <c>characters.cast</c>). The sets don't share
    /// their lists, so what a sheet lacks falls back: a cast to its special and a special to its cast (a boss on a character
    /// sheet has no special, and gathering itself reads better than a swing at nobody), a dodge to standing still, which is
    /// what a miss looks like, anything else to its attack, and then to idle.
    /// </summary>
    public UnitAnim Anim(string key)
    {
        var name = key[(key.LastIndexOf('.') + 1)..];
        return Anims.GetValueOrDefault(name)
            ?? (name == Cast ? Anims.GetValueOrDefault(Special) : name == Special ? Anims.GetValueOrDefault(Cast) : null)
            ?? (name == Dodge ? null : Anims.GetValueOrDefault(Attack))
            ?? Idle;
    }

    /// <summary>A frame's place in the sheet.</summary>
    public ArtRect Source(int column, int row) => new(column * Width, row * Height, Width, Height);
}

/// <summary>A status effect: an animated strip drawn at the unit's anchor, under or over the unit.</summary>
public sealed record StatusEffect(string Path, int Frames, double Fps, bool Under);

/// <summary>A condition's art: its effect on the unit and its icon in <see cref="UnitArt.IconsPath"/>, either of which may be missing.</summary>
public sealed record ConditionLook(StatusEffect? Effect, ArtRect? Icon);

/// <summary>An HP bar's pieces in <see cref="UnitArt.BarsPath"/> (ui/manifest.json, <c>bars</c>).</summary>
/// <param name="Frame">The frame around the bar.</param>
/// <param name="SliceLeft">The frame's left edge, which doesn't stretch.</param>
/// <param name="SliceRight">The frame's right edge.</param>
/// <param name="FillX">Where the fill starts, across; the frame's edges take this much from each end.</param>
/// <param name="FillY">Where the fill starts, down.</param>
/// <param name="FillHeight">The fill's height.</param>
/// <param name="Left">The fill's first column.</param>
/// <param name="Mid">The fill's body.</param>
/// <param name="Right">The fill's bright leading edge.</param>
public sealed record BarArt(ArtRect Frame, int SliceLeft, int SliceRight, int FillX, int FillY, int FillHeight, ArtRect Left, ArtRect Mid, ArtRect Right);

/// <summary>
/// What the pack's manifests say about units: the sprite for a key (<c>characters.goblin_archer</c>,
/// <c>beasts.wolf.dire</c>, <c>bosses.troll.troll</c>), each condition's art, the HP bars, the digits and the turn arrow.
/// </summary>
public sealed class UnitArt
{
    /// <summary>The HP bars' image.</summary>
    public const string BarsPath = "ui/bars.png";

    /// <summary>The digits' image.</summary>
    public const string NumbersPath = "ui/numbers.png";

    /// <summary>The dice image.</summary>
    public const string DicePath = "ui/dice.png";

    /// <summary>The status icons' image.</summary>
    public const string IconsPath = "icons/status.png";

    /// <summary>A status effect's frame width (status/manifest.json).</summary>
    public const int EffectWidth = 32;

    /// <summary>A status effect's frame height.</summary>
    public const int EffectHeight = 40;

    /// <summary>The anchor in a status effect's frame, across.</summary>
    public const int EffectAnchorX = 16;

    /// <summary>The anchor in a status effect's frame, down: the feet of a standard figure, so a figure's size moves its overhead effects.</summary>
    public const int EffectAnchorY = 37;

    private const string CharacterSet = "characters";
    private const int HeadroomShare = 6;   // monsters' sheets don't say where the head is: about this fraction of the frame is empty above it
    private const string DigitStyle = "damage";

    /// <summary>
    /// The rules' conditions that have no art of that name, and the art that says the same thing. The rest either share a
    /// name with the art or have none. My judgment, since the art set predates the conditions.
    /// </summary>
    private static readonly ImmutableDictionary<string, string> Aliases = new Dictionary<string, string>
    {
        ["paralysed"] = "stunned",
        ["incapacitated"] = "stunned",
        ["unconscious"] = "sleeping",
        ["invisible"] = "hidden",
        ["hunters_mark"] = "marked",
        ["hex"] = "marked",
        ["mage_armor"] = "shielded",
        ["barkskin"] = "shielded",
        ["death_ward"] = "shielded",
        ["aura_of_protection"] = "blessed",
    }.ToImmutableDictionary();

    private readonly string _root;
    private readonly Dictionary<string, UnitSprite> _sprites = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StatusEffect> _effects = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ArtRect> _icons = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Style, string Text), (ArtRect Rect, int Advance)> _glyphs = [];
    private readonly Dictionary<UnitTone, UnitSprite> _standIns = [];
    private readonly HashSet<string> _presets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _heads = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _headOffsets = new(StringComparer.Ordinal);
    private ManifestSet _characters = null!;
    private ManifestSet _beasts = null!;
    private ManifestSet _bosses = null!;
    private BarArt _partyBar = null!;
    private BarArt _enemyBar = null!;

    private UnitArt(string root) => _root = root;

    /// <summary>The turn arrow's strip.</summary>
    public ArtImage Arrow { get; private set; } = null!;

    /// <summary>The gold d20 in <see cref="DicePath"/>.</summary>
    public ArtRect DieGold { get; private set; }

    /// <summary>The dark d20 in <see cref="DicePath"/>.</summary>
    public ArtRect DieDark { get; private set; }

    /// <summary>One arrow frame's width.</summary>
    public int ArrowWidth { get; private set; }

    /// <summary>The point of the arrow in its frame, across.</summary>
    public int ArrowAnchorX { get; private set; }

    /// <summary>The point of the arrow in its frame, down.</summary>
    public int ArrowAnchorY { get; private set; }

    /// <summary>Reads the manifests under the pack's root. Throws if one is missing or malformed.</summary>
    public static UnitArt Load(string packRoot)
    {
        var art = new UnitArt(packRoot);
        using var characters = Parse(packRoot, "characters");
        using var beasts = Parse(packRoot, "beasts");
        using var bosses = Parse(packRoot, "bosses");
        using var status = Parse(packRoot, "status");
        using var icons = Parse(packRoot, "icons");
        using var ui = Parse(packRoot, "ui");
        art.ReadCharacters(characters.RootElement);
        art._beasts = new ManifestSet("beasts", beasts.RootElement);
        art._bosses = new ManifestSet("bosses", bosses.RootElement);
        art.ReadStatus(status.RootElement, icons.RootElement);
        art.ReadUi(ui.RootElement);
        foreach (var set in new[] { art._beasts, art._bosses })
            art.ReadMonsters(set, set.Name == "beasts" ? beasts.RootElement.GetProperty("beasts") : bosses.RootElement.GetProperty("monsters"));
        return art;
    }

    /// <summary>The sprite for a key, or null if the pack has none. A monster's unknown or missing variant takes its first.</summary>
    public UnitSprite? Sprite(string? key)
    {
        if (string.IsNullOrEmpty(key))
            return null;
        if (_sprites.TryGetValue(key, out var known))
            return known;
        var parts = key.Split('.');
        if (parts.Length < 2)
            return null;
        if (parts[0] == CharacterSet)
            return _presets.Contains(parts[1]) ? _sprites[key] = Character(parts[1]) : null;
        return _sprites.GetValueOrDefault($"{parts[0]}.{parts[1]}");   // the first variant
    }

    /// <summary>A figure that exists for certain, for a unit whose sprite doesn't. Kept once read: this runs every frame while a beat plays.</summary>
    public UnitSprite StandIn(UnitTone tone)
    {
        if (!_standIns.TryGetValue(tone, out var sprite))
            _standIns[tone] = sprite = Character(tone switch
            {
                UnitTone.Party => "class_fighter",
                UnitTone.Enemy => "bandit",
                _ => "farmer",
            });
        return sprite;
    }

    /// <summary>A condition's art, or null if it has none.</summary>
    public ConditionLook? Condition(string id)
    {
        var name = Aliases.GetValueOrDefault(id, id);
        var effect = _effects.GetValueOrDefault(name);
        ArtRect? icon = _icons.TryGetValue(name, out var rect) ? rect : null;
        return effect is null && icon is null ? null : new ConditionLook(effect, icon);
    }

    /// <summary>The HP bar for a side: green for anyone not against the party, red for the enemy.</summary>
    public BarArt Bar(UnitTone tone) => tone == UnitTone.Enemy ? _enemyBar : _partyBar;

    /// <summary>A digit's rectangle in <see cref="NumbersPath"/>, and how far to move on after it.</summary>
    public (ArtRect Rect, int Advance) Digit(char digit) => Glyph(DigitStyle, digit.ToString());

    /// <summary>A glyph of <see cref="NumbersPath"/>: a digit, a sign, or a word such as <c>MISS</c>, in the style (<c>damage</c>, <c>heal</c>, <c>crit</c>).</summary>
    public (ArtRect Rect, int Advance) Glyph(string style, string text) => _glyphs[(style, text)];

    private static JsonDocument Parse(string root, string set) => JsonDocument.Parse(File.ReadAllText(Path.Combine(root, set, "manifest.json")));

    private static ArtRect Rect(JsonElement e) => new(e[0].GetInt32(), e[1].GetInt32(), e[2].GetInt32(), e[3].GetInt32());

    private static UnitAnim Anim(JsonElement anims, string name)
    {
        var e = anims.GetProperty(name);
        return new UnitAnim([.. e.GetProperty("frames").EnumerateArray().Select(f => f.GetInt32())], e.GetProperty("fps").GetDouble(),
            e.GetProperty("loop").GetBoolean());
    }

    private void ReadCharacters(JsonElement root)
    {
        _characters = new ManifestSet(CharacterSet, root);
        _presets.UnionWith(root.GetProperty("presets").EnumerateArray().Select(p => p.GetString()!));
        foreach (var build in root.GetProperty("builds").EnumerateObject())
            _heads[build.Name] = build.Value.GetProperty("headTop").GetInt32();
    }

    private void ReadMonsters(ManifestSet set, JsonElement monsters)
    {
        foreach (var monster in monsters.EnumerateObject())
        {
            var e = monster.Value;
            var frame = e.TryGetProperty("frame", out var own) ? own : set.Root.GetProperty("frame");
            var (width, height) = (frame.GetProperty("width").GetInt32(), frame.GetProperty("height").GetInt32());
            var anchor = frame.GetProperty("anchor");
            var (ax, ay) = (anchor[0].GetInt32(), anchor[1].GetInt32());
            var first = true;
            foreach (var variant in e.GetProperty("variants").EnumerateObject())
            {
                var sprite = new UnitSprite($"{set.Name}/{variant.Value.GetString()}", width, height, ax, ay, height / HeadroomShare, set.Idle, set.Down, EffectAnchorY - ay, set.Anims);
                _sprites[$"{set.Name}.{monster.Name}.{variant.Name}"] = sprite;
                if (first)
                    _sprites[$"{set.Name}.{monster.Name}"] = sprite;
                first = false;
            }
        }
    }

    private void ReadStatus(JsonElement status, JsonElement icons)
    {
        foreach (var offset in status.GetProperty("headOffset").EnumerateObject())
            _headOffsets[offset.Name] = offset.Value.GetInt32();
        foreach (var effect in status.GetProperty("statuses").EnumerateObject())
        {
            var e = effect.Value;
            _effects[effect.Name] = new StatusEffect($"status/{e.GetProperty("file").GetString()}", e.GetProperty("frames").GetInt32(),
                e.GetProperty("fps").GetDouble(), e.GetProperty("layer").GetString() == "under");
        }
        foreach (var icon in icons.GetProperty("icons").EnumerateObject())
            if (icon.Value.GetProperty("atlas").GetString() == "status.png")
                _icons[icon.Name] = Rect(icon.Value.GetProperty("rect"));
    }

    private void ReadUi(JsonElement ui)
    {
        var bars = ui.GetProperty("bars");
        BarArt Bar(string kind)
        {
            var e = bars.GetProperty("kinds").GetProperty(kind);
            var frame = bars.GetProperty("frames").GetProperty(e.GetProperty("frame").GetString()!);
            var (slice, fill) = (frame.GetProperty("slice"), frame.GetProperty("fill"));
            return new BarArt(Rect(frame.GetProperty("rect")), slice[0].GetInt32(), slice[2].GetInt32(), fill[0].GetInt32(), fill[1].GetInt32(), fill[2].GetInt32(),
                Rect(e.GetProperty("left")), Rect(e.GetProperty("mid")), Rect(e.GetProperty("right")));
        }
        _partyBar = Bar("hp");
        _enemyBar = Bar("enemy_hp");

        foreach (var style in ui.GetProperty("numbers").GetProperty("styles").EnumerateObject())
            foreach (var glyph in style.Value.EnumerateObject())
                _glyphs[(style.Name, glyph.Name)] = (Rect(glyph.Value.GetProperty("rect")), glyph.Value.GetProperty("advance").GetInt32());

        var pieces = ui.GetProperty("dice").GetProperty("pieces");
        DieGold = Rect(pieces.GetProperty("d20_gold").GetProperty("rect"));
        DieDark = Rect(pieces.GetProperty("d20_dark").GetProperty("rect"));

        var arrow = ui.GetProperty("cursor").GetProperty("arrow");
        var (size, anchor) = (arrow.GetProperty("frame"), arrow.GetProperty("anchor"));
        Arrow = new ArtImage($"ui/{arrow.GetProperty("file").GetString()}", arrow.GetProperty("frames").GetInt32(), arrow.GetProperty("fps").GetDouble(), size[1].GetInt32());
        ArrowWidth = size[0].GetInt32();
        (ArrowAnchorX, ArrowAnchorY) = (anchor[0].GetInt32(), anchor[1].GetInt32());
    }

    private UnitSprite Character(string preset)
    {
        var build = BuildOf(preset);
        return new UnitSprite($"{CharacterSet}/presets/{preset}.png", _characters.Width, _characters.Height, _characters.AnchorX, _characters.AnchorY,
            _heads.GetValueOrDefault(build), _characters.Idle, _characters.Down, _headOffsets.GetValueOrDefault(build), _characters.Anims);
    }

    private string BuildOf(string preset)
    {
        const string standard = "standard";
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, CharacterSet, "presets", $"{preset}.json")));
            return doc.RootElement.TryGetProperty("build", out var build) ? build.GetString() ?? standard : standard;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return standard;   // a preset with no notes still draws, a little off on its head
        }
    }

    /// <summary>What a set's manifest says for all its sprites. It keeps a clone of the root: the document itself is short-lived.</summary>
    private sealed class ManifestSet
    {
        public ManifestSet(string name, JsonElement root)
        {
            Name = name;
            Root = root.Clone();
            var anims = root.GetProperty("anims");
            Idle = Anim(anims, "idle");
            Down = Anim(anims, "down");
            Anims = anims.EnumerateObject().ToImmutableDictionary(a => a.Name, a => Anim(anims, a.Name), StringComparer.Ordinal);
            if (root.TryGetProperty("frame", out var frame))
            {
                (Width, Height) = (frame.GetProperty("width").GetInt32(), frame.GetProperty("height").GetInt32());
                var anchor = frame.GetProperty("anchor");
                (AnchorX, AnchorY) = (anchor[0].GetInt32(), anchor[1].GetInt32());
            }
        }

        public string Name { get; }

        public JsonElement Root { get; }

        public int Width { get; }

        public int Height { get; }

        public int AnchorX { get; }

        public int AnchorY { get; }

        public UnitAnim Idle { get; }

        public UnitAnim Down { get; }

        public ImmutableDictionary<string, UnitAnim> Anims { get; }
    }
}
