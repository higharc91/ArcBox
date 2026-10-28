using ArcBox.Host.Games.Redline.World;
using ArcBox.Host.Shell;
using Godot;
using Redline.Logic;
using Redline.Presentation.Ceremony;
using Redline.Presentation.Hud;
using NVector3 = System.Numerics.Vector3;

namespace ArcBox.Host.Games.Redline.Podium;

/// <summary>
/// The podium set (podium spec §5) where <see cref="PodiumLayout"/> puts it: the Blender set (<see cref="PodiumStage"/>)
/// when it loads, else built in code: a stage with three stepped
/// blocks carrying their numerals (2 left, 1 centre and highest, 3 right), a REDLINE media-wall backdrop framed by
/// light towers and LED strips, a lighting truss; a driver on each step (<see cref="PodiumDriver"/>: the driver's Blender
/// figure in team colours holding the champagne, else a procedural figure), a pool of <see cref="ChampagneSpray"/>s per
/// bottle and the <see cref="Confetti"/>.
/// <see cref="RedlineScene"/> shows it for the Anthem and the Champagne (<see cref="ShowCeremony"/>), hides it otherwise
/// (<see cref="HideCeremony"/>) and fires sprays (<see cref="Spray"/>); the drivers shake their bottles as their charge rises.
/// </summary>
public partial class PodiumSet : Node3D
{
    private const int SpraysPerBottle = 3;

    private readonly PodiumLayout _layout;
    private readonly Place[] _places = new Place[3];
    private Confetti _confetti = null!;
    private RacePhase? _phase;
    private double _time;

    private sealed class Place
    {
        /// <summary>The figure, its arms and its bottle (<see cref="PodiumDriver.Bottle"/> carries the sprays).</summary>
        public PodiumDriver Driver = null!;
        public ChampagneSpray[] Sprays = null!;
        public double Charge;
        /// <summary>0 at rest, 1 with the bottle raised and spraying (eased).</summary>
        public double Raise;
        public Transform3D BottleXf = Transform3D.Identity;
    }

    public PodiumSet()
    {
        _layout = null!;
    }

    public PodiumSet(PodiumLayout layout)
    {
        Name = "PodiumSet";
        _layout = layout;
        Visible = false;
        var o = layout.Origin;
        Transform = new Transform3D(new Basis(V(layout.Right), V(layout.Up), V(layout.Front)), new Vector3(o.X, o.Y, o.Z));
    }

    public override void _Ready()
    {
        var stage = PodiumStage.TryCreate(this);
        if (stage is not null)
        {
            AddChild(stage);
        }
        else
        {
            AddChild(Structure());
            AddChild(Lamps());
            AddChild(Backdrop());
            AddNumerals();
        }
        AddLights();
        for (var step = 1; step <= 3; step++)
        {
            _places[step - 1] = BuildPlace(step);
        }
        var top = PodiumLayout.StageHeight + PodiumLayout.BackdropHeight + 0.4f;
        var edge = (PodiumLayout.BackdropWidth / 2) - 0.6f;
        (Vector3, Vector3)[] cannons = stage is not null ? PodiumStage.Cannons(stage) :
            [(new Vector3(-edge, top, PodiumLayout.BackdropZ + 0.4f), new Vector3(0.35f, 1, 0.55f)),
             (new Vector3(edge, top, PodiumLayout.BackdropZ + 0.4f), new Vector3(-0.35f, 1, 0.55f))];
        _confetti = new Confetti(cannons, new Vector3(0, top + 3.5f, 0.5f), new Vector3(10, 0.5f, 5));
        AddChild(_confetti);
    }

    private static Vector3 V(NVector3 v) => new(v.X, v.Y, v.Z);

    // ---- on air ---------------------------------------------------------------------------------------------

    /// <summary>Shows the ceremony in <paramref name="view"/>: the drivers' teams and charges; confetti when Champagne begins.</summary>
    public void ShowCeremony(RedlineHostView view)
    {
        Visible = true;
        if (view.Podium is { } podium)
        {
            for (var i = 0; i < 3; i++)
            {
                var step = podium.Steps.FirstOrDefault(s => s.Step == i + 1);
                var place = _places[i];
                place.Driver.SetVisible(step is not null);
                if (step is null)
                {
                    continue;
                }
                place.Charge = step.Charge;
                if (place.Driver.Code != step.Code)
                {
                    place.Driver.Dress(step);
                    Pose(place, i + 1, 0);
                }
            }
        }
        if (_phase != view.Phase)
        {
            if (view.Phase == RacePhase.Champagne)
            {
                _confetti.Start();
            }
            else
            {
                _confetti.Stop(clear: true);
            }
            _phase = view.Phase;
        }
    }

