using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>
/// The engine's working copy while one command resolves: it starts from a frozen state, collects changes and events, and
/// <see cref="Freeze"/>s back into an immutable state. The original state is never touched. Steps and effect blocks read
/// and change the battle only through a writer.
/// </summary>
public sealed class BattleWriter
{
    private readonly RulesPack _rules;
    private readonly ImmutableArray<BattleEvent>.Builder _events = ImmutableArray.CreateBuilder<BattleEvent>();
    private Terrain? _terrain;
    private ImmutableSortedDictionary<Tile, int>? _terrainHeights;
    private ImmutableArray<Zone> _terrainZones;

    /// <summary>Starts a working copy of <paramref name="state"/> under <paramref name="rules"/>.</summary>
    public BattleWriter(BattleState state, RulesPack rules)
    {
        State = state;
        _rules = rules;
    }

    /// <summary>The state as it is now.</summary>
    public BattleState State { get; set; }

    /// <summary>The events emitted so far, in order.</summary>
    public ImmutableArray<BattleEvent> Events => _events.ToImmutable();

    /// <summary>The unit with this id. Throws <see cref="KeyNotFoundException"/> if there is none.</summary>
    public UnitState Unit(UnitId id)
        => State.Units.TryGetValue(id, out var unit) ? unit : throw new KeyNotFoundException($"There is no unit '{id}'.");

    /// <summary>Replaces the unit with the same id.</summary>
    public void Set(UnitState unit) => State = State with { Units = State.Units.SetItem(unit.Id, unit) };

    /// <summary>Rolls a die in 1..<paramref name="sides"/> from the battle's RNG, advancing it.</summary>
    public int Roll(int sides)
    {
        var (value, next) = State.Rng.Roll(sides);
        State = State with { Rng = next };
        return value;
    }

    /// <summary>Appends an event.</summary>
    public void Emit(BattleEvent e) => _events.Add(e);

    /// <summary>Puts the steps at the head of the queue, in the order given (the first runs next).</summary>
    public void PushFront(params Step[] steps) => State = State with { Queue = State.Queue.InsertRange(0, steps) };

    /// <summary>Removes and returns the head of the queue, or null when it is empty.</summary>
    public Step? PopFront()
    {
        if (State.Queue.IsEmpty)
            return null;
        var head = State.Queue[0];
        State = State with { Queue = State.Queue.RemoveAt(0) };
        return head;
    }

    /// <summary>The map as it is now: the base map plus height changes and difficult zones. Rebuilt when those change.</summary>
    public Terrain Terrain
    {
        get
        {
            if (_terrain is null || !ReferenceEquals(_terrainHeights, State.HeightDelta) || _terrainZones != State.Zones)
            {
                var map = _rules.MapFor(State.MapId)
                          ?? throw new InvalidOperationException($"The rules pack has no map '{State.MapId}'.");
                var difficult = State.Zones.Where(z => z.Difficult).SelectMany(z => z.Tiles).ToImmutableSortedSet();
                _terrain = new Terrain(map, State.HeightDelta, difficult);
                _terrainHeights = State.HeightDelta;
                _terrainZones = State.Zones;
            }
            return _terrain;
        }
    }

    /// <summary>The resulting immutable state.</summary>
    public BattleState Freeze() => State;
}
