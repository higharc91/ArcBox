using System.Text.Json;
using Godot;
using Meridian.Presentation.Demo;

namespace ArcBox.Host.Games.Meridian;

/// <summary>
/// Dev only (<c>--meridian-demo[=&lt;scenario&gt;]</c>; M4 plan, Task 9): plays a recorded bot game through the normal
/// host-view path with no server. A view presenting an event waits for the scene's <c>presented</c> signal for it (or
/// that event's own fallback, as the server would); any other view follows after a short step, at about a bot's pace.
/// </summary>
public partial class MeridianDemoFeeder : Node
{
    private const double Step = 0.35;

    private readonly IReadOnlyList<JsonElement> _views = [];
    private readonly Action<JsonElement> _send = _ => { };
    private int _next;
    private double _wait;
    private int _awaiting;

    public MeridianDemoFeeder()
    {
    }

    public MeridianDemoFeeder(string scenario, Action<JsonElement> send)
    {
        Name = "MeridianDemoFeeder";
        _send = send;
        var path = ProjectSettings.GlobalizePath($"res://games/meridian/Demo/{scenario}.jsonl");
        if (!File.Exists(path))
        {
            GD.PushError($"--meridian-demo: no recording {path}");
            return;
        }
        _views = [.. DemoViews.Read(File.ReadAllText(path))];
        GD.Print($"DEMO meridian {scenario}: {_views.Count} views");
    }

    /// <summary>The scene's signals: a <c>presented</c> for the event on screen moves the demo on.</summary>
    public void Signal(string kind, object payload)
    {
        if (kind == "presented" && JsonSerializer.SerializeToElement(payload).GetProperty("seq").GetInt32() == _awaiting)
        {
            _awaiting = 0;
            _wait = 0;
        }
    }

    public override void _Process(double delta)
    {
        if (_next >= _views.Count)
        {
            return;
        }
        _wait -= delta;
        if (_wait > 0)
        {
            return;
        }
        var view = _views[_next++];
        _send(view);
        if (view.TryGetProperty("now", out var now) && now.ValueKind == JsonValueKind.Object)
        {
            _awaiting = now.GetProperty("seq").GetInt32();
            _wait = now.GetProperty("seconds").GetDouble();   // the server's fallback, if the scene never signals
        }
        else
        {
            _awaiting = 0;
            _wait = Step;
        }
        if (_next == _views.Count)
        {
            GD.Print("DEMO meridian: the end");
        }
    }
}