    /// <summary>Off air: hidden, every spray and every piece of confetti cleared.</summary>
    public void HideCeremony()
    {
        Visible = false;
        _phase = null;
        foreach (var place in _places)
        {
            foreach (var spray in place.Sprays)
            {
                spray.Stop();
            }
            place.Raise = 0;
        }
        _confetti?.Stop(clear: true);
    }

    /// <summary>Pops the cork on <paramref name="step"/>'s bottle.</summary>
    public void Spray(int step, double strength)
    {
        if (step is < 1 or > 3)
        {
            return;
        }
        var place = _places[step - 1];
        var spray = place.Sprays.FirstOrDefault(s => !s.Busy) ?? place.Sprays.MaxBy(s => s.Age)!;
        // The jet leaves along the bottle's axis: point the bottle before the first particles go.
        place.Raise = Math.Max(place.Raise, 0.6);
        Pose(place, step, 0);
        spray.Fire(strength);
    }

    /// <summary>Where <paramref name="step"/>'s bottle mouth is now, in the world (the HUD and the camera may follow it).</summary>
    public Vector3 Mouth(int step) => _places[step - 1].Driver.Bottle.ToGlobal(new Vector3(0, BottleMesh.Height, 0));

    public override void _Process(double delta)
    {
        if (!Visible)
        {
            return;
        }
        _time += delta;
        for (var i = 0; i < 3; i++)
        {
            var place = _places[i];
            var spraying = place.Sprays.Any(s => s.Busy && IsJet(s));
            var target = spraying ? 1.0 : 0.0;
            place.Raise += (target - place.Raise) * Math.Min(1, delta * (spraying ? 10 : 2.5));
            Pose(place, i + 1, delta);
        }
    }

    private static bool IsJet(ChampagneSpray s) => s.Age < s.JetSeconds;

    /// <summary>
    /// Places the bottle and the arms: held at the chest and shaken harder as the charge rises; raised and aimed along
    /// the jet (<see cref="SprayLook.Aim"/>) while spraying.
    /// </summary>
    private void Pose(Place place, int step, double delta)
    {
        var driver = place.Driver;
        var rest = driver.RestBottle.Origin;
        var side = step == 2 ? 1f : step == 3 ? -1f : 0.4f;
        // The Blender figure holds the bottle nearly upright in both hands; the procedural one leans it at the camera.
        var idleUp = driver.HasModel ? new Vector3(0.06f * side, 1, 0.12f).Normalized() : new Vector3(0.18f * side, 1, 0.42f).Normalized();
        var shake = (float)(place.Raise < 0.5 && _phase is RacePhase.Anthem or RacePhase.Champagne ? 0.015 + (0.07 * place.Charge) : 0);
        var rate = 7.5 + step;
        var wobble = Mathf.Sin((float)(_time * rate * Mathf.Tau));
        var idlePos = rest + new Vector3(0, shake * wobble, shake * 0.4f * wobble);
        idleUp = (idleUp + new Vector3(shake * 1.6f * wobble, 0, 0)).Normalized();

        var jet = place.Sprays.Where(s => s.Busy && IsJet(s)).MinBy(s => s.Age);
        var aimUp = V(SprayLook.Aim(step, jet?.Age ?? 0));
        var raisedPos = driver.RaisedBottle;

        var k = (float)RevealSmooth(place.Raise);
        var up = idleUp.Lerp(aimUp, k).Normalized();
        var pos = idlePos.Lerp(raisedPos, k);
        place.BottleXf = new Transform3D(BasisFromUp(up), pos);
        // The arms reach the bottle (two-bone IK on the Blender figure's arm_r); the spray frame follows the hand.
        driver.Pose(place.BottleXf, k);
    }

    private static double RevealSmooth(double t) => global::Redline.Presentation.RevealMotion.SmoothStep(t);

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

    // ---- people and bottles ---------------------------------------------------------------------------------

    private Place BuildPlace(int step)
    {
        var place = new Place { Driver = new PodiumDriver(this, step) };
        place.Sprays = [.. Enumerable.Range(0, SpraysPerBottle).Select(_ => new ChampagneSpray { Position = new Vector3(0, BottleMesh.Height + 0.01f, 0) })];
        foreach (var s in place.Sprays)
        {
            place.Driver.Bottle.AddChild(s);
        }
        Pose(place, step, 0);
        return place;
    }

