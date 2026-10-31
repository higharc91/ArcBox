using System.Collections.Immutable;
using Godot;
using Tactics.Presentation.Board;
using Tactics.Presentation.Hud;

namespace ArcBox.Host.Games.Tactics;

/// <summary>
/// The turn order, as in the mockup: faces in their frames, the active one bigger with its NOW tab, an AI badge on a character
/// whose player is away, each unit's stripe and name. It draws <see cref="BattleHud.Order"/> and works nothing out.
/// </summary>
public partial class TurnStrip : Control
{
    /// <summary>The label's row over the frames.</summary>
    private const int HeaderHeight = 22;
    private const int ActiveBox = 92;
    private const int Box = 66;
    private const int ActiveStep = 104;
    private const int Step = 76;
    private const int TabLift = 12;         // room over a frame for its tab
    private const int FrameInset = 6;       // ui/manifest.json: a frame's window
    private const int ArtScale = 2;         // the tabs are drawn at twice the art
    private const int StripeHeight = 5;
    private const int NameHeight = 28;
    private const int NameSize = 16;

    private static readonly Color Dimmed = new(0.55f, 0.55f, 0.6f);

    private readonly HudArt _art;
    private readonly Func<string, Texture2D?> _load;
    private readonly StyleBoxTexture _active;
    private readonly StyleBoxTexture _inactive;
    private readonly StyleBoxTexture _enemy;
    private readonly Dictionary<string, StyleBoxTexture> _stripes = [];
    private readonly Texture2D? _turn;
    private ImmutableArray<HudSlot> _slots = [];
    private int _more;

    public TurnStrip(HudArt art, Func<string, Texture2D?> load)
    {
        (_art, _load) = (art, load);
        TextureFilter = TextureFilterEnum.Nearest;
        MouseFilter = MouseFilterEnum.Ignore;
        _turn = load(art.TurnPath);
        _active = Frame("frame_active");
        _inactive = Frame("frame_inactive");
        _enemy = Frame("frame_enemy");
    }

    /// <summary>Shows <paramref name="hud"/>'s order, redrawing only if it has changed: this is called on every frame of a beat.</summary>
    public void Show(BattleHud hud)
    {
        if (_more == hud.More && _slots.SequenceEqual(hud.Order))
        {
            return;
        }
        (_slots, _more) = (hud.Order, hud.More);
        var width = _slots.IsEmpty ? ActiveBox : ActiveStep + (_slots.Length - 1) * Step;
        CustomMinimumSize = new Vector2(width, HeaderHeight + TabLift + ActiveBox + StripeHeight + NameHeight);
        QueueRedraw();
    }

    public override void _Draw()
    {
        var font = global::ArcBox.Host.Shell.ArcTheme.Spaced("Bold", 3);
        DrawString(font, new Vector2(0, HeaderHeight - 6), BattleHud.OrderLabel.ToUpperInvariant(), HorizontalAlignment.Left, -1, 16, HudLayer.Muted);
        if (_more > 0)
        {
            DrawString(font, new Vector2(0, HeaderHeight - 6), $"+{_more}", HorizontalAlignment.Right, Size.X, 16, HudLayer.Muted);
        }

        var x = 0f;
        foreach (var slot in _slots)
        {
            DrawSlot(slot, x);
            x += slot.Active ? ActiveStep : Step;
        }
    }

    private void DrawSlot(HudSlot slot, float x)
    {
        var size = slot.Active ? ActiveBox : Box;
        var bottom = HeaderHeight + TabLift + ActiveBox;
        var box = new Rect2(x, bottom - size, size, size);
        var tint = slot.Downed ? Dimmed : Colors.White;

        var window = box.Grow(-FrameInset);
        var face = _art.Portrait(slot.Sprite, slot.Tone);
        if (_load(face.Backdrop) is { } backdrop)
        {
            DrawTextureRect(backdrop, window, false, tint);
        }
        if (_load(face.Path) is { } picture)
        {
            DrawTextureRectRegion(picture, window, Region(face.Source), tint);
        }
        DrawStyleBox(slot.Active ? _active : slot.Tone == UnitTone.Enemy ? _enemy : _inactive, box);

        if (slot.Active)
        {
            DrawPiece("now_tab", new Vector2(box.Position.X + 2 * ArtScale, box.Position.Y - 7 * ArtScale));
        }
        if (slot.Ai)
        {
            var badge = _art.Turn("ai_badge").Rect;
            DrawPiece("ai_badge", new Vector2(box.End.X + 2 * ArtScale - badge.Width * ArtScale, box.Position.Y - 6 * ArtScale));
        }
        DrawStyleBox(Stripe(slot.Stripe), new Rect2(box.Position.X + 2, box.End.Y + 1, box.Size.X - 4, StripeHeight));
        DrawName(slot, box);
    }

    private void DrawPiece(string name, Vector2 at)
    {
        if (_turn is not null)
        {
            var rect = _art.Turn(name).Rect;
            DrawTextureRectRegion(_turn, new Rect2(at, new Vector2(rect.Width, rect.Height) * ArtScale), Region(rect));
        }
    }

    private void DrawName(HudSlot slot, Rect2 box)
    {
        var font = global::ArcBox.Host.Shell.ArcTheme.Bold;
        var room = (slot.Active ? ActiveStep : Step) - 6;
        var left = box.GetCenter().X - room / 2f;
        var baseline = box.End.Y + StripeHeight + NameHeight - 6;
        DrawString(font, new Vector2(left, baseline), Fit(slot.Name, font, room), HorizontalAlignment.Center, room, NameSize, slot.Active ? HudLayer.Gold : HudLayer.Ink);
    }

    private static string Fit(string name, Font font, int room)
    {
        if (font.GetStringSize(name, HorizontalAlignment.Left, -1, NameSize).X <= room)
        {
            return name;
        }
        var cut = name;
        while (cut.Length > 1 && font.GetStringSize(cut + "…", HorizontalAlignment.Left, -1, NameSize).X > room)
        {
            cut = cut[..^1];
        }
        return cut + "…";
    }

    private StyleBoxTexture Frame(string name)
    {
        var piece = _art.Turn(name);
        return new StyleBoxTexture
        {
            Texture = _turn,
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

    private StyleBoxTexture Stripe(string name)
    {
        if (!_stripes.TryGetValue(name, out var box))
        {
            var piece = _art.Turn(name);
            box = new StyleBoxTexture
            {
                Texture = _turn,
                RegionRect = Region(piece.Rect),
                TextureMarginLeft = piece.Slice.Left,
                TextureMarginRight = piece.Slice.Right,
            };
            _stripes[name] = box;
        }
        return box;
    }

    private static Rect2 Region(ArtRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);
}
