using System.Text.Json;
using System.Text.Json.Nodes;
using ArcBox.Sdk.Protocol;

namespace Meridian.Presentation.Demo;

/// <summary>
/// A recorded game for the TV's demo mode (<c>--meridian-demo</c>; M4 plan, Task 9): host views as the TV received them,
/// one JSON line each, the first whole and the rest as host-view patches (the platform's own format), so a whole game
/// stays small. A test records them from seeded bot games and fails if they no longer match what the game sends.
/// </summary>
public static class DemoViews
{
    public static string Write(IEnumerable<JsonElement> views)
    {
        var lines = new List<string>();
        JsonElement? previous = null;
        foreach (var view in views)
        {
            if (previous is not { } before)
            {
                lines.Add(view.GetRawText());
            }
            else if (HostViewPatch.Diff(before, view) is { } patch)
            {
                lines.Add(patch.ToJsonString());
            }
            else
            {
                continue;   // nothing changed for the TV
            }
            previous = view;
        }
        return string.Join('\n', lines) + "\n";
    }

    public static IEnumerable<JsonElement> Read(string text)
    {
        JsonElement? view = null;
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var json = JsonDocument.Parse(line).RootElement.Clone();
            view = view is { } before ? HostViewPatch.Apply(before, json) : json;
            yield return view.Value;
        }
    }
}
