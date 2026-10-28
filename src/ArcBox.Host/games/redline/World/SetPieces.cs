using Godot;
using Redline.Presentation.Models;

namespace ArcBox.Host.Games.Redline.World;

/// <summary>
/// Instances the Blender podium set and landmarks (<see cref="LandmarkModels"/>, <see cref="PodiumModel"/>) through
/// <see cref="ModelLibrary"/>, checked against their naming contracts and with their slots bound
/// (<see cref="SetMaterials"/>). Null means the model can't be used and the caller builds the procedural version.
/// </summary>
public static class SetPieces
{
    /// <summary>Starts loading every landmark and the podium set in the background (safe to call repeatedly).</summary>
    public static void Prewarm() =>
        ModelLibrary.Prewarm([.. LandmarkModels.All.Select(c => c.File), PodiumModel.File]);

    /// <summary>A bound instance of <paramref name="contract"/>'s model, or null (procedural fallback).</summary>
    public static Node3D? Instantiate(ModelContract contract, Color? tint = null, Func<string, Material?>? special = null)
    {
        ArgumentNullException.ThrowIfNull(contract);
        var root = ModelLibrary.Instantiate(contract.File, n =>
        {
            var (nodes, mats) = ModelLibrary.Names(n);
            return contract.Missing(nodes, mats);
        });
        if (root is not null)
        {
            SetMaterials.Bind(root, tint, special);
        }
        return root;
    }

    /// <summary>The first <see cref="MeshInstance3D"/> named <paramref name="name"/> under <paramref name="root"/>.</summary>
    public static MeshInstance3D? Mesh(Node root, string name) => root.FindChild(name, recursive: true, owned: false) as MeshInstance3D;
}
