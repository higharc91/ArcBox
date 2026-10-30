using System.Collections.Immutable;
using System.Text.Json;
using ArcBox.Sdk;

namespace Tactics.Logic.Tests;

public sealed class FlowBlockTests
{
    private static UnitId U(string id) => new(id);

    private static Tile T(int x, int y) => new(x, y);

    private static readonly DamageEffect Zap = new(TestAbilities.Flat("1d4"), "lightning", []);

    private static readonly ConditionId Prone = TestConditions.Prone.Id;

    [Fact]
    public void Chain_bounces_to_nearest_new_targets_in_range()
    {
        const string row = """
            0. 0. 0. 0. 0. 0. 0.
            0. 0. 0. 0. 0. 0. 0.
            0. 0. 0. 0. 0. 0. 0.
            0. 0. 0. 0. 0. 0. 0.
            """;
        var goblin = TestSheets.Goblin with { MaxHp = 30 };
        var battle = TestBattle.On(row)
            .Party("mage", TestSheets.Cleric, T(0, 1))
            .Party("kara", TestSheets.Fighter, T(4, 1))
            .Enemy("g1", goblin, T(2, 1))
            .Enemy("g2", goblin, T(3, 1))
            .Enemy("g3", goblin, T(5, 1))
            .Enemy("g4", goblin, T(2, 3));
        var (state, _) = battle.Start();

        // From g1: g2 is nearest (1). From g2: kara (1) is an ally of the caster, so g3 (2) beats g4 (3). From g3, g4 is 5
        // tiles away, beyond the range of 2: the chain stops after 2 of its 3 bounces.
        var (_, events) = battle.Run(state, "mage", "g1", T(2, 1), new ChainEffect(3, 2, [Zap]));
        Assert.Equal([U("g1"), U("g2"), U("g3")], events.OfType<Damaged>().Select(d => d.Unit));
    }

    [Fact]
    public void Repeat_runs_its_effects_n_times()
    {
        var battle = TestBattle.On("0. 0. 0.")
            .Party("mage", TestSheets.Cleric, T(0, 0))
            .Enemy("gob", TestSheets.Goblin with { MaxHp = 30 }, T(2, 0));
        var (state, _) = battle.Start();

        var dice = TestAbilities.Next(state.Rng, 4, 4, 4);
        var (after, events) = battle.Run(state, "mage", "gob", T(2, 0), new RepeatEffect(3, [Zap]));
        Assert.Equal(dice, events.OfType<Damaged>().Select(d => d.Amount));
        Assert.Equal(30 - dice.Sum(), after.Units[U("gob")].Hp);
    }

    [Fact]
    public void If_else_branches_on_target_condition()
    {
        var battle = TestBattle.On("0. 0. 0.")
            .Party("mage", TestSheets.Cleric, T(0, 0))
            .Enemy("gob", TestSheets.Goblin with { MaxHp = 30 }, T(2, 0))
            .WithConditions([.. TestConditions.All]);
        var (state, _) = battle.Start();
        var knockOrHit = new IfEffect(new WhenDef(TargetHasCondition: Prone), [Zap], [new ApplyEffect(Prone, null)]);

        var (after, events) = battle.Run(state, "mage", "gob", T(2, 0), knockOrHit);
        Assert.Equal<BattleEvent>([new ConditionApplied(U("gob"), Prone)], events);

        (_, events) = battle.Run(after, "mage", "gob", T(2, 0), knockOrHit);
        Assert.IsType<Damaged>(Assert.Single(events));

        // On the outcome of the roll it follows: a failed save (DC 30) takes the "else" branch of a success test.
        var onFail = new SaveEffect(Stat.Dex, 30, [new IfEffect(new WhenDef(Success: true), [Zap], [new ApplyEffect(Prone, null)])], []);
        (after, events) = battle.Run(state, "mage", "gob", T(2, 0), onFail);
        Assert.Equal(new ConditionApplied(U("gob"), Prone), events[^1]);
    }

