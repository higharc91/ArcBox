using Meridian.Presentation.Beats;

namespace Meridian.Presentation.Board;

/// <summary>What a crew member's sprite shows.</summary>
public enum CrewLook
{
    Idle,
    Walk,
    /// <summary>Just hit in a fight, for the fight's beat.</summary>
    Hurt,
    /// <summary>Falling, for the death's beat.</summary>
    Down,
    /// <summary>Dead, and playing on as an echo.</summary>
    Echo,
}

/// <summary>
/// Where a crew member is drawn this frame: on <see cref="Deck"/>, at a fractional cell (<see cref="X"/>, <see cref="Y"/>),
/// facing the way they last moved.
/// </summary>
public sealed record CrewPose(string Seat, DeckId Deck, double X, double Y, Side Facing, CrewLook State, string At)
{
    public bool Walking => State == CrewLook.Walk;
}

/// <summary>
/// Crew that walk between compartments rather than jump (M5 plan, Task 3). A move becomes a walk along the compartments it
/// passed through: the shortest way by open hatches on the same deck, at <see cref="CellsPerSecond"/>, facing each step's
/// way. A lift ride, or a way the TV can't see, is a cut. Hurt, down and echo come from the beat on screen and the view.
/// </summary>
public sealed class CrewMotion
{
    public const double CellsPerSecond = 3.5;

    /// <summary>A walk longer than this is a cut: a long way round reads better as a jump.</summary>
    public const int LongestWalk = 10;

    private sealed class Walker(string seat)
    {
        public string Seat { get; } = seat;
        public string At = "";
        public DeckId Deck;
        public double X, Y;
        public Side Facing = Side.S;
        public readonly Queue<Cell> Path = new();
        public bool Echo;
    }

    private readonly Dictionary<string, Walker> _walkers = new(StringComparer.Ordinal);
    private Beat? _beat;

    /// <summary>The newest view and the beat on screen. A new compartment for a crew member starts a walk there.</summary>
    /// <param name="cut">Place everyone where the view says, walking nobody: a first look, or after a reconnect.</param>
    public void Receive(MeridianHostView view, Beat? beat, bool cut = false)
    {
        _beat = beat;
        var rooms = view.Compartments.ToDictionary(c => c.Id);
        foreach (var marker in view.Crew)
        {
            if (!rooms.TryGetValue(marker.At, out var room))
            {
                continue;
            }
            if (!_walkers.TryGetValue(marker.Id, out var walker))
            {
                walker = new Walker(marker.Id);
                _walkers[marker.Id] = walker;
                cut = true;
            }
            walker.Echo = marker.Echo;
            if (walker.At == marker.At && !cut)
            {
                continue;
            }
            var from = rooms.TryGetValue(walker.At, out var was) ? was.At : (Cell?)null;
            walker.At = marker.At;
            walker.Path.Clear();
            var path = !cut && from is { } start ? Route(view, start, room.At) : null;
            if (path is null || path.Count > LongestWalk)
            {
                (walker.Deck, walker.X, walker.Y) = (room.At.Deck, room.At.X, room.At.Y);   // a cut
                continue;
            }
            foreach (var step in path)
            {
                walker.Path.Enqueue(step);
            }
        }
    }

    public void Tick(double seconds)
    {
        foreach (var walker in _walkers.Values)
        {
            var budget = seconds * CellsPerSecond;
            while (budget > 0 && walker.Path.TryPeek(out var next))
            {
                var (dx, dy) = (next.X - walker.X, next.Y - walker.Y);
                var distance = Math.Abs(dx) + Math.Abs(dy);
                if (distance > 0)
                {
                    walker.Facing = Math.Abs(dx) >= Math.Abs(dy) ? (dx > 0 ? Side.E : Side.W) : (dy > 0 ? Side.S : Side.N);
                }
                if (distance <= budget)
                {
                    (walker.X, walker.Y) = (next.X, next.Y);
                    budget -= distance;
                    walker.Path.Dequeue();
                }
                else
                {
                    walker.X += Math.Sign(dx) * Math.Min(Math.Abs(dx), budget);
                    walker.Y += Math.Sign(dy) * Math.Min(Math.Abs(dy), budget);
                    budget = 0;
                }
            }
        }
    }

    public IReadOnlyList<CrewPose> Poses
        => [.. _walkers.Values.Select(w => new CrewPose(w.Seat, w.Deck, w.X, w.Y, w.Facing, StateOf(w), w.At))];

    private CrewLook StateOf(Walker walker)
    {
        if (_beat is { Kind: "death", Short: false } death && death.Now.Seat.Value == walker.Seat)
        {
            return CrewLook.Down;
        }
        if (walker.Echo)
        {
            return CrewLook.Echo;
        }
        if (_beat is { Kind: "fight", Short: false } fight && fight.Now.Target == walker.Seat)
        {
            return CrewLook.Hurt;
        }
        return walker.Path.Count > 0 ? CrewLook.Walk : CrewLook.Idle;
    }

    /// <summary>
    /// The cells walked from <paramref name="from"/> to <paramref name="to"/> (excluding the start), through hatches open on
    /// both sides, on one deck; null if there's no such way.
    /// </summary>
    public static List<Cell>? Route(MeridianHostView view, Cell from, Cell to)
    {
        if (from.Deck != to.Deck)
        {
            return null;
        }
        if (from == to)
        {
            return [];
        }
        var deck = TileDraw.Of(view.Compartments, from.Deck).ToDictionary(t => t.At);
        var previous = new Dictionary<Cell, Cell> { [from] = from };
        var queue = new Queue<Cell>([from]);
        while (queue.TryDequeue(out var cell))
        {
            if (cell == to)
            {
                var path = new List<Cell>();
                for (var at = to; at != from; at = previous[at])
                {
                    path.Add(at);
                }
                path.Reverse();
                return path;
            }
            foreach (var side in Sides.All)
            {
                var next = cell.Step(side);
                if (deck[cell].Hatch(side) == HatchState.Open && deck.TryGetValue(next, out var there)
                    && there.Hatch(side.Opposite()) == HatchState.Open && previous.TryAdd(next, cell))
                {
                    queue.Enqueue(next);
                }
            }
        }
        return null;
    }
}
