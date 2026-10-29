using ArcBox.Host.Games.Redline.World;
using Godot;
using Redline.Presentation.Models;

namespace ArcBox.Host.Games.Redline.Podium;

/// <summary>
/// The Blender podium set (<see cref="PodiumModel"/>, <c>models/podium.glb</c>): stage, stepped blocks with inset
/// numerals, the LED media wall (its <c>screen</c> slot shows <see cref="PodiumBackdrop"/>'s artwork), truss frame with
/// lamps and light bars, branded panels, stairs and confetti cannons, in <see cref="PodiumSet"/>'s local frame.
/// <see cref="PodiumSet"/> uses it in place of its procedural structure, lamps, backdrop and numerals when it loads.
/// </summary>
public static class PodiumStage
{
    /// <summary>The bound set, or null when the model can't be used (the caller builds the procedural set).</summary>
    public static Node3D? TryCreate(Node host)
    {
        StandardMaterial3D? screen = null;
        var stage = SetPieces.Instantiate(PodiumModel.Contract, special: slot =>
        {
            if (slot != PodiumModel.Screen)
            {
                return null;
            }
            if (screen is null)
            {
                // An LED wall: it shows its picture whatever the sun does.
                screen = new StandardMaterial3D
                {
                    AlbedoColor = new Color(0.92f, 0.92f, 0.92f),
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
                };
                PodiumBackdrop.Apply(host, screen);
            }
            return screen;
        });
        if (stage is not null)
        {
            stage.Name = "Stage";
        }
        return stage;
    }

    /// <summary>The header cannons' muzzles and aims in the set's frame: where <see cref="Confetti"/> fires from.</summary>
    public static (Vector3 At, Vector3 Aim)[] Cannons(Node3D stage) =>
        [.. new[] { PodiumModel.ConfettiLeft, PodiumModel.ConfettiRight }
            .Select(n => stage.FindChild(n, recursive: true, owned: false) as Node3D)
            .Where(n => n is not null)
            .Select(n => (n!.Transform.Origin, n.Transform.Basis.Z.Normalized()))];
}
