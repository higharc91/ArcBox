using ArcBox.Host.Games.Redline.World;
using ArcBox.Host.Shell;
using Godot;
using Redline.Logic;
using Redline.Presentation.Ceremony;
using Redline.Presentation.Hud;
using Redline.Presentation.Models;

namespace ArcBox.Host.Games.Redline.Podium;

/// <summary>
/// One driver on a podium step (podium spec §5): the Blender figure <c>drivers/&lt;CODE&gt;.glb</c>
/// (<see cref="DriverModel"/>) in team colours with the number and surname on the suit and the number on the cap, or,
/// when the model can't be used, the procedural figure in team colours with stretched-cylinder arms and a lathed bottle.
/// <para>
/// <see cref="Bottle"/> is the frame the champagne sprays ride on (the mouth at <see cref="DriverModel.BottleMouth"/>). The
/// <see cref="PodiumSet"/> decides where the bottle goes (<see cref="Pose"/>); with the Blender figure the right arm
/// reaches it with a two-bone IK through <c>arm_r</c>, <c>forearm_r</c> and <c>hand_r</c>, the frame then follows the
/// figure's own <c>bottle</c> node, and the left hand stays under the bottle's base until the spray lifts it away.
/// </para>
/// </summary>
public sealed class PodiumDriver
{
    private readonly Node3D _set;
    private readonly int _step;
    private readonly Node3D _figure;

    // Procedural fallback.
    private readonly MeshInstance3D _body;
    private readonly MeshInstance3D[] _arms;
    private readonly StandardMaterial3D _suit;
    private readonly MeshInstance3D _glass;
    private readonly MeshInstance3D _foil;

    // Blender figure.
    private Node3D? _model;
    private Chain? _right;
    private Chain? _left;
    private Node3D? _modelBottle;
    private Transform3D _bottleRest = Transform3D.Identity;   // the bottle node in the model's root frame, at rest
    private Vector3 _leftInBottle;                              // the left wrist in the rest bottle's frame
    private Vector3 _shoulderL;

    public PodiumDriver(Node3D set, int step)
    {
        _set = set;
        _step = step;
        _suit = new StandardMaterial3D { AlbedoColor = new Color("cccccc"), Roughness = 0.62f };
        _figure = new Node3D { Name = $"Driver{step}", Position = V(PodiumLayout.StepTopCentre(step)) };
        _body = new MeshInstance3D { Name = "Body", MaterialOverride = Materials.VertexColour(0.6f, 0, "podium-figure") };
        _figure.AddChild(_body);
        set.AddChild(_figure);

        var arm = new CylinderMesh { TopRadius = 0.052f, BottomRadius = 0.058f, Height = 1, RadialSegments = 10, Rings = 1 };
        _arms = [.. Enumerable.Range(0, 2).Select(i => new MeshInstance3D { Name = $"Arm{i}", Mesh = arm, MaterialOverride = _suit })];
        foreach (var a in _arms)
        {
            set.AddChild(a);
        }

        Bottle = new Node3D { Name = $"Bottle{step}" };
        set.AddChild(Bottle);
        _glass = new MeshInstance3D
        {
            Name = "Glass",
            MaterialOverride = new StandardMaterial3D
            {
                VertexColorUseAsAlbedo = true,
                VertexColorIsSrgb = true,
                Roughness = 0.07f,
                Metallic = 0.15f,
                MetallicSpecular = 0.9f,
                ClearcoatEnabled = true,
                Clearcoat = 1,
                RimEnabled = true,
                Rim = 0.4f,
            },
        };
        _foil = new MeshInstance3D { Name = "Foil", MaterialOverride = Materials.VertexColour(0.28f, 0.85f, "podium-foil") };
        Bottle.AddChild(_glass);
        Bottle.AddChild(_foil);
        Dress(null);
    }

    /// <summary>The frame the sprays ride on: the bottle's base, +Y up its axis (in the set's frame).</summary>
    public Node3D Bottle { get; }

    /// <summary>The driver code dressed, or null.</summary>
    public string? Code { get; private set; }

    /// <summary>True while the Blender figure is in use.</summary>
    public bool HasModel => _model is not null;

    /// <summary>Starts loading every driver figure in the background (safe to call repeatedly).</summary>
    public static void Prewarm() =>
        ModelLibrary.Prewarm([.. Grid2026.Load().Seats.Select(s => DriverModel.File(s.Driver.Code))]);

    public void SetVisible(bool visible)
    {
        _figure.Visible = visible;
        Bottle.Visible = visible;
        foreach (var a in _arms)
        {
            a.Visible = visible && !HasModel;
        }
    }

