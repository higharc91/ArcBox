using System.Collections.Immutable;
using System.Numerics;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Board;
using Tactics.Presentation.World;

namespace Tactics.Presentation.Tests.World;

/// <summary>
/// A fight in a made-up region, as the host views tell it: a 40 x 30 field of grass with an 8 x 6 map stamped at (16, 10), two heroes, two
/// goblins and a companion. Walkers <c>p1</c> (the leader) and <c>p2</c> are <c>hero1</c> and <c>hero2</c>.
/// </summary>
internal sealed class StageScript
{
    public const string Region = "test_region";
    public static readonly Tile Origin = new(16, 10);
    public static readonly Tile Hero1 = new(1, 2), Hero2 = new(1, 3), Goblin1 = new(6, 2), Goblin2 = new(6, 4), Ally = new(2, 1);

    public StageScript(Func<int, int, TileInfo>? world = null, params PropPlacement[] props)
    {
        Map = StageWorld.Map("stage_map", 8, 6, (_, _) => StageWorld.Flat(0), props);
        (Ground, Stage) = StageWorld.Make(40, 30, world ?? ((_, _) => StageWorld.Flat(0)), Map, Origin, 0);
        Terrain = new BoardTerrain(Ground.Map, null);
    }

    public BattleMap Map { get; }

    public WorldGround Ground { get; }

    public BattleStage Stage { get; }

    public BoardTerrain Terrain { get; }

    public StageDirector Director(WorldState state = WorldState.Ready)
    {
        var director = new StageDirector(id => id == Map.Id ? Map : null);
        director.SetWorld(state, state == WorldState.Ready ? Terrain : null);
        return director;
    }

    public static readonly ImmutableArray<WalkerView> Party = [new("p1", "Ana", "characters.class_fighter", true, true), new("p2", "Bo", "characters.class_rogue", true, false)];

    public static ExploreView Explore(int epoch = 1, Tile? at = null, string? arrived = null, int seq = 0)
        => new(Region, "Test", epoch, seq, at ?? new Tile(12, 12), new Tile(13, 12), false, [], Party, "Ana", Arrived: arrived);

    public static UnitView Unit(string id, string side, Tile at, string life = "Up")
        => new(new UnitId(id), id, side, "characters.goblin_archer", at, 10, 10, 0, life, 0, [], null);

    public static ImmutableArray<UnitView> Units(Tile? hero1 = null, Tile? hero2 = null) =>
    [
        Unit("hero1", "Party", hero1 ?? Hero1), Unit("hero2", "Party", hero2 ?? Hero2), Unit("ally1", "Party", Ally),
        Unit("enemy1", "Enemy", Goblin1), Unit("enemy2", "Enemy", Goblin2),
    ];

    public static BattleView Battle(int lastSeq = 1, ImmutableArray<UnitView>? units = null, bool staged = true, string encounter = "test_fight", Tile? origin = null)
        => new(encounter, "stage_map", 1, [], null, units ?? Units(), [.. Enumerable.Range(Math.Max(1, lastSeq - 19), Math.Min(lastSeq, 20)).Select(s => new Beat(s, null, []))], null,
            BoardChanges.None, Stage: staged ? new StageView(Region, origin ?? Origin, 0, ["p1", "p2"]) : null);

    public static TacticsHostView Walking(ExploreView? explore = null) => new("Exploration", "", null, 1, [], null, null, null, null, Explore: explore ?? Explore());

    public static TacticsHostView Fight(BattleView battle, ExploreView? explore = null, string phase = "Battle")
        => new(phase, "", null, 1, [], null, battle, null, null, Explore: explore ?? Explore());

    public static TacticsHostView Results(BattleView last, ExploreView explore, string outcome = "Won")
        => new("Results", "", null, 1, [], null, null, outcome, [], last, explore);

    /// <summary>The walkers where the walk track has them, standing at <paramref name="p1"/> and <paramref name="p2"/>, in world tiles.</summary>
    public Dictionary<string, UnitPlace> Walkers(Tile p1, Tile p2) => new()
    {
        ["p1"] = new UnitPlace(new Vector2(p1.X + 0.5f, p1.Y + 0.5f), Terrain.Top(p1)),
        ["p2"] = new UnitPlace(new Vector2(p2.X + 0.5f, p2.Y + 0.5f), Terrain.Top(p2)),
    };

    /// <summary>Ticks <paramref name="director"/> on for <paramref name="seconds"/>, a frame at a time.</summary>
    public static void Run(StageDirector director, double seconds, double frame = 1 / 60.0, Action? each = null)
    {
        for (var t = 0.0; t < seconds - 1e-9; t += frame)
        {
            director.Tick(Math.Min(frame, seconds - t));
            each?.Invoke();
        }
    }
}
