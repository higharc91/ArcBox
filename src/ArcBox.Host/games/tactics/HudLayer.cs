using System.Collections.Immutable;
using ArcBox.Host.Shell;
using Godot;
using Tactics.Logic;
using Tactics.Game;
using Tactics.Presentation.Board;
using Tactics.Presentation.Hud;
using Tactics.Presentation.Beats;
using Tactics.Presentation.Story;

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

    private readonly PanelContainer _combat;
    private readonly Label _combatHeading, _combatRolls, _combatEffect;
    private readonly string _root;
    private readonly Dictionary<string, Texture2D?> _textures = [];
    private readonly VBoxContainer _left;
    private readonly Control _head;
    private readonly Label _title;
    private readonly Label _round;
    private readonly Label _tag;
    private readonly Control _objective;
    private readonly Label _objectiveText;
    private readonly Control _notice;
    private readonly Label _noticeText;
    private readonly Control _order;
    private readonly TurnStrip _strip;
    private readonly Control _prompt;
    private readonly Label _promptText;
    private readonly Control _badge;
    private readonly VoteBoard? _retreat;
    private readonly UnitCard? _activeCard;
    private readonly UnitCard? _targetCard;

    /// <param name="story">The story panels' art, for the vote to fall back (#111); with none, the vote isn't drawn.</param>
    public HudLayer(string packRoot, HudArt art, StoryArt? story = null)
    {
        _combatHeading = TvText.Label("", ArcTheme.Bold, 28, Gold);
        _combatRolls = TvText.Label("", ArcTheme.SemiBold, 30, Ink);
        _combatEffect = TvText.Label("", ArcTheme.SemiBold, 26, Ink);
        // Wrap measured text below: this keeps Godot from caching a tall minimum at the initial zero width.
        _combat = new PanelContainer { Position = new Vector2(520, 164), CustomMinimumSize = new Vector2(880, 0), Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        _combat.AddThemeStyleboxOverride("panel", ArcTheme.GlassBox(Gold, 20, 12));
        _combat.AddChild(ArcTheme.VBox(6, _combatHeading, _combatRolls, _combatEffect));
        AddChild(_combat);
        if (story is not null)
        {
            _activeCard = new UnitCard(story, "Active unit");
            _targetCard = new UnitCard(story, "Current target");
            AddChild(_activeCard);
            AddChild(_targetCard);
        }
        _root = packRoot;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        var panels = Scaled(packRoot, art.PanelsPath);
        _title = TvText.Label("", ArcTheme.Bold, 36, Ink);
        _round = TvText.Label("", ArcTheme.SemiBold, 26, Gold);
        _tag = TvText.Label("", ArcTheme.SemiBold, 22, Muted);
        _objectiveText = TvText.Label("", ArcTheme.SemiBold, 24, Ink);
        _objective = ArcTheme.HBox(8, TvText.Label("Objective:", ArcTheme.SemiBold, 24, Muted), _objectiveText);
        _head = ArcTheme.Panel(Box(panels, art.Panel("main")), ArcTheme.VBox(0, _title, _round, _tag, _objective, TvText.Label("○ Party · ◇ Enemy · △ Neutral", ArcTheme.SemiBold, 20, Ink)));
        _head.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;

        _noticeText = TvText.Label("", ArcTheme.SemiBold, 24, Ink);
        _noticeText.CustomMinimumSize = new Vector2(600, 0);
        _noticeText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _notice = ArcTheme.Panel(Box(panels, art.Panel("tooltip")), _noticeText);
        _notice.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        _notice.Visible = false;

        _left = ArcTheme.VBox(14, _head, _notice);
        _left.Position = new Vector2(Margin, Top);
        AddChild(_left);

        _strip = new TurnStrip(art, Load);
        _order = ArcTheme.Panel(Box(panels, art.Panel("turn_strip")), _strip);
        AddChild(_order);

        _promptText = TvText.Label("", ArcTheme.Black, 44, Gold);
        _promptText.HorizontalAlignment = HorizontalAlignment.Center;
        var label = TvText.Meta(BattleHud.PromptLabel, 16, Muted);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        var note = TvText.Label(BattleHud.PromptNote, ArcTheme.SemiBold, 24, Ink);
        note.HorizontalAlignment = HorizontalAlignment.Center;
        _prompt = ArcTheme.Panel(Box(panels, art.Panel("main")), ArcTheme.VBox(0, label, _promptText, note));
        _prompt.Visible = false;
        AddChild(_prompt);

        _badge = ArcTheme.Panel(Box(panels, art.Panel("tooltip")), TvText.Label(BattleHud.AiFinishedBadge, ArcTheme.Bold, 22, Gold));
        _badge.Visible = false;
        AddChild(_badge);

        if (story is not null)
        {
            _retreat = new VoteBoard(story);     // the same panel as a party vote, bottom right
            AddChild(_retreat);
        }
    }

    public void ShowCombat(CombatReadout? readout)
    {
        _combat.Visible = readout is not null;
        if (readout is null) return;
        CombatText(_combatHeading, readout.Heading);
        CombatText(_combatRolls, string.Join("\n", readout.Rolls));
        _combatRolls.Visible = readout.Rolls.Length > 0;
        CombatText(_combatEffect, readout.Effect ?? "");
        _combatEffect.Visible = readout.Effect is not null;
        _combat.Size = new Vector2(880, _combat.GetCombinedMinimumSize().Y);
    }

    private static void CombatText(Label label, string text)
    {
        var font = label.GetThemeFont("font"); var size = label.GetThemeFontSize("font_size");
        var lines = new List<string>();
        foreach (var paragraph in text.Split('\n'))
        {
            var line = "";
            foreach (var word in paragraph.Split(' '))
            {
                var next = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && font.GetStringSize(next, fontSize: size).X > 840) { lines.Add(line); line = word; }
                else line = next;
            }
            lines.Add(line);
        }
        label.Text = string.Join("\n", lines);
    }

    /// <summary>Shows the party's vote to fall back (<see cref="RetreatPlan"/>), or nothing.</summary>
    public void ShowRetreat(VotePanel? vote) => _retreat?.Show(vote);

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
        _tag.Visible = hud.Tag is not null;
        _tag.Text = hud.Tag ?? "";
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

    /// <summary>Cards use the same displayed snapshot and overlap fade as the battlefield HUD.</summary>
    public void ShowCards(BattleView shown, UnitId? active, UnitId? target, ImmutableArray<BoardUnit> units, BoardFit fit)
    {
        PlaceCard(_activeCard, active, Margin);
        PlaceCard(_targetCard, target, LayoutWidth - Margin - 480);
        void PlaceCard(UnitCard? card, UnitId? id, float x)
        {
            if (card is null) return;
            card.Show(shown.Units.FirstOrDefault(u => u.Id == id));
            if (!card.Visible) return;
            card.Position = new Vector2(x, 1080 - Margin - card.Size.Y);
            card.Modulate = Over(card, units, fit) ? new Color(1, 1, 1, SeeThrough) : Colors.White;
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

    /// <summary>A piece of the panels' art as a stylebox, at the size the HUD draws it.</summary>
    internal static StyleBoxTexture Box(Texture2D? texture, HudPiece piece) => new()
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
    internal static Texture2D? Scaled(string root, string file)
    {
        var path = System.IO.Path.Combine(root, file);
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
