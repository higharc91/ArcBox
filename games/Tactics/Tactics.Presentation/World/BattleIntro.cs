using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Beats;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.World;

/// <summary>One hero's way to its slot, in world tiles: where its walker stood, then each step, ending on the slot.</summary>
/// <param name="Unit">The hero.</param>
/// <param name="Walker">Its walker (<see cref="StageView.Heroes"/>).</param>
/// <param name="From">Where the walker stood, in the world; null if the TV never saw it, and then the party fades.</param>
/// <param name="Path">
/// The tile it's on at each step of the walk-in's clock, from the walker's to the slot; empty when the party fades. A tile twice in a row is a
/// step spent waiting, for another hero to go by (<see cref="BattleIntro.Plan"/>).
/// </param>
public sealed record HeroWalk(UnitId Unit, string Walker, UnitPlace? From, ImmutableArray<Tile> Path)
{
    /// <summary>How many steps of the walk-in's clock it takes, waits and all.</summary>
    public int Steps => Math.Max(0, Path.Length - 1);
}

/// <summary>An enemy's or companion's step onto its tile, from one tile out, in world tiles.</summary>
/// <param name="Unit">Who.</param>
/// <param name="From">The tile it steps from: <paramref name="To"/> itself if there's nowhere it could come from, and then it simply appears.</param>
/// <param name="To">Its tile in the fight.</param>
/// <param name="Start">When it starts, in seconds from the walk-in's start.</param>
public sealed record StepIn(UnitId Unit, Tile From, Tile To, double Start)
{
    /// <summary>When it's on its tile.</summary>
    public double End => Start + StageTuning.StepInSeconds;
}

/// <summary>What the walk-in looks like at one moment, in the battle map's own tiles, for <see cref="UnitPlan.Build"/>.</summary>
/// <param name="Places">Units not standing on their tiles.</param>
/// <param name="Poses">Units walking.</param>
/// <param name="Hidden">Units not on the stage yet.</param>
/// <param name="GridAlpha">How much of the grid shows.</param>
/// <param name="Fade">How dark the screen is: 1 is black.</param>
/// <param name="Dark">The heroes have been put on their slots in the dark, so the camera cuts.</param>
public sealed record IntroFrame(ImmutableDictionary<UnitId, UnitPlace> Places, ImmutableDictionary<UnitId, UnitPose> Poses, ImmutableHashSet<UnitId> Hidden,
    float GridAlpha, float Fade, bool Dark);

/// <summary>
/// The walk-in of a fight in the world (overworld 5c §5), worked out once from the first view: the battle's opening sends no events for it
/// (plan 5c, "falls short" 2), so it's read from the units. The heroes walk from where their walkers stood to their slots by the shortest
/// legal path, then the grid comes up, then the enemies and companions step in from a tile out, one after another. No two heroes are ever on
/// one tile at once, past the moment they set off (<see cref="Apart"/>): a hero waits a step, or goes a step round, rather than walk through
/// another, and a party that can't be walked in that way fades. A party that's far
/// (<see cref="StageTuning.FarSteps"/>), that the TV never saw, or whose way passes near an enemy (<see cref="StageTuning.EnemyClearance"/>)
/// fades in instead. All of it ends by <see cref="StageTuning.FormingMost"/>. Pure.
/// </summary>
/// <param name="Stage">Where the fight is.</param>
/// <param name="Heroes">The heroes' ways to their slots.</param>
/// <param name="Others">Everyone else, in the order they step in.</param>
/// <param name="Fades">The party fades in rather than walking.</param>
/// <param name="Step">The seconds a hero takes per step.</param>
/// <param name="WalkEnd">When the heroes are on their slots.</param>
/// <param name="End">When the walk-in is over and the fight's beats can play.</param>
public sealed record BattleIntro(BattleStage Stage, ImmutableArray<HeroWalk> Heroes, ImmutableArray<StepIn> Others, bool Fades, double Step, double WalkEnd, double End)
{
    private const string HeroPrefix = "hero";
    private const string Idle = "idle";
    private const int Detour = 6;           // the steps more than its shortest walk a hero may take, waiting or going round

    /// <summary>
    /// The most places-in-time <see cref="Apart"/> looks at, over all its tries, before it gives up and the party fades. It runs on the TV's main
    /// thread as the fight starts, so it's bounded by work, not by how the heroes happen to stand: a party of six in the open needs a few hundred.
    /// </summary>
    public const int MostSearched = 20_000;