    // ---- the set --------------------------------------------------------------------------------------------

    private static MeshInstance3D Structure()
    {
        var p = new Prims();
        const float w = PodiumLayout.StageWidth, d = PodiumLayout.StageDepth, h = PodiumLayout.StageHeight;
        const float skirt = 1.6f;
        var graphite = new Color("1c1d23");
        var carpet = new Color("2b2c34");
        // Stage: a skirt down into the ground so no gap shows on uneven verge, a darker top.
        p.Box(new Vector3(0, (h - skirt) / 2, -d / 2), new Vector3(w, h + skirt, d), graphite);
        p.Box(new Vector3(0, h + 0.01f, -d / 2), new Vector3(w - 0.1f, 0.02f, d - 0.1f), carpet);
        // Steps: white blocks with a gold, silver or bronze lip.
        string[] lips = ["d4a52c", "c3c8cf", "b8733a"];
        for (var step = 1; step <= 3; step++)
        {
            var x = PodiumLayout.StepX(step);
            var top = PodiumLayout.StepTop(step);
            var rise = top - h;
            p.Box(new Vector3(x, h + (rise / 2), PodiumLayout.StepZ), new Vector3(PodiumLayout.StepWidth, rise, PodiumLayout.StepDepth), new Color("eef0f3"));
            p.Box(new Vector3(x, top - 0.04f, PodiumLayout.StepZ + (PodiumLayout.StepDepth / 2) + 0.012f), new Vector3(PodiumLayout.StepWidth, 0.08f, 0.03f), new Color(lips[step - 1]));
            p.Box(new Vector3(x, top + 0.006f, PodiumLayout.StepZ), new Vector3(PodiumLayout.StepWidth - 0.08f, 0.012f, PodiumLayout.StepDepth - 0.08f), new Color("d9dce1"));
        }
        // Backdrop frame and the light towers either side.
        const float bw = PodiumLayout.BackdropWidth, bh = PodiumLayout.BackdropHeight, bz = PodiumLayout.BackdropZ;
        p.Box(new Vector3(0, h + (bh / 2) - 0.3f, bz - 0.15f), new Vector3(bw + 0.4f, bh + 2.2f, 0.3f), new Color("0f1014"));
        foreach (var sx in new[] { -1f, 1f })
        {
            var x = sx * ((bw / 2) + 0.55f);
            p.Box(new Vector3(x, (h + bh + 1.4f - skirt) / 2, bz + 0.1f), new Vector3(0.7f, h + bh + 1.4f + skirt, 0.7f), new Color("23242b"));
        }
        // Lighting truss across the front, on two posts behind the outer cars' line.
        var trussY = h + bh + 1.0f;
        p.Box(new Vector3(0, trussY, 2.6f), new Vector3(bw + 1.5f, 0.35f, 0.35f), new Color("8e939b"));
        p.Box(new Vector3(0, trussY, 2.6f), new Vector3(bw + 1.5f, 0.06f, 0.5f), new Color("6f747c"));
        foreach (var sx in new[] { -1f, 1f })
        {
            p.Box(new Vector3(sx * ((bw / 2) + 0.55f), (trussY + 0.2f - skirt) / 2, 2.6f), new Vector3(0.3f, trussY + 0.2f + skirt, 0.3f), new Color("8e939b"));
            p.Box(new Vector3(sx * ((bw / 2) + 0.55f), (trussY + 0.15f) / 2 + 0.1f, -1.4f), new Vector3(0.22f, 0.22f, 8.2f), new Color("8e939b"));
        }
        foreach (var x in new[] { -5.2f, -2.8f, 0f, 2.8f, 5.2f })
        {
            p.Cylinder(new Vector3(x, trussY - 0.18f, 2.6f), new Vector3(x, trussY - 0.55f, 2.35f), 0.17f, 0.2f, 12, new Color("1a1b20"));
        }
        return new MeshInstance3D { Name = "Structure", Mesh = p.Commit(), MaterialOverride = Materials.VertexColour(0.55f, 0.15f, "podium") };
    }

