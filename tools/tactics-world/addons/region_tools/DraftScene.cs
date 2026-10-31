using Godot;
using Tactics.Logic;
using Tactics.WorldTools;

namespace Tactics.World.Editor;

/// <summary>
/// The <c>--draft</c> command: writes Claude's draft of a region into its scene. Painted work is never overwritten:
/// <see cref="RegionScene.Save"/> refuses a scene that exists.
/// </summary>
public static class DraftScene
{
    /// <summary>The drafts there are, by region id.</summary>
    private static readonly Dictionary<string, Func<RulesPack, RegionSource>> Drafts = new(StringComparer.Ordinal)
    {
        ["ashen_ford"] = AshenFordDraft.Build,
    };

    /// <summary>Writes the draft of region <paramref name="id"/>. Returns 0 if it did, 2 if there's no such draft, and throws <see cref="SceneExistsException"/> if the scene exists.</summary>
    public static int Run(string id)
    {
        if (!Drafts.TryGetValue(id, out var draft))
        {
            GD.PrintErr($"There's no draft of '{id}'. There are: {string.Join(", ", Drafts.Keys)}.");
            return 2;
        }

        var path = RegionPaths.Scene(id);
        RegionScene.Write(draft(RulesPack.Load([RegionPaths.Rules])), path);
        GD.Print($"Wrote {path}.");
        return 0;
    }
}
