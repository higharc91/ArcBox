using System.Text.Json;
using ArcBox.Host.Shell;
using ArcBox.Sdk;
using Godot;
using Tactics.Presentation.World;

namespace ArcBox.Host.Games.Tactics;

/// <summary>
/// Dev only, <c>--world-demo=things</c>: the art check for things to interact with. Feeds the Tactics scene <see cref="ThingsDemo"/>'s host
/// views on their own clock, through the normal host-view path, with no server, and with <c>--shots</c> saves each moment the script names
/// (<c>NNN-things-…png</c>), then quits.
/// </summary>
public partial class ThingsDemoFeeder : Node
{
    private readonly Action<JsonElement> _send = _ => { };
    private readonly Action<bool> _captions = _ => { };
    private readonly Screenshotter? _shots;
    private readonly IReadOnlyList<ThingsDemoStep> _steps = [];
    private readonly List<(double At, string Name)> _due = [];
    private double _clock;
    private int _next;

    public ThingsDemoFeeder()
    {
    }

    public ThingsDemoFeeder(Action<JsonElement> send, Action<bool> captions, Screenshotter? shots, IReadOnlyList<ThingsDemoStep>? steps = null,
        bool shop = false, bool camp = false, string? packRoot = null, string campId = "af_road_campfire")
    {
        Name = "ThingsDemoFeeder";
        (_send, _captions, _shots) = (send, captions, shots);
        _steps = steps ?? (camp ? CampDemo.Steps(packRoot, campId) : shop ? ShopDemo.Steps() : ThingsDemo.Steps());
        GD.Print($"THINGS demo: {_steps.Count} views, {_steps.Sum(s => s.Shots.Length)} shots");
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        while (_next < _steps.Count && _steps[_next].At <= _clock)
        {
            var step = _steps[_next++];
            GD.Print($"THINGS demo step {step.Label} at {_clock:0.00} s");
            _captions(step.Captions);
            var view = step.View;
            if (view.Explore?.QuestEvent is { Until: var until, Gifts: { Length: > 0 } gifts } notice && until == DateTimeOffset.MaxValue)
                view = view with { Explore = view.Explore with { QuestEvent = notice with { Until = DateTimeOffset.UtcNow.AddSeconds(gifts.Length * 2.4 + 1) } } };
            _send(JsonSerializer.SerializeToElement(view, ArcJson.Options));
            _due.AddRange(step.Shots.Select(s => (_clock + s.After, s.Name)));
        }
        for (var i = _due.Count - 1; i >= 0; i--)
        {
            if (_due[i].At <= _clock)
            {
                _shots?.Capture(_due[i].Name);
                _due.RemoveAt(i);
            }
        }
        if (_next >= _steps.Count && _due.Count == 0 && _clock > _steps[^1].At + 1)
        {
            GD.Print("THINGS demo done");
            SetProcess(false);
            if (_shots is not null)
            {
                var tree = GetTree();
                tree.CreateTimer(1).Timeout += () =>
                {
                    GetParent().QueueFree(); // save the last shot, then stop the scene's loops before the audio server shuts down
                    tree.CreateTimer(0.1).Timeout += () => tree.Quit();
                };
            }
        }
    }
}
