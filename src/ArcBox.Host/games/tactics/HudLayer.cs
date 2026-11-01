using System.Collections.Immutable;
using ArcBox.Host.Shell;
using Godot;
using Tactics.Logic;
using Tactics.Presentation.Board;
using Tactics.Presentation.Hud;

namespace ArcBox.Host.Games.Tactics;

/// <summary>
/// The battle's HUD over the board, in the pack's navy glass and gold: the encounter, round and objective at the top left, the
/// turn order at the top right, and a reaction prompt at the top centre when one is waiting. The corners are what the diamond
/// board leaves free. A panel that does lie over a unit goes see-through, so the unit stays readable. Everything said here is
/// worked out in <see cref="BattleHud"/>; this class only draws it.
/// </summary>
public partial class HudLayer : Control
{
    /// <summary>ui/manifest.json's <c>text.ink</c>.</summary>
    public static readonly Color Ink = new("f4ecd6");

    /// <summary>ui/manifest.json's <c>text.muted</c>.</summary>
    public static readonly Color Muted = new("bdb093");

    /// <summary>ui/manifest.json's <c>text.gold</c>.</summary>
    public static readonly Color Gold = new("e6bd5c");

    private const int ArtScale = 2;      // the panels are drawn at a whole multiple of the art
    private const int Margin = 36;
    private const int Top = 30;
    private const float SeeThrough = 0.45f;
    private const int LayoutWidth = 1920;
    private const int PanelGap = 28;     // past the studs on two panels' corners

    private readonly string _root;
    private readonly Dictionary<string, Texture2D?> _textures = [];
    private readonly VBoxContainer _left;
    private readonly Control _head;
    private readonly Label _title;
    private readonly Label _round;
    private readonly Control _objective;
    private readonly Label _objectiveText;
    private readonly Control _notice;
    private readonly Label _noticeText;
    private readonly Control _order;
    private readonly TurnStrip _strip;
    private readonly Control _prompt;
    private readonly Label _promptText;
    private readonly Control _badge;

    public HudLayer(string packRoot, HudArt art)
    {
        _root = packRoot;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        var panels = Scaled(art.PanelsPath);
        _title = ArcTheme.Label("", ArcTheme.Bold, 36, Ink);
        _round = ArcTheme.Label("", ArcTheme.SemiBold, 26, Gold);
        _objectiveText = ArcTheme.Label("", ArcTheme.SemiBold, 24, Ink);
        _objective = ArcTheme.HBox(8, ArcTheme.Label("Objective:", ArcTheme.SemiBold, 24, Muted), _objectiveText);
        _head = ArcTheme.Panel(Box(panels, art.Panel("main")), ArcTheme.VBox(0, _title, _round, _objective));
        _head.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;

        _noticeText = ArcTheme.Label("", ArcTheme.SemiBold, 22, Ink);
        _notice = ArcTheme.Panel(Box(panels, art.Panel("tooltip")), _noticeText);
        _notice.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        _notice.Visible = false;

        _left = ArcTheme.VBox(14, _head, _notice);
        _left.Position = new Vector2(Margin, Top);
        AddChild(_left);

        _strip = new TurnStrip(art, Load);
        _order = ArcTheme.Panel(Box(panels, art.Panel("turn_strip")), _strip);
        AddChild(_order);

        _promptText = ArcTheme.Label("", ArcTheme.Black, 44, Gold);
        _promptText.HorizontalAlignment = HorizontalAlignment.Center;
        var label = ArcTheme.Meta(BattleHud.PromptLabel, 16, Muted);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        var note = ArcTheme.Label(BattleHud.PromptNote, ArcTheme.SemiBold, 22, Ink);
        note.HorizontalAlignment = HorizontalAlignment.Center;
        _prompt = ArcTheme.Panel(Box(panels, art.Panel("main")), ArcTheme.VBox(0, label, _promptText, note));
        _prompt.Visible = false;
        AddChild(_prompt);

        _badge = ArcTheme.Panel(Box(panels, art.Panel("tooltip")), ArcTheme.Label(BattleHud.AiFinishedBadge, ArcTheme.Bold, 22, Gold));
        _badge.Visible = false;
        AddChild(_badge);
    }

    /// <summary>Shows a note from the game for everyone, under the battle's panel, or nothing.</summary>
    public void ShowNotice(string? notice)
    {
        _notice.Visible = notice is not null;
        _noticeText.Text = notice ?? "";
    }

