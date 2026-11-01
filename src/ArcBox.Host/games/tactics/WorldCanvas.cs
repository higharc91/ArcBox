using System.Collections.Immutable;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Tactics.Logic;
using Tactics.Presentation.Board;
using Tactics.Presentation.World;

namespace ArcBox.Host.Games.Tactics;

/// <summary>
/// A whole region on the TV: cached <see cref="WorldBlock"/>s, with a <see cref="BoardCanvas"/> over them for what moves. It's a layer of its own, under
/// the UI, as a block's Z sorts across its whole layer and would otherwise paint over the HUD.
/// <para>
/// It opens behind a fade. The plan and its baked colours are built off the main thread, then textures load one a frame, then the blocks in view are
/// recorded, and only then does the fade lift. The rest are recorded at <see cref="WorldTuning.BlockBudgetMs"/> a frame, nearest to where the party is heading
/// first, and a block that's seen before its turn logs <c>WORLD late block</c> and shows nothing.
/// </para>
/// <para>
/// While a fight or a list is up it's <see cref="Suspend"/>ed: nothing draws or runs, and it comes back whole with <see cref="Resume"/>. Leaving the tree ends
/// it. A build still under way is cancelled, what it was doing goes nowhere, and the textures go at once.
/// </para>
/// <para>
/// Each frame, the board over it paints the units with only the items that sort after them and cross their rectangles (<see cref="WorldPlan.Over"/>).
/// Those are the same pixels again, in the same baked light, so nothing but what's behind them changes, except that what's half see-through blends twice.
/// </para>
/// <para>
/// A fight in the world (overworld 5c) draws on a board of its own over that one, in the battle map's pixels, from the region's own tiles
/// (<see cref="StagePlan"/>): <see cref="ShowStage"/>. It shares the world's painter, so its water and flames step with the world's, and its
/// colours are baked in the same light. The camera's shake moves the whole world, board and all. Blocks a fight changes are built again off the
/// main thread (<see cref="Repaint"/>) and swapped in when they're ready.
/// </para>
/// </summary>
internal sealed partial class WorldCanvas : CanvasLayer
{
    /// <summary>How far it's got.</summary>
    public enum Stage
    {
        /// <summary>Off the main thread.</summary>
        Building,

        /// <summary>A texture a frame, behind the fade.</summary>
        Textures,

        /// <summary>The blocks in view, behind the fade.</summary>
        Near,

        /// <summary>Showing: the rest are recorded as the frames allow.</summary>
        Open,

        /// <summary>It can't be built. It shows nothing and never will.</summary>
        Failed,
    }

    private sealed record Built(WorldGround Ground, WorldPlan Plan, object[][] Items, string Times);

    private const int BelowUi = -1;
    private const float ViewMargin = 64;                    // art pixels round the view for halos, the widest sway of any flame
    private const double FirstGuessMsPerItem = 0.005;       // until a block's been timed
    private static readonly Color Dark = new(0.043f, 0.051f, 0.078f);

    private readonly BoardPainter _painter = new();
    private readonly FrameTextures _frames = new();
    private readonly Func<WorldGround> _ground;
    private readonly int _blockSize;
    private readonly List<WorldBlock> _blocks = [];
    private readonly List<WorldBlock> _recording = [];
    private readonly HashSet<int> _late = [];
    private readonly CancellationTokenSource _closing = new();

    private Task<Built> _building = null!;
    private Node2D _world = null!;
    private BoardCanvas _over = null!;
    private ColorRect _back = null!;
    private ColorRect _fade = null!;
    private BlockQueue _queue = null!;
    private object[][] _items = [];
    private int _texture;
    private double _clock;
    private double _fading;
    private double _msPerItem = FirstGuessMsPerItem;
    private int _backAt;
    private int _backCheck;
    private ArtArea _view;
    private System.Numerics.Vector2 _aim;
    private System.Numerics.Vector2? _hurry;     // where to record first this frame, ahead of the party (Shows)
    private BoardCanvas? _stage;
    private StagePlan? _staged;
    private Task<(BlockPlan Block, object[] Items)[]>? _repainting;

