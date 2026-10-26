using System.Text.Json;
using ArcBox.Sdk;
using Godot;
using Redline.Logic;
using Redline.Presentation.Demo;
using Redline.Presentation.Hud;

namespace ArcBox.Host.Games.Redline.Demo;

/// <summary>
/// Dev-only <c>--redline-demo[=label]</c>: plays <see cref="DemoRace.Steps"/> into the TV as if the server had sent them,
/// on the scene clock (so <c>--fixed-fps</c> runs are deterministic). With a label (driverselect, grid, throttle,
/// question, reveal, chequered) it starts at that step, sending the step before it first so transitions still have a
/// previous view. Each view is stamped with a deadline like the server's: the phase's full length from when the phase
/// started, measured on the scene clock. Under <c>--fixed-fps</c> the wall clock runs far ahead of the scene, so the
/// stamp is "now + what is left of the phase in scene time", and the HUD's countdown reads naturally in screenshots.
/// </summary>
public partial class RedlineDemoFeeder : Node
{
    private readonly IReadOnlyList<DemoStep> _steps = DemoRace.Steps();
    private readonly Action<JsonElement> _send;
    private int _next;
    private double _wait;
    private double _clock;
    private double _phaseStart;
    private (RacePhase Phase, int Round)? _phase;

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
        _clock += delta;
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
        var phase = (step.View.Phase, step.View.Round);
        if (_phase != phase)
        {
            _phase = phase;
            _phaseStart = _clock;
        }
        var left = Math.Max(0, HudClock.PhaseSeconds(step.View.Phase) - (_clock - _phaseStart));
        var view = step.View with { DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(left) };
        _send(JsonSerializer.SerializeToElement(view, ArcJson.Options));
    }
}
