using System.Diagnostics;
using System.Text.Json;
using ArcBox.Host.Games.Redline.Camera;
using ArcBox.Host.Games.Redline.Cars;
using ArcBox.Host.Games.Redline.Hud;
using ArcBox.Host.Games.Redline.World;
using ArcBox.Host.Shell;
using ArcBox.Sdk;
using Godot;
using Redline.Logic;
using Redline.Presentation;
using Redline.Presentation.Hud;

namespace ArcBox.Host.Games.Redline;

/// <summary>
/// REDLINE's TV scene (spec §4.8–4.9): Circuit of the Americas with the 22 procedural 2026 cars, rendered from the latest
/// <see cref="RedlineHostView"/>. It never decides anything: car slots come from <see cref="Formation"/>, Reveal motion
/// from <see cref="RevealMotion"/>, shots from <see cref="CameraDirector"/>.
/// <para>
/// HUD seam (Task 6): <see cref="Hud"/> gets every view through <see cref="RedlineHud.Render"/> and, every frame,
/// <see cref="RedlineHud.UpdateTags"/> with <see cref="ActiveCamera"/> and <see cref="Cars"/>.
/// </para>
/// </summary>
public partial class RedlineScene : Node3D, IGameScene
{
    /// <summary>Seconds into Reveal when the camera cuts to the follow-up shot.</summary>
    public const double FollowUpSeconds = 3.5;
    /// <summary>Phase screenshots wait this long so cuts, blends and launches have settled.</summary>
    private const double ShotDelay = 0.9;

    private readonly Dictionary<string, CarNode> _cars = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Node3D> _carNodes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _spun = new(StringComparer.Ordinal);
    private IReadOnlyDictionary<string, CarSlot> _from = new Dictionary<string, CarSlot>();
    private IReadOnlyDictionary<string, CarSlot> _to = new Dictionary<string, CarSlot>();

    private Screenshotter? _shots;
    private CircuitAssets? _assets;
    private BuiltWorld? _world;
    private CameraRig _rig = null!;
    private RedlineHud _hud = null!;
    private Preset _preset = Preset.Ultra;

    private RedlineHostView? _view;
    private double _time;
    private double _phaseStart;
    private bool _followUp;
    private readonly List<(double At, string Label)> _pendingShots = [];

    /// <summary>The camera on air.</summary>
    public Camera3D ActiveCamera => _rig.Camera;

    /// <summary>Every car by driver code (origin on the ground between the axles).</summary>
    public IReadOnlyDictionary<string, Node3D> Cars => _carNodes;

    /// <summary>The HUD layer (Task 6 fills it in).</summary>
    public RedlineHud Hud => _hud;

    /// <summary>The latest view rendered, or null before the first.</summary>
    public RedlineHostView? View => _view;

    public void SetShots(Screenshotter? shots) => _shots = shots;

    public override void _Ready()
    {
        _preset = RenderPreset.Parse(AppArgs.Parse(OS.GetCmdlineUserArgs()).Preset);
        _rig = new CameraRig { Name = "CameraRig" };
        AddChild(_rig);
        _hud = new RedlineHud();
        AddChild(_hud);
    }

    /// <summary>
    /// Shows <paramref name="view"/>. The scene keeps its own typed copy of the view it rendered last, which is what
    /// <paramref name="previous"/> holds, so only <paramref name="view"/> is read.
    /// </summary>
    public void Render(JsonElement view, JsonElement? previous)
    {
        var v = view.Deserialize<RedlineHostView>(ArcJson.Options);
        if (v is null)
        {
            return;
        }
        EnsureWorld(v.CircuitId);

        var before = _view;
        _view = v;
        var newPhase = before is null || before.Phase != v.Phase || before.Round != v.Round;
        var lap = _assets!.Circuit.LapLength;

        if (newPhase)
        {
            _phaseStart = _time;
            _followUp = false;
            _pendingShots.Clear();
            var label = v.Phase.ToString().ToLowerInvariant();
            QueueShot(ShotDelay, label);
            if (v.Phase == RacePhase.Chequered)
            {
                QueueShot(CameraRig.FinishLineSeconds + 1.5, $"{label}-podium");
            }
            if (v.Phase == RacePhase.Reveal)
            {
                for (var t = 2; t <= 6; t += 2)
                {
                    QueueShot(t, $"{label}-{t}s");
                }
            }
            GD.Print($"REDLINE {v.Phase} round {v.Round}");
        }

        _to = Formation.Current(v, lap);
        if (v.Phase == RacePhase.Reveal)
        {
            if (newPhase)
            {
                _from = Formation.RoundStart(v, lap);
                _spun.Clear();
                foreach (var d in v.Drivers.Where(d => d.Outcome == RoundOutcome.SpunOut))
                {
                    _spun.Add(d.Code);
                }
            }
        }
        else
        {
            _from = _to;
            _spun.Clear();
        }
        PlaceCars(0);

        // A repeated Reveal view (e.g. after a reconnect) must not undo the follow-up cut.
        var shot = v.Phase == RacePhase.Reveal && _followUp ? CameraDirector.RevealFollowUp(v) : CameraDirector.Pick(v, before);
        _rig.SetShot(shot);
        if (v.Phase == RacePhase.DriverSelect && !newPhase && shot is LineupShot { Subject: not null })
        {
            QueueShot(_time - _phaseStart + 1.2, "driverselect-claim");
        }
        _rig.Advance(_time);

        _hud.Render(v, before);
    }