    /// <summary>A world that shows what <paramref name="ground"/> builds, off the main thread, with art from <paramref name="packRoot"/>.</summary>
    public WorldCanvas(string packRoot, Func<WorldGround> ground, int blockSize = WorldTuning.BlockSize)
    {
        _painter.Root = packRoot;
        _painter.Frames = _frames;
        _ground = ground;
        _blockSize = blockSize;
        Layer = BelowUi;
    }

    /// <summary>How far it's got.</summary>
    public Stage Now { get; private set; } = Stage.Building;

    /// <summary>The region's ground, once it's built.</summary>
    public WorldGround? Ground { get; private set; }

    /// <summary>The board of a fight in the world, while one is shown.</summary>
    public BoardCanvas? StageBoard => _stage is { Visible: true } stage ? stage : null;

    /// <summary>
    /// Shows a fight's board on the world, as <paramref name="plan"/> has it. The plan's items are prepared and bound again only when it's a new
    /// plan; the units and effects are the caller's, on <see cref="StageBoard"/>.
    /// </summary>
    public BoardCanvas? ShowStage(StagePlan plan)
    {
        if (Plan is null)
        {
            return null;
        }
        if (_stage is null)
        {
            _stage = new BoardCanvas(_painter) { ZIndex = Plan.Blocks.Max(b => b.Z) + 2 };
            _world.AddChild(_stage);
        }
        if (!ReferenceEquals(plan, _staged))
        {
            _staged = plan;
            var baked = plan.Lighting.Active ? plan.Lighting : null;
            var items = plan.Lit.Items.Select(i => BoardPainter.Prepare(i, baked)).ToArray();
            foreach (var item in items)
            {
                _painter.Bind(item);
            }
            _stage.JoinWorld(baked);
            _stage.SetDrawn(items, plan.Lit.Items.Select(i => i.Order));
            _stage.SetFit(new BoardFit(BoardFit.StageOffset(plan.Stage), 1));
        }
        _stage.Clock = _clock;
        _stage.Visible = true;
        _over.SetView(System.Numerics.Vector2.Zero, System.Numerics.Vector2.Zero);    // the stage has the halos round it: they'd show twice
        return _stage;
    }

    /// <summary>Puts the fight's board away, and gives the world its halos back.</summary>
    public void HideStage()
    {
        if (_stage is not { Visible: true } stage)
        {
            return;
        }
        stage.Visible = false;
        _staged = null;
        _over.SetView(_view.Min - new System.Numerics.Vector2(ViewMargin), _view.Max + new System.Numerics.Vector2(ViewMargin));
    }

    /// <summary>True while blocks are being built again: the next repaint waits (<see cref="RepaintQueue"/> keeps what's asked meanwhile).</summary>
    public bool Repainting => _repainting is not null;

    /// <summary>
    /// Builds the blocks <paramref name="work"/> gives (<see cref="StagePlan.WorldBlocks"/>) off the main thread and swaps them in once they're
    /// ready. The plan's own blocks stay as they were, so what the walkers' overlay reads is the region as it is. Only while not <see cref="Repainting"/>.
    /// </summary>
    public void Repaint(Func<ImmutableArray<BlockPlan>> work)
    {
        if (_repainting is not null)
        {
            throw new InvalidOperationException("A repaint is already under way.");
        }
        var baked = Plan?.Lighting is { Active: true } lighting ? lighting : null;
        _repainting = Task.Run(() => work().Select(b => (b, b.Lit.Items.Select(i => BoardPainter.Prepare(i, baked)).ToArray())).ToArray());
    }

    private void SwapRepainted()
    {
        if (_repainting is not { IsCompleted: true } done)
        {
            return;
        }
        _repainting = null;
        if (done.IsCompletedSuccessfully)
        {
            foreach (var (block, items) in done.Result)
            {
                foreach (var item in items)
                {
                    _painter.Bind(item);
                }
                _blocks[block.Index].Replace(items);
            }
            GD.Print($"WORLD repainted {done.Result.Length} blocks round a fight");
        }
        else
        {
            GD.PushError($"The world's blocks can't be built again: {done.Exception?.GetBaseException().Message}");
        }
    }

    /// <summary>The plan, once it's built.</summary>
    public WorldPlan? Plan { get; private set; }

    /// <summary>How the build went.</summary>
    public string BuildTimes { get; private set; } = "";

