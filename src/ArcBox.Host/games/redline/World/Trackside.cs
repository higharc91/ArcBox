using System.Diagnostics;
using ArcBox.Host.Shell;
using Godot;
using Redline.Presentation.Course;
using Redline.Presentation.Meshes;
using Redline.Presentation.Models;
using Plan = Redline.Presentation.Course.Trackside;

namespace ArcBox.Host.Games.Redline.World;

/// <summary>
/// The barriers, catch fence and trackside furniture (COTA upgrade spec §2.3) from <see cref="Plan.Plan"/>: TecPro blocks,
/// tyre walls and armco posts along the barrier line, the catch fence's posts, marshal posts with numbered huts, corner
/// boards T1–T20, brake boards 300/200/100, TV camera platforms and the big screen at the main grandstand. Props are the
/// Blender models (<see cref="TracksideModels"/>) as MultiMeshes, split into <see cref="Chunk"/>-metre cells so each cell
/// is culled on its own and the barrier blocks switch to their LOD mesh beyond <see cref="LodDistance"/>; procedural
/// stand-ins replace a model that can't be used. The armco rails, the fence's cables and its mesh are track-following
/// meshes from <c>TracksideMeshes</c>. Numbers come from one atlas (<see cref="TracksideAtlas"/>) drawn once and read per
/// instance. The big screen shows the broadcast picture: a small <see cref="SubViewport"/> renders the current camera's
/// view (without the screen itself) every few frames while the screen is in shot; the Low preset shows a static REDLINE
/// card instead, draws every other armco and fence post and the barrier blocks' LOD mesh at every distance
/// (<see cref="SetPreset"/>). <c>REDLINE_NO_TRACKSIDE=1</c> leaves all of it out (comparison shots).
/// </summary>
public static partial class Trackside
{
    public const string NodeName = "Trackside";
    /// <summary>Cells for culling and LOD (metres).</summary>
    public const float Chunk = 100;
    /// <summary>Barrier blocks within this distance of a cell's middle draw the full model, beyond it the LOD mesh.</summary>
    public const float LodDistance = 140;
    /// <summary>The big screen's face is on render layer 20 only, which its own feed camera does not see.</summary>
    public const uint ScreenLayer = 1u << 19;
    /// <summary>The feed's resolution (16:9) and how often it renders (every Nth frame).</summary>
    public static readonly Vector2I FeedSize = new(640, 360);
    public const int FeedEvery = 3;

    private sealed record Built(TracksidePlan Plan, ArrayMesh Rails, ArrayMesh Cables);

    private static readonly Dictionary<string, Built> Cache = new(StringComparer.Ordinal);
    private static readonly bool Disabled = System.Environment.GetEnvironmentVariable("REDLINE_NO_TRACKSIDE") == "1";

    /// <summary>Starts loading the trackside models in the background (safe to call repeatedly).</summary>
    public static void Prewarm() => ModelLibrary.Prewarm([.. TracksideModels.All.Select(c => c.File)]);

    /// <summary>Builds the trackside under one node and puts the catch fence's mesh material on <paramref name="fence"/>.</summary>
    public static Node3D Build(CircuitAssets a, MeshInstance3D fence)
    {
        var root = new Node3D { Name = NodeName };
        if (Disabled)
        {
            return root;
        }
        Prewarm();
        var b = Get(a);
        var plan = b.Plan;
        fence.MaterialOverride = FenceMesh;

        var barriers = Variants(TracksideModels.Barriers);
        Mesh(barriers, "tecpro_block", out var tecpro, out var tecproLod);
        Mesh(barriers, "tyre_module", out var tyre, out var tyreLod);
        root.AddChild(Chunked("TecPro", plan.TecPro, tecpro, tecproLod, p => TecProColour((int)p.Shade), shadows: true, lod: true));
        root.AddChild(Chunked("Tyres", plan.Tyres, tyre, tyreLod, _ => Colors.White, shadows: true, lod: true));
        root.AddChild(Chunked("ArmcoPosts", plan.ArmcoPosts, barriers["armco_post"], null, _ => Colors.White, shadows: false, end: 450, half: true));
        root.AddChild(Chunked("FencePosts", [.. plan.FencePosts.Where(p => p.Variant == 0)], barriers["fence_post"], null, _ => Colors.White, shadows: false, end: 600, half: true));
        root.AddChild(Chunked("FencePostsOverhang", [.. plan.FencePosts.Where(p => p.Variant == 1)], barriers["fence_post_overhang"], null, _ => Colors.White,
            shadows: false, end: 600, half: true, xf: p => Xf(p, new Vector3(1, 1, p.Shade))));
        root.AddChild(Surface("ArmcoRails", b.Rails, Slot("galvanised_rail")!, shadows: true));
        root.AddChild(Surface("FenceCables", b.Cables, Slot("cable")!, shadows: false));

        var furniture = Variants(TracksideModels.Furniture);
        root.AddChild(Signs("MarshalPosts", plan.MarshalPosts, furniture["marshal_post"]));
        root.AddChild(Signs("CornerBoards", plan.CornerBoards, furniture["corner_board"]));
        root.AddChild(Signs("BrakeBoards", plan.BrakeBoards, furniture["brake_board"]));
        root.AddChild(Chunked("CameraPlatforms", plan.CameraPlatforms, furniture["camera_platform"], null, _ => Colors.White, shadows: true));
        root.AddChild(BigScreen(plan.BigScreen));
        Textures.Ensure(root);
        return root;
    }

