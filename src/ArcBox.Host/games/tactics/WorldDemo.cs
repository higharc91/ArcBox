using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using Godot;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Board;
using Tactics.Presentation.World;

namespace ArcBox.Host.Games.Tactics;

/// <summary>
/// Dev only: <c>--world-demo</c>. Opens a whole region the way the TV will (<see cref="WorldCanvas"/>) and walks a stand-in the length of it at twice
/// walking pace, the camera on its heels from the moment the fade starts to lift, so the blocks still to record are recorded during the walk. It prints what
/// each step cost in <c>WORLD</c> lines. Then, with <c>--shots</c>, it goes to each of <see cref="WorldSpots"/> and saves <c>world-&lt;spot&gt;.png</c>,
/// and it quits. The spec is <c>&lt;region&gt;|hilly[:&lt;block tiles&gt;]</c>.
/// </summary>
internal sealed partial class WorldDemo : Node
{
    private const string Stress = "hilly";
    private const float StepsPerSecond = 2 / 0.3f;      // twice the walking pace of a tile per 0.3 s
    private const double RegularFrameMs = 16;           // the task's line: a frame over this, regularly, stops the work
    private const double ColdSeconds = 1;               // frames this close to the start have the JIT in them
    private const int SettleFrames = 3;                 // before a shot, for the overlay to have drawn what the shot is of
    private const double SecondLook = 0.3;              // between two shots of water, to see that it moved

    private enum Stage { Loading, Crossing, Spots, Done }

    private readonly record struct Frame(double Seconds, double Ms, double RenderCpu, double RenderGpu, double Record, double Query, double Draw, int Items, double DrawCalls);

    private readonly string _spec;
    private readonly string _pack;
    private readonly string? _shots;
    private readonly List<Frame> _frames = [];

    private WorldCanvas _canvas = null!;
    private UnitArt _art = null!;
    private WorldGround? _ground;
    private ImmutableArray<Tile> _route = [];
    private ImmutableArray<WorldSpot> _spots = [];
    private Stage _stage = Stage.Loading;
    private long _baselineManaged;
    private double _baselineEngine;
    private double _clock;
    private double _crossing;
    private float _steps;
    private int _spot;
    private int _settling;
    private double _waited;
    private bool _secondShot;
    private bool _logged;

    public WorldDemo(string spec, string packRoot, string? shots)
    {
        _shots = shots;
        _spec = spec;
        _pack = packRoot;
    }

    public override void _Ready()
    {
        var size = GetWindow().Size;
        Log($"window {size.X}x{size.Y} at {GetWindow().Position}, spec {_spec}, optimised {Optimised()}, adapter {RenderingServer.GetVideoAdapterName()}");
        _baselineManaged = GC.GetTotalMemory(true);
        _baselineEngine = Performance.GetMonitor(Performance.Monitor.MemoryStatic);
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
        _art = UnitArt.Load(_pack);
        var parts = _spec.Split(':');
        var blocks = parts.Length > 1 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : WorldTuning.BlockSize;
        _canvas = new WorldCanvas(_pack, () => Ground(parts[0]), blocks);
        AddChild(_canvas);
    }

    private WorldGround Ground(string region)
    {
        var ground = region == Stress ? DemoRegion.Hilly(160, 120) : DemoRegion.FromPack(_pack, region);
        var start = region == Stress ? new Tile(80, 60) : RegionStart(region);
        _route = WorldRoute.Across(ground.Terrain, start, ClassDef.DefaultJump);
        _spots = WorldSpots.Of(new BoardTerrain(ground.Map, null), ground.Terrain);
        _ground = ground;
        return ground;
    }

