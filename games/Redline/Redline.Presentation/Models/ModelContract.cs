namespace Redline.Presentation.Models;

/// <summary>
/// The naming contract of one Blender export: the nodes Godot looks up and the material slots it binds, plus the
/// triangle budget <c>tools/blender/check.py</c> and the glb tests hold it to.
/// </summary>
public sealed record ModelContract(string File, int Budget, IReadOnlyList<string> Nodes, IReadOnlyList<string> Slots)
{
    /// <summary>
    /// What a loaded model lacks: required node names, then required material slots (prefixed "material "). Empty means
    /// the model can be used; anything else means the TV draws the procedural version.
    /// </summary>
    public IReadOnlyList<string> Missing(IEnumerable<string> nodeNames, IEnumerable<string> materialNames)
    {
        ArgumentNullException.ThrowIfNull(nodeNames);
        ArgumentNullException.ThrowIfNull(materialNames);
        var nodes = nodeNames.ToHashSet(StringComparer.Ordinal);
        var mats = materialNames.ToHashSet(StringComparer.Ordinal);
        return
        [
            .. Nodes.Where(n => !nodes.Contains(n)),
            .. Slots.Where(m => !mats.Contains(m)).Select(m => $"material {m}"),
        ];
    }
}
