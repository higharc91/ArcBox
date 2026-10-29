using Godot;
using Redline.Presentation.Ceremony;

namespace ArcBox.Host.Games.Redline.Podium;

/// <summary>
/// One champagne spray (podium spec §5): a jet of golden foam with bright droplets that sparkle through the glow, a haze
/// at the mouth, foam dripping off afterwards, and the cork flying off with a small pop flash. Every particle lives in
/// world space, so as the bottle waves about the jet draws arcs. Size, speed, duration and amount follow
/// <see cref="SprayLook"/>. The node sits at the bottle's mouth with its +Y along the jet; <see cref="PodiumSet"/> moves it.
/// </summary>
public partial class ChampagneSpray : Node3D
{
    private const int MaxDroplets = 420;
    private const int MaxMist = 60;
    private const int MaxDrips = 60;
    private const float DripSeconds = 1.6f;

    private readonly GpuParticles3D _foam;
    private readonly GpuParticles3D _droplets;
    private readonly GpuParticles3D _mist;
    private readonly GpuParticles3D _drips;
    private readonly ParticleProcessMaterial _foamProcess;
    private readonly ParticleProcessMaterial _dropletProcess;
    private readonly ParticleProcessMaterial _mistProcess;
    private readonly MeshInstance3D _cork;
    private readonly MeshInstance3D _burst;
    private readonly OmniLight3D _flash;

    private double _age = double.PositiveInfinity;
    private SprayLook _look;
    private Vector3 _corkVelocity;
    private Vector3 _corkSpin;
    private bool _corkFlying;

    public ChampagneSpray()
    {
        Name = "ChampagneSpray";
        _foamProcess = ChampagneFx.FoamProcess();
        _dropletProcess = ChampagneFx.DropletProcess();
        _mistProcess = ChampagneFx.MistProcess();
        _foam = Emitter("Foam", SprayLook.MaxBlobs, _foamProcess, ChampagneFx.FoamQuad);
        _droplets = Emitter("Droplets", MaxDroplets, _dropletProcess, ChampagneFx.DropletQuad);
        _mist = Emitter("Mist", MaxMist, _mistProcess, ChampagneFx.MistQuad);
        _drips = Emitter("Drips", MaxDrips, ChampagneFx.DripProcess(), ChampagneFx.FoamQuad);
        _drips.Lifetime = 1.3;
        foreach (var e in new[] { _foam, _droplets, _mist, _drips })
        {
            AddChild(e);
        }

        _cork = new MeshInstance3D
        {
            Name = "Cork",
            Mesh = ChampagneFx.CorkMesh,
            Visible = false,
            TopLevel = true,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_cork);
        _burst = new MeshInstance3D
        {
            Name = "PopBurst",
            Mesh = ChampagneFx.BurstQuad,
            MaterialOverride = ChampagneFx.BurstMaterial(),
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_burst);
        _flash = new OmniLight3D { Name = "PopFlash", LightColor = new Color("fff1c4"), OmniRange = 3.5f, LightEnergy = 0, ShadowEnabled = false, Visible = false };
        AddChild(_flash);
    }

    /// <summary>True while the jet, its drips or the cork are still going.</summary>
    public bool Busy => _age < _look.Seconds + _look.Lifetime + DripSeconds + 0.5;

    /// <summary>How long this spray's jet lasts.</summary>
    public double JetSeconds => _look.Seconds;

    /// <summary>Seconds since the cork popped.</summary>
    public double Age => _age;

    private static GpuParticles3D Emitter(string name, int amount, ParticleProcessMaterial process, Mesh draw) => new()
    {
        Name = name,
        Amount = amount,
        LocalCoords = false,
        Emitting = false,
        // Simulated in 60 Hz steps whatever the frame rate, so the jet is a stream, not a clump per frame, even in a
        // slow software render (and --fixed-fps captures look like the real thing).
        FixedFps = 60,
        Interpolate = true,
        ProcessMaterial = process,
        DrawPass1 = draw,
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        VisibilityAabb = new Aabb(new Vector3(-20, -12, -20), new Vector3(40, 30, 40)),
    };