    private Tile RegionStart(string region)
    {
        var file = System.IO.Path.Combine(_pack, "rules", "world", $"{region}.json");
        using var document = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(file));
        var start = document.RootElement.GetProperty("spawns").GetProperty("start");
        return new Tile(start[0].GetInt32(), start[1].GetInt32());
    }

    private static bool Optimised()
        => typeof(WorldDemo).Assembly.GetCustomAttributes(typeof(DebuggableAttribute), false).OfType<DebuggableAttribute>().All(d => !d.IsJITOptimizerDisabled);

    public override void _Process(double delta)
    {
        _clock += delta;
        switch (_stage)
        {
            case Stage.Loading:
                Open();
                break;
            case Stage.Crossing:
                Cross(delta);
                break;
            case Stage.Spots:
                Visit(delta);
                break;
            case Stage.Done:
                GetTree().Quit();
                break;
        }
    }

    private void Open()
    {
        if (_canvas.Now == WorldCanvas.Stage.Failed)
        {
            _stage = Stage.Done;
            return;
        }
        if (_canvas.Plan is null)
        {
            return;
        }
        if (!_logged)
        {
            _logged = true;
            LogPlan();
            Walk(0);    // behind the fade, so the view's blocks are the right ones and the walker's art is in
        }
        if (_canvas.Now == WorldCanvas.Stage.Open)
        {
            var load = _canvas.LoadMs;
            Log($"textures {_canvas.Plan.Images.Length} in {load.Textures:0} ms, worst {load.Worst:0.0} ms; binding items (and baking glows) {load.Bind:0} ms; {_canvas.Waiting} blocks still to record at reveal");
            _stage = Stage.Crossing;
        }
    }

    private void LogPlan()
    {
        var plan = _canvas.Plan!;
        var lit = plan.Blocks.Select(b => b.Lit.Items.Length).ToArray();
        var tiles = plan.Terrain.Map.Width * plan.Terrain.Map.Height;
        Log($"build {_canvas.BuildTimes}");
        Log($"items {lit.Sum():N0} lit; {tiles:N0} tiles, {plan.Blocks.Length} blocks, block items median {Median(lit.Select(i => (double)i)):0}, most {lit.Max()}; "
            + $"{plan.Lighting.Lights.Length} lights, active {plan.Lighting.Active}; {plan.Paths.Length} images; route {_route.Length} tiles; spots {string.Join(' ', _spots.Select(s => $"{s.Name}@{s.Look.X},{s.Look.Y}"))}");
        Log($"memory after build: {MemoryText()}");
    }

    /// <summary>Puts the walker <paramref name="steps"/> along the route and the camera on it.</summary>
    private UnitPlace Walk(float steps)
    {
        var terrain = _canvas.Plan!.Terrain;
        var place = WorldRoute.At(terrain, _route, steps);
        var heading = WorldRoute.Heading(_route, steps);
        _canvas.Place(WorldView.Centred(place.Feet));
        _canvas.Aim(place.Map, heading * StepsPerSecond);
        _canvas.ShowUnits(DemoRegion.StandIn(terrain, _art, place, heading, _clock), _art);
        return place;
    }

    private void Cross(double delta)
    {
        var rid = GetViewport().GetViewportRid();
        var (query, draw) = (_canvas.QueryMs, _canvas.OverDrawMs);
        _frames.Add(new Frame(_crossing, delta * 1000, RenderingServer.ViewportGetMeasuredRenderTimeCpu(rid) + RenderingServer.GetFrameSetupTimeCpu(),
            RenderingServer.ViewportGetMeasuredRenderTimeGpu(rid), _canvas.RecordMs, query, draw, _canvas.OverItems,
            Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame)));
        _crossing += delta;
        _steps = (float)_crossing * StepsPerSecond;
        Walk(_steps);
        if (_steps < _route.Length - 1)
        {
            return;
        }
        LogCrossing();
        Shoot($"world-{_spec.Replace(':', '-')}.png");
        _stage = _shots is null ? Stage.Done : Stage.Spots;
    }

    private void LogCrossing()
    {
        var all = _frames;
        var warm = all.Where(f => f.Seconds >= ColdSeconds).ToArray();
        var recording = all.Where(f => f.Record > 0).ToArray();
        var over = all.Where(f => f.Ms > RegularFrameMs).ToArray();
        Log($"crossing {_route.Length} tiles at {StepsPerSecond:0.0} tiles/s, {_crossing:0.0} s, {all.Count} frames: frame ms {Spread(all.Select(f => f.Ms))}");
        Log($"crossing after the first {ColdSeconds:0} s: frame ms {Spread(warm.Select(f => f.Ms))}");
        Log($"frames over {RegularFrameMs:0} ms: {over.Length} of {all.Count}" + (over.Length == 0 ? "" : $", at {string.Join(' ', over.Take(12).Select(f => $"{f.Seconds:0.00}s={f.Ms:0.0}"))}, {over.Count(f => f.Record > 0)} while recording"));
        Log($"recording during the walk: {recording.Length} frames, ms a frame {(recording.Length == 0 ? "none" : Spread(recording.Select(f => f.Record)))}");
        if (recording.Length > 0)
        {
            Log($"frames while recording: frame ms {Spread(recording.Select(f => f.Ms))}");
        }
        Log($"_Draw per block, first time: {Spread(_canvas.BlockMs)}, sum {_canvas.BlockMs.Sum():0.0} ms");
        Log($"render CPU ms {Spread(all.Select(f => f.RenderCpu))}; GPU ms {Spread(all.Select(f => f.RenderGpu))}; at most {all.Max(f => f.DrawCalls):N0} draw calls");
        Log($"overlay query ms {Spread(all.Select(f => f.Query))}; draw ms {Spread(all.Select(f => f.Draw))}; items {Spread(all.Select(f => (double)f.Items))}");
        Log($"blocks recorded once, whatever the walk did: {_canvas.RecordedOnce}");
        Log($"memory at end of crossing: {MemoryText()}");
    }

    private void Visit(double delta)
    {
        if (_spot >= _spots.Length)
        {
            _stage = Stage.Done;
            return;
        }
        var spot = _spots[_spot];
        var terrain = _canvas.Plan!.Terrain;
        var place = UnitPlace.On(terrain, spot.Stand);
        _canvas.Place(WorldView.Centred(terrain.Centre(spot.Look)));
        _canvas.ShowUnits(DemoRegion.StandIn(terrain, _art, place, new System.Numerics.Vector2(1, 0), _clock), _art);
        if (++_settling < SettleFrames)
        {
            return;
        }
        if (!_secondShot)
        {
            Shoot($"world-{spot.Name}.png");
            _secondShot = true;
            _waited = 0;
            return;
        }
        if (terrain.Look(spot.Look).Water)
        {
            _waited += delta;
            if (_waited < SecondLook)
            {
                return;
            }
            Shoot($"world-{spot.Name}-later.png");
        }
        _spot++;
        _settling = 0;
        _secondShot = false;
    }

    /// <summary>With <c>--shots</c>, the picture. It stalls a frame, so none is taken while the walk is being timed.</summary>
    private void Shoot(string name)
    {
        if (_shots is not null)
        {
            var file = System.IO.Path.Combine(_shots, name);
            GetViewport().GetTexture().GetImage().SavePng(file);
            Log($"shot {file}");
        }
    }

    private string MemoryText()
    {
        GC.Collect();
        var managed = GC.GetTotalMemory(true);
        var engine = Performance.GetMonitor(Performance.Monitor.MemoryStatic);
        var textures = Performance.GetMonitor(Performance.Monitor.RenderTextureMemUsed);
        var buffers = Performance.GetMonitor(Performance.Monitor.RenderBufferMemUsed);
        return $"managed {Mb(managed):0.0} MB (+{Mb(managed - _baselineManaged):0.0}), engine {Mb(engine):0.0} MB (+{Mb(engine - _baselineEngine):0.0}), "
            + $"textures {Mb(textures):0.0} MB, buffers {Mb(buffers):0.0} MB, process {Mb(System.Environment.WorkingSet):0} MB";
    }

    private static double Mb(double bytes) => bytes / (1024 * 1024);

    private static double Median(IEnumerable<double> values) => Percentile(values, 0.5);

    private static double Percentile(IEnumerable<double> values, double share)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length == 0 ? 0 : sorted[(int)Math.Min(sorted.Length - 1, sorted.Length * share)];
    }

    private static string Spread(IEnumerable<double> values)
    {
        var all = values.ToArray();
        return $"median {Median(all):0.00}, p95 {Percentile(all, 0.95):0.00}, p99 {Percentile(all, 0.99):0.00}, worst {(all.Length == 0 ? 0 : all.Max()):0.00}";
    }

    private static void Log(string line) => GD.Print($"WORLD {line}");
}
