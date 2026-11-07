using System.Collections.Immutable;
using System.Text.Json;
using ArcBox.Sdk;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.Beats;

/// <summary>A deterministic showcase sample made by the real engine, consumed by the ordinary TV scene.</summary>
public sealed record FxShowcaseSample(string Id, string Name, string Description, int Level, BattleView Before, BattleView After);

public sealed record FxShowcaseFeature(string Id, string Name, int Level, string? Subclass, string Kind, string CutIn, PresentationDef? Presentation);

public static class FxShowcase
{
    /// <summary>Capture the last actual cue's complete tail, including long wards, without delaying the game's shown signal.</summary>
    public static double TailFor(FxShowcaseSample sample, FxArt art)
    {
        var script = BeatScript.Of(sample.After.Beats.Single(), sample.Before, sample.After, art);
        return Math.Max(.85, script.Cues.Select(c => c.At + c.Length).DefaultIfEmpty(0).Max() - script.Length + .1);
    }

    /// <summary>The step-0 reel covers an ordinary attack, cantrip, paid spell and signature effect.</summary>
    public static ImmutableArray<FxShowcaseSample> StepZero(string packRoot)
    {
        var rules = RulesPack.Load([Path.Combine(packRoot, "rules")]);
        return [Sample("wizard", RulesPack.CommonAttack.Id, 1, rules, 1),
            Sample("wizard", new("wizard.fire_bolt"), 1, rules, 2),
            Sample("wizard", new("wizard.magic_missile"), 1, rules, 3),
            Sample("wizard", new("wizard.fireball"), 5, rules, 4)];
    }

    /// <summary>Includes every feature choice and subclass, not just the default character's chosen loadout.</summary>
    public static ImmutableArray<FxShowcaseFeature> Inventory(string cls, RulesPack rules)
    {
        var definition = rules.Classes[cls];
        var rows = new Dictionary<string, FxShowcaseFeature>(StringComparer.Ordinal);
        Add(definition.Features, null);
        foreach (var subclass in definition.Subclasses) Add(subclass.Features, subclass.Id);
        return [.. rows.Values.OrderBy(r => r.Subclass).ThenBy(r => r.Level).ThenBy(r => r.Id, StringComparer.Ordinal)];

        void Add(ImmutableSortedDictionary<int, ImmutableArray<FeatureRef>> levels, string? subclass)
        {
            foreach (var (level, features) in levels)
                foreach (var feature in features)
                {
                    var ids = (feature.From.IsDefault ? [] : feature.From).AsEnumerable();
                    if (feature.Ability is { } ability) ids = ids.Append(ability.Value);
                    if (feature.Passive is { } passive) ids = ids.Append(passive);
                    foreach (var id in ids)
                    {
                        FxShowcaseFeature row;
                        if (rules.Abilities.TryGetValue(new(id), out var abilityDef))
                            row = new(id, abilityDef.Name, level, subclass, "ability", Tier(abilityDef), abilityDef.Presentation);
                        else if (rules.Passives.TryGetValue(id, out var passiveDef))
                            row = new(id, string.Join(' ', id.Split('.')[^1].Split('_').Select(s => char.ToUpperInvariant(s[0]) + s[1..])),
                                level, subclass, passiveDef.Hook is not null && !passiveDef.Effects.IsDefaultOrEmpty ? "proc" : "static", "none", null);
                        else throw new InvalidOperationException($"Unknown showcase feature {id}.");
                        rows.TryAdd($"{subclass}:{id}", row); // levels are ordered, so the earliest unlock wins
                    }
                }
        }
    }

    // This neutral harness must also compile on the frozen pre-cut-in main. Do not reference the new timeline types here.
    private static string Tier(AbilityDef ability)
    {
        var presentation = JsonSerializer.SerializeToElement(ability.Presentation, ArcJson.Options);
        if (presentation.ValueKind == JsonValueKind.Object && presentation.TryGetProperty("cutIn", out var authored)) return authored.GetString()!;
        if (ability.Cost.Reaction > 0) return "none";
        if (new[] { ability.Presentation?.Anim, ability.Presentation?.Fx, ability.Presentation?.Projectile }.Any(k => k?.StartsWith("signature.", StringComparison.Ordinal) == true)) return "full";
        return ability.Tier > 0 || ability.Cost.Uses is not null || ability.Cost.Resource?.Any(p => p.Value > 0) == true ? "short" : "none";
    }