    /// <summary>How long loading took on the main thread, in milliseconds: texture total, worst texture, binding the items.</summary>
    public (double Textures, double Worst, double Bind) LoadMs { get; private set; }

    /// <summary>True once the fade has lifted.</summary>
    public bool Revealed => Now == Stage.Open && _fade is { Visible: false };

    /// <summary>How far the fade has lifted, 0 to 1: 0 until it's <see cref="Stage.Open"/>, and 1 once it's <see cref="Revealed"/>.</summary>
    public double Lifted => Now != Stage.Open ? 0 : _fade is { Visible: true } ? Math.Min(_fading / WorldTuning.FadeSeconds, 1) : 1;

    /// <summary>The blocks not yet recorded.</summary>
    public int Waiting => (_queue?.Count ?? 0) + _recording.Count;

    /// <summary>How long recording blocks took in the frame just gone, in milliseconds.</summary>
    public double RecordMs { get; private set; }

    /// <summary>Every block's first recording, in milliseconds.</summary>
    public List<double> BlockMs { get; } = [];

    /// <summary>How long finding what goes over the units took, in milliseconds.</summary>
    public double QueryMs { get; private set; }

    /// <summary>How many items went over the units.</summary>
    public int OverItems { get; private set; }

    /// <summary>How long the board over the world took to draw, in milliseconds.</summary>
    public double OverDrawMs => _over.LastDrawMs;

    /// <summary>True when every block was recorded exactly once.</summary>
    public bool RecordedOnce => _blocks.Count > 0 && _blocks.All(b => b.Recorded == 1);

    /// <summary>The items in a block.</summary>
    public int BlockItems(int block) => _blocks[block].Items;

    /// <summary>What's on screen, in art pixels.</summary>
    public ArtArea View => _view;

    /// <summary>What the engine holds, for the log.</summary>
    public static string MemoryText()
        => $"textures {Performance.GetMonitor(Performance.Monitor.RenderTextureMemUsed) / (1024 * 1024):0.0} MB, buffers {Performance.GetMonitor(Performance.Monitor.RenderBufferMemUsed) / (1024 * 1024):0.0} MB, engine {Performance.GetMonitor(Performance.Monitor.MemoryStatic) / (1024 * 1024):0.0} MB, managed {GC.GetTotalMemory(false) / (1024.0 * 1024):0.0} MB";

    /// <summary>True while a fight or a list has the screen.</summary>
    public bool Suspended { get; private set; }

    /// <summary>
    /// Hides it and stops it running, with everything kept, and a build under way carrying on off the main thread. It's hidden in the rendering server, not on the
    /// node: Godot redraws a node each time it's shown, which would record every block again on the way back from a fight, all in one frame.
    /// </summary>
    public void Suspend()
    {
        Suspended = true;
        RenderingServer.CanvasItemSetVisible(_world.GetCanvasItem(), false);
        _back.Visible = false;
        _fade.Visible = false;
        ProcessMode = ProcessModeEnum.Disabled;
    }

    /// <summary>
    /// Brings it back. What had already been shown lifts from the fade again, unless it's not to <paramref name="fade"/>: a fight in the world
    /// comes in with its own walk-in, which a fade would eat.
    /// </summary>
    public void Resume(bool fade = true)
    {
        Suspended = false;
        RenderingServer.CanvasItemSetVisible(_world.GetCanvasItem(), true);
        _back.Visible = true;
        ProcessMode = ProcessModeEnum.Inherit;
        _backAt = _blocks.Sum(b => b.Recorded);
        _backCheck = 3;
        _fading = 0;
        _fade.Modulate = Colors.White;
        _fade.Visible = fade;
    }

    public override void _ExitTree()
    {
        _closing.Cancel();
        _blocks.Clear();
        _recording.Clear();
        _items = [];
        _painter.Release();
        _frames.Release();
        Plan = null;
        GD.Print("WORLD closed");
    }

    public override void _Ready()
    {
        _back = new ColorRect { Color = Dark, ZIndex = -1, MouseFilter = Control.MouseFilterEnum.Ignore };
        _back.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_back);

        _world = new Node2D
        {
            Scale = new Vector2(WorldTuning.Scale, WorldTuning.Scale),
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            TextureRepeat = CanvasItem.TextureRepeatEnum.Enabled,    // a side deeper than its texture repeats
        };
        AddChild(_world);