    /// <summary>Everything that glows: the backdrop's LED surround, the tower lamps, the truss lamp faces and the stage's red strip.</summary>
    private static Node3D Lamps()
    {
        var p = new Prims();
        const float h = PodiumLayout.StageHeight, bw = PodiumLayout.BackdropWidth, bh = PodiumLayout.BackdropHeight, bz = PodiumLayout.BackdropZ;
        var white = Colors.White;
        p.Box(new Vector3(0, h + bh + 0.12f, bz + 0.03f), new Vector3(bw + 0.2f, 0.1f, 0.06f), white);
        foreach (var sx in new[] { -1f, 1f })
        {
            p.Box(new Vector3(sx * ((bw / 2) + 0.1f), h + (bh / 2), bz + 0.03f), new Vector3(0.1f, bh + 0.3f, 0.06f), white);
            var x = sx * ((bw / 2) + 0.55f);
            // A column of round stage lamps down each tower.
            for (var i = 0; i < 8; i++)
            {
                var y = h + 0.7f + (i * 0.95f);
                p.Cylinder(new Vector3(x, y, bz + 0.45f), new Vector3(x, y, bz + 0.5f), 0.19f, 0.19f, 16, white);
            }
        }
        var trussY = h + bh + 1.0f;
        foreach (var x in new[] { -5.2f, -2.8f, 0f, 2.8f, 5.2f })
        {
            p.Cylinder(new Vector3(x, trussY - 0.53f, 2.36f), new Vector3(x, trussY - 0.57f, 2.33f), 0.16f, 0.16f, 12, white);
        }
        var red = new Prims();
        red.Box(new Vector3(0, h - 0.06f, 0.01f), new Vector3(PodiumLayout.StageWidth, 0.07f, 0.03f), white);
        var node = new Node3D { Name = "Lamps" };
        node.AddChild(new MeshInstance3D
        {
            Name = "Warm",
            Mesh = p.Commit(),
            MaterialOverride = Materials.Emissive("ffe7c4", 6),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        node.AddChild(new MeshInstance3D
        {
            Name = "RedStrip",
            Mesh = red.Commit(),
            MaterialOverride = Materials.Emissive("ff1a12", 5),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        return node;
    }

    private MeshInstance3D Backdrop()
    {
        var material = new StandardMaterial3D
        {
            // An LED wall: it shows its picture whatever the sun does.
            AlbedoColor = new Color(0.92f, 0.92f, 0.92f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        };
        PodiumBackdrop.Apply(this, material);
        return new MeshInstance3D
        {
            Name = "Backdrop",
            Mesh = new QuadMesh { Size = new Vector2(PodiumLayout.BackdropWidth, PodiumLayout.BackdropHeight) },
            MaterialOverride = material,
            Position = new Vector3(0, PodiumLayout.StageHeight + (PodiumLayout.BackdropHeight / 2), PodiumLayout.BackdropZ + 0.02f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    private void AddNumerals()
    {
        for (var step = 1; step <= 3; step++)
        {
            var rise = PodiumLayout.StepTop(step) - PodiumLayout.StageHeight;
            AddChild(new Label3D
            {
                Name = $"Numeral{step}",
                Text = step.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Font = ArcTheme.BlackItalic,
                FontSize = 192,
                OutlineSize = 0,
                PixelSize = rise * 0.0042f,
                Modulate = new Color("e10600"),
                Shaded = true,
                DoubleSided = false,
                Position = new Vector3(PodiumLayout.StepX(step), PodiumLayout.StageHeight + (rise * 0.47f), PodiumLayout.StepZ + (PodiumLayout.StepDepth / 2) + 0.012f),
            });
        }
        AddChild(new Label3D
        {
            Name = "StageMark",
            Text = "REDLINE",
            Font = ArcTheme.BlackItalic,
            FontSize = 160,
            PixelSize = 0.0028f,
            Modulate = new Color(1, 1, 1, 0.92f),
            Shaded = true,
            Position = new Vector3(0, PodiumLayout.StageHeight * 0.5f, 0.012f),
        });
    }

    private void AddLights()
    {
        var trussY = PodiumLayout.StageHeight + PodiumLayout.BackdropHeight + 1.0f;
        foreach (var x in new[] { -2.8f, 0f, 2.8f })
        {
            var light = new SpotLight3D
            {
                Name = $"Spot{x:0}",
                LightColor = new Color("fff1dc"),
                LightEnergy = 9,
                SpotRange = 16,
                SpotAngle = 24,
                SpotAngleAttenuation = 0.6f,
                ShadowEnabled = false,
            };
            AddChild(light);
            var from = new Vector3(x, trussY - 0.6f, 2.3f);
            var to = new Vector3(x * 1.05f, PodiumLayout.StageHeight + 1.6f, PodiumLayout.StepZ);
            light.Transform = Transform3D.Identity.Translated(from).LookingAt(to, Vector3.Up);
        }
    }
}