    private static readonly IntroFrame Done = new(ImmutableDictionary<UnitId, UnitPlace>.Empty, ImmutableDictionary<UnitId, UnitPose>.Empty, [], 1, 0, false);

    /// <summary>When the grid starts to come up.</summary>
    public double GridStart => WalkEnd;

    /// <summary>When the grid is all there.</summary>
    public double GridEnd => WalkEnd + StageTuning.GridIn;

    /// <summary>
    /// The walk-in for <paramref name="battle"/> on <paramref name="stage"/>, over <paramref name="world"/> (the region as drawn), with the
    /// walkers where <paramref name="walkers"/> (<see cref="WalkTrack.Places"/>, world tiles) has them.
    /// </summary>
    public static BattleIntro Plan(BattleView battle, BattleStage stage, BoardTerrain world, IReadOnlyDictionary<string, UnitPlace>? walkers)
    {
        var heroes = battle.Stage?.Heroes ?? [];
        var drawn = battle.Units.Where(u => u.Life is "Up" or "Downed").ToArray();
        var enemies = drawn.Where(u => u.Side == "Enemy").Select(u => stage.ToWorld(u.At)).ToArray();

        var walks = new List<HeroWalk>();
        var others = new List<UnitView>();
        foreach (var unit in drawn)
        {
            if (WalkerOf(unit.Id, heroes) is not { } walker)
            {
                others.Add(unit);
                continue;
            }
            var from = walkers is not null && walkers.TryGetValue(walker, out var place) ? place : (UnitPlace?)null;
            var slot = stage.ToWorld(unit.At);
            var path = from is { } start ? PathTo(world.Rules, TileUnder(start.Map), slot) : [];
            walks.Add(new HeroWalk(unit.Id, walker, from, path));
        }
        // A party that fades anyway pays nothing for timing its walks; and a timed walk keeps clear of the enemies too, so a detour never makes it fade.
        bool NearEnemy(Tile t) => enemies.Any(e => Distance(t, e) <= StageTuning.EnemyClearance);
        var fades = walks.Any(w => w.From is null || w.Path.IsEmpty || w.Path.SkipLast(1).Any(NearEnemy));
        if (!fades && Apart(world.Rules, walks, NearEnemy, out _) is { } apart)
            walks = [.. apart];
        else
            fades = true;
        if (fades)
            walks = [.. walks.Select(w => w with { Path = [] })];

        var longest = walks.Count == 0 ? 0 : walks.Max(w => w.Steps);
        var step = longest == 0 ? 0 : Math.Min(StageTuning.StepSeconds, StageTuning.WalkInMost / longest);
        var walkEnd = fades ? StageTuning.FadeDown + StageTuning.FadeUp : longest * step;

        var slots = walks.Select(w => stage.ToWorld(battle.Units.First(u => u.Id == w.Unit).At)).ToArray();
        var middle = slots.Length == 0
            ? new Vector2(stage.Origin.X + stage.Width / 2f, stage.Origin.Y + stage.Depth / 2f)
            : slots.Aggregate(Vector2.Zero, (sum, t) => sum + Centre(t)) / slots.Length;
        var stagger = others.Count <= 1 ? 0 : Math.Min(StageTuning.StepInStagger, (StageTuning.StepInMost - StageTuning.StepInSeconds) / (others.Count - 1));
        var taken = drawn.Select(u => stage.ToWorld(u.At)).ToHashSet();
        ImmutableArray<StepIn> steps = [.. others.Select((u, i) =>
        {
            var to = stage.ToWorld(u.At);
            return new StepIn(u.Id, OneOut(world.Rules, to, middle, taken), to, walkEnd + i * stagger);
        })];

        var end = Math.Max(walkEnd + StageTuning.GridIn, steps.IsEmpty ? walkEnd : steps[^1].End);
        return new BattleIntro(stage, [.. walks], steps, fades, step, walkEnd, end);
    }