        _fade = new ColorRect { Color = Colors.Black, ZIndex = (int)RenderingServer.CanvasItemZMax, MouseFilter = Control.MouseFilterEnum.Ignore };
        _fade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_fade);

        _building = Task.Run(() => Build(_closing.Token));
    }

    private Built Build(CancellationToken closing)
    {
        var clock = Stopwatch.StartNew();
        var ground = _ground();
        var parsed = clock.Elapsed.TotalMilliseconds;
        closing.ThrowIfCancellationRequested();
        var art = ArtIndex.Load(_painter.Root);
        var plan = WorldPlan.Build(new BoardTerrain(ground.Map, null), art, _blockSize);
        StagePlan.Warm(ground);      // what every stage of the region shares, worked out here, so a fight's first frame doesn't
        var planned = clock.Elapsed.TotalMilliseconds;
        closing.ThrowIfCancellationRequested();
        var baked = plan.Lighting.Active ? plan.Lighting : null;
        var items = new object[plan.Blocks.Length][];
        foreach (var block in plan.Blocks)
        {
            closing.ThrowIfCancellationRequested();
            items[block.Index] = [.. block.Lit.Items.Select(i => BoardPainter.Prepare(i, baked))];
        }
        plan.DropProbes();
        var prepared = clock.Elapsed.TotalMilliseconds;
        return new Built(ground, plan, items, $"ground {parsed:0} ms, plan+light {planned - parsed:0} ms, bake {prepared - planned:0} ms, total {prepared:0} ms");
    }

    /// <summary>Puts the world where <paramref name="fit"/> says.</summary>
    public void Place(BoardFit fit)
    {
        var size = GetViewport().GetVisibleRect().Size;
        var window = new System.Numerics.Vector2(size.X, size.Y);
        var origin = WorldView.OriginIn(fit, window);
        _world.Position = new Vector2(origin.X, origin.Y);
        _view = WorldView.Seen(origin, window);
        if (_stage is not { Visible: true })
        {
            _over?.SetView(_view.Min - new System.Numerics.Vector2(ViewMargin), _view.Max + new System.Numerics.Vector2(ViewMargin));
        }
    }

    /// <summary>
    /// True when every block <paramref name="view"/> shows, and a little round it, is recorded. If not, they're recorded first, nearest to
    /// <paramref name="aim"/> (a tile), ahead of the party's: once, this frame, so call it each frame for as long as it's wanted.
    /// </summary>
    public bool Shows(ArtArea view, System.Numerics.Vector2 aim)
    {
        if (Plan is null || Now != Stage.Open)
        {
            return false;
        }
        var margin = new System.Numerics.Vector2(ViewMargin);
        if (Plan.BlocksIn(new ArtArea(view.Min - margin, view.Max + margin)).All(b => _blocks[b.Index].Recorded > 0))
        {
            return true;
        }
        _hurry = aim;
        return false;
    }

    /// <summary>Where blocks are queued around: the party's tile and its speed in tiles a second.</summary>
    public void Aim(System.Numerics.Vector2 tile, System.Numerics.Vector2 velocity) => _aim = BlockQueue.Aim(tile, velocity);

    /// <summary>Shows <paramref name="units"/>, with what stands in front of each painted over them.</summary>
    public void ShowUnits(ImmutableArray<BoardUnit> units, UnitArt art)
    {
        if (Plan is null || _over is null)
        {
            return;
        }
        var clock = Stopwatch.GetTimestamp();
        var plan = Plan;
        var over = plan.Overlay(units);
        QueryMs = Stopwatch.GetElapsedTime(clock).TotalMilliseconds;
        OverItems = over.Length;
        _over.SetDrawn(over.Select(at => _items[at.Block][at.Item]), over.Select(plan.OrderOf));
        _over.SetUnits(units, art);
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        _frames.Step(_clock);
        SwapRepainted();
        if (_stage is { Visible: true } stage)
        {
            stage.Clock = _clock;
        }
        if (_backCheck > 0 && --_backCheck == 0)
        {
            GD.Print($"WORLD back: {_blocks.Sum(b => b.Recorded) - _backAt} blocks recorded again, of {_blocks.Count}, frame {delta * 1000:0.0} ms");
        }
        switch (Now)
        {
            case Stage.Building when _building.IsFaulted || _building.IsCanceled:
                Now = Stage.Failed;
                GD.PushError($"The world can't be built: {_building.Exception?.GetBaseException().Message}");
                break;
            case Stage.Building when _building.IsCompleted:
                FinishBuild();
                break;
            case Stage.Textures:
                LoadOneTexture();
                break;
            case Stage.Near:
                Tally();
                RecordWithinBudget();
                if (Plan!.BlocksIn(_view).All(b => _blocks[b.Index].Recorded > 0))
                {
                    Now = Stage.Open;
                }
                break;
            case Stage.Open:
                Tally();
                RecordWithinBudget();
                NoteLate();
                Lift(delta);
                break;
        }
    }

    private void FinishBuild()
    {
        var built = _building.Result;
        BuildTimes = built.Times;
        (Ground, Plan) = (built.Ground, built.Plan);
        _items = built.Items;
        _queue = new BlockQueue(Plan.Blocks);
        foreach (var block in Plan.Blocks)
        {
            var node = new WorldBlock(_painter, _items[block.Index], block.Z);
            _blocks.Add(node);
            _world.AddChild(node);
        }

        _over = new BoardCanvas(_painter) { ZIndex = Plan.Blocks.Max(b => b.Z) + 1 };
        _world.AddChild(_over);
        _over.JoinWorld(Plan.Lighting.Active ? Plan.Lighting : null);
        if (_view.Max == _view.Min)
        {
            Place(WorldView.Centred(BoardProjection.Corner(0, 0, 0)));
        }
        Now = Stage.Textures;
    }

    private void LoadOneTexture()
    {
        var plan = Plan!;
        var images = plan.Images.Length;
        var clock = Stopwatch.GetTimestamp();
        if (_texture < images)
        {
            var image = plan.Images[_texture++];
            _painter.Preload(image.Image, image.Flat);
        }
        else if (_texture < images + plan.Halos.Length)
        {
            _painter.Load(plan.Halos[_texture++ - images]);
        }
        else
        {
            foreach (var block in _blocks)
            {
                block.Bind();
            }
            LoadMs = LoadMs with { Bind = Stopwatch.GetElapsedTime(clock).TotalMilliseconds };
            Now = Stage.Near;
            return;
        }
        var ms = Stopwatch.GetElapsedTime(clock).TotalMilliseconds;
        LoadMs = LoadMs with { Textures = LoadMs.Textures + ms, Worst = Math.Max(LoadMs.Worst, ms) };
    }

    /// <summary>A block is recorded when the frame ends, so this queues what the last blocks' cost per item says will fit, and always one.</summary>
    private void RecordWithinBudget()
    {
        foreach (var block in _queue.TakeWithin(_hurry ?? _aim, b => _blocks[b.Index].Items * _msPerItem, WorldTuning.BlockBudgetMs))
        {
            _recording.Add(_blocks[block.Index]);
            _blocks[block.Index].Record();
        }
        _hurry = null;
    }

    private void Tally()
    {
        var (spent, items) = (0.0, 0);
        for (var i = _recording.Count - 1; i >= 0; i--)
        {
            if (_recording[i].Drawn)
            {
                var ms = _recording[i].TakeDrawn();
                BlockMs.Add(ms);
                spent += ms;
                items += _recording[i].Items;
                _recording.RemoveAt(i);
            }
        }
        RecordMs = spent;
        if (items > 0)
        {
            _msPerItem = spent / items;
        }
    }

    private void NoteLate()
    {
        if (_queue.Count == 0 && _recording.Count == 0)
        {
            return;
        }
        foreach (var block in Plan!.BlocksIn(_view))
        {
            if (_blocks[block.Index].Recorded == 0 && _late.Add(block.Index))
            {
                GD.Print($"WORLD late block {block.Index}");
            }
        }
    }

    private void Lift(double delta)
    {
        if (!_fade.Visible)
        {
            return;
        }
        _fading += delta;
        _fade.Modulate = new Color(1, 1, 1, 1 - (float)Math.Min(_fading / WorldTuning.FadeSeconds, 1));
        _fade.Visible = _fading < WorldTuning.FadeSeconds;
    }
}