    /// <summary>
    /// Low: every other armco and fence post, the barrier blocks' LOD mesh at every distance and the static card on the big
    /// screen; otherwise everything, LOD by distance and the live feed.
    /// </summary>
    public static void SetPreset(Node world, Preset preset)
    {
        if (world.FindChild(NodeName, recursive: true, owned: false) is not Node3D root)
        {
            return;
        }
        var low = preset == Preset.Low;
        foreach (var group in root.GetChildren().OfType<Node3D>())
        {
            foreach (var mmi in group.FindChildren("*", "MultiMeshInstance3D", true, false).OfType<MultiMeshInstance3D>())
            {
                if (group.HasMeta("half"))
                {
                    var n = mmi.Multimesh.InstanceCount;
                    mmi.Multimesh.VisibleInstanceCount = low ? (n + 1) / 2 : -1;
                }
                if (mmi.HasMeta("lod"))
                {
                    var near = mmi.GetMeta("lod").AsBool();
                    // Low: the near model is never drawn and the LOD mesh is drawn from 0 m.
                    mmi.Visible = !(low && near);
                    if (!near)
                    {
                        mmi.VisibilityRangeBegin = low ? 0 : LodDistance;
                    }
                }
            }
        }
        if (root.FindChild(ScreenFeed.NodeName, recursive: true, owned: false) is ScreenFeed feed)
        {
            feed.Live = !low;
        }
    }

    private static Built Get(CircuitAssets a)
    {
        var key = a.Circuit.Name;
        if (Cache.TryGetValue(key, out var cached))
        {
            return cached;
        }
        var sw = Stopwatch.StartNew();
        var site = new TracksideSite(a.Samples, a.Track, a.Ground, a.Circuit.LapLength, Plan.Structures(a.Circuit, a.Samples));
        var plan = Plan.Plan(site);
        var built = new Built(plan, MeshConvert.ToArrayMesh(plan.Rails), MeshConvert.ToArrayMesh(plan.Cables));
        GD.Print($"REDLINE trackside planned in {sw.ElapsedMilliseconds} ms: {plan.TecPro.Count} TecPro blocks, {plan.Tyres.Count} tyre modules, "
            + $"{plan.ArmcoPosts.Count} armco posts, {plan.FencePosts.Count} fence posts, {plan.MarshalPosts.Count} marshal posts, "
            + $"{plan.CornerBoards.Count} corner boards, {plan.BrakeBoards.Count} brake boards, {plan.CameraPlatforms.Count} camera platforms");
        Cache[key] = built;
        return built;
    }

    // ---- instancing ---------------------------------------------------------------------------------------

    private static Transform3D Xf(Prop p, Vector3 scale) =>
        new(new Basis(Vector3.Up, p.Yaw).Scaled(scale * p.Scale), MeshConvert.V(p.Position));

    private static void Mesh(IReadOnlyDictionary<string, ArrayMesh> meshes, string name, out ArrayMesh near, out ArrayMesh far)
    {
        near = meshes[name];
        far = meshes[name + "_lod"];
    }