    /// <summary>Where the bottle rests in the set's frame (held in front of the stomach).</summary>
    public Transform3D RestBottle => HasModel
        ? new Transform3D(Basis.Identity, _figure.Transform * _bottleRest.Origin)
        : new Transform3D(Basis.Identity, V(PodiumLayout.Bottle(_step)));

    /// <summary>Where the bottle's base goes while spraying, before it is aimed (in the set's frame).</summary>
    public Vector3 RaisedBottle => HasModel
        ? _figure.Transform * (_right!.Shoulder + new Vector3(-0.03f, 0.22f, 0.36f))
        : V(PodiumLayout.Bottle(_step)) + new Vector3(0, 0.32f, 0.16f);

    /// <summary>Puts the driver of <paramref name="step"/> on the podium (null: a neutral figure).</summary>
    public void Dress(PodiumStep? step)
    {
        Code = step?.Code;
        var team = TeamAccent.Team(step?.TeamId);
        var accent = TeamAccent.ById(step?.TeamId);
        _model?.QueueFree();
        _model = null;
        _right = _left = null;
        _modelBottle = null;
        if (step is not null && team is not null && FromModel(step.Code, team, accent))
        {
            _body.Visible = false;
            _glass.Visible = false;
            _foil.Visible = false;
            foreach (var a in _arms)
            {
                a.Visible = false;
            }
            return;
        }
        var suit = team is null ? new Color("d0d0d4") : new Color(team.Livery.Top);
        _suit.AlbedoColor = suit;
        _body.Visible = _glass.Visible = _foil.Visible = true;
        _body.Mesh = Figure(suit, new Color(accent));
        var bottle = BottleMesh.Build(accent);
        _glass.Mesh = MeshConvert.ToArrayMesh(bottle.Glass);
        _foil.Mesh = MeshConvert.ToArrayMesh(bottle.Foil);
    }

    /// <summary>
    /// Puts the bottle at <paramref name="bottle"/> (set frame: origin at the base, +Y along the bottle) and the arms to
    /// it. <paramref name="release"/>: 0 with the left hand under the base, 1 with it let go (spraying).
    /// </summary>
    public void Pose(Transform3D bottle, float release)
    {
        if (_model is null || _right is null || _left is null || _modelBottle is null)
        {
            Bottle.Transform = bottle;
            var top = V(PodiumLayout.StepTopCentre(_step));
            var handR = bottle * new Vector3(0.05f, 0.2f, 0);
            var handL = bottle * new Vector3(-0.03f, 0.04f, 0.02f);
            Limb(_arms[0], top + new Vector3(0.23f, 1.42f, 0.02f), handR);
            Limb(_arms[1], top + new Vector3(-0.23f, 1.42f, 0.02f), handL);
            return;
        }
        // Into the model's root frame.
        var b = _figure.Transform.AffineInverse() * bottle;
        var handBasis = b.Basis.Orthonormalized();
        // The bottle hangs off the hand at a fixed offset: put the wrist where that offset lands the bottle on target.
        var wrist = b.Origin + (handBasis * _right.WristRest);
        _right.Solve(wrist, handBasis, new Vector3(-0.45f, -0.6f, -0.65f).Lerp(new Vector3(-0.75f, -0.55f, -0.1f), release));

        // Left hand: under the base, or let go and lowered beside the body.
        var under = b * (_leftInBottle);
        var free = _shoulderL + new Vector3(0.09f, -0.52f, 0.14f);
        var leftAt = under.Lerp(free, Mathf.SmoothStep(0, 1, release));
        var leftBasis = new Basis(handBasis.GetRotationQuaternion().Slerp(Quaternion.Identity, release));
        _left.Solve(leftAt, leftBasis, new Vector3(0.55f, -0.55f, -0.6f));

        Bottle.GlobalTransform = _modelBottle.GlobalTransform;
    }

    // ---- the Blender figure -------------------------------------------------------------------------------

