using Godot;
using Redline.Logic;

namespace ArcBox.Host.Games.Redline.Hud;

/// <summary>
/// The broadcast HUD layer over the 3D race (plan Task 6). <see cref="RedlineScene"/> owns one, calls
/// <see cref="Render"/> with every new host view and <see cref="UpdateTags"/> every frame with the live camera and cars.
/// This is the hook only: the timing tower, question panel, track map, name tags, Driver Select board, grid strip, round
/// result and Chequered screens are built in Task 6.
/// </summary>
public partial class RedlineHud : CanvasLayer
{
    public RedlineHud()
    {
        Name = "RedlineHud";
        Layer = 10;
    }

    /// <summary>A new host view arrived (<paramref name="previous"/> is the one rendered before it, if any).</summary>
    public virtual void Render(RedlineHostView view, RedlineHostView? previous)
    {
    }

    /// <summary>Per frame: project name tags for the player cars through <paramref name="camera"/>.</summary>
    public virtual void UpdateTags(Camera3D camera, IReadOnlyDictionary<string, Node3D> cars)
    {
    }
}
