using System.Collections.Immutable;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Beats;
using Tactics.Presentation.Tests.Board;

namespace Tactics.Presentation.Tests.Beats;

/// <summary>
/// <see cref="BeatScript"/> on the engine's own event streams. A level 9 party with every caster, which takes the most targets it
/// can each turn, produces what a level 1 AI fight doesn't: area spells, passives firing inside them, on-hit passives, extra attacks.
/// </summary>
public sealed class RealBattleTimingTests
{
    private static readonly Lazy<RulesPack> Rules = new(() => RulesPack.Load([Path.Combine(Pack.Root, "rules")]));
    private static readonly Lazy<FxArt> Fx = new(() => FxArt.Load(Pack.Root));
    private static readonly Lazy<ImmutableArray<(Beat Beat, BattleView Before)>> Beats = new(Play);

    private static BattleView ViewOf(BattleState s) => new("e", s.MapId, s.Round, s.Order, s.Active,
        [.. s.Units.Values.Select(u => new UnitView(u.Id, u.Sheet.Name, u.Sheet.Side.ToString(), u.Sheet.Sprite, u.Pos, u.Hp, u.Sheet.MaxHp, u.TempHp, u.Life.ToString(),
            u.Countdown, [.. u.Conditions.Select(c => c.Id.Value)], null))],
        [], null, BoardChanges.Of(s));

    private static ImmutableArray<(Beat, BattleView)> Play()
    {
        var rules = Rules.Value;
        var beats = ImmutableArray.CreateBuilder<(Beat, BattleView)>();
        string[] classes = ["wizard", "sorcerer", "cleric", "fighter", "druid", "warlock"];
        // Curated presentation coverage, not a random balance sample: several bridge seeds exercise phases/terrain.
        // Forest seed 7 is restored after the three-beam/Repelling area-beat fix (R8). The historical 1.15 s
        // save-area report from Task 3 has not been proven to be that same fault; river seed 4 is a sample,
        // not proof about seed 3. The 1.1 s bound is unchanged.
        (string, ulong)[] fights = [("river_crossing", 4), ("warlord_bridge", 5), ("forest_ruins", 7), ("crypt_hall", 9), ("crystal_cave", 11), ("warlord_bridge", 13), ("crypt_hall", 15), ("forest_ruins", 17), ("warlord_bridge", 19), ("warlord_bridge", 21), ("warlord_bridge", 23), ("warlord_bridge", 25), ("warlord_bridge", 27), ("warlord_bridge", 29)];
        foreach (var (encounter, seed) in fights)
        {
            PartyMember[] party = [.. classes.Select((c, i) => new PartyMember(new UnitId($"hero{i + 1}"),
                SheetBuilder.DefaultCharacter(c, "human", c, null, 9, rules), new AiControlled("companion")))];
            var s = Battle.Start(Encounters.Setup(encounter, [.. party], Difficulty.Normal, rules), rules, seed).State;
            for (var step = 0; step < 4000 && s.Outcome is null && Next(s, rules) is { } command; step++)
            {
                var result = Battle.Apply(s, command, rules);
                if (result.Rejected is not null && s.Active is { } active)
                    result = Battle.Apply(s, new EndTurn(active), rules);
                if (result.Rejected is not null)
                    break;
                if (!result.Events.IsDefaultOrEmpty)
                    beats.Add((new Beat(beats.Count + 1, command.Actor, result.Events), ViewOf(s)));
                s = result.State;
            }
        }
        // A guaranteed real failed save keeps Indomitable coverage independent of which spells the tuned AI chooses.
        PartyMember[] passiveParty = [.. classes.Select((c, i) => new PartyMember(new UnitId($"hero{i + 1}"),
            SheetBuilder.DefaultCharacter(c, "human", c, null, 9, rules), new AiControlled("companion")))];
        var probe = Battle.Start(Encounters.Setup("warlord_bridge", [.. passiveParty], Difficulty.Normal, rules), rules, 5).State;
        var fighter = new UnitId("hero4");
        var unit = probe.Units[fighter];
        probe = probe with
        {
            TurnIndex = probe.Order.IndexOf(fighter),
            Units = probe.Units.SetItem(fighter, unit with { Conditions = unit.Conditions.Add(
                new ActiveCondition(new("poisoned"), fighter, null, true, 99, Stat.Con, false)) }),
        };
        var saved = Battle.Apply(probe, new EndTurn(fighter), rules);
        Assert.Null(saved.Rejected);
        Assert.Contains(saved.Events, e => e is PassiveTriggered { Passive: "fighter.indomitable" });
        beats.Add((new Beat(beats.Count + 1, fighter, saved.Events), ViewOf(probe)));
        // A real flanked strike keeps the on-hit passive sample stable when area-heavy parties kill guards at range.
        var hob = new UnitId("probe.hob");
        var victim = new UnitId("probe.fighter");
        var defender = SheetBuilder.ForCharacter(passiveParty[3].Character, rules) with { Ac = 0, Abilities = [] };
        var guard = SheetBuilder.ForMonster("hobgoblin", rules, 5) with { InitiativeBonus = 100, Attacks = 1 };
        var setup = new BattleSetup(rules.Maps["warlord_bridge"],
            [new(victim, defender, new AiControlled("companion"), new(1, 5), []),
             new(hob, guard, new AiControlled("brute"), new(1, 4), []),
             new(new("probe.ally"), guard with { InitiativeBonus = 0 }, new AiControlled("brute"), new(1, 6), [])],
            ObjectivesDef.Default, [], Difficulty.Normal, 0, []);
        var flanked = Battle.Start(setup, rules, 7).State;
        var struck = Battle.Apply(flanked, new Use(hob, RulesPack.CommonAttack.Id, new Target(Unit: victim)), rules);
        Assert.Null(struck.Rejected);
        Assert.Contains(struck.Events, e => e is PassiveTriggered { Passive: "hobgoblin.martial_advantage" });
        beats.Add((new Beat(beats.Count + 1, hob, struck.Events), ViewOf(flanked)));
        return beats.ToImmutable();
    }

