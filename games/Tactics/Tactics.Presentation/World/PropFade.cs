using System.Collections.Immutable;
using System.Numerics;
using Tactics.Logic;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.World;

/// <summary>
/// Manages fading of tall props in the world when party walkers pass behind them, so the party is never lost behind a tree.
/// Pure C# computation: eases prop alpha between 1.0 and <see cref="WorldTuning.PropFadeAlpha"/> over <see cref="WorldTuning.PropFadeSeconds"/>.
/// Props never fade during fights.
/// </summary>
public sealed class PropFade
{
    /// <summary>A tall prop tracked for occlusion and fading.</summary>
    public readonly record struct TallProp(ItemAt At, long Order, ArtArea Area);

    private readonly TallProp[] _props;
    private readonly Dictionary<ItemAt, int> _byAt;
    private readonly float[] _alphas;
    private readonly float[] _targetAlphas;

    /// <summary>Total tall props managed.</summary>
    public int Count => _props.Length;

    /// <summary>True during a fight: props never fade for units.</summary>
    public bool InFight { get; set; }

    public PropFade(WorldPlan plan) : this(CollectTallProps(plan)) { }

    public PropFade(IEnumerable<TallProp> props)
    {
        _props = props.ToArray();
        _byAt = new Dictionary<ItemAt, int>(_props.Length);
        _alphas = new float[_props.Length];
        _targetAlphas = new float[_props.Length];
        for (var i = 0; i < _props.Length; i++)
        {
            if (_props[i].At != default)
            {
                _byAt[_props[i].At] = i;
            }
            _alphas[i] = 1f;
            _targetAlphas[i] = 1f;
        }
    }

    private static List<TallProp> CollectTallProps(WorldPlan plan)
    {
        var list = new List<TallProp>();
        foreach (var block in plan.Blocks)
        {
            for (var i = 0; i < block.Lit.Items.Length; i++)
            {
                var item = block.Lit.Items[i];
                if (item is LitSprite { IsTallProp: true } sprite)
                {
                    var (min, max) = BoardPlan.Bounds(sprite.Sprite);
                    list.Add(new TallProp(new ItemAt(block.Index, i), sprite.Order, new ArtArea(min, max)));
                }
            }
        }
        return list;
    }

    /// <summary>Current opacity of tall prop at <paramref name="prop"/> index (0 to <see cref="Count"/> - 1).</summary>
    public float AlphaOf(int prop) => _alphas[prop];

    /// <summary>True if the item at <paramref name="at"/> is a tall prop that is occluded or fading.</summary>
    public bool IsFaded(ItemAt at) => _byAt.TryGetValue(at, out var i) && (_targetAlphas[i] < 1f || _alphas[i] < 0.999f);

    /// <summary>Snaps all prop alphas immediately to their target alphas.</summary>
    public void Snap()
    {
        for (var i = 0; i < _props.Length; i++)
        {
            _alphas[i] = _targetAlphas[i];
        }
    }

    /// <summary>Updates target alphas from walkers and eases current alphas toward them over <paramref name="dt"/> seconds.</summary>
    public void Update(IEnumerable<BoardUnit> walkers, double dt)
    {
        UpdateTargets(walkers);
        if (dt <= 0)
        {
            return;
        }

        var speed = (float)(dt / WorldTuning.PropFadeSeconds) * (1f - WorldTuning.PropFadeAlpha);
        for (var i = 0; i < _props.Length; i++)
        {
            var target = _targetAlphas[i];
            if (MathF.Abs(_alphas[i] - target) > 0.0001f)
            {
                if (_alphas[i] < target)
                {
                    _alphas[i] = _alphas[i] + speed >= target - 0.0001f ? target : _alphas[i] + speed;
                }
                else
                {
                    _alphas[i] = _alphas[i] - speed <= target + 0.0001f ? target : _alphas[i] - speed;
                }
            }
        }
    }

    private void UpdateTargets(IEnumerable<BoardUnit> walkers)
    {
        var walkerList = walkers as IList<BoardUnit> ?? walkers.ToList();
        var walkerAreas = new (ArtArea Area, long Order)[walkerList.Count];
        for (var w = 0; w < walkerList.Count; w++)
        {
            walkerAreas[w] = (WorldPlan.AreaOf(walkerList[w]), walkerList[w].Order);
        }

        for (var i = 0; i < _props.Length; i++)
        {
            var occluded = false;
            if (!InFight)
            {
                var prop = _props[i];
                for (var w = 0; w < walkerAreas.Length; w++)
                {
                    if (prop.Order > walkerAreas[w].Order && prop.Area.Overlaps(walkerAreas[w].Area))
                    {
                        occluded = true;
                        break;
                    }
                }
            }
            _targetAlphas[i] = occluded ? WorldTuning.PropFadeAlpha : 1f;
        }
    }
}
