using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using Godot;
using Tactics.Logic;
using Tactics.Presentation.Board;
using Tactics.Presentation.World;

namespace ArcBox.Host.Games.Tactics;

/// <summary>
/// Dev only: <c>--world-demo</c>. Draws a whole region the way the TV will, in the order the plan's loading has it, and prints what each step
/// costs in <c>WORLD</c> lines: the off-thread build, the textures one a frame, every block at the frame budget nearest first, a pan
/// at twice walking pace, then every block once more with the JIT warm, then it quits. Water is frozen at frame 0, and nothing is on
/// top: the frame loop, the overlay and the halos are the world's canvas's, later. The spec is <c>&lt;region&gt;|hilly[:&lt;block tiles&gt;]</c>.
/// </summary>
internal sealed partial class WorldDemo : Node
{
    private const string Stress = "hilly";
    private const float PanTilesPerSecond = 2 / 0.3f;       // twice the walking pace of a tile per 0.3 s
    private const double PanSeconds = 15;
    private const double SettleSeconds = 1;                 // frames to skip, after recording, before a pan is timed

    private enum Stage { Building, Textures, Recording, Settling, Panning, Rerecording, Done }

    private sealed record Built(WorldPlan Plan, object[][] Items, string Times);

    private readonly string _spec;
    private readonly string _pack;
    private readonly string? _shots;
    private readonly BoardPainter _painter = new();
    private readonly List<WorldBlock> _blocks = [];
    private readonly List<double> _frames = [];
    private readonly List<double> _renderCpu = [];
    private readonly List<double> _renderGpu = [];
    private readonly List<double> _draws = [];
    private readonly List<double> _frameDraws = [];
    private readonly List<WorldBlock> _pending = [];

    private Stage _stage = Stage.Building;
    private Task<Built> _building = null!;
    private WorldPlan _plan = null!;
    private Node2D _world = null!;
    private BlockQueue _queue = null!;
    private Vector2 _start;
    private Vector2 _end;
    private int _texture;
    private int _next;
    private double _elapsed;
    private double _worstTexture;
    private double _texturesMs;
    private double _msPerItem = 0.005;
    private long _baselineManaged;
    private double _baselineStatic;
    private double _maxDrawCalls;
    private double _maxPrimitives;
    private int _mostChunks;

    public WorldDemo(string spec, string packRoot, string? shots)
    {
        _shots = shots;
        _spec = spec;
        _pack = packRoot;
        _painter.Root = packRoot;
    }

    public override void _Ready()
    {
        var size = GetWindow().Size;
        Log($"window {size.X}x{size.Y} at {GetWindow().Position}, spec {_spec}, optimised {Optimised()}, adapter {RenderingServer.GetVideoAdapterName()}");
        _baselineManaged = GC.GetTotalMemory(true);
        _baselineStatic = Performance.GetMonitor(Performance.Monitor.MemoryStatic);
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
        _building = Task.Run(Build);
    }

    private static bool Optimised()
        => typeof(WorldDemo).Assembly.GetCustomAttributes(typeof(DebuggableAttribute), false).OfType<DebuggableAttribute>().All(d => !d.IsJITOptimizerDisabled);

    private Built Build()
    {
        var parts = _spec.Split(':');
        var size = parts.Length > 1 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : WorldTuning.BlockSize;
        var clock = Stopwatch.StartNew();
        var art = ArtIndex.Load(_pack);
        var manifests = clock.Elapsed.TotalMilliseconds;
        var ground = parts[0] == Stress ? DemoRegion.Hilly(160, 120) : DemoRegion.FromPack(_pack, parts[0]);
        var parsed = clock.Elapsed.TotalMilliseconds;
        var terrain = new BoardTerrain(ground.Map, null);
        var plan = WorldPlan.Build(terrain, art, size);
        var planned = clock.Elapsed.TotalMilliseconds;
        var baked = plan.Lighting.Active ? plan.Lighting : null;
        object[][] items = [.. plan.Blocks.Select(b => b.Lit.Items.Select(i => BoardPainter.Prepare(i, baked)).ToArray())];
        var prepared = clock.Elapsed.TotalMilliseconds;
        return new Built(plan, items,
            $"manifests {manifests:0} ms, ground {parsed - manifests:0} ms, plan+light {planned - parsed:0} ms, prepare {prepared - planned:0} ms, total {prepared:0} ms");
    }