    /// <summary>Fail with the complete fixture-gap list rather than silently omitting an ability or visible passive.</summary>
    public static ImmutableArray<FxShowcaseSample> ForClass(string cls, RulesPack rules)
    {
        var samples = ImmutableArray.CreateBuilder<FxShowcaseSample>();
        var gaps = new List<string>();
        samples.Add(Sample(cls, RulesPack.CommonAttack.Id, 1, rules, 1));
        foreach (var feature in Inventory(cls, rules).Where(r => r.Kind != "static"))
            try
            {
                var sample = feature.Kind == "proc"
                    ? ProcSample(cls, feature, rules, samples.Count + 1)
                    : Sample(cls, new(feature.Id), feature.Level, rules, samples.Count + 1, feature.Subclass, realistic: true);
                samples.Add(sample with { Name = feature.Name, Description = feature.Subclass is null ? sample.Description
                    : $"{rules.Classes[cls].Subclasses.First(s => s.Id == feature.Subclass).Name}: {sample.Description}" });
            }
            catch (InvalidOperationException error) { gaps.Add($"{feature.Id}: {error.Message}"); }
        if (gaps.Count != 0) throw new InvalidOperationException($"Incomplete {cls} showcase; add real trigger fixtures before capturing this class:\n{string.Join('\n', gaps)}");
        if (cls == "sorcerer")
            foreach (var id in new[] { "chaos.wild_surge", "chaos.controlled_surge" })
            {
                var feature = Inventory(cls, rules).Single(f => f.Id == id);
                var shown = samples.Single(s => s.Id == id).After.Beats.Single().Events.OfType<Damaged>().First().Type;
                foreach (var type in (id == "chaos.wild_surge" ? new[] { "force", "lightning", "psychic" } : new[] { "force", "fire" }).Where(t => t != shown))
                    samples.Add(SurgeSample(feature, type, rules, samples.Count + 1));
            }
        return samples.ToImmutable();
    }

    private static FxShowcaseSample SurgeSample(FxShowcaseFeature feature, string type, RulesPack rules, int sequence)
    {
        for (ulong seed = 7; seed < 128; seed++)
        {
            var sample = Sample("sorcerer", new(feature.Id), feature.Level, rules, sequence, feature.Subclass, realistic: true, seed: seed);
            if (sample.After.Beats.Single().Events.OfType<Damaged>().FirstOrDefault()?.Type == type)
                return sample with { Id = feature.Id + "." + type, Name = feature.Name + " (" + type + ")",
                    Description = sample.Description + $" Random outcome fixture seed {seed}." };
        }
        throw new InvalidOperationException($"No real {type} outcome found for {feature.Id}.");
    }

    public static FxShowcaseSample Specific(string cls, string id, RulesPack rules)
    {
        if (cls == "sorcerer" && id.Count(c => c == '.') == 2)
        {
            var split = id.LastIndexOf('.');
            return SurgeSample(Inventory(cls, rules).Single(f => f.Id == id[..split]), id[(split + 1)..], rules, 1);
        }
        var feature = Inventory(cls, rules).FirstOrDefault(r => r.Id == id);
        if (feature?.Kind == "static") throw new InvalidOperationException($"{id} is a static modifier, not a standalone visible proc.");
        return feature?.Kind == "proc" ? ProcSample(cls, feature, rules, 1)
            : Sample(cls, new(id), feature?.Level ?? 1, rules, 1, feature?.Subclass, realistic: true);
    }