    /// <summary>
    /// The props as MultiMeshes in <see cref="Chunk"/>-metre cells (each cell's node at its middle, so culling and
    /// visibility ranges work per cell). With <paramref name="far"/> each cell has a near and a far MultiMesh split at
    /// <see cref="LodDistance"/>; <paramref name="end"/> hides a cell beyond that distance. Plan order is kept inside a cell,
    /// so the first half of a cell's instances (Low, <paramref name="half"/>) is every other prop.
    /// </summary>
    private static Node3D Chunked(string name, IReadOnlyList<Prop> props, ArrayMesh near, ArrayMesh? far, Func<Prop, Color> colour,
        bool shadows, bool lod = false, float end = 0, bool half = false, Func<Prop, Transform3D>? xf = null)
    {
        var node = new Node3D { Name = name };
        if (half)
        {
            node.SetMeta("half", true);
        }
        xf ??= p => Xf(p, Vector3.One);
        var cells = props.GroupBy(p => ((int)MathF.Floor(p.Position.X / Chunk), (int)MathF.Floor(p.Position.Z / Chunk)));
        foreach (var cell in cells.OrderBy(c => c.Key.Item1).ThenBy(c => c.Key.Item2))
        {
            var list = cell.ToList();
            var centre = list.Aggregate(Vector3.Zero, (s, p) => s + MeshConvert.V(p.Position)) / list.Count;
            var mm = Multi(list, near, colour, p => xf(p).Translated(-centre));
            var mmi = Instance($"{name}_{cell.Key.Item1}_{cell.Key.Item2}", mm, centre, shadows);
            if (far is not null && lod)
            {
                mmi.VisibilityRangeEnd = LodDistance;
                mmi.SetMeta("lod", true);
                node.AddChild(mmi);
                var lodMmi = Instance(mmi.Name + "_lod", Multi(list, far, colour, p => xf(p).Translated(-centre)), centre, shadows);
                lodMmi.VisibilityRangeBegin = LodDistance;
                lodMmi.SetMeta("lod", false);
                node.AddChild(lodMmi);
                continue;
            }
            if (end > 0)
            {
                mmi.VisibilityRangeEnd = end;
            }
            node.AddChild(mmi);
        }
        return node;
    }

