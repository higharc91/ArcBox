using System.Collections.Immutable;
using Godot;
using Tactics.Logic;
using Tactics.Presentation.Board;
using Tactics.Presentation.World;

namespace ArcBox.Host.Games.Tactics;

/// <summary>
/// Dev/Spike: manages tall props (manifest height >= 2) in their own layer, separated from static blocks so they can fade.
/// Keeps props in Blocks order and sorted between blocks by ZIndex.
/// Supports both <see cref="Mode.PerBlock"/> (re-recording only blocks whose prop alpha changed) and
/// <see cref="Mode.PerProp"/> (one canvas item per prop).
/// </summary>
internal sealed partial class TallProps : Node2D
{
    public enum Mode { PerBlock, PerProp }

    public Mode CurrentMode { get; set; } = Mode.PerBlock;

    private readonly BoardPainter _painter;
    private readonly List<PropState> _allProps = [];
    private readonly List<TallBlockNode> _blockNodes = [];
    private double _clock;

    internal sealed class PropState(object item, int blockIndex, long order, Rect2 target, Tile tile)
    {
        public object Item { get; } = item;
        public int BlockIndex { get; } = blockIndex;
        public long Order { get; } = order;
        public Rect2 Target { get; } = target;
        public Tile Tile { get; } = tile;
        public float Alpha { get; set; } = 1f;
        public float TargetAlpha { get; set; } = 1f;
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
                _painter.Paint(this, prop.Item, null, _clock, prop.Alpha);
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

    public TallProps(BoardPainter painter, ImmutableArray<BlockPlan> blocks, object[][] tallItemsByBlock, Mode mode = Mode.PerBlock)
    {
        _painter = painter;
        CurrentMode = mode;

        for (var b = 0; b < blocks.Length; b++)
        {
            var block = blocks[b];
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

    /// <summary>Updates prop alphas from walker occlusion and eases towards target.</summary>
    public void Update(ImmutableArray<BoardUnit> walkers, double delta, double clock)
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

        // Fade ease: over ~0.2 s (WorldTuning.PropFadeSeconds) from 1.0 to 0.35 (WorldTuning.PropFadeAlpha)
        var speed = delta > 0 ? (float)(delta / 0.20) * (1f - 0.35f) : 1f;

        var dirtyBlocks = new HashSet<int>();

        foreach (var prop in _allProps)
        {
            var occluded = false;
            foreach (var unit in walkers)
            {
                var area = WorldPlan.AreaOf(unit);
                var unitRect = new Rect2(new Vector2(area.Min.X, area.Min.Y), new Vector2(area.Max.X - area.Min.X, area.Max.Y - area.Min.Y));
                if (prop.Target.Intersects(unitRect) && prop.Order > unit.Order)
                {
                    occluded = true;
                    break;
                }
            }

            prop.TargetAlpha = occluded ? 0.35f : 1f;
            if (MathF.Abs(prop.Alpha - prop.TargetAlpha) > 0.001f)
            {
                prop.Alpha = Mathf.MoveToward(prop.Alpha, prop.TargetAlpha, speed);
                if (CurrentMode == Mode.PerBlock)
                {
                    dirtyBlocks.Add(prop.BlockIndex);
                }
                else
                {
                    prop.ItemNode?.SetClock(clock);
                    prop.ItemNode?.QueueRedraw();
                }
            }
        }

        if (CurrentMode == Mode.PerBlock)
        {
            foreach (var b in dirtyBlocks)
            {
                if (_blockNodes[b].Visible)
                {
                    _blockNodes[b].QueueRedraw();
                }
            }
        }
    }
}
