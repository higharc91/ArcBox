using ArcBox.Host.Audio;
using Godot;

namespace ArcBox.Host.Shell;

/// <summary>
/// The TV's top layer above every screen: the F3 performance readout (fps, frame time, draw calls, primitives, and the
/// narrator's voice and last line) in the top-left corner, and a short toast at the top centre (F2's preset name, M's
/// mute). Draws only; <see cref="Main"/> owns the keys.
/// </summary>
public partial class HostOverlay : CanvasLayer
{
    private const double ToastSeconds = 1.8;
    private const double ToastFade = 0.4;
    private const double PerfRefresh = 0.25;
    private const double PerfLogEvery = 5;

    private PanelContainer _perf = null!;
    private Label _perfText = null!;
    private PanelContainer _toast = null!;
    private Label _toastText = null!;
    private double _toastLeft;

    // Frame times over the last refresh window.
    private double _sum;
    private double _worst;
    private int _frames;
    private double _sinceRefresh;
    private double _sinceLog;

    /// <summary>The preset name the readout shows.</summary>
    public string Preset { get; set; } = "";

    public bool PerfVisible => _perf.Visible;

    /// <summary>The shell's narrator, whose state (Google / Fallback / Off) and last line the readout shows.</summary>
    public Narrator? Narrator { get; set; }

    /// <summary>Also print the readout to the log every few seconds while it is on (<c>--perf</c>).</summary>
    public bool LogPerf { get; set; }

    public override void _Ready()
    {
        Layer = 100;
        Name = "HostOverlay";

        _perfText = ArcTheme.Label("", ArcTheme.SemiBold, 18);
        _perf = ArcTheme.Panel(ArcTheme.GlassBox(ArcTheme.Gold, 14, 8), _perfText);
        _perf.Position = new Vector2(12, 12);
        _perf.MouseFilter = Control.MouseFilterEnum.Ignore;
        _perf.Visible = false;
        AddChild(_perf);

        _toastText = ArcTheme.Label("", ArcTheme.Spaced("Black", 4), 30);
        _toast = ArcTheme.Panel(ArcTheme.GlassBox(ArcTheme.Red, 28, 12), _toastText);
        _toast.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop);
        _toast.GrowHorizontal = Control.GrowDirection.Both;
        _toast.OffsetTop = 170;   // below the REDLINE header (1920×1080 base)
        _toast.MouseFilter = Control.MouseFilterEnum.Ignore;
        _toast.Visible = false;
        AddChild(_toast);
    }

    public void TogglePerf()
    {
        _perf.Visible = !_perf.Visible;
        _sum = _worst = _sinceRefresh = 0;
        _frames = 0;
        _perfText.Text = "measuring…";
    }

    /// <summary>Shows <paramref name="text"/> for a moment, replacing any toast on screen.</summary>
    public void Toast(string text)
    {
        _toastText.Text = text;
        _toast.Visible = true;
        _toast.Modulate = Colors.White;
        _toastLeft = ToastSeconds;
    }

    public override void _Process(double delta)
    {
        if (_toast.Visible)
        {
            _toastLeft -= delta;
            _toast.Modulate = new Color(1, 1, 1, (float)Math.Clamp(_toastLeft / ToastFade, 0, 1));
            _toast.Visible = _toastLeft > 0;
        }

        if (!_perf.Visible)
        {
            return;
        }
        _sinceLog += delta;
        _sum += delta;
        _worst = Math.Max(_worst, delta);
        _frames++;
        _sinceRefresh += delta;
        if (_sinceRefresh < PerfRefresh)
        {
            return;
        }

        var avgMs = _sum / _frames * 1000;
        var calls = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
        var prims = Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame);
        var size = GetViewport().GetVisibleRect().Size;
        var window = GetWindow().Size;
        _perfText.Text =
            $"{1000 / avgMs:0} fps   {avgMs:0.0} ms (worst {_worst * 1000:0.0})\n" +
            $"{calls:N0} draw calls   {Primitives(prims)} primitives\n" +
            $"{Preset.ToUpperInvariant()} · {window.X}×{window.Y}" + (size.X != window.X ? $" (UI {size.X:0}×{size.Y:0})" : "") +
            (Narrator is { } n ? $"\nVOICE {n.State.ToString().ToUpperInvariant()}{(AudioBuses.Levels.Muted ? " · MUTED" : "")}{LastLine(n.LastLine)}" : "");
        if (LogPerf && _sinceLog >= PerfLogEvery)
        {
            _sinceLog = 0;
            GD.Print($"PERF {_perfText.Text.Replace("\n", " | ")}");
        }
        _sum = _worst = _sinceRefresh = 0;
        _frames = 0;
    }

    private static string LastLine(string? line) =>
        line is null ? "" : $" · “{(line.Length > 44 ? line[..44] + "…" : line)}”";

    private static string Primitives(double n) => n >= 1e6 ? $"{n / 1e6:0.00} M" : n >= 1e3 ? $"{n / 1e3:0.0} k" : $"{n:0}";
}