    [Fact]
    public void Random_table_weighted_by_rng()
    {
        var table = new RandomEffect([new RandomEntry(1, [new ApplyEffect(Prone, null)]), new RandomEntry(3, [Zap])]);
        var seen = new SortedSet<int>();
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var battle = TestBattle.On("0. 0. 0.")
                .Party("mage", TestSheets.Cleric, T(0, 0))
                .Enemy("gob", TestSheets.Goblin with { MaxHp = 30 }, T(2, 0))
                .WithConditions([.. TestConditions.All]);
            var (state, _) = battle.Start(seed);

            // One d4 over the total weight: 1 picks the first entry (weight 1), 2–4 the second (weight 3).
            var roll = TestAbilities.Next(state.Rng, 4)[0];
            seen.Add(roll);
            var (_, events) = battle.Run(state, "mage", "gob", T(2, 0), table);
            if (roll == 1)
                Assert.Equal<BattleEvent>([new ConditionApplied(U("gob"), Prone)], events);
            else
                Assert.IsType<Damaged>(Assert.Single(events));
        }
        Assert.Contains(1, seen);
        Assert.True(seen.Count > 1);
    }

    /// <summary>Every block key and the test class that pins its behaviour.</summary>
    private static readonly ImmutableSortedDictionary<string, string> TestedKeys = Ordinal.Map(
        ("attack", nameof(UseCommandTests)), ("save", nameof(UseCommandTests)), ("damage", nameof(UseCommandTests)),
        ("heal", nameof(UseCommandTests)), ("apply", nameof(ConditionTests)), ("remove", nameof(UseCommandTests)),
        ("help", nameof(DownedTests)), ("push", nameof(MotionTests)), ("pull", nameof(MotionTests)),
        ("teleport", nameof(MotionTests)), ("swap", nameof(MotionTests)), ("dash", nameof(MotionTests)),
        ("jumpTo", nameof(MotionTests)),
        ("tempHp", nameof(CombatBlockTests)), ("revive", nameof(CombatBlockTests)), ("cleanse", nameof(CombatBlockTests)),
        ("dispel", nameof(CombatBlockTests)),
        ("zone", nameof(MapBlockTests)), ("alterTerrain", nameof(MapBlockTests)), ("hazard", nameof(MapBlockTests)),
        ("summon", nameof(UnitBlockTests)), ("transform", nameof(NamedHookTests)),
        ("grantResource", nameof(CombatBlockTests)), ("extraAction", nameof(CombatBlockTests)),
        ("refreshReaction", nameof(CombatBlockTests)), ("statMod", nameof(CombatBlockTests)), ("mark", nameof(CombatBlockTests)),
        ("taunt", nameof(CombatBlockTests)), ("reveal", nameof(CombatBlockTests)),
        ("turnOrder", nameof(TurnOrderBlockTests)),
        ("chain", nameof(FlowBlockTests)), ("repeat", nameof(FlowBlockTests)), ("if", nameof(FlowBlockTests)),
        ("random", nameof(FlowBlockTests)));

    [Fact]
    public void Every_registered_key_has_a_test()
        => Assert.Equal(TestedKeys.Keys, BlockRegistry.Keys);

    [Fact]
    public void New_blocks_round_trip_in_single_key_form()
    {
        ImmutableArray<EffectDef> effects =
        [
            new TempHpEffect(TestAbilities.Flat("1d4"), ["mod.wis"]),
            new ReviveEffect(1),
            new CleanseEffect(Ordinal.Set("magical", "B", "a")),
            new DispelEffect(Concentration: true),
            new ZoneEffect("fire", new AreaDef(AreaShape.Burst, 1), 3,
                [new PassiveDef("zone.fire", HookKind.OnEnterTile, null, [Zap], null, Target: PassiveTarget.Self)], Difficult: true),
            new AlterTerrainEffect(new AreaDef(AreaShape.Single), -2),
            new HazardEffect("hazards.spikes", new AreaDef(AreaShape.Single), 2, [Zap]),
            new SummonEffect("wolf", 2, null, Concentration: true),
            new TransformEffect("wild_shape", JsonDocument.Parse("""{"monster":"wolf"}""").RootElement.Clone()),
            new GrantResourceEffect("mana", 2),
            new ExtraActionEffect("bonus"),
            new RefreshReactionEffect(),
            new StatModEffect(new StaticMods(Ac: 2), 10, Concentration: true),
            new MarkEffect(10),
            new TauntEffect(1),
            new RevealEffect(new AreaDef(AreaShape.Burst, 2)),
            new TurnOrderEffect("swap"),
            new ChainEffect(2, 3, [Zap]),
            new RepeatEffect(3, [Zap]),
            new IfEffect(new WhenDef(HpBelow: 50), [Zap], [new ApplyEffect(Prone, 1)]),
            new RandomEffect([new RandomEntry(1, [Zap]), new RandomEntry(2, [])]),
        ];

        var json = JsonSerializer.Serialize(effects, ArcJson.Options);
        var back = JsonSerializer.Deserialize<ImmutableArray<EffectDef>>(json, ArcJson.Options);
        Assert.Equal(json, JsonSerializer.Serialize(back, ArcJson.Options));
        Assert.Equal(effects.Select(e => e.GetType()), back.Select(e => e.GetType()));

        Assert.Contains("""{"cleanse":{"tags":["B","a","magical"]}}""", json);
        Assert.Same(StringComparer.Ordinal, ((CleanseEffect)back[2]).Tags.KeyComparer);
        Assert.Contains("""{"refreshReaction":{}}""", json);
        Assert.Contains("""{"chain":{"times":2,"range":3},"effects":[{"damage":""", json);
        Assert.Contains("""{"if":{"when":{"hpBelow":50}},"then":[""", json);
        Assert.Contains("""{"transform":{"hook":"wild_shape","args":{"monster":"wolf"}}}""", json);
        Assert.Contains("""{"turnOrder":{"op":"swap"}}""", json);
        Assert.Contains("""{"summon":{"monster":"wolf","count":2,"concentration":true}}""", json);
        Assert.Contains("""{"hazard":{"hazardArt":"hazards.spikes","area":{"shape":"single"},"rounds":2},"onEnter":[""", json);
    }
}
