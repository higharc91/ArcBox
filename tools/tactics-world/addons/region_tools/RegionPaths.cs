using Godot;

namespace Tactics.World.Editor;

/// <summary>Where the workspace finds the content pack, which sits two folders above the project.</summary>
public static class RegionPaths
{
    /// <summary>The base content pack.</summary>
    public static string Content => Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "..", "content", "tactics", "base"));

    /// <summary>The base rules pack, which exports write into.</summary>
    public static string Rules => Path.Combine(Content, "rules");

    /// <summary>The scene of the region with this id. Anything that's already a scene path stays as it is.</summary>
    public static string Scene(string idOrPath) => idOrPath.EndsWith(".tscn", StringComparison.Ordinal) ? idOrPath : $"res://regions/{idOrPath}.tscn";
}