    private bool FromModel(string code, GridTeam team, string accent)
    {
        var contract = DriverModel.Contract(code);
        var root = ModelLibrary.Instantiate(contract.File, n =>
        {
            var (nodes, mats) = ModelLibrary.Names(n);
            return contract.Missing(nodes, mats);
        });
        if (root is null)
        {
            return false;
        }
        root.Name = "Model";
        _figure.AddChild(root);
        FigureMaterials.Bind(root, (slot, imported) => FigureMaterials.Driver(slot, imported, team.Livery, accent), 1);
        if (root.FindChild(DriverModel.HairTop, recursive: true, owned: false) is Node3D hairTop)
        {
            hairTop.Visible = false; // the cap is on
        }
        Node3D N(string name) => (Node3D)root.FindChild(name, recursive: true, owned: false)!;
        var spine = N(DriverModel.Spine);
        _right = new Chain(spine, N(DriverModel.ArmR), N(DriverModel.ForearmR), N(DriverModel.HandR));
        _left = new Chain(spine, N(DriverModel.ArmL), N(DriverModel.ForearmL), N(DriverModel.HandL));
        _modelBottle = N(DriverModel.Bottle);
        _bottleRest = new Transform3D(Basis.Identity, _right.WristRestRoot + _modelBottle.Position);
        _leftInBottle = _left.WristRestRoot - _bottleRest.Origin;
        _shoulderL = _left.Shoulder;
        _right.WristRest = _right.WristRestRoot - _bottleRest.Origin;

        var seat = Grid2026.Load().ByCode(code);
        var number = seat.Driver.Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var ink = Ink(team.Livery);
        Label(N(DriverModel.NumberSuit), number, 0.055f, ink, ArcTheme.BlackItalic);
        Label(N(DriverModel.NameSuit), DriverModel.Surname(seat.Driver.Name), 0.021f, ink, ArcTheme.BlackItalic);
        Label(N(DriverModel.NumberCap), number, 0.03f, Ink(team.Livery), ArcTheme.BlackItalic);
        _model = root;
        return true;
    }

    /// <summary>Text colour on the suit: the livery's number colour when it reads on the main colour, else black or white.</summary>
    private static Color Ink(Livery livery)
    {
        static float Luma(Color c) => (0.2126f * c.R) + (0.7152f * c.G) + (0.0722f * c.B);
        var bg = new Color(livery.Top);
        var want = new Color(livery.Number);
        if (Mathf.Abs(Luma(want) - Luma(bg)) > 0.25f)
        {
            return want;
        }
        return Luma(bg) > 0.45f ? new Color("111111") : Colors.White;
    }

    private static void Label(Node3D at, string text, float glyph, Color colour, Font font)
    {
        const int fontSize = 96;
        at.AddChild(new Label3D
        {
            Text = text,
            Font = font,
            FontSize = fontSize,
            OutlineSize = 0,
            PixelSize = glyph / (fontSize * 0.7f),
            Modulate = colour,
            Shaded = true,
            DoubleSided = false,
            AlphaCut = Label3D.AlphaCutMode.Discard,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Position = new Vector3(0, 0, 0.002f),
        });
    }

    /// <summary>A shoulder-elbow-wrist chain of the figure (rest rotations identity, spine not rotated).</summary>
    private sealed class Chain
    {
        private readonly Node3D _arm;
        private readonly Node3D _fore;
        private readonly Node3D _hand;
        private readonly Vector3 _e0;
        private readonly float _a;
        private readonly float _b;
        private readonly Vector3 _n0;

        public Chain(Node3D spine, Node3D arm, Node3D fore, Node3D hand)
        {
            _arm = arm;
            _fore = fore;
            _hand = hand;
            Shoulder = spine.Position + arm.Position;
            _e0 = Shoulder + fore.Position;
            WristRestRoot = _e0 + hand.Position;
            _a = fore.Position.Length();
            _b = hand.Position.Length();
            _n0 = (_e0 - Shoulder).Cross(WristRestRoot - _e0).Normalized();
        }

        public Vector3 Shoulder { get; }

        /// <summary>The wrist at rest, in the model's root frame.</summary>
        public Vector3 WristRestRoot { get; }

        /// <summary>The wrist relative to the bottle's base at rest (right hand only).</summary>
        public Vector3 WristRest { get; set; }