    /// <summary>The walk-in <paramref name="seconds"/> after it started, on <paramref name="world"/>.</summary>
    public IntroFrame At(double seconds, BoardTerrain world)
    {
        if (seconds >= End)
            return Done;
        var places = ImmutableDictionary.CreateBuilder<UnitId, UnitPlace>();
        var poses = ImmutableDictionary.CreateBuilder<UnitId, UnitPose>();
        var hidden = ImmutableHashSet.CreateBuilder<UnitId>();
        var dark = Fades && seconds >= StageTuning.FadeDown;

        foreach (var walk in Heroes)
        {
            if (Fades)
            {
                if (dark)
                    continue;
                if (walk.From is { } from)
                    places[walk.Unit] = Local(from);
                else
                    hidden.Add(walk.Unit);
                continue;
            }
            if (walk.Steps == 0 || seconds >= walk.Steps * Step)
                continue;
            var along = seconds / Step;
            var leg = Math.Min((int)along, walk.Steps - 1);
            var part = (float)(along - leg);
            var (a, b) = (walk.Path[leg], walk.Path[leg + 1]);
            var start = leg == 0 && walk.From is { } stood ? stood : new UnitPlace(Centre(a), world.Top(a));
            var map = Vector2.Lerp(start.Map, Centre(b), part);
            var waits = a == b;
            var height = float.Lerp(start.Height, world.Top(b), part) + (waits ? 0 : BeatPlayer.StepHop * MathF.Sin(MathF.PI * part));
            places[walk.Unit] = Local(new UnitPlace(map, height));
            if (waits)
            {
                if (walk.Path.Skip(leg + 1).Where(t => t != a).Cast<Tile?>().FirstOrDefault() is { } next)
                    poses[walk.Unit] = new UnitPose(Idle, seconds, new Vector2(next.X - a.X, next.Y - a.Y));     // standing, looking the way it'll go
                continue;
            }
            poses[walk.Unit] = new UnitPose(BeatScript.WalkAnim, seconds, new Vector2(b.X - a.X, b.Y - a.Y));
        }

        foreach (var step in Others)
        {
            if (seconds < step.Start)
            {
                hidden.Add(step.Unit);
                continue;
            }
            if (seconds >= step.End || step.From == step.To)
                continue;
            var part = (float)((seconds - step.Start) / StageTuning.StepInSeconds);
            var height = float.Lerp(world.Top(step.From), world.Top(step.To), part) + BeatPlayer.StepHop * MathF.Sin(MathF.PI * part);
            places[step.Unit] = Local(new UnitPlace(Vector2.Lerp(Centre(step.From), Centre(step.To), part), height));
            poses[step.Unit] = new UnitPose(BeatScript.WalkAnim, seconds - step.Start, new Vector2(step.To.X - step.From.X, step.To.Y - step.From.Y));
        }

        var grid = (float)Math.Clamp((seconds - GridStart) / StageTuning.GridIn, 0, 1);
        var fade = !Fades ? 0
            : seconds < StageTuning.FadeDown ? seconds / StageTuning.FadeDown
            : Math.Max(0, 1 - (seconds - StageTuning.FadeDown) / StageTuning.FadeUp);
        return new IntroFrame(places.ToImmutable(), poses.ToImmutable(), hidden.ToImmutable(), grid, (float)Math.Clamp(fade, 0, 1), dark);
    }

