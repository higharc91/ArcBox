using System.Text.Json;
using ArcBox.Sdk;
using Godot;
using Redline.Presentation.Demo;

namespace ArcBox.Host.Games.Redline.Demo;

/// <summary>
/// Dev-only <c>--redline-demo[=label]</c>: plays <see cref="DemoRace.Steps"/> into the TV as if the server had sent them,
/// on the scene clock (so <c>--fixed-fps</c> runs are deterministic). With a label (driverselect, grid, throttle,
/// question, reveal, chequered) it starts at that step, sending the step before it first so transitions still have a
/// previous view. Each view is stamped with a deadline when it is sent, like the server does.
/// </summary>
public partial class RedlineDemoFeeder : Node
{
    private readonly IReadOnlyList<DemoStep> _steps = DemoRace.Steps();
    private readonly Action<JsonElement> _send;
    private int _next;
    private double _wait;

    public RedlineDemoFeeder()
    {
        _send = _ => { };
    }

    public RedlineDemoFeeder(Action<JsonElement> send, string? from)
    {
        Name = "RedlineDemoFeeder";
        _send = send;
        if (from is not null)
        {
            var index = _steps.ToList().FindIndex(s => s.Label == from);
            if (index < 0)
            {
                GD.PushWarning($"--redline-demo: no step '{from}'; starting at the beginning");
            }
            else if (index > 0)
            {
                Send(_steps[index - 1]);
                _next = index;
            }
        }
    }

    public override void _Process(double delta)
    {
        _wait -= delta;
        if (_wait > 0 || _next >= _steps.Count)
        {
            return;
        }
        var step = _steps[_next++];
        Send(step);
        _wait = step.Seconds;
        if (_next == _steps.Count)
        {
            GD.Print("DEMO end");
        }
    }

    private void Send(DemoStep step)
    {
        GD.Print($"DEMO {step.Label} ({step.View.Phase}, round {step.View.Round})");
        var view = step.View with { DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(step.Seconds) };
        _send(JsonSerializer.SerializeToElement(view, ArcJson.Options));
    }
}
