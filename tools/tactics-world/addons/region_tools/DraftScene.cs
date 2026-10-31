using Godot;
using Tactics.Logic;
using Tactics.WorldTools;

namespace Tactics.World.Editor;

/// <summary>
/// The <c>--draft</c> command: writes Claude's draft of a region into its scene. Painted work is never overwritten, so it does nothing if
/// the scene exists.
/// </summary>
public static class DraftScene
{
    /// <summary>The drafts there are, by region id.</summary>
    private static readonly Dictionary<string, Func<RulesPack, RegionSource>> Drafts = new(StringComparer.Ordinal)
    {
        ["ashen_ford"] = AshenFordDraft.Build,
    };

    /// <summary>Writes the draft of region <paramref name="id"/>. Returns 0 if it did, and 2 if it changed nothing.</summary>
    public static int Run(string id)
    {
        if (!Drafts.TryGetValue(id, out var draft))
        {
            GD.PrintErr($"There's no draft of '{id}'. There are: {string.Join(", ", Drafts.Keys)}.");
            return 2;
        }

        var path = RegionPaths.Scene(id);
        if (File.Exists(ProjectSettings.GlobalizePath(path)))
        {
            GD.PrintErr($"{path} already exists, and nothing has been changed.");
            return 2;
        }

        var root = RegionScene.Build(draft(RulesPack.Load([RegionPaths.Rules])));
        RegionScene.Save(root, path);
        root.Free();
        GD.Print($"Wrote {path}.");
        return 0;
    }
}
