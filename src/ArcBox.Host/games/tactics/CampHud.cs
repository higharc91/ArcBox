using ArcBox.Host.Shell;
using Godot;
using Tactics.Game;
using Tactics.Presentation.Board;
using Tactics.Presentation.World;

namespace ArcBox.Host.Games.Tactics;

/// <summary>The camp's quiet sky, honest phase counts and compact party ribbon. Phones retain all choices.</summary>
internal sealed partial class CampHud(Func<RecoveryMemberView, Control> card, string root, UnitArt art) : Control
{
    private CampView? _camp;
    private double _seconds;
    private double _clock;
    private Label _title = null!;
    private Label _progress = null!;
    private readonly List<Label> _steps = [];
    private HBoxContainer _ribbon = null!;
    private PanelContainer _caption = null!;
    private Label _result = null!;
    private Texture2D? _hearth;
    private readonly Dictionary<string, Texture2D> _characters = new(StringComparer.Ordinal);

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        TextureFilter = TextureFilterEnum.Nearest;
        var top = ArcTheme.VBox(6);

        _title = ArcTheme.Label("", ArcTheme.Bold, 30, ArcTheme.Gold);
        _progress = ArcTheme.Label("", ArcTheme.SemiBold, 23);
        var timeline = ArcTheme.HBox(25);
        foreach (var text in new[] { "SETTLE IN", "CAMP JOBS", "NIGHT", "DAWN" })
        { var label = ArcTheme.Meta(text); _steps.Add(label); timeline.AddChild(label); }
        top.AddChild(_title); top.AddChild(timeline); top.AddChild(_progress);
        var heading = ArcTheme.Panel(ArcTheme.GlassBox(null, 20, 12), top);
        heading.Position = new Vector2(50, 25);
        AddChild(heading);
        var ribbonPanel = ArcTheme.Panel(ArcTheme.GlassBox(null, 12, 10), _ribbon = ArcTheme.HBox(7));
        ribbonPanel.SetAnchorsPreset(LayoutPreset.BottomWide);
        ribbonPanel.OffsetLeft = 42; ribbonPanel.OffsetRight = -42; ribbonPanel.OffsetTop = -184; ribbonPanel.OffsetBottom = -26;
        AddChild(ribbonPanel);
        _caption = ArcTheme.Panel(ArcTheme.GlassBox(ArcTheme.Gold, 20, 12), _result = ArcTheme.Label("", ArcTheme.SemiBold, 25));
        _result.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _caption.SetAnchorsPreset(LayoutPreset.CenterBottom);
        _caption.OffsetLeft = -520; _caption.OffsetRight = 520; _caption.OffsetTop = -300; _caption.OffsetBottom = -210;
        AddChild(_caption);
        var file = Path.Combine(root, "camp", "hearth.png");
        if (File.Exists(file)) _hearth = ImageTexture.CreateFromImage(Image.LoadFromFile(file));
    }

    public void Show(CampView? camp)
    {
        _camp = camp; Visible = CampScene.Active(camp);
        if (camp is null) return;
        _title.Text = camp.Mode == "inn" ? "THE LAST COAL · A NIGHT BY THE HEARTH" : CampScene.Theme(camp) == "memorial" ? "THE OLD CHAPEL · ASHEN FORD" : "THE EAST ROAD · ASHEN FORD";
        foreach (var child in _ribbon.GetChildren()) { _ribbon.RemoveChild(child); child.QueueFree(); }
        foreach (var member in camp.Members ?? [])
        { var view = card(member); view.SizeFlagsHorizontal = SizeFlags.ExpandFill; _ribbon.AddChild(view); }
        _ribbon.GetParent<Control>().Visible = camp.Stage != "talk";
        _caption.Visible = camp.Stage is "results" or "dawn";
        if (camp.Stage == "results" && camp.Result is { } result)
        {
            var die = Math.Max(result.Die, result.SecondDie ?? result.Die);
            _result.Text = (result.Die > 0 ? $"{result.Skill} · {die} + {result.Bonus} = {die + result.Bonus}\n" : "") + result.Text;
        }
        else _result.Text = "The sun returns. Wounds tended, ready for the road.";
    }
    public void Tick(double seconds, double delta)
    {
        _seconds = seconds; _clock += delta;
        if (_camp is not { } camp) return;
        var phase = camp.Stage == "dawn" ? 3 : camp.Stage is "talk" or "sleep" ? 2 : camp.Stage == "jobs" && seconds < 2 ? 0 : 1;
        for (var i = 0; i < _steps.Count; i++)
        { _steps[i].Modulate = i == phase ? ArcTheme.Gold : new Color(.65f, .68f, .74f); }
        _steps[1].Text = camp.Mode == "inn" ? "TAKING ROOMS" : "CAMP JOBS";
        _progress.Text = CampScene.Phase(camp) + " · " + CampScene.Progress(camp);
        if (camp.Until is { } until && camp.Stage is "jobs" or "rooms")
            _progress.Text += $" · {Math.Max(0, (int)Math.Ceiling((until - DateTimeOffset.UtcNow).TotalSeconds))}s";
        QueueRedraw();
    }
    public override void _Draw()
    {
        if (_camp is not { } camp) return;
        if (camp.Mode == "inn" && _hearth is not null) { DrawInn(camp); return; }
        var dawn = camp.Stage == "dawn" ? (float)Math.Clamp(_seconds / 4.5, 0, 1) : 0;
        var width = Size.X;
        // A soft frame brings the eye to the party while keeping the authored landscape visible.
        for (var i = 0; i < 12; i++)
        {
            var alpha = (1 - i / 12f) * .035f;
            DrawRect(new Rect2(i * 12, 0, 12, Size.Y), new Color(.015f, .02f, .05f, alpha));
            DrawRect(new Rect2(width - (i + 1) * 12, 0, 12, Size.Y), new Color(.015f, .02f, .05f, alpha));
        }
        for (var y = 0; y < 260; y += 8)
        {
            var fade = 1 - y / 260f;
            var colour = new Color(.025f, .04f, .12f, .85f).Lerp(new Color(.52f, .69f, .84f, .6f), dawn);
            colour.A *= fade;
            DrawRect(new Rect2(0, y, width, 8), colour);
        }
        for (var i = 0; i < 35; i++)
        {
            var x = (i * 317 + 79) % Math.Max(1, (int)width);
            var y = (i * 71 + 23) % 150;
            var alpha = (.5f + .2f * MathF.Sin((float)_clock + i)) * (1 - dawn);
            DrawRect(new Rect2(x, y, i % 6 == 0 ? 3 : 2, 2), new Color(.9f, .94f, 1, alpha));
        }
        if (dawn < .7f)
        {
            DrawCircle(new Vector2(width - 160, 95), 30, new Color(.86f, .88f, 1, 1 - dawn));
            DrawCircle(new Vector2(width - 149, 84), 28, new Color(.035f, .05f, .13f, 1 - dawn));
        }
        if (dawn > 0)
        {
            var sun = new Vector2(width - 185, 210 - dawn * 145);
            DrawCircle(sun, 65, new Color(1, .66f, .31f, .12f));
            DrawCircle(sun, 42, new Color(1, .87f, .56f, dawn));
            for (var i = 0; i < 8; i++)
            {
                var angle = i * MathF.PI / 4 + (float)_clock * .02f;
                var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                DrawLine(sun + direction * 51, sun + direction * 65, new Color(1, .87f, .6f, dawn * .65f), 3);
            }
            var warmth = MathF.Sin(dawn * MathF.PI) * .12f;
            DrawRect(new Rect2(Vector2.Zero, Size), new Color(1, .64f, .35f, warmth));
        }
    }
    private void DrawInn(CampView camp)
    {
        var morning = camp.Stage == "dawn" ? (float)Math.Clamp(_seconds / 4.5, 0, 1) : 0;
        DrawTextureRect(_hearth!, new Rect2(Vector2.Zero, Size), false, new Color(1, 1, 1).Lerp(new Color(1.12f, 1.1f, 1), morning));
        var hearthGlow = .035f + .012f * MathF.Sin((float)_clock * 6);
        DrawCircle(new Vector2(Size.X * .13f, Size.Y * .72f), Size.Y * .17f, new Color(1, .62f, .24f, hearthGlow * (1 - morning)));
        if (morning > 0)
        {
            var scale = Size / new Vector2(320, 180);
            foreach (var rect in new[] { new Rect2(68, 31, 28, 31), new Rect2(150, 29, 27, 32) })
                DrawRect(new Rect2(rect.Position * scale, rect.Size * scale), new Color(1, .83f, .49f, morning * .38f));
        }
        if (camp.Members is not { } members) return;
        for (var i = 0; i < members.Length; i++)
        {
            var member = members[i]; var sprite = art.Sprite("characters.class_" + member.Class);
            if (sprite is null) continue;
            if (!_characters.TryGetValue(sprite.Path, out var texture))
            { texture = ImageTexture.CreateFromImage(Image.LoadFromFile(Path.Combine(root, sprite.Path))); _characters[sprite.Path] = texture; }
            var anim = sprite.Anim(camp.Stage == "sleep" ? "doze" : camp.Stage == "dawn" ? "idle" : "sit");
            var frame = anim.FrameAt(_clock + i * .2);
            var x = 500 + i % 3 * 280; var y = 660 + i / 3 * 125;
            var colour = member.Room ? new Color(1, .94f, .8f) : new Color(.85f, .78f, .7f);
            DrawTextureRectRegion(texture, new Rect2(x - sprite.AnchorX * 4, y - sprite.AnchorY * 4, sprite.Width * 4, sprite.Height * 4),
                new Rect2(frame * sprite.Width, i % 2 * sprite.Height, sprite.Width, sprite.Height), colour);
        }
    }
}
