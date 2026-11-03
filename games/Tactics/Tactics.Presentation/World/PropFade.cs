using System.Numerics;
using Tactics.Logic;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.World;

/// <summary>
/// Fades tall props in the world when party walkers pass behind them, so the party is never lost behind a tree. Pure: it eases each prop's alpha between 1 and
/// <see cref="WorldTuning.PropFadeAlpha"/> over <see cref="WorldTuning.PropFadeSeconds"/>, and says which props are faded. A faded prop is left out of its recorded block and
/// painted in its sorted place in the frame's overlay (<see cref="WorldPlan.PaintOver"/>). Nothing fades during a fight.
/// </summary>
public sealed class PropFade
{
    /// <summary>
    /// A tall prop or a piece of a building, tracked for occlusion and fading. Pieces of a <paramref name="Group"/> fade together, and only a <paramref name="Covers"/> piece,
    /// a building's wall and not its smoke, fades them.
    /// </summary>
    private readonly record struct TallProp(ItemAt At, long Order, ArtArea Area, int Group, bool Covers);

    private const float Solid = 0.999f;

    private readonly TallProp[] _props;
    private readonly Dictionary<ItemAt, int> _byAt;
    private readonly float[] _alphas;
    private readonly float[] _targetAlphas;
    private readonly bool[] _faded;
    private readonly bool[] _covered;
    private readonly List<int> _fadedNow = [];
    private readonly List<int> _flipped = [];
    private bool _inFight;

    /// <summary>The tall props of <paramref name="plan"/>.</summary>
    public PropFade(WorldPlan plan)
    {
        _props = [.. Collect(plan)];
        _covered = new bool[_props.Length == 0 ? 0 : _props.Max(p => p.Group) + 1];
        _byAt = new Dictionary<ItemAt, int>(_props.Length);
        _alphas = new float[_props.Length];
        _targetAlphas = new float[_props.Length];
        _faded = new bool[_props.Length];
        for (var i = 0; i < _props.Length; i++)
        {
            _byAt[_props[i].At] = i;
            _alphas[i] = 1f;
            _targetAlphas[i] = 1f;
        }
    }

    /// <summary>Total tall props managed.</summary>
    public int Count => _props.Length;

    /// <summary>True during a fight: props never fade for units, and what's faded is solid again at once, as a fight's frames may not read this at all.</summary>
    public bool InFight
    {
        get => _inFight;
        set
        {
            _inFight = value;
            if (value)
            {
                Array.Fill(_targetAlphas, 1f);
                Snap();
            }
        }
    }

    /// <summary>The props faded now, as indexes for <see cref="AlphaOf"/> and <see cref="AtOf"/>. Valid until the next change.</summary>
    public IReadOnlyList<int> Faded => _fadedNow;

    private static IEnumerable<TallProp> Collect(WorldPlan plan)
    {
        var buildings = plan.Terrain.Map.Buildings.Length;
        var (group, tall) = (buildings, 0);
        foreach (var block in plan.Blocks)
        {
            for (var i = 0; i < block.Lit.Items.Length; i++)
            {
                var (drawn, part, covers) = block.Lit.Items[i] switch
                {
                    LitSprite { IsTallProp: true } sprite => (sprite.Sprite, -1, true),
                    LitSprite { Sprite.Part: >= 0 } sprite => (sprite.Sprite, sprite.Sprite.Part, false),
                    LitFace { Face.Part: >= 0 } face => ((BoardItem)face.Face, face.Face.Part, true),
                    _ => (default(BoardItem), -1, false),
                };
                if (drawn is null)
                {
                    continue;
                }
                var (min, max) = BoardPlan.Bounds(drawn);
                yield return new TallProp(new ItemAt(block.Index, i), drawn.Order, new ArtArea(min, max), part >= 0 ? part : group + tall++, covers);
            }
        }
    }

    /// <summary>Current opacity of tall prop <paramref name="prop"/> (0 to <see cref="Count"/> - 1).</summary>
    public float AlphaOf(int prop) => _alphas[prop];

    /// <summary>Where tall prop <paramref name="prop"/> is in the plan.</summary>
    public ItemAt AtOf(int prop) => _props[prop].At;

    /// <summary>True if the item at <paramref name="at"/> is a tall prop that is occluding a walker or still easing back.</summary>
    public bool IsFaded(ItemAt at) => _byAt.TryGetValue(at, out var i) && _faded[i];

    /// <summary>
    /// Passes on each prop that has stopped or started being faded since the last call, so a block is recorded again only when what it holds changes, not as the alpha eases.
    /// </summary>
    public void TakeChanges(Action<ItemAt, bool> changed)
    {
        foreach (var prop in _flipped)
        {
            changed(_props[prop].At, _faded[prop]);
        }
        _flipped.Clear();
    }

    /// <summary>Snaps all prop alphas immediately to their target alphas.</summary>
    public void Snap()
    {
        for (var i = 0; i < _props.Length; i++)
        {
            _alphas[i] = _targetAlphas[i];
        }
        Settle();
    }

    /// <summary>Updates target alphas from walkers and eases current alphas toward them over <paramref name="dt"/> seconds.</summary>
    public void Update(IReadOnlyList<BoardUnit> walkers, double dt)
    {
        UpdateTargets(walkers);
        if (dt > 0)
        {
            var speed = (float)(dt / WorldTuning.PropFadeSeconds) * (1f - WorldTuning.PropFadeAlpha);
            for (var i = 0; i < _props.Length; i++)
            {
                var target = _targetAlphas[i];
                if (MathF.Abs(_alphas[i] - target) > 0.0001f)
                {
                    _alphas[i] = _alphas[i] < target
                        ? (_alphas[i] + speed >= target - 0.0001f ? target : _alphas[i] + speed)
                        : (_alphas[i] - speed <= target + 0.0001f ? target : _alphas[i] - speed);
                }
            }
        }
        Settle();
    }

    private void Settle()
    {
        _fadedNow.Clear();
        for (var i = 0; i < _props.Length; i++)
        {
            var faded = _targetAlphas[i] < 1f || _alphas[i] < Solid;
            if (faded)
            {
                _fadedNow.Add(i);
            }
            if (faded != _faded[i])
            {
                _faded[i] = faded;
                _flipped.Add(i);
            }
        }
    }

    private void UpdateTargets(IReadOnlyList<BoardUnit> walkers)
    {
        Array.Clear(_covered);
        if (!_inFight)
        {
            foreach (var prop in _props)
            {
                for (var w = 0; prop.Covers && !_covered[prop.Group] && w < walkers.Count; w++)
                {
                    _covered[prop.Group] = prop.Order > walkers[w].Order && prop.Area.Overlaps(WorldPlan.BodyAreaOf(walkers[w]));
                }
            }
        }
        for (var i = 0; i < _props.Length; i++)
        {
            _targetAlphas[i] = _covered[_props[i].Group] ? WorldTuning.PropFadeAlpha : 1f;
        }
    }
}
