using ArcBox.Host.Shell;
using Godot;
using Tactics.Presentation.Board;
using Tactics.Presentation.Story;

namespace ArcBox.Host.Games.Tactics;

/// <summary>
/// A skill check, full over the dimmed world (mockup 03): the skill and its DC at the top, a card for each character who can roll, with
/// their face, player, bonus and where it comes from, and at the bottom the countdown on picking who rolls, or the roll: the gold d20
/// tumbling and landing on its face, the sum, and the verdict. The cards are rebuilt only when what's on them changes.
/// </summary>
public sealed partial class CheckBoard : Control
{
    private const int CardWidth = 250;
    private const int FaceSize = 150;
    private const int DieSize = 132;
    private const float Dim = 0.6f;
    private const int EdgeHeight = 6;
    private const float AwayEdge = 0.55f;   // an away player's edge, dimmed as their face is

    private readonly StoryArt _art;
    private readonly ColorRect _dim;
    private readonly Control _head;
    private readonly Label _skill;
    private readonly Label _dc;
    private readonly Label _title;
    private readonly HBoxContainer _cards;
    private readonly Control _foot;
    private readonly Label _footText;
    private readonly Label _clock;
    private readonly Control _roll;
    private readonly DieFace _die;
    private readonly Label _sum;
    private readonly Label _verdict;
    private readonly Label _roller;
    private CheckPanel? _shown;

    public CheckBoard(StoryArt art)
    {
        _art = art;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _dim = new ColorRect { Color = new Color(0.02f, 0.02f, 0.06f, Dim), MouseFilter = MouseFilterEnum.Ignore };
        _dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_dim);

        var kicker = TvText.Meta("Skill check", 20, HudLayer.Muted);
        kicker.HorizontalAlignment = HorizontalAlignment.Center;
        _skill = StoryArt.Text("", ArcTheme.Spaced("Black", 8), 76, HudLayer.Gold);
        _skill.HorizontalAlignment = HorizontalAlignment.Center;
        _dc = StoryArt.Text("", ArcTheme.Black, 30, new Color("2a1b04"));
        var dc = ArcTheme.Panel(ArcTheme.Flat(HudLayer.Gold, 8), _dc);
        _title = StoryArt.Text("", ArcTheme.SemiBold, 28, HudLayer.Ink);
        var line = ArcTheme.HBox(16, dc, _title);
        line.Alignment = BoxContainer.AlignmentMode.Center;
        _head = ArcTheme.Panel(art.Box("main"), ArcTheme.Margin(40, 6, 40, 10, ArcTheme.VBox(4, kicker, _skill, line)));
        AddChild(_head);

        _cards = ArcTheme.HBox(22);
        _cards.Alignment = BoxContainer.AlignmentMode.Center;
        AddChild(_cards);

        _footText = StoryArt.Text("", ArcTheme.SemiBold, 28, HudLayer.Ink);
        _clock = StoryArt.Text("", ArcTheme.Display("Black", 2, 6, 6), 48, HudLayer.Ink);
        _foot = ArcTheme.Panel(art.Box("main"), ArcTheme.Margin(30, 8, 30, 8, ArcTheme.HBox(30, _footText, _clock)));
        AddChild(_foot);