    public override void _Process(double delta)
    {
        switch (_stage)
        {
            case Stage.Building when _building.IsCompleted:
                FinishBuild();
                break;
            case Stage.Textures:
                LoadOneTexture();
                break;
            case Stage.Recording:
                _elapsed += delta;
                _frames.Add(delta * 1000);
                TallyDraws();
                QueueWithinBudget();
                if (_queue.Count == 0 && _pending.Count == 0)
                {
                    FinishRecording();
                }
                break;
            case Stage.Settling:
                _elapsed += delta;
                if (_elapsed >= SettleSeconds)
                {
                    _elapsed = 0;
                    _frames.Clear();
                    _stage = Stage.Panning;
                }
                break;
            case Stage.Panning:
                Pan(delta);
                break;
            case Stage.Rerecording:
                _elapsed += delta;
                _frames.Add(delta * 1000);
                TallyDraws();
                if (_next < _blocks.Count)
                {
                    _pending.Add(_blocks[_next]);
                    _blocks[_next++].Record();
                }
                else if (_pending.Count == 0)
                {
                    FinishRerecording();
                }
                break;
            case Stage.Done:
                GetTree().Quit();
                break;
        }
    }

    private void FinishBuild()
    {
        var built = _building.Result;
        _plan = built.Plan;
        var plain = _plan.Blocks.Select(b => b.Plan.Items.Length).ToArray();
        var lit = built.Items.Select(i => i.Length).ToArray();
        var tiles = _plan.Terrain.Map.Width * _plan.Terrain.Map.Height;
        var faces = _plan.Blocks.Sum(b => b.Plan.Items.Count(i => i is Face));
        Log($"build {built.Times}");
        Log($"items {plain.Sum():N0} planned ({(double)plain.Sum() / tiles:0.00} a tile, {faces:N0} faces), {lit.Sum():N0} lit; {tiles:N0} tiles, {_plan.Blocks.Length} blocks; "
            + $"block items median {Median(lit):0}, most {lit.Max()}; {_plan.Lighting.Lights.Length} lights, active {_plan.Lighting.Active}; {_plan.Paths.Length} images");
        Log($"memory after build: {MemoryText()}");

        _world = new Node2D
        {
            Scale = new Vector2(WorldTuning.Scale, WorldTuning.Scale),
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            TextureRepeat = CanvasItem.TextureRepeatEnum.Enabled,
        };
        var layer = new CanvasLayer { Layer = -1 };
        layer.AddChild(_world);
        AddChild(layer);
        foreach (var block in _plan.Blocks)
        {
            var node = new WorldBlock(_painter, built.Items[block.Index], block.Z);
            _blocks.Add(node);
            _world.AddChild(node);
        }

        var region = _plan.Terrain.Map;
        _start = new Vector2(region.Width * 0.2f, region.Height * 0.55f);
        _end = _start + new Vector2(1, 0.02f) * PanTilesPerSecond * (float)PanSeconds;
        Look(_start);
        _elapsed = 0;
        _stage = Stage.Textures;
        _queue = new BlockQueue(_plan.Blocks);
    }

    private void LoadOneTexture()
    {
        if (_texture >= _plan.Paths.Length)
        {
            var bind = Stopwatch.StartNew();
            for (var i = 0; i < _blocks.Count; i++)
            {
                _blocks[i].Bind();
            }
            Log($"textures {_plan.Paths.Length} in {_texturesMs:0} ms, worst {_worstTexture:0.0} ms; binding items (and baking glows) {bind.Elapsed.TotalMilliseconds:0} ms");
            _elapsed = 0;
            _frames.Clear();
            _stage = Stage.Recording;
            return;
        }
        var clock = Stopwatch.StartNew();
        _painter.Load(_plan.Paths[_texture++]);
        var ms = clock.Elapsed.TotalMilliseconds;
        _texturesMs += ms;
        _worstTexture = Math.Max(_worstTexture, ms);
    }

    /// <summary>A block is recorded when the frame ends, so this queues what the last blocks' cost per item says will fit in the budget, and always one.</summary>
    private void QueueWithinBudget()
    {
        var planned = 0.0;
        var aim = BlockQueue.Aim(new System.Numerics.Vector2(_start.X, _start.Y), System.Numerics.Vector2.Zero);
        while (_queue.Peek(aim) is { } block)
        {
            var cost = _blocks[block.Index].Items * _msPerItem;
            if (planned > 0 && planned + cost > WorldTuning.BlockBudgetMs)
            {
                return;
            }
            _queue.Next(aim);
            _pending.Add(_blocks[block.Index]);
            _blocks[block.Index].Record();
            planned += cost;
        }
    }

    private void TallyDraws()
    {
        var (frame, items) = (0.0, 0);
        for (var i = _pending.Count - 1; i >= 0; i--)
        {
            if (_pending[i].Drawn)
            {
                var ms = _pending[i].TakeDrawn();
                _draws.Add(ms);
                frame += ms;
                items += _pending[i].Items;
                _pending.RemoveAt(i);
            }
        }
        if (items > 0)
        {
            _frameDraws.Add(frame);
            _msPerItem = frame / items;
        }
    }

