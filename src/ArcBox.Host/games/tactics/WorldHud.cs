using System.Collections.Immutable;
using ArcBox.Host.Shell;
using Godot;
using Tactics.Presentation.Board;
using Tactics.Presentation.Hud;
using Tactics.Presentation.World;

namespace ArcBox.Host.Games.Tactics;

/// <summary>
/// The exploration screen's two panels (mockup 02), in the battle HUD's navy glass and gold: the chapter and region at the top left, the party's leader
/// at the top right. A note from the game goes under the left one. A panel over the party goes see-through, as the battle's do. What's said comes
/// from <see cref="WalkHud"/>. <see cref="Show"/> comes 3 to 5 times a second, so it does nothing for a panel that hasn't changed.
/// </summary>
public partial class WorldHud : Control
{
    private const int Margin = 36;
    private const int Top = 30;
    private const float SeeThrough = 0.45f;
    private const int LayoutWidth = 1920;
    private const int FaceSize = 92;

    private readonly VBoxContainer _left;
    private readonly Label _title;
    private readonly Label _detail;
    private readonly Control _notice;
    private readonly Label _noticeText;
    private readonly Control _right;
    private readonly Label _leader;
    private readonly LeaderFace _face;
    private WalkHud? _shown;
    private string? _shownNotice;

    public WorldHud(string packRoot, HudArt art)
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        var panels = HudLayer.Scaled(packRoot, art.PanelsPath);
        _title = TvText.Label("", ArcTheme.Spaced("Bold", 2), 38, HudLayer.Ink);
        _detail = TvText.Label("", ArcTheme.SemiBold, 26, HudLayer.Muted);
        var head = ArcTheme.Panel(HudLayer.Box(panels, art.Panel("main")), ArcTheme.VBox(0, _title, _detail));
        head.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;

        _noticeText = TvText.Label("", ArcTheme.SemiBold, 24, HudLayer.Ink);
        _notice = ArcTheme.Panel(HudLayer.Box(panels, art.Panel("tooltip")), _noticeText);
        _notice.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        _notice.Visible = false;

        _left = ArcTheme.VBox(14, head, _notice);
        _left.Position = new Vector2(Margin, Top);
        AddChild(_left);

        _face = new LeaderFace(packRoot, art) { CustomMinimumSize = new Vector2(FaceSize, FaceSize), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        _leader = TvText.Label("", ArcTheme.Spaced("Bold", 1), 36, HudLayer.Ink);
        var words = ArcTheme.VBox(0, TvText.Meta(WalkHud.LeaderLabel, 18, HudLayer.Gold), _leader, TvText.Label(WalkHud.LeaderNote, ArcTheme.SemiBold, 24, HudLayer.Muted));
        words.Alignment = BoxContainer.AlignmentMode.Center;
        _right = ArcTheme.Panel(HudLayer.Box(panels, art.Panel("main")), ArcTheme.HBox(18, _face, words));
        AddChild(_right);
    }

    /// <summary>Shows <paramref name="hud"/> and <paramref name="notice"/>.</summary>
    public void Show(WalkHud hud, string? notice)
    {
        if (hud == _shown && notice == _shownNotice)
        {
            return;
        }
        (_shown, _shownNotice) = (hud, notice);
        _title.Text = hud.Title.ToUpperInvariant();
        _detail.Text = hud.Detail;
        _leader.Text = hud.Leader;
        _face.Show(hud.LeaderSprite);
        _notice.Visible = notice is not null;
        _noticeText.Text = notice ?? "";
        Place();
    }

    /// <summary>Makes a panel over <paramref name="units"/> see-through.</summary>
    public void Clear(ImmutableArray<BoardUnit> units, BoardFit fit)
    {
        foreach (var panel in new[] { _left, _right })
        {
            var over = BattleHud.Covers(new BoardArea(panel.Position.X, panel.Position.Y, panel.Size.X, panel.Size.Y), units, fit);
            var wanted = over ? new Color(1, 1, 1, SeeThrough) : Colors.White;
            if (panel.Modulate != wanted)
            {
                panel.Modulate = wanted;
            }
        }
    }

    /// <summary>Placed by hand, as what's in them changes their size and the right-hand one grows from a fixed point.</summary>
    private void Place()
    {
        _left.Size = _left.GetCombinedMinimumSize();
        _right.Size = _right.GetCombinedMinimumSize();
        _right.Position = new Vector2(LayoutWidth - Margin - _right.Size.X, Top);
    }

    /// <summary>The leader's portrait in its frame, as the turn strip draws one.</summary>
    private sealed partial class LeaderFace : Control
    {
        private const int FrameInset = 6;       // ui/manifest.json: a frame's window
        private const string FrameName = "frame_active";

        private readonly string _root;
        private readonly HudArt _art;
        private readonly Texture2D? _sheet;
        private readonly StyleBoxTexture _frame;
        private readonly Dictionary<string, Texture2D?> _textures = [];
        private string? _sprite;

        public LeaderFace(string packRoot, HudArt art)
        {
            (_root, _art) = (packRoot, art);
            TextureFilter = TextureFilterEnum.Nearest;
            MouseFilter = MouseFilterEnum.Ignore;
            _sheet = Load(art.TurnPath);
            var piece = art.Turn(FrameName);
            _frame = new StyleBoxTexture
            {
                Texture = _sheet,
                RegionRect = Region(piece.Rect),
                TextureMarginLeft = piece.Slice.Left,
                TextureMarginTop = piece.Slice.Top,
                TextureMarginRight = piece.Slice.Right,
                TextureMarginBottom = piece.Slice.Bottom,
                ExpandMarginLeft = piece.Outset.Left,
                ExpandMarginTop = piece.Outset.Top,
                ExpandMarginRight = piece.Outset.Right,
                ExpandMarginBottom = piece.Outset.Bottom,
                DrawCenter = false,
            };
        }

        /// <summary>Shows the face for <paramref name="sprite"/>, or an empty frame for null.</summary>
        public void Show(string? sprite)
        {
            _sprite = sprite;
            QueueRedraw();
        }

        public override void _Draw()
        {
            var box = new Rect2(Vector2.Zero, Size);
            var window = box.Grow(-FrameInset);
            var face = _art.Portrait(_sprite, UnitTone.Party);
            if (Load(face.Backdrop) is { } backdrop)
            {
                DrawTextureRect(backdrop, window, false);
            }
            if (_sprite is not null && Load(face.Path) is { } picture)
            {
                DrawTextureRectRegion(picture, window, Region(face.Source));
            }
            DrawStyleBox(_frame, box);
        }

        private Texture2D? Load(string file)
        {
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

        private static Rect2 Region(ArtRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);
    }
}