    private static MultiMesh Multi(IReadOnlyList<Prop> list, ArrayMesh mesh, Func<Prop, Color> colour, Func<Prop, Transform3D> xf)
    {
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = mesh,
            InstanceCount = list.Count,
        };
        for (var i = 0; i < list.Count; i++)
        {
            mm.SetInstanceTransform(i, xf(list[i]));
            mm.SetInstanceColor(i, colour(list[i]));
        }
        return mm;
    }

    private static MultiMeshInstance3D Instance(string name, MultiMesh mm, Vector3 at, bool shadows) => new()
    {
        Name = name,
        Multimesh = mm,
        Position = at,
        CastShadow = shadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
    };

    /// <summary>Numbered furniture: one MultiMesh, each instance's atlas cell in its custom data (read by the number face).</summary>
    private static MultiMeshInstance3D Signs(string name, IReadOnlyList<Sign> signs, ArrayMesh mesh)
    {
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseCustomData = true,
            Mesh = mesh,
            InstanceCount = signs.Count,
        };
        for (var i = 0; i < signs.Count; i++)
        {
            mm.SetInstanceTransform(i, Xf(signs[i].Prop, Vector3.One));
            mm.SetInstanceCustomData(i, new Color(signs[i].Cell, 0, 0, 0));
        }
        return new MultiMeshInstance3D { Name = name, Multimesh = mm };
    }

    private static MeshInstance3D Surface(string name, ArrayMesh mesh, Material material, bool shadows) => new()
    {
        Name = name,
        Mesh = mesh,
        MaterialOverride = material,
        CastShadow = shadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
    };

    private static readonly Dictionary<string, IReadOnlyDictionary<string, ArrayMesh>> VariantCache = new(StringComparer.Ordinal);

    /// <summary>Each node's mesh with its slots' materials on the mesh (a MultiMesh ignores overrides), or the stand-ins.</summary>
    private static IReadOnlyDictionary<string, ArrayMesh> Variants(ModelContract contract)
    {
        if (VariantCache.TryGetValue(contract.File, out var cached))
        {
            return cached;
        }
        IReadOnlyDictionary<string, ArrayMesh>? result = null;
        if (SetPieces.Instantiate(contract) is { } root)
        {
            var meshes = contract.Nodes.ToDictionary(n => n, n => SetPieces.Mesh(root, n)?.Mesh as ArrayMesh, StringComparer.Ordinal);
            root.Free();
            if (meshes.Values.All(m => m is not null))
            {
                foreach (var m in meshes.Values)
                {
                    for (var i = 0; i < m!.GetSurfaceCount(); i++)
                    {
                        if (Slot(ModelLibrary.SurfaceSlot(m, i)) is { } mat)
                        {
                            m.SurfaceSetMaterial(i, mat);
                        }
                    }
                }
                result = meshes.ToDictionary(kv => kv.Key, kv => kv.Value!, StringComparer.Ordinal);
            }
        }
        result ??= Fallbacks(contract);
        VariantCache[contract.File] = result;
        return result;
    }

    /// <summary>Simple stand-ins when a trackside model can't be used (same frames: X along, -Z towards the track).</summary>
    private static Dictionary<string, ArrayMesh> Fallbacks(ModelContract contract)
    {
        var white = new Color("eeeeea");
        var grey = new Color("a9adb2");
        var dark = new Color("1a1b1e");
        var mat = Materials.VertexColour(0.6f, 0, "trackside-fallback");
        ArrayMesh Make(Prims p)
        {
            var m = p.Commit();
            m.SurfaceSetMaterial(0, mat);
            return m;
        }
        return contract.Nodes.ToDictionary(n => n, n => Make(n switch
        {
            "tecpro_block" or "tecpro_block_lod" => new Prims().Box(new Vector3(0, 0.3f, 0.475f), new Vector3(1.48f, 1.5f, 0.95f), Colors.White),
            "tyre_module" or "tyre_module_lod" => new Prims().Box(new Vector3(0, 0.3f, 0.62f), new Vector3(1.8f, 1.5f, 1.25f), dark),
            "armco_post" => new Prims().Box(new Vector3(0, 0.3f, 0.25f), new Vector3(0.15f, 1.5f, 0.1f), grey),
            "fence_post" => new Prims().Cylinder(new Vector3(0, -1, 0), new Vector3(0, 4.4f, 0), 0.055f, 0.055f, 6, grey),
            "fence_post_overhang" => new Prims().Cylinder(new Vector3(0, -1, 0), new Vector3(0, 3.4f, 0), 0.055f, 0.055f, 6, grey)
                .Cylinder(new Vector3(0, 3.4f, 0), new Vector3(0, 4.4f, -1), 0.045f, 0.045f, 6, grey),
            "marshal_post" => new Prims().Box(new Vector3(0, 1.1f, 1), new Vector3(2.6f, 2.4f, 2), white)
                .Box(new Vector3(0, 2.4f, 0.85f), new Vector3(3.1f, 0.16f, 2.6f), new Color("d0101c")),
            "corner_board" => new Prims().Box(new Vector3(0, 3.75f, 0.03f), new Vector3(2.5f, 1.6f, 0.06f), white)
                .Box(new Vector3(0, 1.5f, 0.1f), new Vector3(1.9f, 3.0f, 0.04f), grey),
            "brake_board" => new Prims().Box(new Vector3(0, 0.92f, 0.015f), new Vector3(1.25f, 0.85f, 0.03f), white),
            "camera_platform" => new Prims().Box(new Vector3(0, 2, 0), new Vector3(2.4f, 4, 2.4f), grey),
            _ => new Prims().Box(new Vector3(0, 8.9f, 0.5f), new Vector3(12.4f, 7, 1), dark),
        }), StringComparer.Ordinal);
    }

    // ---- the big screen -----------------------------------------------------------------------------------

    private static Node3D BigScreen(Prop p)
    {
        var xf = Xf(p, Vector3.One);
        var feed = new ScreenFeed { Name = ScreenFeed.NodeName };
        var screenMaterial = new ShaderMaterial { Shader = ScreenShader.Value };
        screenMaterial.SetShaderParameter("feed", Textures.Card);
        feed.Material = screenMaterial;
        Node3D node;
        if (SetPieces.Instantiate(TracksideModels.BigScreen, special: s => s == "screen" ? screenMaterial : Slot(s)) is { } model)
        {
            node = model;
            foreach (var mi in node.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
            {
                // The face (and the whole screen, one mesh) is not seen by its own feed camera.
                mi.Layers = ScreenLayer;
            }
        }
        else
        {
            var (w, h) = TracksideModels.ScreenSize;
            node = new Node3D();
            node.AddChild(new MeshInstance3D
            {
                Mesh = new Prims().Box(new Vector3(0, 8.9f, 0.6f), new Vector3(w + 0.6f, h + 0.6f, 1), new Color("1a1b1e"))
                    .Box(new Vector3(-4, 2.8f, 0.6f), new Vector3(0.5f, 5.6f, 0.5f), new Color("dcdfe3"))
                    .Box(new Vector3(4, 2.8f, 0.6f), new Vector3(0.5f, 5.6f, 0.5f), new Color("dcdfe3")).Commit(),
                MaterialOverride = Materials.VertexColour(0.5f, 0.2f, "screen-fallback"),
                Layers = ScreenLayer,
            });
            node.AddChild(new MeshInstance3D
            {
                Mesh = new QuadMesh { Size = new Vector2(w, h), FlipFaces = true },
                Position = new Vector3(0, 8.875f, 0.05f),
                MaterialOverride = screenMaterial,
                Layers = ScreenLayer,
            });
        }
        node.Name = "BigScreen";
        node.Transform = xf;
        var (sw, sh) = TracksideModels.ScreenSize;
        feed.Corners = [.. new[] { (-1f, 5.5f), (1f, 5.5f), (-1f, 5.5f + sh), (1f, 5.5f + sh) }.Select(c => xf * new Vector3(c.Item1 * sw / 2, c.Item2, 0))];
        node.AddChild(feed);
        return node;
    }

    /// <summary>
    /// The big screen's live feed: a <see cref="SubViewport"/> sharing the world, whose camera copies the current camera
    /// (render layers without <see cref="ScreenLayer"/>, a cheaper copy of the environment) and renders every
    /// <see cref="FeedEvery"/>th frame while the screen is in shot. Not live (Low), the screen shows the REDLINE card.
    /// </summary>
    private sealed partial class ScreenFeed : Node
    {
        public const string NodeName = "ScreenFeed";

        private SubViewport? _vp;
        private Camera3D? _cam;
        private int _frame;
        private bool _live = true;

        public ShaderMaterial? Material { get; set; }

        public Vector3[] Corners { get; set; } = [];

        public bool Live
        {
            get => _live;
            set
            {
                _live = value;
                Apply();
            }
        }

        public override void _Ready()
        {
            _vp = new SubViewport
            {
                Name = "Feed",
                Size = FeedSize,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
                Msaa3D = Viewport.Msaa.Disabled,
                ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Fxaa,
                PositionalShadowAtlasSize = 0,
            };
            _cam = new Camera3D { Name = "FeedCamera", CullMask = 0xFFFFF & ~ScreenLayer, Far = 3000 };
            _vp.AddChild(_cam);
            AddChild(_vp);
            Apply();
        }

        private void Apply()
        {
            if (Material is null || _vp is null)
            {
                return;
            }
            Material.SetShaderParameter("feed", _live ? _vp.GetTexture() : Textures.Card);
            if (!_live)
            {
                _vp.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
            }
        }

        public override void _Process(double delta)
        {
            if (!_live || _vp is null || _cam is null || ++_frame % FeedEvery != 0)
            {
                return;
            }
            var main = GetViewport().GetCamera3D();
            if (main is null || !InShot(main))
            {
                return;
            }
            if (_cam.Environment is null && GetTree().Root.FindChild("WorldEnvironment", true, false) is WorldEnvironment { Environment: { } env })
            {
                _cam.Environment = Cheap(env);
            }
            _cam.GlobalTransform = main.GlobalTransform;
            _cam.Fov = main.Fov;
            _cam.Near = main.Near;
            _vp.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        }

        /// <summary>Whether any corner of the screen is in front of the camera, in its frustum and within 1.2 km.</summary>
        private bool InShot(Camera3D cam) =>
            Corners.Any(c => cam.IsPositionInFrustum(c) && cam.GlobalPosition.DistanceTo(c) < 1200);

        /// <summary>The world's environment without the expensive screen-space and GI effects (it is a 640×360 picture).</summary>
        private static Godot.Environment Cheap(Godot.Environment env)
        {
            var e = (Godot.Environment)env.Duplicate();
            e.SdfgiEnabled = false;
            e.SsrEnabled = false;
            e.SsaoEnabled = false;
            e.SsilEnabled = false;
            e.VolumetricFogEnabled = false;
            e.GlowEnabled = false;
            return e;
        }
    }

    /// <summary>
    /// The screen's face: the feed (or card) as an LED wall, unlit (it is already a lit, tone-mapped picture) with a fine
    /// pixel grid that fades out once it is smaller than a screen pixel.
    /// </summary>
    private static readonly Lazy<Shader> ScreenShader = new(() => new Shader
    {
        Code = $$"""
            shader_type spatial;
            render_mode unshaded, fog_disabled;

            uniform sampler2D feed : source_color, filter_linear, repeat_disable;
            uniform float gain = 0.92;

            void fragment() {
                vec3 c = texture(feed, UV).rgb * gain;
                vec2 px = UV * vec2({{FeedSize.X}}.0, {{FeedSize.Y}}.0);
                vec2 f = abs(fract(px) - 0.5);
                float fine = 1.0 - smoothstep(0.25, 0.8, max(fwidth(px.x), fwidth(px.y)));
                float grid = 1.0 - 0.22 * smoothstep(0.36, 0.5, max(f.x, f.y)) * fine;
                ALBEDO = c * grid;
            }
            """,
    });

    // ---- textures: the number atlas and the REDLINE card ---------------------------------------------------

    /// <summary>The number atlas and the static screen card, drawn once per process through off-screen viewports.</summary>
    private static class Textures
    {
        public const int CellWidth = 192, CellHeight = 120;

        private static readonly ImageTexture Placeholder = ImageTexture.CreateFromImage(Image.CreateEmpty(4, 4, false, Image.Format.Rgba8));
        private static ImageTexture? _atlas, _card;
        private static bool _started;

        public static Texture2D Atlas => _atlas ?? Placeholder;

        public static Texture2D Card => _card ?? Placeholder;

        public static void Ensure(Node host)
        {
            if (_started)
            {
                Bind();
                return;
            }
            _started = true;
            Render(host, new Vector2I(TracksideAtlas.Columns * CellWidth, TracksideAtlas.Rows * CellHeight), AtlasArt(), t =>
            {
                _atlas = t;
                Bind();
            });
            Render(host, new Vector2I(1280, 720), CardArt(), t =>
            {
                _card = t;
                Bind();
            });
        }

        private static void Bind()
        {
            NumberMaterial.SetShaderParameter("atlas", Atlas);
            foreach (var feed in Feeds())
            {
                if (!feed.Live && feed.Material is { } m)
                {
                    m.SetShaderParameter("feed", Card);
                }
            }
        }

        private static IEnumerable<ScreenFeed> Feeds() =>
            Engine.GetMainLoop() is SceneTree tree
                ? tree.Root.FindChildren(ScreenFeed.NodeName, "", true, false).OfType<ScreenFeed>()
                : [];

        private static async void Render(Node host, Vector2I size, Control art, Action<ImageTexture> done)
        {
            var vp = new SubViewport { Size = size, RenderTargetUpdateMode = SubViewport.UpdateMode.Once, Disable3D = true, TransparentBg = false };
            art.Size = size;
            vp.AddChild(art);
            host.AddChild(vp);
            await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            if (GodotObject.IsInstanceValid(vp))
            {
                var image = vp.GetTexture().GetImage();
                image.GenerateMipmaps();
                done(ImageTexture.CreateFromImage(image));
                vp.QueueFree();
            }
        }

        /// <summary>
        /// The cells in <see cref="TracksideAtlas.Cells"/> order: corner boards white on graphite with a red foot, brake
        /// boards black on white inside a red border, marshal post numbers black on white.
        /// </summary>
        private static Control AtlasArt()
        {
            var root = new Control();
            var cells = TracksideAtlas.Cells;
            for (var i = 0; i < cells.Count; i++)
            {
                var corner = i < TracksideAtlas.BrakeFirst;
                var brake = !corner && i < TracksideAtlas.PostFirst;
                var origin = new Vector2(i % TracksideAtlas.Columns * CellWidth, i / TracksideAtlas.Columns * CellHeight);
                var bg = new ColorRect { Color = new Color(corner ? "17191e" : brake ? "d0101c" : "f4f4f4"), Position = origin, Size = new Vector2(CellWidth, CellHeight) };
                root.AddChild(bg);
                if (brake)
                {
                    root.AddChild(new ColorRect { Color = new Color("f4f4f4"), Position = origin + new Vector2(12, 12), Size = new Vector2(CellWidth - 24, CellHeight - 24) });
                }
                if (corner)
                {
                    root.AddChild(new ColorRect { Color = new Color("d0101c"), Position = origin + new Vector2(0, CellHeight - 16), Size = new Vector2(CellWidth, 16) });
                }
                var label = new Label
                {
                    Text = cells[i],
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Position = origin + new Vector2(0, corner ? -6 : 0),
                    Size = new Vector2(CellWidth, CellHeight),
                };
                label.AddThemeFontOverride("font", corner ? ArcTheme.BlackItalic : ArcTheme.Black);
                label.AddThemeFontSizeOverride("font_size", corner ? 84 : brake ? 74 : 92);
                label.AddThemeColorOverride("font_color", new Color(corner ? "ffffff" : "111216"));
                root.AddChild(label);
            }
            return root;
        }

        /// <summary>The static card (Low, or before the feed's first frame): REDLINE over a dark field with a red bar.</summary>
        private static Control CardArt()
        {
            var root = new ColorRect { Color = new Color("101116") };
            root.AddChild(new ColorRect { Color = new Color("e10600"), Position = new Vector2(0, 430), Size = new Vector2(1280, 18) });
            var title = new Label
            {
                Text = "REDLINE",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Position = new Vector2(0, 150),
                Size = new Vector2(1280, 280),
            };
            title.AddThemeFontOverride("font", ArcTheme.BlackItalic);
            title.AddThemeFontSizeOverride("font_size", 250);
            title.AddThemeColorOverride("font_color", new Color("ffffff"));
            root.AddChild(title);
            var sub = new Label
            {
                Text = "ARCBOX RACING TRIVIA",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Position = new Vector2(0, 470),
                Size = new Vector2(1280, 120),
            };
            sub.AddThemeFontOverride("font", ArcTheme.Bold);
            sub.AddThemeFontSizeOverride("font_size", 64);
            sub.AddThemeColorOverride("font_color", new Color("c8ccd4"));
            root.AddChild(sub);
            return root;
        }
    }

    // ---- materials ----------------------------------------------------------------------------------------

    private static readonly Dictionary<string, Material> MatCache = new(StringComparer.Ordinal);

    private static Material Cached(string key, Func<Material> make)
    {
        if (!MatCache.TryGetValue(key, out var m))
        {
            MatCache[key] = m = make();
        }
        return m;
    }

    /// <summary>The trackside's own slots; the shared ones (concrete, steel, red, LEDs, brand) are <see cref="SetMaterials"/>'.</summary>
    private static Material? Slot(string slot) => slot switch
    {
        // White under the per-instance red, white or blue.
        "tecpro" => Cached(slot, () => new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            VertexColorIsSrgb = true,
            Roughness = 0.5f,
            ClearcoatEnabled = true,
            Clearcoat = 0.35f,
            ClearcoatRoughness = 0.4f,
        }),
        "strap" => Cached(slot, () => new StandardMaterial3D { AlbedoColor = new Color("16171a"), Roughness = 0.7f }),
        "belt" => Cached(slot, () => new StandardMaterial3D
        {
            // Conveyor belt rubber: faint horizontal ribs (UVs in metres) and scuffs.
            AlbedoColor = new Color("1d1e21"),
            AlbedoTexture = Materials.Noise(256, 0.05f, 91, FastNoiseLite.NoiseTypeEnum.Value, 3),
            Uv1Scale = new Vector3(0.6f, 0.6f, 0.6f),
            Roughness = 0.78f,
            NormalEnabled = true,
            NormalTexture = Ribs,
            NormalScale = 0.6f,
        }),
        "tyre" => Cached(slot, () => new StandardMaterial3D { AlbedoColor = new Color("141416"), Roughness = 0.86f }),
        "tyre_inner" => Cached(slot, () => new StandardMaterial3D { AlbedoColor = new Color("060607"), Roughness = 1 }),
        "galvanised" => Cached(slot, () => new StandardMaterial3D
        {
            AlbedoColor = new Color("c2c6ca"),
            AlbedoTexture = Materials.Noise(256, 0.18f, 93, FastNoiseLite.NoiseTypeEnum.Cellular, 2),
            Uv1Triplanar = true,
            Uv1Scale = new Vector3(2, 2, 2),
            Metallic = 0.45f,
            Roughness = 0.45f,
        }),
        // The armco's W-beams: drawn one-sided towards the track, so both faces show.
        "galvanised_rail" => Cached(slot, () => new StandardMaterial3D
        {
            AlbedoColor = new Color("cdd1d5"),
            AlbedoTexture = Materials.Noise(256, 0.18f, 93, FastNoiseLite.NoiseTypeEnum.Cellular, 2),
            Uv1Scale = new Vector3(0.25f, 4, 1),
            Metallic = 0.45f,
            Roughness = 0.4f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        }),
        "cable" => Cached(slot, () => new StandardMaterial3D
        {
            AlbedoColor = new Color("9a9ea3"),
            Metallic = 0.5f,
            Roughness = 0.45f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        }),
        "hut" => Cached(slot, () => new StandardMaterial3D { AlbedoColor = new Color("eeeeea"), Roughness = 0.6f }),
        "hut_dark" => Cached(slot, () => new StandardMaterial3D { AlbedoColor = new Color("1e2024"), Roughness = 0.9f }),
        "hut_roof" => SetMaterials.For("red"),
        "number" => NumberMaterial,
        "board_back" => Cached(slot, () => new StandardMaterial3D { AlbedoColor = new Color("5c6068"), Metallic = 0.4f, Roughness = 0.5f }),
        "led_panel" => Materials.Emissive("18e05a", 2.2f),
        "camera" => Cached(slot, () => new StandardMaterial3D { AlbedoColor = new Color("1d1f24"), Metallic = 0.3f, Roughness = 0.4f }),
        "operator" => Cached(slot, () => new StandardMaterial3D { AlbedoColor = new Color("ff6a13"), Roughness = 0.7f }),
        "canvas" => Cached(slot, () => new StandardMaterial3D { AlbedoColor = new Color("f4f3ef"), Roughness = 0.65f, BacklightEnabled = true, Backlight = new Color(0.3f, 0.3f, 0.28f) }),
        "deck" => Cached(slot, () => new StandardMaterial3D { AlbedoColor = new Color("7a7d82"), Metallic = 0.6f, Roughness = 0.5f }),
        "screen_frame" => Cached(slot, () => new StandardMaterial3D { AlbedoColor = new Color("15161a"), Metallic = 0.3f, Roughness = 0.45f }),
        _ => SetMaterials.For(slot),
    };

    private static Color TecProColour(int shade) => shade switch
    {
        0 => new Color("c8102e"),
        1 => new Color("f2f2f0"),
        _ => new Color("1d4f9e"),
    };

    /// <summary>The conveyor belt's ribs: a stretched noise normal map.</summary>
    private static NoiseTexture2D Ribs => RibsTexture.Value;

    private static readonly Lazy<NoiseTexture2D> RibsTexture = new(() => new NoiseTexture2D
    {
        Width = 64,
        Height = 256,
        AsNormalMap = true,
        BumpStrength = 4,
        Seamless = true,
        Noise = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin, Frequency = 0.03f, FractalOctaves = 1 },
    });

    /// <summary>A board's number face: its instance's atlas cell (custom data x) across UVs 0..1.</summary>
    private static ShaderMaterial NumberMaterial => (ShaderMaterial)Cached("number", () => new ShaderMaterial
    {
        Shader = new Shader
        {
            Code = $$"""
                shader_type spatial;

                uniform sampler2D atlas : source_color, filter_linear_mipmap, repeat_disable;
                varying flat float cell;

                void vertex() {
                    cell = INSTANCE_CUSTOM.x;
                }

                void fragment() {
                    float c = floor(cell + 0.5);
                    vec2 grid = vec2({{TracksideAtlas.Columns}}.0, {{TracksideAtlas.Rows}}.0);
                    vec2 at = vec2(mod(c, grid.x), floor(c / grid.x));
                    vec2 uv = (at + mix(vec2(0.015), vec2(0.985), clamp(UV, 0.0, 1.0))) / grid;
                    ALBEDO = texture(atlas, uv).rgb;
                    ROUGHNESS = 0.45;
                    SPECULAR = 0.4;
                }
                """,
        },
    });

    /// <summary>
    /// The catch fence's mesh: 50 mm diamond chain-link (UVs in metres along and up the panel) with a darker steel strip
    /// at the knee where it cranks over, fading to an even translucent tint with distance so it never shimmers.
    /// </summary>
    private static readonly string KneeText = TracksideMeshes.Knee.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static ShaderMaterial FenceMesh => (ShaderMaterial)Cached("fence-mesh", () => new ShaderMaterial
    {
        Shader = new Shader
        {
            Code = $$"""
                shader_type spatial;
                render_mode cull_disabled, blend_mix, depth_draw_opaque;

                uniform vec4 wire : source_color = vec4(0.76, 0.78, 0.80, 1.0);
                uniform float pitch = 0.055;
                uniform float knee = {{KneeText}};

                void fragment() {
                    vec2 d = vec2(UV.x + UV.y, UV.x - UV.y) / pitch;
                    vec2 f = abs(fract(d) - 0.5);
                    float w = max(max(fwidth(d.x), fwidth(d.y)), 1e-4);
                    float lines = 1.0 - smoothstep(0.0, w * 1.1 + 0.09, 0.5 - max(f.x, f.y));
                    // Far away the wires are sub-pixel: an even tint of the same average coverage.
                    float mesh = mix(lines, 0.36, smoothstep(0.15, 0.5, w));
                    float px = max(fwidth(UV.y), 1e-4);
                    float strip = 1.0 - smoothstep(0.03, 0.03 + px * 1.5, abs(UV.y - knee));
                    ALBEDO = mix(wire.rgb, wire.rgb * 0.7, strip);
                    ALPHA = max(mesh, strip);
                    METALLIC = 0.4;
                    ROUGHNESS = 0.5;
                }
                """,
        },
    });
}