    private static Command? Next(BattleState s, RulesPack rules)
    {
        if (s.Pending is ReactionOffer offer)
            return new React(offer.Reactor, Ai.React(s, offer, rules));
        if (s.Active is not { } active)
            return null;
        if (s.Units[active].Sheet.Side == Side.Party)
        {
            var widest = Choices.For(s, active, rules).Abilities.Where(a => a.Item is null).SelectMany(a => a.Targets.Select(t => (a.Ability, t)))
                .OrderByDescending(x => x.t.Affected.Length).FirstOrDefault();
            if (widest.t is { Affected.Length: >= 2 })
                return new Use(active, widest.Ability, widest.t.Target, null);
        }
        var plan = Ai.Plan(s, active, rules, new AiOptions()).Commands;
        return plan.IsDefaultOrEmpty ? new EndTurn(active) : plan[0];
    }

    private static BeatScript Script((Beat Beat, BattleView Before) real) => BeatScript.Of(real.Beat, real.Before, real.Before, Fx.Value);

    private static ImmutableArray<BattleEvent> OneAbility((Beat Beat, BattleView Before) real, int at)
    {
        var rest = real.Beat.Events.Skip(at + 1).TakeWhile(e => e is not (AbilityUsed or TurnEnded));
        return [real.Beat.Events[at], .. rest];
    }

    private static bool OneAbilityOnly((Beat Beat, BattleView Before) real) => real.Beat.Events.OfType<AbilityUsed>().Count() == 1;

    [Fact]
    public void The_battles_really_do_hold_what_these_tests_are_about()
    {
        var events = Beats.Value.SelectMany(b => b.Beat.Events).ToArray();

        Assert.Contains(events, e => e is PassiveTriggered { Passive: "hobgoblin.martial_advantage" });
        Assert.Contains(events, e => e is PassiveTriggered { Passive: "fighter.indomitable" });
        Assert.Contains(Beats.Value, b => b.Beat.Events.OfType<Rolled>().Where(r => r.Kind == RollKind.Save).Select(r => r.Roller).Distinct().Count() >= 3);
    }

    [Fact]
    public void Every_real_save_area_lands_within_about_a_second()
    {
        var areas = Beats.Value.Where(OneAbilityOnly)
            .Where(b => b.Beat.Events.OfType<Rolled>().Where(r => r.Kind == RollKind.Save).Select(r => r.Roller).Distinct().Count() >= 3).ToArray();

        Assert.NotEmpty(areas);
        Assert.All(areas, b =>
        {
            var last = Script(b).Cues.OfType<ApplyCue>().Where(a => a.Event is Damaged).Select(a => a.At).DefaultIfEmpty(0).Max();
            if (last > 1.1)
                Assert.Fail($"Last damage at {last}:\n{string.Join("\n", b.Beat.Events)}\nCues:\n{string.Join("\n", Script(b).Cues)}");
            Assert.InRange(last, 0, 1.1);
        });
    }

    [Fact]
    public void Every_real_attack_on_one_target_swings_once_per_roll()
    {
        var single = Beats.Value.Where(OneAbilityOnly).Where(b =>
        {
            var used = b.Beat.Events.OfType<AbilityUsed>().Single();
            return used.Target.Unit is { } target && target != used.Source && b.Beat.Events.OfType<AttackResolved>().Any()
                && b.Beat.Events.OfType<AttackResolved>().All(a => a.Target == target)
                && b.Before.Units.Any(u => u.Id == used.Source);
        }).ToArray();

        Assert.True(single.Length > 20, $"only {single.Length}");
        Assert.All(single, b =>
        {
            var script = Script(b);
            var swings = script.Cues.OfType<LungeCue>().Count() + script.Cues.OfType<PoseCue>().Count(p => p.Toward is not null);
            Assert.Equal(b.Beat.Events.OfType<AttackResolved>().Count(), swings);
        });
    }
}
