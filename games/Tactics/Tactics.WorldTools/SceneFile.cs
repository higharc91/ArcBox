namespace Tactics.WorldTools;

/// <summary>A scene file is already there, and a save that didn't ask to replace it stopped before touching it.</summary>
public sealed class SceneExistsException(string path) : IOException($"{path} already exists, and nothing has been changed.");

/// <summary>The rule that painted work is never overwritten, for whatever saves a region scene.</summary>
public static class SceneFile
{
    /// <summary>Throws <see cref="SceneExistsException"/> if <paramref name="path"/> exists, unless the caller says it means to replace it.</summary>
    public static void Refuse(string path, bool overwrite)
    {
        if (!overwrite && File.Exists(path))
            throw new SceneExistsException(path);
    }
}
