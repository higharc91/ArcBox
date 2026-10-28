using Godot;
using FileAccess = Godot.FileAccess;
using Redline.Presentation.Models;

namespace ArcBox.Host.Games.Redline.World;

/// <summary>
/// Loads the Blender-built <c>.glb</c> models once per process and hands out instances. Each model is tried in the
/// order of <see cref="ModelChoice"/>: Godot's imported scene (loaded on a background thread from <see cref="Prewarm"/>),
/// else the raw <c>.glb</c> through GLTFDocument (a checkout nobody has imported yet), else nothing, and callers draw
/// the procedural mesh. A model that loads but misses part of its naming contract is also refused. Every outcome is
/// logged once per model. Setting the environment variable <c>REDLINE_NO_MODELS=1</c> refuses every model, so the TV
/// draws the procedural meshes (for side-by-side comparisons and to exercise the fallback).
/// </summary>
public static class ModelLibrary
{
    public const string Dir = "res://games/redline/models/";

    private sealed class Entry(string file)
    {
        public readonly string File = file;
        public string Path => Dir + File;
        public ModelSource Source = ModelSource.Procedural;
        public bool Decided;
        public bool Requested;
        public bool Resolved;
        public PackedScene? Scene;
    }

    private static readonly Dictionary<string, Entry> Entries = new(StringComparer.Ordinal);
    private static readonly bool Disabled = System.Environment.GetEnvironmentVariable("REDLINE_NO_MODELS") == "1";

    private static Entry Get(string file)
    {
        if (!Entries.TryGetValue(file, out var e))
        {
            Entries[file] = e = new Entry(file);
        }
        if (!e.Decided)
        {
            e.Decided = true;
            e.Source = Disabled ? ModelSource.Procedural : ModelChoice.Decide(ImportedExists(e.Path), FileAccess.FileExists(e.Path));
        }
        return e;
    }

    /// <summary>Starts loading the imported scenes of <paramref name="files"/> in the background; returns at once.</summary>
    public static void Prewarm(params string[] files)
    {
        foreach (var file in files)
        {
            var e = Get(file);
            if (e.Source == ModelSource.Imported && !e.Requested)
            {
                e.Requested = ResourceLoader.LoadThreadedRequest(e.Path, "PackedScene") == Error.Ok;
            }
        }
    }

    /// <summary>
    /// True unless the model is known to be unusable (no file at all, or a load that already failed): a cheap guess
    /// used to skip building fallback geometry nobody will need.
    /// </summary>
    public static bool LikelyAvailable(string file) => Get(file).Source != ModelSource.Procedural;

    /// <summary>
    /// A new instance of the model, or null when it can't be used (the caller falls back to procedural).
    /// <paramref name="missing"/> checks a first instance against the naming contract: the names it lacks, empty when
    /// it is complete. Blocks until a background load from <see cref="Prewarm"/> finishes.
    /// </summary>
    public static Node3D? Instantiate(string file, Func<Node3D, IReadOnlyList<string>> missing)
    {
        var e = Get(file);
        if (!e.Resolved)
        {
            Resolve(e, missing);
        }
        return e.Source == ModelSource.Procedural ? null : e.Scene!.Instantiate<Node3D>();
    }

    private static void Resolve(Entry e, Func<Node3D, IReadOnlyList<string>> missing)
    {
        e.Resolved = true;
        var rawExists = FileAccess.FileExists(e.Path);
        while (e.Source != ModelSource.Procedural)
        {
            var tried = e.Source;
            var node = tried == ModelSource.Imported ? LoadImported(e) : LoadRuntime(e);
            IReadOnlyList<string> lacks = node is null ? [] : missing(node);
            e.Source = ModelChoice.After(tried, node is not null, lacks, rawExists);
            node?.Free();
            if (e.Source == tried)
            {
                GD.Print($"REDLINE model {e.File}: {(tried == ModelSource.Imported ? "imported scene" : "parsed at runtime (run tools/godot-import.sh to import it)")}");
                return;
            }
            e.Scene = null;
            GD.PushWarning(lacks.Count > 0
                ? $"REDLINE model {e.File} lacks {string.Join(", ", lacks)}; using the procedural mesh"
                : $"REDLINE model {e.File} did not load from {(tried == ModelSource.Imported ? "the imported scene" : "the .glb")}; trying {e.Source}");
        }
        GD.Print($"REDLINE model {e.File}: not available; using the procedural mesh");
    }

    private static Node3D? LoadImported(Entry e)
    {
        Resource? res = e.Requested ? ResourceLoader.LoadThreadedGet(e.Path) : ResourceLoader.Load(e.Path, "PackedScene");
        e.Scene = res as PackedScene;
        return e.Scene?.Instantiate() as Node3D;
    }

    /// <summary>Parses the raw .glb and packs the result into a scene, so instances share its meshes like an import's.</summary>
    private static Node3D? LoadRuntime(Entry e)
    {
        var doc = new GltfDocument();
        var state = new GltfState();
        if (doc.AppendFromFile(ProjectSettings.GlobalizePath(e.Path), state) != Error.Ok || doc.GenerateScene(state) is not Node3D root)
        {
            return null;
        }
        Own(root, root);
        var scene = new PackedScene();
        var packed = scene.Pack(root) == Error.Ok;
        root.Free();
        e.Scene = packed ? scene : null;
        return e.Scene?.Instantiate() as Node3D;

        static void Own(Node n, Node root)
        {
            foreach (var c in n.GetChildren())
            {
                c.Owner = root;
                Own(c, root);
            }
        }
    }

    /// <summary>True when the Godot import artefact named by <c>&lt;path&gt;.import</c> is on disk (or packed).</summary>
    private static bool ImportedExists(string path)
    {
        var importFile = path + ".import";
        if (!FileAccess.FileExists(importFile))
        {
            return false;
        }
        var imported = ModelChoice.ImportedPath(FileAccess.GetFileAsString(importFile));
        return imported is not null && FileAccess.FileExists(imported);
    }

    /// <summary>Every node name under <paramref name="root"/> and every surface material name, for contract checks.</summary>
    public static (List<string> Nodes, List<string> Materials) Names(Node root)
    {
        var nodes = new List<string>();
        var mats = new List<string>();
        Walk(root);
        return (nodes, mats);

        void Walk(Node n)
        {
            nodes.Add(n.Name);
            if (n is MeshInstance3D { Mesh: { } mesh })
            {
                for (var i = 0; i < mesh.GetSurfaceCount(); i++)
                {
                    mats.Add(SurfaceSlot(mesh, i));
                }
            }
            foreach (var c in n.GetChildren())
            {
                Walk(c);
            }
        }
    }

    /// <summary>The material slot name of a surface: its material's resource name, else the surface name.</summary>
    public static string SurfaceSlot(Mesh mesh, int surface)
    {
        var name = mesh.SurfaceGetMaterial(surface)?.ResourceName;
        if (string.IsNullOrEmpty(name) && mesh is ArrayMesh am)
        {
            name = am.SurfaceGetName(surface);
        }
        return name ?? "";
    }
}