        /// <summary>Two-bone IK: the wrist to <paramref name="target"/> (or as near as the arm reaches), the elbow bending
        /// towards <paramref name="pole"/>, the hand turned to <paramref name="handBasis"/> (all in the root frame).</summary>
        public void Solve(Vector3 target, Basis handBasis, Vector3 pole)
        {
            var d = target - Shoulder;
            var dist = Mathf.Clamp(d.Length(), Mathf.Abs(_a - _b) + 0.002f, _a + _b - 0.002f);
            var dir = d.LengthSquared() > 1e-8f ? d.Normalized() : Vector3.Down;
            var cosA = ((_a * _a) + (dist * dist) - (_b * _b)) / (2 * _a * dist);
            var sinA = Mathf.Sqrt(Mathf.Max(0, 1 - (cosA * cosA)));
            var p = pole - (dir * pole.Dot(dir));
            p = p.LengthSquared() > 1e-8f ? p.Normalized() : dir.Cross(Vector3.Right).Normalized();
            var elbow = Shoulder + (dir * _a * cosA) + (p * _a * sinA);
            var wrist = Shoulder + (dir * dist);
            var u0 = (_e0 - Shoulder) / _a;
            var u1 = (elbow - Shoulder) / _a;
            var v0 = (WristRestRoot - _e0) / _b;
            var v1 = (wrist - elbow) / _b;
            var n1 = u1.Cross(v1);
            n1 = n1.LengthSquared() > 1e-8f ? n1.Normalized() : _n0;
            var r1 = Frame(u1, n1) * Frame(u0, _n0).Transposed();
            var r2 = Frame(v1, n1) * Frame(v0, _n0).Transposed();
            _arm.Basis = r1;
            _fore.Basis = r1.Inverse() * r2;
            _hand.Basis = r2.Inverse() * handBasis;
        }

        private static Basis Frame(Vector3 u, Vector3 n)
        {
            var w = u.Cross(n).Normalized();
            var nn = w.Cross(u).Normalized();
            return new Basis(u, nn, w);
        }
    }

    // ---- the procedural fallback ------------------------------------------------------------------------

    private static Vector3 V(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    private static Basis BasisFromUp(Vector3 up)
    {
        var x = up.Cross(Vector3.Back).Normalized();
        if (x.LengthSquared() < 1e-4f)
        {
            x = Vector3.Right;
        }
        var z = x.Cross(up).Normalized();
        return new Basis(x, up, z);
    }

    /// <summary>Stretches a unit-height cylinder from <paramref name="from"/> to <paramref name="to"/>.</summary>
    private static void Limb(MeshInstance3D limb, Vector3 from, Vector3 to)
    {
        var d = to - from;
        var len = Mathf.Max(0.05f, d.Length());
        var y = d / len;
        var b = BasisFromUp(y);
        var basis = new Basis(b.X, b.Y * len, b.Z);
        limb.Transform = new Transform3D(basis, from + (d / 2));
    }

    /// <summary>A driver in race suit and team cap, facing the camera (+Z), feet on the origin.</summary>
    private static ArrayMesh Figure(Color suit, Color accent)
    {
        var p = new Prims();
        var skin = new Color("c99a7a");
        var boot = new Color("15151a");
        var dark = suit.Darkened(0.25f);
        foreach (var x in new[] { -0.11f, 0.11f })
        {
            p.Box(new Vector3(x, 0.05f, 0.04f), new Vector3(0.12f, 0.1f, 0.27f), boot);
            p.Cylinder(new Vector3(x, 0.08f, 0), new Vector3(x * 0.95f, 0.88f, 0), 0.078f, 0.104f, 12, suit);
        }
        p.Ellipsoid(new Vector3(0, 0.92f, 0), new Vector3(0.2f, 0.12f, 0.14f), 14, 7, suit);
        p.Cylinder(new Vector3(0, 0.88f, 0), new Vector3(0, 0.96f, 0), 0.198f, 0.198f, 16, dark);     // belt
        p.Ellipsoid(new Vector3(0, 1.17f, 0), new Vector3(0.215f, 0.3f, 0.145f), 16, 9, suit);
        p.Ellipsoid(new Vector3(0, 1.37f, 0), new Vector3(0.255f, 0.1f, 0.145f), 16, 6, suit);
        // Accent panels down the sides of the torso.
        foreach (var x in new[] { -0.2f, 0.2f })
        {
            p.Ellipsoid(new Vector3(x, 1.16f, 0), new Vector3(0.03f, 0.22f, 0.12f), 8, 6, accent);
        }
        p.Cylinder(new Vector3(0, 1.42f, 0), new Vector3(0, 1.53f, 0), 0.055f, 0.05f, 10, skin);
        p.Ellipsoid(new Vector3(0, 1.62f, 0.005f), new Vector3(0.092f, 0.112f, 0.1f), 14, 9, skin);
        // The cap: crown and a brim pointing at the camera.
        p.Ellipsoid(new Vector3(0, 1.665f, -0.005f), new Vector3(0.102f, 0.072f, 0.11f), 14, 7, accent);
        p.Ellipsoid(new Vector3(0, 1.655f, 0.1f), new Vector3(0.088f, 0.012f, 0.075f), 12, 4, accent.Darkened(0.15f));
        return p.Commit();
    }
}