    /// <summary>Shows <paramref name="hud"/>, over <paramref name="units"/> as they're drawn now, with the badge on <paramref name="aiFinished"/>.</summary>
    public void Show(BattleHud hud, ImmutableArray<BoardUnit> units, BoardFit fit, UnitId? aiFinished)
    {
        _title.Text = hud.Title.ToUpperInvariant();
        _round.Text = hud.Turn is { } turn ? $"{hud.Round} · {turn}" : hud.Round;
        _objective.Visible = hud.Objective is not null;
        _objectiveText.Text = hud.Objective ?? "";
        _strip.Show(hud);
        _prompt.Visible = hud.Prompt is not null;
        _promptText.Text = hud.Prompt ?? "";

        Place();
        if (Over(_prompt, units, fit))
        {
            _prompt.Position = new Vector2(Margin, _left.Position.Y + _left.Size.Y + PanelGap);   // it has to be read, so it moves rather than fades
        }
        foreach (var panel in new[] { _left, _order, _prompt })
        {
            panel.Modulate = Over(panel, units, fit) ? new Color(1, 1, 1, SeeThrough) : Colors.White;
        }

        var badged = aiFinished is { } id ? units.FirstOrDefault(u => u.Id == id) : null;
        _badge.Visible = badged is not null;
        if (badged is not null)
        {
            var size = _badge.GetCombinedMinimumSize();
            var at = BattleHud.BadgeAt(badged, fit, new System.Numerics.Vector2(size.X, size.Y));
            _badge.Position = new Vector2(at.X, at.Y);
        }
    }

    private static bool Over(Control panel, ImmutableArray<BoardUnit> units, BoardFit fit)
        => BattleHud.Covers(new BoardArea(panel.Position.X, panel.Position.Y, panel.Size.X, panel.Size.Y), units, fit);

    /// <summary>The panels are placed by hand, as what's in them changes their size and the two right-hand ones grow from a fixed point.</summary>
    private void Place()
    {
        _left.Size = _left.GetCombinedMinimumSize();
        _order.Size = _order.GetCombinedMinimumSize();
        _order.Position = new Vector2(LayoutWidth - Margin - _order.Size.X, Top);
        _prompt.Size = _prompt.GetCombinedMinimumSize();
        var centred = (LayoutWidth - _prompt.Size.X) / 2;
        _prompt.Position = new Vector2(Math.Min(centred, _order.Position.X - PanelGap - _prompt.Size.X), Top);
    }

    private static StyleBoxTexture Box(Texture2D? texture, HudPiece piece) => new()
    {
        Texture = texture,
        RegionRect = new Rect2(piece.Rect.X * ArtScale, piece.Rect.Y * ArtScale, piece.Rect.Width * ArtScale, piece.Rect.Height * ArtScale),
        TextureMarginLeft = piece.Slice.Left * ArtScale,
        TextureMarginTop = piece.Slice.Top * ArtScale,
        TextureMarginRight = piece.Slice.Right * ArtScale,
        TextureMarginBottom = piece.Slice.Bottom * ArtScale,
        ExpandMarginLeft = piece.Outset.Left * ArtScale,
        ExpandMarginTop = piece.Outset.Top * ArtScale,
        ExpandMarginRight = piece.Outset.Right * ArtScale,
        ExpandMarginBottom = piece.Outset.Bottom * ArtScale,
        ContentMarginLeft = piece.Padding.Left * ArtScale + 6,
        ContentMarginTop = piece.Padding.Top * ArtScale,
        ContentMarginRight = piece.Padding.Right * ArtScale + 6,
        ContentMarginBottom = piece.Padding.Bottom * ArtScale,
    };

    /// <summary>A stylebox can't scale its art, so the image itself is made bigger, with nearest filtering, as the manifest asks.</summary>
    private Texture2D? Scaled(string file)
    {
        var path = System.IO.Path.Combine(_root, file);
        var image = System.IO.File.Exists(path) ? Image.LoadFromFile(path) : null;
        if (image is null || image.IsEmpty())
        {
            GD.PushWarning($"Tactics HUD: can't load {path}");
            return null;
        }
        image.Resize(image.GetWidth() * ArtScale, image.GetHeight() * ArtScale, Image.Interpolation.Nearest);
        return ImageTexture.CreateFromImage(image);
    }

    private Texture2D? Load(string? file)
    {
        if (file is null)
        {
            return null;
        }
        if (_textures.TryGetValue(file, out var known))
        {
            return known;
        }
        var path = System.IO.Path.Combine(_root, file);
        var image = System.IO.File.Exists(path) ? Image.LoadFromFile(path) : null;
        if (image is null || image.IsEmpty())
        {
            GD.PushWarning($"Tactics HUD: can't load {path}");
            return _textures[file] = null;
        }
        return _textures[file] = ImageTexture.CreateFromImage(image);
    }
}
