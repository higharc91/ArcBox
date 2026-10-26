using System.Text.Json;

namespace ArcBox.Host.Shell;

/// <summary>
/// A game's TV scene (platform §6.2). The shell is game-agnostic, so views arrive as JSON; each scene deserializes
/// its own view type with <c>ArcJson.Options</c>. Scenes only animate between views and never decide outcomes.
/// </summary>
public interface IGameScene
{
    /// <summary>
    /// Called on the main thread with the newest view only (views that arrive in the same frame are coalesced),
    /// after the scene has entered the tree. <paramref name="previous"/> is the view rendered before this one
    /// in the same scene instance, or null for the first.
    /// </summary>
    void Render(JsonElement view, JsonElement? previous);

    /// <summary>Called once before the first <see cref="Render"/>; null when <c>--shots</c> is off.</summary>
    void SetShots(Screenshotter? shots);
}