    /// <summary>The walker a battle unit is, if it's one of the heroes: <c>hero&lt;n&gt;</c> is the n-th of <paramref name="heroes"/>.</summary>
    public static string? WalkerOf(UnitId unit, ImmutableArray<string> heroes)
        => unit.Value.StartsWith(HeroPrefix, StringComparison.Ordinal)
            && int.TryParse(unit.Value.AsSpan(HeroPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            && n >= 1 && !heroes.IsDefault && n <= heroes.Length
            ? heroes[n - 1]
            : null;

    /// <summary>
    /// The shortest walk from <paramref name="from"/> to <paramref name="to"/> by the party's own steps, both ends included, or empty if it's
    /// <see cref="StageTuning.FarSteps"/> or more, or there's none. Ties go the way the steps are tried, so it's the same every time.
    /// </summary>
    public static ImmutableArray<Tile> PathTo(Terrain rules, Tile from, Tile to)
    {
        if (from == to)
            return [from];
        var back = new Dictionary<Tile, Tile> { [from] = from };
        var frontier = new List<Tile> { from };
        for (var depth = 1; depth < StageTuning.FarSteps && frontier.Count > 0; depth++)
        {
            var next = new List<Tile>();
            foreach (var tile in frontier)
                foreach (var step in WorldWalk.Steps(rules, tile, ClassDef.DefaultJump))
                {
                    if (!back.TryAdd(step, tile))
                        continue;
                    if (step == to)
                    {
                        var path = new List<Tile> { to };
                        for (var at = tile; at != from; at = back[at])
                            path.Add(at);
                        path.Add(from);
                        path.Reverse();
                        return [.. path];
                    }
                    next.Add(step);
                }
            frontier = next;
        }
        return [];
    }

    /// <summary>
    /// The heroes' walks timed so that no two are ever on one tile at the same step of the clock, nor pass through each other on the way:
    /// each walks in turn on the steps left free by those before it, waiting where it stands or going round (at most <see cref="Detour"/>
    /// steps over its shortest, and under <see cref="StageTuning.FarSteps"/> in all), never onto a tile <paramref name="avoid"/> says to keep off
    /// but its slot, and stays on its slot once there, so nobody walks through it.
    /// <para>
    /// Heroes already on their slots go first and stay; then the walkers, longest walk first. If a walker can't get through, it goes first among
    /// the walkers next try, so there are at most as many tries as walkers. It's given up, and the party fades (null), when a walker can't get
    /// through even at the front of the walkers, with only the standing heroes before it (one of them is in its way, or its slot can't be
    /// reached in time, and no order changes that), or when the search has looked at <see cref="MostSearched"/> places in time
    /// (<paramref name="searched"/> says how many it did). Heroes whose walkers stood together share that tile only at the start: from the
    /// first step on, they're apart.
    /// </para>
    /// </summary>
    public static ImmutableArray<HeroWalk>? Apart(Terrain rules, IReadOnlyList<HeroWalk> walks, Func<Tile, bool>? avoid, out int searched)
    {
        searched = 0;
        if (walks.Count <= 1)
            return [.. walks];
        var standing = Enumerable.Range(0, walks.Count).Where(i => walks[i].Steps == 0).OrderBy(i => walks[i].Unit.Value, StringComparer.Ordinal).ToArray();
        var walking = Enumerable.Range(0, walks.Count).Where(i => walks[i].Steps > 0)
            .OrderByDescending(i => walks[i].Steps).ThenBy(i => walks[i].Unit.Value, StringComparer.Ordinal).ToList();
        var budget = new Budget(rules);
        for (var tries = 0; tries < Math.Max(1, walking.Count); tries++)
        {
            var taken = new Taken();
            var timed = new ImmutableArray<Tile>[walks.Count];
            int? failed = null;
            foreach (var i in standing.Concat(walking))
            {
                var most = Math.Max(walks[i].Steps, Math.Min(walks[i].Steps + Detour, StageTuning.FarSteps - 1));     // a walk-in of more steps would be a scurry
                if (TimedPath(rules, walks[i].Path[0], walks[i].Path[^1], most, taken, avoid, budget) is not { } path)
                {
                    failed = i;
                    break;
                }
                taken.Add(path);
                timed[i] = path;
            }
            searched = budget.Spent;
            if (failed is not { } stuck)
                return [.. walks.Select((w, i) => w with { Path = timed[i] })];
            if (budget.Out || walking[0] == stuck)
                return null;        // it failed at the front, with only the standing heroes before it, or the search has run long
            walking.Remove(stuck);
            walking.Insert(0, stuck);
        }
        return null;
    }

    /// <summary><see cref="Apart"/> with no tiles to keep off, for its tests.</summary>
    public static ImmutableArray<HeroWalk>? Apart(Terrain rules, IReadOnlyList<HeroWalk> walks) => Apart(rules, walks, null, out _);

    /// <summary>How much of <see cref="MostSearched"/> the search has used, and each tile's steps, worked out once for all the tries.</summary>
    private sealed class Budget(Terrain rules)
    {
        private readonly Dictionary<Tile, Tile[]> _steps = [];

        public int Spent { get; private set; }

        /// <summary>Staying put, then the party's own steps from <paramref name="tile"/>.</summary>
        public Tile[] Moves(Tile tile)
        {
            if (!_steps.TryGetValue(tile, out var moves))
                _steps[tile] = moves = [tile, .. WorldWalk.Steps(rules, tile, ClassDef.DefaultJump)];
            return moves;
        }

        public bool Out => Spent >= MostSearched;

        public bool Take()
        {
            if (Out)
                return false;
            Spent++;
            return true;
        }
    }

    /// <summary>The tiles and steps of the clock taken by the walks planned so far.</summary>
    private sealed class Taken
    {
        private readonly HashSet<(Tile Tile, int Step)> _at = [];
        private readonly HashSet<(Tile From, Tile To, int Step)> _moves = [];
        private readonly Dictionary<Tile, int> _last = [];       // the last step anyone is on a tile; int.MaxValue once someone stops there
        private readonly Dictionary<Tile, int> _stops = [];      // where a walk ends, from which step

        public void Add(ImmutableArray<Tile> path)
        {
            for (var k = 0; k < path.Length; k++)
            {
                _at.Add((path[k], k));
                _last[path[k]] = Math.Max(_last.GetValueOrDefault(path[k], -1), k);
                if (k + 1 < path.Length)
                    _moves.Add((path[k], path[k + 1], k));
            }
            _stops[path[^1]] = path.Length - 1;
            _last[path[^1]] = int.MaxValue;
        }

        /// <summary>A walk may be on <paramref name="to"/> at step <paramref name="k"/> + 1, having come from <paramref name="from"/>.</summary>
        public bool Free(Tile from, Tile to, int k)
            => !_at.Contains((to, k + 1)) && !(_stops.TryGetValue(to, out var stop) && k + 1 >= stop) && !_moves.Contains((to, from, k));

        /// <summary>A walk may end on <paramref name="tile"/> at step <paramref name="k"/>: nobody comes onto it later.</summary>
        public bool Clear(Tile tile, int k) => _last.GetValueOrDefault(tile, -1) < k;
    }

    /// <summary>
    /// The quickest walk from <paramref name="from"/> to <paramref name="to"/> on the steps <paramref name="taken"/> leaves free, waits included,
    /// in at most <paramref name="most"/> steps; null if there's none. Waiting is tried before stepping, then the steps in their usual order.
    /// </summary>
    private static ImmutableArray<Tile>? TimedPath(Terrain rules, Tile from, Tile to, int most, Taken taken, Func<Tile, bool>? avoid, Budget budget)
    {
        var back = new Dictionary<(Tile, int), Tile>();
        var frontier = new List<Tile> { from };
        if (from == to && taken.Clear(to, 0))
            return [from];
        for (var k = 0; k < most && frontier.Count > 0; k++)
        {
            var next = new List<Tile>();
            foreach (var tile in frontier)
            {
                if (!budget.Take())
                    return null;
                foreach (var step in budget.Moves(tile))
                {
                    if (!taken.Free(tile, step, k) || (step != to && step != tile && avoid?.Invoke(step) == true) || !back.TryAdd((step, k + 1), tile))
                        continue;
                    if (step == to && taken.Clear(to, k + 1))
                    {
                        var path = new Tile[k + 2];
                        (path[k + 1], path[0]) = (to, from);
                        for (var (at, j) = (tile, k); j > 0; (at, j) = (back[(at, j)], j - 1))
                            path[j] = at;
                        return [.. path];
                    }
                    next.Add(step);
                }
            }
            frontier = next;
        }
        return null;
    }

    /// <summary>The tile next to <paramref name="to"/> an enemy steps in from: one it could walk from, free, and farthest from the party.</summary>
    private static Tile OneOut(Terrain rules, Tile to, Vector2 party, HashSet<Tile> taken)
    {
        Tile[] near = [new(to.X + 1, to.Y), new(to.X - 1, to.Y), new(to.X, to.Y + 1), new(to.X, to.Y - 1)];
        return near.Where(t => rules.Walkable(t) && !taken.Contains(t) && WorldWalk.Legal(rules, t, to, ClassDef.DefaultJump))
            .OrderByDescending(t => Vector2.DistanceSquared(Centre(t), party)).ThenBy(t => t)
            .Cast<Tile?>().FirstOrDefault() ?? to;
    }

    private UnitPlace Local(UnitPlace world) => new(world.Map - new Vector2(Stage.Origin.X, Stage.Origin.Y), world.Height - Stage.Height);

    private static int Distance(Tile a, Tile b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

    private static Vector2 Centre(Tile t) => new(t.X + 0.5f, t.Y + 0.5f);

    private static Tile TileUnder(Vector2 map) => new((int)MathF.Floor(map.X), (int)MathF.Floor(map.Y));
}