    /// <summary>Pops the cork and starts the jet at <paramref name="strength"/> (0.15..1).</summary>
    public void Fire(double strength)
    {
        _look = SprayLook.For(strength);
        _age = 0;
        var s = (float)_look.Strength;

        _foamProcess.InitialVelocityMin = (float)_look.SpeedMin;
        _foamProcess.InitialVelocityMax = (float)_look.SpeedMax;
        _foamProcess.Spread = (float)_look.Spread;
        _foamProcess.ScaleMin = (float)_look.Size * 0.6f;
        _foamProcess.ScaleMax = (float)_look.Size * 1.5f;
        _foam.Lifetime = _look.Lifetime;
        _foam.AmountRatio = _look.Blobs / (float)SprayLook.MaxBlobs;

        _dropletProcess.InitialVelocityMin = (float)_look.SpeedMin * 0.9f;
        _dropletProcess.InitialVelocityMax = (float)_look.SpeedMax * 1.15f;
        _dropletProcess.Spread = (float)_look.Spread + 5;
        _droplets.Lifetime = _look.Lifetime * 0.95;
        _droplets.AmountRatio = 0.12f + (0.33f * s);

        _mistProcess.InitialVelocityMax = 0.6f + (2.2f * s);
        _mistProcess.ScaleMax = 0.35f + (0.9f * s);
        _mist.Lifetime = 0.7 + (0.8 * s);
        _mist.AmountRatio = 0.4f + (0.6f * s);

        foreach (var e in new[] { _foam, _droplets, _mist, _drips })
        {
            e.Restart();
            e.Emitting = true;
        }

        // The cork: off the mouth along the jet, tumbling.
        _cork.GlobalTransform = GlobalTransform;
        _cork.Visible = true;
        _corkFlying = true;
        var rng = new RandomNumberGenerator();
        _corkVelocity = (GlobalBasis.Y.Normalized() * (float)_look.CorkSpeed) + new Vector3(rng.RandfRange(-0.6f, 0.6f), rng.RandfRange(0.4f, 1.4f), rng.RandfRange(-0.6f, 0.6f));
        _corkSpin = new Vector3(rng.RandfRange(-25, 25), rng.RandfRange(-10, 10), rng.RandfRange(-25, 25));

        _burst.Visible = true;
        _flash.Visible = true;
        _flash.LightEnergy = (float)_look.Flash;
    }

    /// <summary>Stops everything at once (a new race, the podium leaving the air).</summary>
    public void Stop()
    {
        _age = double.PositiveInfinity;
        foreach (var e in new[] { _foam, _droplets, _mist, _drips })
        {
            e.Emitting = false;
            e.Restart();
            e.Emitting = false;
        }
        _cork.Visible = false;
        _corkFlying = false;
        _burst.Visible = false;
        _flash.Visible = false;
    }

    public override void _Process(double delta)
    {
        if (double.IsPositiveInfinity(_age))
        {
            return;
        }
        _age += delta;
        if (_foam.Emitting && _age >= _look.Seconds)
        {
            _foam.Emitting = false;
            _droplets.Emitting = false;
        }
        if (_mist.Emitting && _age >= _look.Seconds * 0.8)
        {
            _mist.Emitting = false;
        }
        if (_drips.Emitting && _age >= _look.Seconds + DripSeconds)
        {
            _drips.Emitting = false;
        }

        // Pop flash: a bright star that blooms and fades in a tenth of a second.
        var flash = (float)Math.Max(0, 1 - (_age / 0.14));
        _flash.LightEnergy = (float)_look.Flash * flash * flash;
        _flash.Visible = flash > 0;
        _burst.Visible = flash > 0;
        if (flash > 0)
        {
            var k = 0.15f + (0.55f * (1 - flash)) * (0.5f + (float)_look.Strength);
            _burst.Scale = new Vector3(k, k, k);
            ((StandardMaterial3D)_burst.MaterialOverride).AlbedoColor = new Color(1, 0.96f, 0.8f, flash);
        }

        if (_corkFlying)
        {
            var dt = (float)delta;
            _corkVelocity += new Vector3(0, -9.81f, 0) * dt;
            var xf = _cork.GlobalTransform;
            xf.Origin += _corkVelocity * dt;
            xf.Basis = xf.Basis.Rotated(_corkSpin.Normalized(), _corkSpin.Length() * dt).Orthonormalized();
            _cork.GlobalTransform = xf;
            if (_age > 2.2 || xf.Origin.Y < GlobalPosition.Y - 4)
            {
                _corkFlying = false;
                _cork.Visible = false;
            }
        }
        if (!Busy)
        {
            _age = double.PositiveInfinity;
        }
    }
}
