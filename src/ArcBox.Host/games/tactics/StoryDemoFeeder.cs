using System.Text.Json;
using ArcBox.Sdk;
using Godot;
using Tactics.Logic;
using Tactics.Presentation.Story;

namespace ArcBox.Host.Games.Tactics;

/// <summary>
/// Dev only, <c>--story-demo[=&lt;step&gt;]</c>: feeds the Tactics scene <see cref="StoryDemo"/>'s host views on their own clock, through the
/// normal host-view path, with no server. A step names where to start (<see cref="StoryDemo.Labels"/>); the views before it are skipped,
/// but the one just before is sent, so the TV joins mid-story as a TV would, and the deadlines stay where the script has them.
/// </summary>
public partial class StoryDemoFeeder : Node
{
    private readonly Action<JsonElement> _send = _ => { };
    private readonly IReadOnlyList<StoryDemoStep> _steps = [];
    private readonly double _offset;
    private double _clock;
    private int _next;

    public StoryDemoFeeder()
    {
    }

    public StoryDemoFeeder(string packRoot, string? from, Action<JsonElement> send)
    {
        Name = "StoryDemoFeeder";
        _send = send;
        var lore = LoreBook.Load(packRoot);
        var start = DateTimeOffset.UtcNow;
        var steps = StoryDemo.Steps(lore, start);
        var index = string.IsNullOrEmpty(from) ? 0 : steps.ToList().FindIndex(s => s.Label == from);
        if (index < 0)
        {
            GD.PushWarning($"--story-demo: no step '{from}' (try {string.Join(", ", StoryDemo.Labels)}); starting at the beginning");
            index = 0;
        }
        _offset = steps[index].At;
        steps = StoryDemo.Steps(lore, start - TimeSpan.FromSeconds(_offset));     // the deadlines, as the script has them from here
        _steps = steps;
        _next = index;
        if (index > 0)
        {
            Send(steps[index - 1]);
        }
        GD.Print($"STORY demo from '{steps[index].Label}', {steps.Length - index} views");
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        while (_next < _steps.Count && _steps[_next].At - _offset <= _clock)
        {
            Send(_steps[_next++]);
        }
    }

    private void Send(StoryDemoStep step)
    {
        if (step.Label.Length > 0)
        {
            GD.Print($"STORY demo step {step.Label}");
        }
        _send(JsonSerializer.SerializeToElement(step.View, ArcJson.Options));
    }
}
