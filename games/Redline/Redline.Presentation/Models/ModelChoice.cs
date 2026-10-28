namespace Redline.Presentation.Models;

/// <summary>Where a Blender model comes from on the TV.</summary>
public enum ModelSource
{
    /// <summary>Godot's imported scene (<c>.godot/imported/*.scn</c>, made by the editor or <c>--headless --import</c>).</summary>
    Imported,

    /// <summary>The raw <c>.glb</c> parsed at runtime with GLTFDocument (a checkout nobody has imported yet; slower).</summary>
    Runtime,

    /// <summary>The model is unusable: draw the procedural C# mesh instead (spec §1: no regression).</summary>
    Procedural,
}

/// <summary>
/// The fallback rules for Blender models (plan Review Focus 1): use the imported scene when its artefact exists; else
/// parse the raw .glb; else, or when a load fails or the model misses part of its naming contract, the procedural mesh.
/// </summary>
public static class ModelChoice
{
    /// <summary>The first source to try.</summary>
    public static ModelSource Decide(bool importedExists, bool rawExists) =>
        importedExists ? ModelSource.Imported : rawExists ? ModelSource.Runtime : ModelSource.Procedural;

    /// <summary>
    /// The source to use after trying <paramref name="tried"/>: the same one when it loaded and has nothing
    /// <paramref name="missing"/>; otherwise the next one down (an imported scene that won't load falls back to the
    /// raw file when there is one; a model missing names goes straight to procedural, since the raw file is the same).
    /// </summary>
    public static ModelSource After(ModelSource tried, bool loaded, IReadOnlyList<string> missing, bool rawExists)
    {
        ArgumentNullException.ThrowIfNull(missing);
        if (tried == ModelSource.Procedural || (loaded && missing.Count == 0))
            return tried;
        if (!loaded && tried == ModelSource.Imported && rawExists)
            return ModelSource.Runtime;
        return ModelSource.Procedural;
    }

    /// <summary>The imported artefact (<c>path="res://.godot/imported/…"</c>) named by a Godot <c>.import</c> file, or null.</summary>
    public static string? ImportedPath(string importFile)
    {
        ArgumentNullException.ThrowIfNull(importFile);
        foreach (var raw in importFile.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("path=", StringComparison.Ordinal) || line.StartsWith("path.", StringComparison.Ordinal))
            {
                var q0 = line.IndexOf('"', StringComparison.Ordinal);
                var q1 = line.LastIndexOf('"');
                if (q0 >= 0 && q1 > q0)
                    return line[(q0 + 1)..q1];
            }
        }
        return null;
    }
}