    private static FxShowcaseSample ProcSample(string cls, FxShowcaseFeature feature, RulesPack rules, int sequence)
    {
        var passive = rules.Passives[feature.Id];
        if (passive.Hook is not (HookKind.OnAttackHit or HookKind.BeforeAttackRoll))
            throw new InvalidOperationException($"No trigger fixture yet for {passive.Hook}. It must not be skipped.");
        var trigger = passive.When?.IsSpell == true ? new AbilityId(rules.Abilities.ContainsKey(new(cls + ".fire_bolt")) ? cls + ".fire_bolt" : "wizard.fire_bolt") : RulesPack.CommonAttack.Id;
        var sample = Sample(cls, trigger, feature.Level, rules, sequence, feature.Subclass, feature.Id, realistic: true);
        if (!sample.After.Beats.SelectMany(b => b.Events).OfType<PassiveTriggered>().Any(e => e.Passive == feature.Id))
            throw new InvalidOperationException("The real engine did not trigger this passive; supply its required conditions.");
        return sample with { Id = feature.Id, Name = feature.Name, Description = $"Visible passive triggered during {sample.Name}. {sample.Description}" };
    }

    public static FxShowcaseSample Sample(string cls, AbilityId abilityId, int level, RulesPack rules, int sequence,
        string? subclass = null, string? passive = null, bool realistic = false, ulong seed = 7)
    {
        var ability = rules.Abilities[abilityId];
        if (ability.Cost.Reaction > 0) return ReactionSample(cls, ability, level, rules, sequence, subclass);
        var map = rules.Maps["crypt_hall"];
        var terrain = new Terrain(map, ImmutableSortedDictionary<Tile, int>.Empty, []);
        var occupied = new HashSet<Tile>();
        Tile Free(Tile preferred) => Enumerable.Range(0, map.Height).SelectMany(y => Enumerable.Range(0, map.Width).Select(x => new Tile(x, y)))
            .Where(t => terrain.Walkable(t) && !occupied.Contains(t))
            .OrderBy(t => Reach.Distance(t, preferred)).ThenBy(t => t).First();
        Tile Place(Tile preferred) { var tile = Free(preferred); occupied.Add(tile); return tile; }
        var caster = new UnitId("hero1");
        var sheet = SheetBuilder.ForCharacter(SheetBuilder.DefaultCharacter("Ember", "human", cls, subclass, level, rules), rules)
            with { InitiativeBonus = 100, Sprite = "characters.class_" + cls, Abilities = [abilityId] };
        if (passive is not null) sheet = sheet with { Passives = [passive] };
        var from = Place(new(3, 5));
        var melee = ability.IsWeaponAttack ? !sheet.Weapon.Ranged : ability.Target.Range == 1;
        var target = Place(melee ? new(from.X + 1, from.Y) : new(7, 5));
        var goblin = SheetBuilder.ForMonster("goblin", rules) with { MaxHp = realistic && abilityId.Value.EndsWith(".sleep", StringComparison.Ordinal) ? 5 : 200, InitiativeBonus = 0 };
        var ally = sheet with { Name = "Ally", InitiativeBonus = 0, Abilities = [] };
        var placements = ImmutableArray.CreateBuilder<UnitPlacement>();
        placements.Add(new(caster, sheet, new PlayerControlled(new("preview")), from, []));
        placements.Add(new(new("hero2"), ally, new AiControlled("companion"), Place(passive is null ? new(3, 7) : new(target.X, target.Y + 1)), []));
        placements.Add(new(new("target1"), goblin, new AiControlled("brute"), target, []));
        if (!realistic || ability.Target.Area.Shape != AreaShape.Single)
        {
            placements.Add(new(new("target2"), goblin, new AiControlled("brute"), Place(new(target.X + 1, target.Y)), []));
            placements.Add(new(new("target3"), goblin, new AiControlled("brute"), Place(new(target.X, target.Y + 1)), []));
        }
        var setup = new BattleSetup(map, placements.ToImmutable(),
            ObjectivesDef.Default, [], Difficulty.Normal, 0, []);
        var state = Battle.Start(setup, rules, seed).State;
        if (realistic)
        {
            var revive = ability.Effects.Any(e => e is ReviveEffect);
            state = state with { Units = state.Units.ToImmutableSortedDictionary(kv => kv.Key, kv =>
                kv.Value.Sheet.Side != Side.Party ? kv.Value : kv.Value with
                { Hp = revive && kv.Key != caster ? 0 : Math.Max(1, kv.Value.Sheet.MaxHp / 2),
                    Life = revive && kv.Key != caster ? LifeState.Downed : LifeState.Up,
                    Countdown = revive && kv.Key != caster ? 2 : 0 }) };
            if (ability.Effects.Any(e => e is GrantResourceEffect))
                state = state with { Units = state.Units.SetItem(caster, state.Units[caster] with
                { Resources = state.Units[caster].Resources.ToImmutableSortedDictionary(p => p.Key,
                    p => ability.Effects.OfType<GrantResourceEffect>().Any(g => g.Resource == p.Key) ? p.Value / 2 : p.Value, StringComparer.Ordinal) }) };
        }
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

    private static FxShowcaseSample ReactionSample(string cls, AbilityDef ability, int level, RulesPack rules, int sequence, string? subclass)
    {
        var spell = ability.Ai?.Tags.Contains("reactsTo:spellCast") == true;
        if (!spell && ability.Ai?.Tags.Contains("reactsTo:beforeHit") != true)
            throw new InvalidOperationException("No supported engine trigger fixture for this reaction; it must not be skipped.");
        var map = rules.Maps["crypt_hall"];
        var hero = new UnitId("hero1");
        var threat = new UnitId("threat");
        var defender = SheetBuilder.ForCharacter(SheetBuilder.DefaultCharacter("Ember", "human", cls, subclass, level, rules), rules)
            with { InitiativeBonus = 0, Sprite = "characters.class_" + cls, Abilities = [ability.Id] };
        var trigger = spell ? new AbilityId("wizard.fireball") : RulesPack.CommonAttack.Id;
        var attacker = SheetBuilder.ForCharacter(SheetBuilder.DefaultCharacter("Threat", "human", spell ? "wizard" : "fighter", null, spell ? 5 : 10, rules), rules)
            with { Side = Side.Enemy, InitiativeBonus = 100, Abilities = [trigger], Sprite = "characters.class_" + (spell ? "wizard" : "fighter") };
        var setup = new BattleSetup(map,
            [new(hero, defender, new PlayerControlled(new("preview")), new(3, 5), []),
             new(threat, attacker, new AiControlled("brute"), spell ? new(7, 5) : new(4, 5), [])], ObjectivesDef.Default, [], Difficulty.Normal, 0, []);
        // Use the first deterministic seed that produces the required real offer. Record it in the caption for reproducibility.
        for (ulong seed = 7; seed < 39; seed++)
        {
            var state = Battle.Start(setup, rules, seed).State;
            var first = Battle.Apply(state, new Use(threat, trigger, spell ? new Target(Tile: new(3, 5)) : new Target(Unit: hero)), rules);
            if (first.Rejected is not null || first.State.Pending is not ReactionOffer offer) continue;
            var option = offer.Options.FirstOrDefault(o => o.Ability == ability.Id);
            if (option is null) continue;
            var response = Battle.Apply(first.State, new React(hero, option), rules);
            if (response.Rejected is not null || response.State.Pending is not null) continue;
            BattleView View(BattleState s, ImmutableArray<Beat> beats) => new("fx-showcase", map.Id, s.Round, s.Order, s.Active,
                [.. s.Units.Values.Select(u => new UnitView(u.Id, u.Sheet.Name, u.Sheet.Side.ToString(), u.Sheet.Sprite, u.Pos,
                    u.Hp, u.Sheet.MaxHp, u.TempHp, u.Life.ToString(), u.Countdown, [.. u.Conditions.Select(c => c.Id.Value)], null))], beats, null, BoardChanges.Of(s));
            return new(ability.Id.Value, ability.Name, $"Real reaction to {rules.Abilities[trigger].Name}; fixture seed {seed}.", level,
                View(state, []), View(response.State, [new(sequence, threat, first.Events.AddRange(response.Events))]));
        }
        throw new InvalidOperationException("The real engine did not offer this reaction in its fixture.");
    }
}