    private void FinishRecording()
    {
        Log($"record {_blocks.Count} blocks in {_elapsed:0.00} s over {_frames.Count} frames, at most {_frameDraws.Max():0.00} ms a frame; "
            + $"frame ms {Spread(_frames)}");
        Log($"_Draw cold: {DrawText()}");
        Log($"memory after record: {MemoryText()}");
        _draws.Clear();
        _frameDraws.Clear();
        _elapsed = 0;
        _stage = Stage.Settling;
    }

    private void Pan(double delta)
    {
        _elapsed += delta;
        _frames.Add(delta * 1000);
        var rid = GetViewport().GetViewportRid();
        _renderCpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeCpu(rid) + RenderingServer.GetFrameSetupTimeCpu());
        _renderGpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeGpu(rid));
        _maxDrawCalls = Math.Max(_maxDrawCalls, Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame));
        _maxPrimitives = Math.Max(_maxPrimitives, Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame));
        var at = Look(_start.Lerp(_end, (float)Math.Min(_elapsed / PanSeconds, 1)));
        var view = new System.Numerics.Vector2(GetWindow().Size.X, GetWindow().Size.Y) / WorldTuning.Scale;
        var middle = BoardProjection.Corner(at.X, at.Y, 0);
        var low = middle - view / 2;
        _mostChunks = Math.Max(_mostChunks, WorldPlan.ChunksIn(new ArtArea(low, low + view)).Length);
        if (_elapsed < PanSeconds)
        {
            return;
        }
        Log($"pan {PanTilesPerSecond:0.0} tiles/s for {_elapsed:0.0} s, {_frames.Count} frames: frame ms {Spread(_frames)}");
        Log($"pan render CPU ms {Spread(_renderCpu)}; GPU ms {Spread(_renderGpu)}; at most {_maxDrawCalls:N0} draw calls, {_maxPrimitives:N0} primitives, {_mostChunks} chunks in view");
        Log($"blocks recorded once, whatever the pan did: {_blocks.All(b => b.Recorded == 1)}");
        Shoot();
        _frames.Clear();
        _elapsed = 0;
        _stage = Stage.Rerecording;
    }

    /// <summary>With <c>--shots</c>, the picture. It stalls a frame, so it's only taken once the pan's timing is done.</summary>
    private void Shoot()
    {
        if (_shots is not null)
        {
            var file = System.IO.Path.Combine(_shots, $"world-{_spec.Replace(':', '-')}.png");
            GetViewport().GetTexture().GetImage().SavePng(file);
            Log($"shot {file}");
        }
    }

    private void FinishRerecording()
    {
        Log($"_Draw warm, one block a frame: {DrawText()}");
        Log($"memory at end: {MemoryText()}");
        _stage = Stage.Done;
    }

    private string DrawText()
    {
        var items = _blocks.Sum(b => b.Items);
        return $"worst {_draws.Max():0.00} ms, median {Median(_draws):0.00} ms, first {_draws[0]:0.00} ms, sum {_draws.Sum():0.0} ms, {_draws.Sum() * 1000 / items:0.00} us an item";
    }

    /// <summary>Centres the view on a map point, in tiles, on whole layout pixels. Returns the point.</summary>
    private Vector2 Look(Vector2 tile)
    {
        var art = BoardProjection.Corner(tile.X, tile.Y, 0);
        var window = GetViewport().GetVisibleRect().Size;
        _world.Position = (window / 2 - new Vector2(art.X, art.Y) * WorldTuning.Scale).Round();
        return tile;
    }

    private string MemoryText()
    {
        GC.Collect();
        var managed = GC.GetTotalMemory(true);
        var engine = Performance.GetMonitor(Performance.Monitor.MemoryStatic);
        var textures = Performance.GetMonitor(Performance.Monitor.RenderTextureMemUsed);
        var buffers = Performance.GetMonitor(Performance.Monitor.RenderBufferMemUsed);
        return $"managed {Mb(managed):0.0} MB (+{Mb(managed - _baselineManaged):0.0}), engine {Mb(engine):0.0} MB (+{Mb(engine - _baselineStatic):0.0}), "
            + $"textures {Mb(textures):0.0} MB, buffers {Mb(buffers):0.0} MB, process {Mb(System.Environment.WorkingSet):0} MB";
    }

    private static double Mb(double bytes) => bytes / (1024 * 1024);

    private static double Median(IEnumerable<double> values) => Percentile(values, 0.5);

    private static double Median(IEnumerable<int> values) => Percentile(values.Select(v => (double)v), 0.5);

    private static double Percentile(IEnumerable<double> values, double share)
    {
        var sorted = values.Order().ToArray();
        return sorted[(int)Math.Min(sorted.Length - 1, sorted.Length * share)];
    }

    private static string Spread(IReadOnlyCollection<double> values)
        => $"median {Median(values):0.00}, p95 {Percentile(values, 0.95):0.00}, p99 {Percentile(values, 0.99):0.00}, worst {values.Max():0.00}";

    private static void Log(string line) => GD.Print($"WORLD {line}");
}
