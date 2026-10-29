using ArcBox.Host.Shell;
using Godot;
using Redline.Logic;
using Redline.Presentation;
using Redline.Presentation.Hud;
using NVector2 = System.Numerics.Vector2;

namespace ArcBox.Host.Games.Redline.Hud;

/// <summary>
/// The live track map, top-right (mockup 15 <c>.tmap</c>): the circuit as two <see cref="Line2D"/>s (a soft halo and the
/// line), the start/finish marker, and 22 team-coloured dots read from the car nodes every frame (so they move with the
/// Reveal tween); player dots are larger with a white ring and drawn on top.
/// </summary>
public partial class TrackMap : Control
{
    public const float MapWidth = 456;
    public const float HeadHeight = 34;
    public const float MapHeight = 250;

    private readonly Line2D _halo;
    private readonly Line2D _line;
    private readonly TrackMapDots _dots;
    private TrackMapLayout? _layout;
    private string? _circuitId;
    private string _title = "";

    public TrackMap()
    {
        Name = "TrackMap";
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(MapWidth, HeadHeight + MapHeight);
        Size = CustomMinimumSize;
        _halo = new Line2D { Width = 10, DefaultColor = new Color(1, 1, 1, 0.22f), Closed = true, JointMode = Line2D.LineJointMode.Round, Antialiased = true };
        _line = new Line2D { Width = 3.2f, DefaultColor = new Color("e8e8ee"), Closed = true, JointMode = Line2D.LineJointMode.Round, Antialiased = true };
        _dots = new TrackMapDots { MouseFilter = MouseFilterEnum.Ignore };
        foreach (var n in new Node[] { _halo, _line, _dots })
        {
            AddChild(n);
        }
        _halo.Position = _line.Position = new Vector2(0, HeadHeight);
        _dots.Position = new Vector2(0, HeadHeight);
        _dots.Size = new Vector2(MapWidth, MapHeight);
    }

    public void Show(RedlineHostView view)
    {
        if (_circuitId != view.CircuitId)
        {
            _circuitId = view.CircuitId;
            var circuit = CircuitData.Load(view.CircuitId);
            _layout = new TrackMapLayout(circuit.Points, MapWidth, MapHeight, 22);
            var pts = _layout.Line.Select(p => new Vector2(p.X, p.Y)).ToArray();
            _halo.Points = pts;
            _line.Points = pts;
            _dots.StartLine = _layout.StartLine(9);
            _title = circuit.Name.ToUpperInvariant();
            QueueRedraw();
        }
        _dots.Drivers = [.. view.Drivers.Select(d => (d.Code, HudStyle.Hex(TeamAccent.ById(d.TeamId)), d.Player is not null))];
    }

    /// <summary>Per frame: move the dots to where the cars are.</summary>
    public void Track(IReadOnlyDictionary<string, Node3D> cars)
    {
        if (_layout is null || !Visible)
        {
            return;
        }
        _dots.Positions.Clear();
        foreach (var (code, car) in cars)
        {
            if (car.Visible)
            {
                var p = car.GlobalPosition;
                var m = _layout.Map(new NVector2(p.X, p.Z));
                _dots.Positions[code] = new Vector2(m.X, m.Y);
            }
        }
        _dots.QueueRedraw();
    }

    public override void _Draw()
    {
        HudStyle.DrawPanel(this, new Rect2(Vector2.Zero, Size));
        var head = ArcTheme.Spaced("Bold", 3);
        HudStyle.DrawLeft(this, head, _title, 16, HeadHeight / 2 + 2, 15, HudStyle.Dim);
        var legend = "PLAYER";
        var right = Size.X - 14;
        HudStyle.DrawRight(this, head, legend, right, HeadHeight / 2 + 2, 15, HudStyle.Dim);
        var dot = new Vector2(right - HudStyle.Width(head, legend, 15) - 12, (HeadHeight / 2) + 2);
        DrawCircle(dot, 5.5f, Colors.White);
        DrawCircle(dot, 3.5f, HudStyle.Red);
    }
}

/// <summary>The dots layer of <see cref="TrackMap"/>.</summary>
public partial class TrackMapDots : Control
{
    public Dictionary<string, Vector2> Positions { get; } = new(StringComparer.Ordinal);
    public IReadOnlyList<(string Code, Color Colour, bool Human)> Drivers { get; set; } = [];
    public (NVector2 A, NVector2 B) StartLine { get; set; }

    public override void _Draw()
    {
        var (a, b) = StartLine;
        DrawLine(new Vector2(a.X, a.Y), new Vector2(b.X, b.Y), Colors.White, 3);
        foreach (var (code, colour, human) in Drivers.OrderBy(d => d.Human))
        {
            if (!Positions.TryGetValue(code, out var p))
            {
                continue;
            }
            if (human)
            {
                DrawCircle(p, 9.5f, Colors.White);
                DrawCircle(p, 6.5f, colour);
            }
            else
            {
                DrawCircle(p, 6, new Color("0b0b10"));
                DrawCircle(p, 4.6f, colour);
            }
        }
    }
}
