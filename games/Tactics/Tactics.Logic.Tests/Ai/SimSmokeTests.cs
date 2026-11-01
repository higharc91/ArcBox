using System.Collections.Immutable;
using Tactics.Sim;

namespace Tactics.Logic.Tests;

/// <summary>
/// The simulator plays battles on every core, so its tests run alone, after the parallel ones, and never take CPU from
/// <see cref="AiPerformanceTests"/>' time budget.
/// </summary>
[CollectionDefinition(DisableParallelization = true)]
public sealed class SimCollection;

/// <summary>
/// The balance simulator end to end (spec §5.6): every encounter in the base pack, played by AI on both sides, finishes
/// with an outcome, and the report renders every section.
/// </summary>
[Collection(typeof(SimCollection))]
public sealed class SimSmokeTests
{
    [Fact]
    public void Every_encounter_ends_with_3_seeds()
    {
        var rules = BasePackTests.Rules;
        var config = new SimConfig([.. rules.Encounters.Keys], Levels: [1], PartiesPerSize: 2, PartySizes: [4], Seeds: 3,
            Difficulty.Normal, RoundCap: 30);

        var battles = SimRunner.Run(config, rules).ToArray();

        Assert.Equal(rules.Encounters.Count * 2 * 3, battles.Length);
        foreach (var encounter in rules.Encounters.Keys)
            Assert.Equal(6, battles.Count(b => b.Encounter == encounter));
        Assert.All(battles, b =>
        {
            Assert.True(Enum.IsDefined(b.Outcome));
            Assert.InRange(b.Rounds, 1, config.RoundCap);
            Assert.Equal(4, b.Classes.Length);
            Assert.NotEmpty(b.PerClass);
            Assert.All(b.Abilities.Values, a => Assert.InRange(a.Used, 0, a.Legal));
        });
        Assert.Equal(new ulong[] { 1, 2, 3 }, battles.Select(b => b.Seed).Distinct().Order());
        Assert.Contains(battles, b => b.Abilities.Values.Any(a => a.Used > 0));

        // Damage is credited from the events as HP actually lost. Enemies here don't heal, so no battle credits more than
        // the enemies' HP, those placed and those a trigger may spawn (overkill doesn't count), and a battle won by
        // defeating every enemy credits nearly all of the placed enemies' HP: only an unsourced kill (a fall, deep water) is
        // left out. (A battle won another way, by surviving, say, needn't have defeated anyone.)
        double Dealt(SimBattle b) => b.PerClass.Sum(p => p.Value.DamagePerRound * b.Classes.Count(c => c == p.Key) * b.Rounds);
        double EnemyHp(SimBattle b) => rules.Encounters[b.Encounter].Enemies
            .Sum(e => e.Count * SheetBuilder.ForMonster(e.Unit, rules, e.Level).MaxHp);
        double SpawnHp(SimBattle b) => rules.Encounters[b.Encounter].Triggers.SelectMany(t => t.Do).Select(a => a.Spawn)
            .OfType<SpawnAction>().Sum(s => s.Count * SheetBuilder.ForMonster(s.Unit, rules, s.Level).MaxHp);
        Assert.All(battles, b => Assert.True(Dealt(b) <= EnemyHp(b) + SpawnHp(b) + 0.01,
            $"Seed {b.Seed} credits {Dealt(b):0.00} damage against {EnemyHp(b) + SpawnHp(b)} enemy HP."));
        var won = battles.Where(b => b.Outcome == BattleOutcome.Won
                                     && rules.Encounters[b.Encounter].Objectives.Win.Leaf == Objectives.DefeatAll).ToArray();
        Assert.NotEmpty(won);
        Assert.True(won.Sum(Dealt) >= 0.9 * won.Sum(EnemyHp), $"The party is credited {won.Sum(Dealt):0} damage against {won.Sum(EnemyHp)} enemy HP.");

        // Reactions answer someone else's action: they have no legal turns, so they aren't counted.
        Assert.All(battles.SelectMany(b => b.Abilities.Keys), id => Assert.Equal(0, rules.Abilities[id].Cost.Reaction));

        var report = Report.Markdown(battles, config);
        string[] headings =
        [
            "## 1. Summary", "## 2. Win rate by encounter and level", "## 3. Rounds per battle", "## 4. Role bands",
            "## 5. Rarely used abilities", "## 6. Downs, Outs and AI item use", "## 7. Configuration and seeds",
        ];
        Assert.Equal(headings, Report.Headings);
        var at = 0;
        foreach (var heading in headings)
        {
            var next = report.IndexOf(heading, at, StringComparison.Ordinal);
            Assert.True(next >= at, $"The report is missing \"{heading}\" (in order).");
            at = next;
        }
    }

    [Fact]
    public void Same_config_gives_the_same_battles()
    {
        var rules = BasePackTests.Rules;
        var config = new SimConfig([.. rules.Encounters.Keys.Take(1)], Levels: [3], PartiesPerSize: 1, PartySizes: [4], Seeds: 1,
            Difficulty.Normal);

        var first = SimRunner.Run(config, rules).Single();
        var second = SimRunner.Run(config, rules).Single();

        Assert.Equal(Report.Markdown([first], config), Report.Markdown([second], config));
        Assert.Equal((first.Outcome, first.Rounds, first.Downs, first.Outs), (second.Outcome, second.Rounds, second.Downs, second.Outs));
    }

    [Fact]
    public void Sampled_parties_are_fixed_distinct_and_have_a_healer_or_defender()
    {
        var rules = BasePackTests.Rules;
        var config = new SimConfig([], Levels: [1], PartiesPerSize: 6, PartySizes: [4, 5, 6], Seeds: 1, Difficulty.Normal);

        var parties = SimRunner.Parties(config, rules);

        Assert.Equal(parties, SimRunner.Parties(config, rules));
        foreach (var size in config.PartySizes)
        {
            var ofSize = parties.Where(p => p.Length == size).ToArray();
            Assert.Equal(6, ofSize.Length);
            Assert.Equal(6, ofSize.Select(p => string.Join(",", p)).Distinct().Count());
        }
        Assert.All(parties, p =>
        {
            Assert.Contains(p, c => SimRunner.BandOf(c) is RoleBand.Healer or RoleBand.Defender);
            Assert.All(p, c => Assert.True(rules.Classes.ContainsKey(c)));
            Assert.Equal(p.Order(StringComparer.Ordinal), p);
        });
    }
}
