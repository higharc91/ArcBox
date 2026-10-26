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

/// <summary>
/// A scene that draws in 3D with the render presets: gets the preset name (<c>ultra</c>, <c>high</c>, <c>low</c>) before
/// its first <see cref="IGameScene.Render"/>, and again whenever F2 changes it.
/// </summary>
public interface IRenderPresetScene
{
    void SetRenderPreset(string preset);
}

/// <summary>
/// A scene the lobby can get ready while the game is only selected: <see cref="Preload"/> is called with the scene in
/// the tree (drawn behind the lobby, which covers it), so slow first-time work (building a 3D world, compiling its
/// shaders and pipelines on the first frame that draws it) happens before the game starts. Once
/// <see cref="PreloadDone"/>, the scene stops drawing but stays in the tree (leaving it would recreate its render
/// instances); when the game starts the shell shows that same instance and calls <see cref="EndPreload"/> before the
/// first <see cref="IGameScene.Render"/>. When a game ends, the shell keeps its scene the same way with
/// <see cref="Park"/> (ready at once) for as long as that game stays selected, instead of building a new one.
/// </summary>
public interface IPreloadScene
{
    void Preload();

    /// <summary>The game on screen is over: stop drawing and be ready to start again (<see cref="PreloadDone"/>).</summary>
    void Park();

    bool PreloadDone { get; }

    void EndPreload();
}
