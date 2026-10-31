using System.Collections.Immutable;
using System.Diagnostics;

namespace Tactics.Logic.Tests;

/// <summary>
/// The AI's time budget (spec §5.2): a turn plan in at most 250 ms at the 95th percentile on 6 vs 10 units, in a stress
/// setup and in the largest shipped encounters.
/// </summary>
public sealed class AiPerformanceTests(ITestOutputHelper output)
{
    private const int Warmup = 5;
    private const int Timed = 100;
    private const double BudgetMs = 250;

    [Fact]
    [Trait("Category", "Perf")]
    public void P95_under_250ms_on_6_vs_10()
    {
        var rules = BasePackTests.Rules;
        AssertWithinBudget(Setup(rules), rules, "river_crossing stress, level 10");
    }

    /// <summary>
    /// The shipped encounters with the most enemies (10 each), as written and at their intended level, against 6 heroes.
    /// The stress test above uses inflated HP to keep the field crowded; these time what a real battle asks of the AI.
    /// </summary>
    [Theory]
    [Trait("Category", "Perf")]
    [InlineData("crypt_hall", 5)]
    [InlineData("crystal_cave", 7)]
    public void P95_under_250ms_in_the_largest_sample_encounters(string encounter, int level)
    {
        var rules = BasePackTests.Rules;
        var setup = Encounters.Setup(encounter, Party(rules, level), Difficulty.Normal, rules);
        Assert.Equal(10, setup.Units.Count(u => u.Sheet.Side == Side.Enemy));
        AssertWithinBudget(setup, rules, $"{encounter}, level {level}");
    }

    private void AssertWithinBudget(BattleSetup setup, RulesPack rules, string label)
    {
        // The start of every turn in two played-out battles, cycled: warm up, then time only the Plan calls.
        var turns = new ulong[] { 1, 2 }.SelectMany(seed => Played(setup, rules, seed, turns: 32)).ToArray();
        for (var i = 0; i < Warmup; i++)
            Ai.Plan(turns[i % turns.Length], turns[i % turns.Length].Active!.Value, rules);
        var times = new double[Timed];
        var watch = new Stopwatch();
        for (var i = 0; i < Timed; i++)
        {
            var state = turns[(i + Warmup) % turns.Length];
            var unit = state.Active!.Value;
            watch.Restart();
            var plan = Ai.Plan(state, unit, rules);
            watch.Stop();
            times[i] = watch.Elapsed.TotalMilliseconds;
            Assert.IsType<EndTurn>(plan.Commands[^1]);
        }

        Array.Sort(times);
        var p95 = times[(int)Math.Ceiling(0.95 * Timed) - 1];
        output.WriteLine($"{label}: {turns.Length} turn states; plan times (ms): median {times[Timed / 2]:0.0}, p95 {p95:0.0}, max {times[^1]:0.0}");
        Assert.True(p95 <= BudgetMs, $"{label}: p95 {p95:0.0} ms is over the {BudgetMs} ms budget (median {times[Timed / 2]:0.0} ms).");
    }

    /// <summary>
    /// 6 heroes (fighter, cleric, rogue, wizard, fighter, cleric; the last three absent, so companions) at
    /// <paramref name="level"/>.
    /// </summary>
    private static ImmutableArray<PartyMember> Party(RulesPack rules, int level)
    {
        string[] classes = ["fighter", "cleric", "rogue", "wizard", "fighter", "cleric"];
        var profiles = new Dictionary<string, string>
        {
            ["fighter"] = "brute", ["cleric"] = "healer", ["rogue"] = "skirmisher", ["wizard"] = "caster",
        };
        return [.. classes.Select((cls, i) =>
        {
            var id = $"{cls}{i + 1}";
            return new PartyMember(new UnitId(id), SheetBuilder.DefaultCharacter(id, "human", cls, null, level, rules),
                new AiControlled(i < 3 ? profiles[cls] : "companion"));
        })];
    }

    /// <summary>
    /// 6 level-10 heroes (fighter, cleric, rogue, wizard, fighter, cleric; the last three absent, so companions) against 10
    /// level-10 monsters with triple HP (so the battle lasts) on the river crossing: the encounter's six goblins plus four
    /// more placed on the free tiles nearest their camp.
    /// </summary>
    private static BattleSetup Setup(RulesPack rules)
    {
        var setup = Encounters.Setup("river_crossing", Party(rules, 10), Difficulty.Normal, rules);

        // Four more monsters on the free walkable tiles nearest the goblin camp.
        var terrain = new Terrain(setup.Map, ImmutableSortedDictionary<Tile, int>.Empty, []);
        var taken = setup.Units.Select(u => u.At).ToHashSet();
        var camp = new Tile(9, 6);
        var free = Enumerable.Range(0, setup.Map.Width).SelectMany(x => Enumerable.Range(0, setup.Map.Height).Select(y => new Tile(x, y)))
            .Where(t => t.X >= 7 && terrain.Walkable(t) && !taken.Contains(t))
            .OrderBy(t => Reach.Distance(t, camp)).ThenBy(t => t).Take(4).ToArray();
        string[] extra = ["hobgoblin", "goblin_boss", "skeleton", "hobgoblin"];
        var more = extra.Select((m, i) =>
        {
            var sheet = SheetBuilder.ForMonster(m, rules) with { Side = Side.Enemy };
            return new UnitPlacement(new UnitId($"{m}.x{i + 1}"), sheet, new AiControlled(sheet.AiProfile ?? "brute"), free[i], []);
        });
        return setup with
        {
            Units = [.. setup.Units.AddRange(more).Select(u => u.Sheet.Side == Side.Enemy
                ? u with { Sheet = u.Sheet with { Level = 10, MaxHp = 3 * u.Sheet.MaxHp } }
                : u)],
        };
    }

    /// <summary>
    /// The state at the start of each turn, the AI playing every unit, for <paramref name="turns"/> turns: later turns see a
    /// closed-in, messier field.
    /// </summary>
    private static IEnumerable<BattleState> Played(BattleSetup setup, RulesPack rules, ulong seed, int turns)
    {
        var state = Battle.Start(setup, rules, seed).State;
        Assert.Equal(16, state.Units.Count);
        for (var turn = 0; turn < turns && state.Outcome is null; turn++)
        {
            var unit = state.Active!.Value;
            yield return state;
            foreach (var command in Ai.Plan(state, unit, rules).Commands)
            {
                var resolution = Battle.Apply(state, command, rules);
                if (resolution.Rejected is not null)
                    break;
                state = resolution.State;
                if (state.Outcome is not null || state.Active != unit
                    || command is Move move && state.Units[unit].Pos != move.Path[^1])
                    break;
            }
            if (state.Outcome is null && state.Active == unit)
                state = Battle.Apply(state, new EndTurn(unit), rules).State;
        }
    }
}
