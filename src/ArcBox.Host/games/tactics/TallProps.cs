using System.Collections.Immutable;
using Godot;
using Tactics.Logic;
using Tactics.Presentation.Board;
using Tactics.Presentation.World;

namespace ArcBox.Host.Games.Tactics;

/// <summary>
/// Manages tall props (manifest height >= 2) in their own layer, separated from static blocks so they can fade.
/// Keeps props in Blocks order and sorted between blocks by ZIndex.
/// Supports both <see cref="Mode.PerBlock"/> (re-recording only blocks whose prop alpha changed) and
/// <see cref="Mode.PerProp"/> (one canvas item per prop).
/// Faded props are drawn above the walker overlay at maxZ + 2 with modulated alpha.
/// </summary>
internal sealed partial class TallProps : Node2D
{
    public enum Mode { PerBlock, PerProp }

    public Mode CurrentMode { get; set; } = Mode.PerBlock;

    private readonly BoardPainter _painter;
    private readonly List<PropState> _allProps = [];
    private readonly List<TallBlockNode> _blockNodes = [];
    private readonly int[] _blockZ;
    private readonly int _maxZ;
    private readonly FadedNode _fadedNode;
    private bool _hadFaded;
    private double _clock;
    private PropFade? _fallbackFade;

    internal sealed class PropState(object item, int blockIndex, long order, Rect2 target, Tile tile)
    {
        public object Item { get; } = item;
        public int BlockIndex { get; } = blockIndex;
        public long Order { get; } = order;
        public Rect2 Target { get; } = target;
        public Tile Tile { get; } = tile;
        public float Alpha { get; set; } = 1f;
        public TallItemNode? ItemNode { get; set; }
    }

    internal sealed partial class TallBlockNode : Node2D
    {
        private readonly BoardPainter _painter;
        private readonly List<PropState> _props;
        private double _clock;

        public TallBlockNode(BoardPainter painter, List<PropState> props, int z)
        {
            _painter = painter;
            _props = props;
            ZIndex = z;
            Visible = false;
        }

        public void SetClock(double clock) => _clock = clock;

        public override void _Draw()
        {
            foreach (var prop in _props)
            {
                if (prop.Alpha >= 0.999f)
                {
                    _painter.Paint(this, prop.Item, null, _clock, 1f);
                }
            }
        }
    }

    internal sealed partial class TallItemNode : Node2D
    {
        private readonly BoardPainter _painter;
        private readonly PropState _prop;
        private double _clock;

        public TallItemNode(BoardPainter painter, PropState prop, int z)
        {
            _painter = painter;
            _prop = prop;
            ZIndex = z;
            Visible = false;
        }

        public void SetClock(double clock) => _clock = clock;

        public override void _Draw()
        {
            _painter.Paint(this, _prop.Item, null, _clock, _prop.Alpha);
        }
    }

    internal sealed partial class FadedNode : Node2D
    {
        private readonly BoardPainter _painter;
        private readonly List<PropState> _faded = [];
        private double _clock;

        public FadedNode(BoardPainter painter, int z)
        {
            _painter = painter;
            ZIndex = z;
            Visible = false;
        }

        public void SetClock(double clock) => _clock = clock;

        public void SetFaded(List<PropState> faded)
        {
            _faded.Clear();
            _faded.AddRange(faded);
            Visible = _faded.Count > 0;
            QueueRedraw();
        }

        public override void _Draw()
        {
            foreach (var prop in _faded)
            {
                _painter.Paint(this, prop.Item, null, _clock, prop.Alpha);
            }
        }
    }

