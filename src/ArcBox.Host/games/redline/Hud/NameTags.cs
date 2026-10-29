using ArcBox.Host.Shell;
using Godot;
using Redline.Logic;
using Redline.Presentation.Hud;

namespace ArcBox.Host.Games.Redline.Hud;

/// <summary>
/// Name tags over the player cars only (hud3d.css <c>.tag3d</c>): team bar, position, code and the player name in
/// yellow, plus the round's gain in Reveal. Each frame the cars are projected with <c>Camera3D.UnprojectPosition</c>;
/// <see cref="NameTagLayout"/> hides tags behind the camera, off screen, too far away or under a HUD panel, and stacks
/// overlapping ones.
/// </summary>
public partial class NameTags : Control
{
    /// <summary>Anchor height above the car's ground origin (just over the airbox).</summary>
    public const float AnchorHeight = 1.35f;
    private const int FontSize = 19;
    private const float TagHeight = 32;

    private readonly List<(string Code, string Text, string Name, string? Delta, bool Up, int Position, Color Accent)> _humans = [];
    private IReadOnlyList<TagPlacement> _placed = [];

    public NameTags()
    {
        Name = "NameTags";
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void Show(RedlineHostView view)
    {
        _humans.Clear();
        foreach (var d in view.Drivers.Where(d => d.Player is not null))
        {
            var delta = d.Distance - d.PreviousDistance;
            _humans.Add((d.Code, d.Code, (d.PlayerName ?? "").ToUpperInvariant(),
                view.Phase == RacePhase.Reveal && d.Outcome is not null ? Tower.Signed(delta) : null, delta >= 0,
                view.Phase == RacePhase.DriverSelect ? d.Number : d.Position, HudStyle.Hex(TeamAccent.ById(d.TeamId))));
        }
    }

    /// <summary>Projects and lays out the tags; <paramref name="occluders"/> are HUD panel rects in canvas pixels.</summary>
    public void Update(Camera3D camera, IReadOnlyDictionary<string, Node3D> cars, IReadOnlyList<ScreenRect> occluders)
    {
        var inputs = new List<TagInput>(_humans.Count);
        foreach (var h in _humans)
        {
            if (!cars.TryGetValue(h.Code, out var car) || !car.Visible)
            {
                continue;
            }
            var anchor = car.GlobalPosition + (car.GlobalBasis.Y.Normalized() * AnchorHeight);
            var behind = camera.IsPositionBehind(anchor);
            var p = behind ? Vector2.Zero : camera.UnprojectPosition(anchor);
            inputs.Add(new TagInput(h.Code, p.X, p.Y, behind, camera.GlobalPosition.DistanceTo(anchor), TagWidth(h), TagHeight));
        }
        var size = GetViewportRect().Size;
        _placed = NameTagLayout.Layout(inputs, size.X, size.Y, occluders);
        QueueRedraw();
    }

    public void Clear()
    {
        _placed = [];
        QueueRedraw();
    }

    private float TagWidth((string Code, string Text, string Name, string? Delta, bool Up, int Position, Color Accent) h)
    {
        var w = 4 + 9 + HudStyle.Width(HudStyle.Black, h.Position.ToString(), FontSize) + 7 + HudStyle.Width(HudStyle.Bold, h.Code, FontSize)
            + 8 + HudStyle.Width(ArcTheme.Spaced("Black", 1), h.Name, FontSize) + 12;
        if (h.Delta is { } d)
        {
            w += HudStyle.Width(HudStyle.Black, d, FontSize) + 9;
        }
        return w;
    }

    public override void _Draw()
    {
        foreach (var t in _placed.Where(p => p.Visible))
        {
            var h = _humans.First(x => x.Code == t.Code);
            var r = new Rect2((float)t.Rect.X, (float)t.Rect.Y, (float)t.Rect.Width, (float)t.Rect.Height);
            var stemTop = new Vector2(r.GetCenter().X, r.End.Y);
            var anchor = new Vector2((float)t.AnchorX, (float)t.AnchorY);
            DrawLine(stemTop, new Vector2(stemTop.X, Mathf.Max(stemTop.Y, anchor.Y - 2)), new Color(1, 1, 1, 0.6f), 1.5f);

            DrawRect(r, new Color(14 / 255f, 14 / 255f, 20 / 255f, 0.88f));
            DrawRect(r, new Color(1, 1, 1, 0.18f), false, 1);
            DrawRect(new Rect2(r.Position, new Vector2(4, r.Size.Y)), h.Accent);

            var mid = r.GetCenter().Y;
            var x = r.Position.X + 4 + 9;
            var pos = h.Position.ToString();
            HudStyle.DrawLeft(this, HudStyle.Black, pos, x, mid, FontSize, HudStyle.Dim);
            x += HudStyle.Width(HudStyle.Black, pos, FontSize) + 7;
            HudStyle.DrawLeft(this, HudStyle.Bold, h.Code, x, mid, FontSize, Colors.White);
            x += HudStyle.Width(HudStyle.Bold, h.Code, FontSize) + 8;
            var nameFont = ArcTheme.Spaced("Black", 1);
            HudStyle.DrawLeft(this, nameFont, h.Name, x, mid, FontSize, HudStyle.Player);
            if (h.Delta is { } d)
            {
                x += HudStyle.Width(nameFont, h.Name, FontSize) + 9;
                HudStyle.DrawLeft(this, HudStyle.Black, d, x, mid, FontSize, d == "0" ? HudStyle.Dim : h.Up ? HudStyle.Good : HudStyle.Bad);
            }
        }
    }
}