    public override void _Process(double delta)
    {
        _time += delta;
        if (_view is null || _assets is null)
        {
            return;
        }

        var phaseTime = _time - _phaseStart;
        PlaceCars(delta);

        if (_view.Phase == RacePhase.Reveal && !_followUp && phaseTime >= FollowUpSeconds)
        {
            _followUp = true;
            _rig.SetShot(CameraDirector.RevealFollowUp(_view));
        }

        _world!.Gantry.SetLit(StartLights.Lit(_view.Phase, phaseTime));
        _rig.Advance(_time);
        _hud.UpdateTags(ActiveCamera, _carNodes);

        for (var i = _pendingShots.Count - 1; i >= 0; i--)
        {
            if (phaseTime >= _pendingShots[i].At)
            {
                _shots?.Capture(_pendingShots[i].Label);
                _pendingShots.RemoveAt(i);
            }
        }
    }

    /// <summary>Captures <paramref name="label"/> once the phase has run <paramref name="at"/> seconds.</summary>
    private void QueueShot(double at, string label)
    {
        if (_shots is not null)
        {
            _pendingShots.Add((at, label));
        }
    }

    /// <summary>Puts every car where the view says it is now (Reveal: along its tween), rolling wheels by <paramref name="delta"/>.</summary>
    private void PlaceCars(double delta)
    {
        var samples = _assets!.Samples;
        var reveal = _view!.Phase == RacePhase.Reveal;
        var t = _time - _phaseStart;
        foreach (var (code, car) in _cars)
        {
            if (!_to.TryGetValue(code, out var to))
            {
                car.Visible = false;
                continue;
            }
            car.Visible = true;
            if (reveal && _from.TryGetValue(code, out var from))
            {
                var m = RevealMotion.Sample(from, to, _spun.Contains(code), t);
                car.Place(samples, m.RaceMeters, m.Lateral, m.Yaw);
                car.Roll(m.Speed * Math.Cos(m.Yaw) * delta);
                car.Smoke((float)m.Smoke);
            }
            else
            {
                car.Place(samples, to.RaceMeters, to.Lateral, 0);
                car.Smoke(0);
            }
        }
    }

    private void EnsureWorld(string circuitId)
    {
        if (_world is not null)
        {
            return;
        }
        var sw = Stopwatch.StartNew();
        var prewarm = RedlineAssets.Prewarm(circuitId);
        prewarm.Wait();
        var cpu = sw.ElapsedMilliseconds;

        _assets = RedlineAssets.Circuit(circuitId);
        _world = WorldBuilder.Build(_assets, _preset);
        AddChild(_world.Root);
        MoveChild(_world.Root, 0);
        RenderPreset.Apply(_world.Environment.Environment, _world.Sun, GetViewport(), _preset);

        var cars = new Node3D { Name = "Cars" };
        _world.Root.AddChild(cars);
        foreach (var code in RedlineAssets.Cars.Keys)
        {
            var car = CarFactory.Create(code);
            _cars[code] = car;
            _carNodes[code] = car;
            cars.AddChild(car);
        }

        _rig.Setup(_assets, () => _cars, () => _view?.Drivers.Select(d => d.Code).ToList() ?? []);
        var timings = string.Join(", ", RedlineAssets.Timings.Select(kv => $"{kv.Key} {kv.Value} ms"));
        GD.Print($"REDLINE world ready in {sw.ElapsedMilliseconds} ms (geometry {cpu} ms: {timings}; preset {_preset})");
    }
}
