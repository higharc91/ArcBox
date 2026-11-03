namespace Tactics.Presentation.Story;

/// <summary>
/// Where the story layer goes among the scene's children: just under the fight's caption, so it's over the world and its panels (which the
/// check and the chapter's card dim) and the fight's caption stays on top. Godot's <c>move_child(node, to)</c> takes the node out and puts it
/// back at <c>to</c>, so moving it to the caption's own index when it's already just under it puts it just over it: asked on every view,
/// that flips the two each time and reorders the canvas. This says where to move it, and only when it's out of place.
/// </summary>
public static class StoryOrder
{
    /// <summary>The index to move the story at <paramref name="story"/> to, or null when it's just under the caption at <paramref name="caption"/>.</summary>
    public static int? Move(int story, int caption)
    {
        var target = story < caption ? caption - 1 : caption;     // the caption shifts down one as a story under it is taken out
        return story == target ? null : target;
    }
}