    public TallProps(BoardPainter painter, ImmutableArray<BlockPlan> blocks, object[][] tallItemsByBlock, Mode mode = Mode.PerBlock)
    {
        _painter = painter;
        CurrentMode = mode;
        _maxZ = blocks.Length > 0 ? blocks.Max(b => b.Z) : 0;
        _blockZ = new int[blocks.Length];

        for (var b = 0; b < blocks.Length; b++)
        {
            var block = blocks[b];
            _blockZ[b] = block.Z;
            var items = tallItemsByBlock[b];
            var blockProps = new List<PropState>(items.Length);
            foreach (var item in items)
            {
                var state = new PropState(item, b, BoardPainter.OrderOf(item), BoardPainter.TargetOf(item), BoardPainter.TileOf(item));
                blockProps.Add(state);
                _allProps.Add(state);
            }

            var blockNode = new TallBlockNode(_painter, blockProps, block.Z);
            _blockNodes.Add(blockNode);
            AddChild(blockNode);

            if (CurrentMode == Mode.PerProp)
            {
                blockNode.Visible = false;
                foreach (var prop in blockProps)
                {
                    var itemNode = new TallItemNode(_painter, prop, block.Z);
                    prop.ItemNode = itemNode;
                    AddChild(itemNode);
                }
            }
        }

        _fadedNode = new FadedNode(_painter, _maxZ + 2);
        AddChild(_fadedNode);
    }

    /// <summary>Total tall props managed.</summary>
    public int Count => _allProps.Count;

    /// <summary>Binds textures on the main thread.</summary>
    public void Bind()
    {
        foreach (var prop in _allProps)
        {
            _painter.Bind(prop.Item);
        }
    }

    /// <summary>Shows and records the tall props of block <paramref name="blockIndex"/>.</summary>
    public void Record(int blockIndex)
    {
        if (blockIndex >= 0 && blockIndex < _blockNodes.Count)
        {
            if (CurrentMode == Mode.PerBlock)
            {
                _blockNodes[blockIndex].Visible = true;
                _blockNodes[blockIndex].QueueRedraw();
            }
            else
            {
                foreach (var prop in _allProps.Where(p => p.BlockIndex == blockIndex))
                {
                    if (prop.ItemNode is not null)
                    {
                        prop.ItemNode.Visible = true;
                        prop.ItemNode.QueueRedraw();
                    }
                }
            }
        }
    }

    /// <summary>Updates prop alphas from <see cref="PropFade"/> and redraws dirty blocks or faded props.</summary>
    public void Update(PropFade fade, double clock)
    {
        _clock = clock;
        if (_allProps.Count == 0)
        {
            return;
        }

        foreach (var node in _blockNodes)
        {
            node.SetClock(clock);
        }
        _fadedNode.SetClock(clock);

        var dirtyBlocks = new HashSet<int>();
        var fadedProps = new List<PropState>();

        for (var i = 0; i < _allProps.Count; i++)
        {
            var prop = _allProps[i];
            var alpha = fade.AlphaOf(i);
            if (MathF.Abs(prop.Alpha - alpha) > 0.001f)
            {
                prop.Alpha = alpha;
                if (CurrentMode == Mode.PerBlock)
                {
                    dirtyBlocks.Add(prop.BlockIndex);
                }
                else
                {
                    if (prop.ItemNode is not null)
                    {
                        prop.ItemNode.ZIndex = prop.Alpha < 0.999f ? _maxZ + 2 : _blockZ[prop.BlockIndex];
                        prop.ItemNode.SetClock(clock);
                        prop.ItemNode.QueueRedraw();
                    }
                }
            }

            if (prop.Alpha < 0.999f)
            {
                fadedProps.Add(prop);
            }
        }

        if (CurrentMode == Mode.PerBlock)
        {
            if (fadedProps.Count > 0 || _hadFaded)
            {
                _fadedNode.SetFaded(fadedProps);
                _hadFaded = fadedProps.Count > 0;
            }

            foreach (var b in dirtyBlocks)
            {
                if (_blockNodes[b].Visible)
                {
                    _blockNodes[b].QueueRedraw();
                }
            }
        }
    }

    /// <summary>Updates prop alphas from walker occlusion and eases towards target (demo / fallback).</summary>
    public void Update(ImmutableArray<BoardUnit> walkers, double delta, double clock)
    {
        _fallbackFade ??= new PropFade(_allProps.Select(p => new PropFade.TallProp(default, p.Order, new ArtArea(new System.Numerics.Vector2(p.Target.Position.X, p.Target.Position.Y), new System.Numerics.Vector2(p.Target.End.X, p.Target.End.Y)))));
        _fallbackFade.Update(walkers, delta);
        Update(_fallbackFade, clock);
    }
}
