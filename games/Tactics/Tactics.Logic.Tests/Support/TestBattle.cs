using System.Collections.Immutable;
using ArcBox.Sdk;

namespace Tactics.Logic.Tests;

/// <summary>
/// Builds a battle on an ASCII map for tests:
/// <c>TestBattle.On(ascii).Party("kara", TestSheets.Fighter, at).Enemy("gob", TestSheets.Goblin, at).Start(seed)</c>.
/// Party members are player-controlled (profile <c>pr_&lt;id&gt;</c>) unless a controller is given; enemies use their
/// sheet's AI profile. <see cref="Rules"/> holds the map, so the result can be passed straight to <see cref="Battle.Apply"/>.
/// </summary>
public sealed class TestBattle
{
    private readonly BattleMap _map;
    private readonly ImmutableArray<UnitPlacement>.Builder _units = ImmutableArray.CreateBuilder<UnitPlacement>();
    private readonly List<AbilityDef> _abilities = [];
    private readonly List<PassiveDef> _passives = [];
    private readonly List<ConditionDef> _conditions = [];
    private readonly List<InjuryDef> _injuries = [];

    /// <summary>Tests check the engine's invariants after every command, in any build configuration.</summary>
    static TestBattle() => Invariants.Enabled = true;

    private TestBattle(BattleMap map)
    {
        _map = map;
        Rules = RulesPack.InMemory("test", maps: [map]);
    }

    /// <summary>
    /// The rules pack the battle runs under: the map, the built-in abilities and any added with <see cref="With"/>,
    /// <see cref="WithPassives"/>, <see cref="WithConditions"/> and <see cref="WithInjuries"/> (without any, the pack's
    /// single test injury).
    /// </summary>
    public RulesPack Rules { get; private set; }

    /// <summary>Starts a builder on the given ASCII map (see <see cref="AsciiMap"/>).</summary>
    public static TestBattle On(string ascii) => new(AsciiMap.Parse(ascii));

    /// <summary>Adds abilities to the rules pack.</summary>
    public TestBattle With(params AbilityDef[] abilities)
    {
        _abilities.AddRange(abilities);
        return Rebuild();
    }

    /// <summary>Adds passives to the rules pack.</summary>
    public TestBattle WithPassives(params PassiveDef[] passives)
    {
        _passives.AddRange(passives);
        return Rebuild();
    }

    /// <summary>Adds conditions to the rules pack.</summary>
    public TestBattle WithConditions(params ConditionDef[] conditions)
    {
        _conditions.AddRange(conditions);
        return Rebuild();
    }

    /// <summary>Replaces the rules pack's injury table.</summary>
    public TestBattle WithInjuries(params InjuryDef[] injuries)
    {
        _injuries.AddRange(injuries);
        return Rebuild();
    }

    private TestBattle Rebuild()
    {
        Rules = RulesPack.InMemory("test", abilities: _abilities, passives: _passives, conditions: _conditions, maps: [_map],
            injuries: _injuries.Count > 0 ? _injuries : null);
        return this;
    }

    /// <summary>Adds a party member.</summary>
    public TestBattle Party(string id, UnitSheet sheet, Tile at, Controller? controller = null)
        => Add(id, sheet with { Side = Side.Party }, controller ?? new PlayerControlled(new ProfileId("pr_" + id)), at);

    /// <summary>Adds an enemy.</summary>
    public TestBattle Enemy(string id, UnitSheet sheet, Tile at, Controller? controller = null)
        => Add(id, sheet with { Side = Side.Enemy }, controller ?? new AiControlled(sheet.AiProfile ?? "brute"), at);

    /// <summary>Starts the battle and returns its first state and events.</summary>
    public (BattleState State, ImmutableArray<BattleEvent> Events) Start(ulong seed = 1)
    {
        var setup = new BattleSetup(_map, _units.ToImmutable(), ObjectivesDef.Default, [], Difficulty.Normal, Xp: 250, Loot: []);
        var resolution = Battle.Start(setup, Rules, seed);
        return (resolution.State, resolution.Events);
    }

    /// <summary>Applies a command that must be accepted.</summary>
    public (BattleState State, ImmutableArray<BattleEvent> Events) Apply(BattleState state, Command command)
    {
        var resolution = Battle.Apply(state, command, Rules);
        Assert.Null(resolution.Rejected);
        return (resolution.State, resolution.Events);
    }

    private TestBattle Add(string id, UnitSheet sheet, Controller controller, Tile at)
    {
        _units.Add(new UnitPlacement(new UnitId(id), sheet, controller, at, []));
        return this;
    }
}
