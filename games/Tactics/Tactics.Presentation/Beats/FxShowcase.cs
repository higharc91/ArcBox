using System.Collections.Immutable;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.Beats;

/// <summary>A deterministic showcase sample made by the real engine, consumed by the ordinary TV scene.</summary>
public sealed record FxShowcaseSample(string Id, string Name, string Description, int Level, BattleView Before, BattleView After);

public static class FxShowcase
{
    /// <summary>The step-0 reel covers an ordinary attack, cantrip, paid spell and signature effect.</summary>
    public static ImmutableArray<FxShowcaseSample> StepZero(string packRoot)
    {
        var rules = RulesPack.Load([Path.Combine(packRoot, "rules")]);
        return [Sample("wizard", RulesPack.CommonAttack.Id, 1, rules, 1),
            Sample("wizard", new("wizard.fire_bolt"), 1, rules, 2),
            Sample("wizard", new("wizard.magic_missile"), 1, rules, 3),
            Sample("wizard", new("wizard.fireball"), 5, rules, 4)];
    }

    public static FxShowcaseSample Sample(string cls, AbilityId abilityId, int level, RulesPack rules, int sequence)
    {
        var ability = rules.Abilities[abilityId];
        var map = rules.Maps["crypt_hall"];
        var terrain = new Terrain(map, ImmutableSortedDictionary<Tile, int>.Empty, []);
        var occupied = new HashSet<Tile>();
        Tile Free(Tile preferred) => Enumerable.Range(0, map.Height).SelectMany(y => Enumerable.Range(0, map.Width).Select(x => new Tile(x, y)))
            .Where(t => terrain.Walkable(t) && !occupied.Contains(t))
            .OrderBy(t => Reach.Distance(t, preferred)).ThenBy(t => t).First();
        Tile Place(Tile preferred) { var tile = Free(preferred); occupied.Add(tile); return tile; }
        var caster = new UnitId("hero1");
        var sheet = SheetBuilder.ForCharacter(SheetBuilder.DefaultCharacter("Ember", "human", cls, null, level, rules), rules)
            with { InitiativeBonus = 100, Sprite = "characters.class_" + cls, Abilities = [abilityId] };
        var from = Place(new(3, 5));
        var melee = ability.IsWeaponAttack || ability.Target.Range == 1;
        var target = Place(melee ? new(from.X + 1, from.Y) : new(7, 5));
        var goblin = SheetBuilder.ForMonster("goblin", rules) with { MaxHp = 200, InitiativeBonus = 0 };
        var ally = sheet with { Name = "Ally", InitiativeBonus = 0, Abilities = [] };
        var setup = new BattleSetup(map,
            [new(caster, sheet, new PlayerControlled(new("preview")), from, []),
             new(new("hero2"), ally, new AiControlled("companion"), Place(new(3, 7)), []),
             new(new("target1"), goblin, new AiControlled("brute"), target, []),
             new(new("target2"), goblin, new AiControlled("brute"), Place(new(target.X + 1, target.Y)), []),
             new(new("target3"), goblin, new AiControlled("brute"), Place(new(target.X, target.Y + 1)), [])],
            ObjectivesDef.Default, [], Difficulty.Normal, 0, []);
        var state = Battle.Start(setup, rules, 7).State;
        var choice = Choices.For(state, caster, rules).Abilities.FirstOrDefault(a => a.Ability == abilityId);
        var aim = choice?.Targets.OrderByDescending(t => t.Affected.Count(u => state.Units[u.Unit].Sheet.Side == Side.Enemy))
            .ThenBy(t => Reach.Distance(t.Aim, target)).FirstOrDefault();
        if (aim is null) throw new InvalidOperationException($"No legal demo target for {abilityId.Value}; add a scenario fixture.");
        var result = Battle.Apply(state, new Use(caster, abilityId, aim.Target), rules);
        if (result.Rejected is { } rejected) throw new InvalidOperationException($"Demo {abilityId.Value}: {rejected.Code}");
        if (result.State.Pending is not null) throw new InvalidOperationException($"Demo {abilityId.Value} needs a reaction fixture.");
        BattleView View(BattleState s, ImmutableArray<Beat> beats) => new("fx-showcase", map.Id, s.Round, s.Order, s.Active,
            [.. s.Units.Values.Select(u => new UnitView(u.Id, u.Sheet.Name, u.Sheet.Side.ToString(), u.Sheet.Sprite, u.Pos,
                u.Hp, u.Sheet.MaxHp, u.TempHp, u.Life.ToString(), u.Countdown, [.. u.Conditions.Select(c => c.Id.Value)], null))],
            beats, null, BoardChanges.Of(s), Name: $"{ability.Name} · Level {level}");
        return new(abilityId.Value, ability.Name, choice!.Tooltip, level, View(state, []),
            View(result.State, [new Beat(sequence, caster, result.Events)]));
    }
}
