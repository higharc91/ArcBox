using System.Collections.Immutable;
using System.Diagnostics;
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
/// Each frame, the board over it paints the units with only the items that sort after them and cross their rectangles (<see cref="WorldPlan.Over"/>).
/// Those are the same pixels again, in the same baked light, so nothing but what's behind them changes, except that what's half see-through blends twice.
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
    }

    private sealed record Built(WorldPlan Plan, object[][] Items, string Times);

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

    private Task<Built> _building = null!;
    private Node2D _world = null!;
    private BoardCanvas _over = null!;
    private ColorRect _fade = null!;
    private BlockQueue _queue = null!;
    private object[][] _items = [];
    private int _texture;
    private double _clock;
    private double _fading;
    private double _msPerItem = FirstGuessMsPerItem;
    private ArtArea _view;
    private System.Numerics.Vector2 _aim;

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

    /// <summary>The plan, once it's built.</summary>
    public WorldPlan? Plan { get; private set; }

    /// <summary>How the build went.</summary>
    public string BuildTimes { get; private set; } = "";

    /// <summary>How long loading took on the main thread, in milliseconds: texture total, worst texture, binding the items.</summary>
    public (double Textures, double Worst, double Bind) LoadMs { get; private set; }

    /// <summary>True once the fade has lifted.</summary>
    public bool Revealed => _fade is { Visible: false };

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

    public override void _Ready()
    {
        var back = new ColorRect { Color = Dark, ZIndex = -1, MouseFilter = Control.MouseFilterEnum.Ignore };
        back.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(back);

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

        _building = Task.Run(Build);
    }

    private Built Build()
    {
        var clock = Stopwatch.StartNew();
        var ground = _ground();
        var parsed = clock.Elapsed.TotalMilliseconds;
        var plan = WorldPlan.Build(new BoardTerrain(ground.Map, null), ArtIndex.Load(_painter.Root), _blockSize);
        var planned = clock.Elapsed.TotalMilliseconds;
        var baked = plan.Lighting.Active ? plan.Lighting : null;
        object[][] items = [.. plan.Blocks.Select(b => b.Lit.Items.Select(i => BoardPainter.Prepare(i, baked)).ToArray())];
        plan.DropProbes();
        var prepared = clock.Elapsed.TotalMilliseconds;
        return new Built(plan, items, $"ground {parsed:0} ms, plan+light {planned - parsed:0} ms, bake {prepared - planned:0} ms, total {prepared:0} ms");
    }

    /// <summary>Puts the middle of the screen at a point of the map, in art pixels, on whole layout pixels.</summary>
    public void Look(System.Numerics.Vector2 art)
    {
        var window = GetViewport().GetVisibleRect().Size;
        var middle = new Vector2(art.X, art.Y) * WorldTuning.Scale;
        _world.Position = (window / 2 - middle).Round();
        var half = new System.Numerics.Vector2(window.X, window.Y) / (2 * WorldTuning.Scale);
        _view = new ArtArea(art - half, art + half);
        _over?.SetView(_view.Min - new System.Numerics.Vector2(ViewMargin), _view.Max + new System.Numerics.Vector2(ViewMargin));
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
        var over = units.SelectMany(u => plan.Over(WorldPlan.AreaOf(u), u.Order)).Distinct()
            .Select(at => (Order: plan.Blocks[at.Block].Lit.Items[at.Item].Order, At: at))
            .OrderBy(i => i.Order)
            .ToArray();
        QueryMs = Stopwatch.GetElapsedTime(clock).TotalMilliseconds;
        OverItems = over.Length;
        _over.SetDrawn(over.Select(i => _items[i.At.Block][i.At.Item]), over.Select(i => i.Order));
        _over.SetUnits(units, art);
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        _frames.Step(_clock);
        switch (Now)
        {
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
        Plan = built.Plan;
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
        Look(_view.Max == _view.Min ? BoardProjection.Corner(0, 0, 0) : (_view.Min + _view.Max) / 2);
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
        foreach (var block in _queue.TakeWithin(_aim, b => _blocks[b.Index].Items * _msPerItem, WorldTuning.BlockBudgetMs))
        {
            _recording.Add(_blocks[block.Index]);
            _blocks[block.Index].Record();
        }
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
