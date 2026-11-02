namespace Tactics.Presentation.World;

/// <summary>Props rendered as moving actors in the TV overworld, rather than baked into a cached terrain block.</summary>
public static class WorldAnimalIds
{
    private static readonly HashSet<string> Ids = new(StringComparer.Ordinal)
    {
        "farm_goat", "pack_mule", "field_crow", "river_duck", "red_fox",
    };

    /// <summary>Whether a prop is one of the overworld's living creatures.</summary>
    public static bool Contains(string id) => Ids.Contains(id);

    /// <summary>The manifest family that holds the creature's animated overworld strip.</summary>
    public static string AnimationFamily(string id) => id + "_anim";
}
