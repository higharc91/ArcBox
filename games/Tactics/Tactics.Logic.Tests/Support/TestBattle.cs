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
    private readonly List<(string Id, UnitSheet Sheet)> _monsters = [];
    private readonly List<ItemDef> _items = [];
    private readonly List<AiProfileDef> _aiProfiles = [];
    private ObjectivesDef _objectives = ObjectivesDef.Default;
    private ImmutableArray<TriggerDef> _triggers = [];

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

    /// <summary>Starts a builder on <paramref name="map"/>.</summary>
    public static TestBattle On(BattleMap map) => new(map);

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

    /// <summary>Adds consumable items to the rules pack.</summary>
    public TestBattle WithItems(params ItemDef[] items)
    {
        _items.AddRange(items);
        return Rebuild();
    }

    /// <summary>Adds monster sheets (for summons and transformations) to the rules pack.</summary>
    public TestBattle WithMonsters(params (string Id, UnitSheet Sheet)[] monsters)
    {
        _monsters.AddRange(monsters);
        return Rebuild();
    }

    /// <summary>Adds AI profiles (weight sets for <see cref="Ai.Plan"/>) to the rules pack.</summary>
    public TestBattle WithAiProfiles(IEnumerable<AiProfileDef> profiles)
    {
        _aiProfiles.AddRange(profiles);
        return Rebuild();
    }

    private TestBattle Rebuild()
    {
        Rules = RulesPack.InMemory("test", abilities: _abilities, passives: _passives, conditions: _conditions, maps: [_map],
            injuries: _injuries.Count > 0 ? _injuries : null, monsters: _monsters, items: _items, aiProfiles: _aiProfiles);
        return this;
    }

    /// <summary>Sets the battle's objectives (the default: defeat every enemy; lose when the party is out).</summary>
    public TestBattle WithObjectives(ObjectivesDef objectives)
    {
        _objectives = objectives;
        return this;
    }

    /// <summary>Sets the battle's triggers.</summary>
    public TestBattle WithTriggers(params TriggerDef[] triggers)
    {
        _triggers = [.. triggers];
        return this;
    }

    /// <summary>Adds a protected NPC on the party's side, AI-controlled.</summary>
    public TestBattle Npc(string id, UnitSheet sheet, Tile at)
    {
        _units.Add(new UnitPlacement(new UnitId(id), sheet with { Side = Side.Party }, new AiControlled(sheet.AiProfile ?? "brute"), at,
            [], Protected: true));
        return this;
    }

    /// <summary>Adds a party member.</summary>
    public TestBattle Party(string id, UnitSheet sheet, Tile at, Controller? controller = null, ImmutableArray<ItemStack> bag = default)
        => Add(id, sheet with { Side = Side.Party }, controller ?? new PlayerControlled(new ProfileId("pr_" + id)), at, bag);

    /// <summary>Adds an enemy.</summary>
    public TestBattle Enemy(string id, UnitSheet sheet, Tile at, Controller? controller = null)
        => Add(id, sheet with { Side = Side.Enemy }, controller ?? new AiControlled(sheet.AiProfile ?? "brute"), at, default);

    /// <summary>Starts the battle and returns its first state and events.</summary>
    public (BattleState State, ImmutableArray<BattleEvent> Events) Start(ulong seed = 1)
    {
        var setup = new BattleSetup(_map, _units.ToImmutable(), _objectives, _triggers, Difficulty.Normal, Xp: 250, Loot: []);
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

    /// <summary>
    /// Runs <paramref name="effects"/> from <paramref name="source"/> on <paramref name="target"/> (null for a tile run) aimed
    /// at <paramref name="aim"/>, outside any ability, and drains the queue. The result must keep the engine's invariants.
    /// </summary>
    public (BattleState State, ImmutableArray<BattleEvent> Events) Run(BattleState state, string source, string? target, Tile? aim,
        params EffectDef[] effects)
    {
        var w = new BattleWriter(state, Rules);
        w.PushFront(new RunEffectsStep(new UnitId(source), target is null ? null : new UnitId(target), aim, null, [.. effects]));
        while (w.PopFront() is { } step)
            step.Run(w, Rules);
        Invariants.Check(w.State, Rules);
        return (w.Freeze(), w.Events);
    }

    private TestBattle Add(string id, UnitSheet sheet, Controller controller, Tile at, ImmutableArray<ItemStack> bag)
    {
        _units.Add(new UnitPlacement(new UnitId(id), sheet, controller, at, bag.IsDefault ? [] : bag));
        return this;
    }
}