        _die = new DieFace(art) { CustomMinimumSize = new Vector2(DieSize, DieSize) };
        _sum = StoryArt.Text("", ArcTheme.Black, 54, HudLayer.Ink);
        _verdict = StoryArt.Text("", ArcTheme.Spaced("Black", 8), 60, StoryArt.Pass);
        _roller = StoryArt.Text("", ArcTheme.SemiBold, 28, HudLayer.Muted);
        var words = ArcTheme.VBox(0, ArcTheme.HBox(40, _sum, _verdict), _roller);
        words.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _roll = ArcTheme.Panel(art.Box("main"), ArcTheme.Margin(30, 10, 40, 10, ArcTheme.HBox(36, _die, words)));
        AddChild(_roll);
        Visible = false;
    }

    /// <summary>Shows <paramref name="check"/>, or hides the board, dim and all, for null.</summary>
    public void Show(CheckPanel? check)
    {
        if (check == _shown)
        {
            return;
        }
        var rebuild = check is null || _shown is null || !check.Cards.SequenceEqual(_shown.Cards);
        _shown = check;
        Visible = check is not null;
        if (check is null)
        {
            return;
        }
        _skill.Text = check.Skill.ToUpperInvariant();
        _dc.Text = check.Dc;
        _title.Text = check.Title;
        if (rebuild)
        {
            Fill(check);
        }
        _foot.Visible = check.Roll is null && (check.Footer is not null || check.Clock is not null);
        _footText.Text = check.Footer ?? "";
        _clock.Text = check.Clock ?? "";
        _clock.Visible = check.Clock is not null;
        _roll.Visible = check.Roll is not null;
        if (check.Roll is { } roll)
        {
            _die.Show(roll.Face);
            _sum.Text = roll.Landed ? roll.Sum : "";
            _verdict.Text = roll.Landed ? roll.Verdict.ToUpperInvariant() : "";
            _verdict.AddThemeColorOverride("font_color", roll.Success ? StoryArt.Pass : StoryArt.Fail);
            _roller.Text = roll.Line;
        }
        Place();
    }

    private void Fill(CheckPanel check)
    {
        foreach (var child in _cards.GetChildren())
        {
            _cards.RemoveChild(child);
            child.QueueFree();
        }
        foreach (var card in check.Cards)
        {
            _cards.AddChild(Card(card));
        }
    }

    private Control Card(CheckCard card)
    {
        var face = new StoryFace(_art, new Vector2(FaceSize, FaceSize)) { SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        face.Show(_art.Hud.Portrait(card.Sprite, UnitTone.Party), card.Here);
        Label Centred(Label l)
        {
            l.HorizontalAlignment = HorizontalAlignment.Center;
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            return l;
        }
        var inside = ArcTheme.VBox(6,
            Edge(card),
            face,
            Centred(StoryArt.Text(card.Name.ToUpperInvariant(), ArcTheme.Spaced("Bold", 3), 30, HudLayer.Ink)),
            Centred(StoryArt.Text(card.Who, ArcTheme.SemiBold, 22, HudLayer.Muted)),
            Centred(StoryArt.Text(card.Bonus, ArcTheme.Black, 52, card.Chosen ? HudLayer.Gold : HudLayer.Ink)),
            Centred(StoryArt.Text(card.Note, ArcTheme.SemiBold, 24, HudLayer.Muted)),
            Centred(StoryArt.Text(card.VotesText.Length > 0 ? card.VotesText : " ", ArcTheme.Bold, 20, HudLayer.Gold)));
        var box = ArcTheme.Panel(_art.Box(card.Chosen ? "card_selected" : "card"), ArcTheme.Margin(8, 10, 8, 10, inside));
        box.CustomMinimumSize = new Vector2(CardWidth, 0);
        if (card.Tag is not { } tag)
        {
            return ArcTheme.VBox(4, Gap(), box);
        }
        var label = StoryArt.Text(tag.ToUpperInvariant(), ArcTheme.Spaced("Bold", 2), 16, card.Chosen ? new Color("2a1b04") : HudLayer.Ink);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        var tab = ArcTheme.Panel(ArcTheme.Flat(card.Chosen ? HudLayer.Gold : new Color("3a6fb0"), 4), label);
        tab.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        return ArcTheme.VBox(4, tab, box);
    }

    /// <summary>
    /// A stripe across the top of the card in its player's colour (<see cref="CheckCard.Colour"/>), dimmed with the face when they're away;
    /// with no colour, the same room left empty, so every card's face sits at the same height.
    /// </summary>
    private static Control Edge(CheckCard card)
    {
        var size = new Vector2(0, EdgeHeight);
        if (card.Colour is not { } c)
        {
            return new Control { CustomMinimumSize = size, MouseFilter = MouseFilterEnum.Ignore };
        }
        var colour = new Color(c.R, c.G, c.B, card.Here ? 1 : AwayEdge);
        var box = ArcTheme.Flat(colour);
        box.SetCornerRadiusAll(EdgeHeight / 2);
        var edge = ArcTheme.Panel(box);
        edge.CustomMinimumSize = size;
        edge.MouseFilter = MouseFilterEnum.Ignore;
        return edge;
    }

    /// <summary>The room a card's tab takes, so the cards without one line up with those with.</summary>
    private static Control Gap() => new Control { CustomMinimumSize = new Vector2(0, 30), MouseFilter = MouseFilterEnum.Ignore };

    private void Place()
    {
        _head.Size = _head.GetCombinedMinimumSize();
        _head.Position = new Vector2((StoryArt.Layout.X - _head.Size.X) / 2, 50);
        _cards.Size = _cards.GetCombinedMinimumSize();
        _cards.Position = new Vector2((StoryArt.Layout.X - _cards.Size.X) / 2, _head.Position.Y + _head.Size.Y + 40);
        var bottom = StoryArt.Layout.Y - 60;
        foreach (var panel in new[] { _foot, _roll })
        {
            panel.Size = panel.GetCombinedMinimumSize();
            panel.Position = new Vector2((StoryArt.Layout.X - panel.Size.X) / 2, bottom - panel.Size.Y);
        }
    }

    /// <summary>The pack's gold d20 with the face showing, in the TV's type: the number box of ui/manifest.json's d20.</summary>
    private sealed partial class DieFace : Control
    {
        private readonly StoryArt _art;
        private int _face;

        public DieFace(StoryArt art)
        {
            _art = art;
            TextureFilter = TextureFilterEnum.Nearest;
            MouseFilter = MouseFilterEnum.Ignore;
        }

        public void Show(int face)
        {
            if (face != _face)
            {
                _face = face;
                QueueRedraw();
            }
        }

        public override void _Draw()
        {
            var box = new Rect2(Vector2.Zero, Size);
            if (_art.Load(UnitArt.DicePath) is { } dice)
            {
                DrawTextureRectRegion(dice, box, StoryArt.Region(_art.Units.DieGold));
            }
            var text = _face.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var font = ArcTheme.Black;
            var size = (int)(Size.Y * 0.3f);
            var width = font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
            // The number's centre in the art is at (16, 17) of 32.
            var centre = new Vector2(Size.X * 16 / 32, Size.Y * 17 / 32);
            DrawString(font, new Vector2(centre.X - width / 2, centre.Y + size * 0.36f), text, HorizontalAlignment.Left, -1, size, new Color("2a1b04"));
        }
    }
}
