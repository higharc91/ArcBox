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
        if (cls == "warlock")
        {
            var parent = rows.Values.Single(r => r.Id == "warlock.hunger_of_hadar");
            foreach (var proc in rules.Abilities[new(parent.Id)].Effects.OfType<ZoneEffect>().SelectMany(z => z.Passives))
                rows.TryAdd(parent.Subclass + ":" + proc.Id,
                    new(proc.Id, "Hadar Turn Pulse", parent.Level, parent.Subclass, "proc", "none", null));
        }
        if (cls == "cleric")
        {
            var parent = rows.Values.Single(r => r.Id == "cleric.death_ward");
            foreach (var proc in rules.Conditions[new("death_ward")].Passives)
                rows.TryAdd(parent.Subclass + ":" + proc.Id,
                    new(proc.Id, "Death Ward Rescue", parent.Level, parent.Subclass, "proc", "none", null));
        }
        if (cls is "druid" or "paladin" or "bard" or "ranger")
            foreach (var parent in rows.Values.Where(r => r.Kind == "ability").ToArray())
                foreach (var proc in rules.Abilities[new(parent.Id)].Effects.OfType<ZoneEffect>().SelectMany(z => z.Passives))
                    rows.TryAdd(parent.Subclass + ":" + proc.Id,
                        new(proc.Id, parent.Name + " · " + proc.Hook, parent.Level, parent.Subclass, "proc", "none", null));
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
        if (cls == "warlock")
            foreach (var level in new[] { 5, 7 })
            {
                var sample = Sample(cls, new("warlock.eldritch_blast"), level, rules, samples.Count + 1, realistic: true);
                samples.Add(sample with { Id = "warlock.eldritch_blast.level" + level,
                    Name = "Eldritch Blast " + (level == 5 ? "II" : "III"),
                    Description = sample.Description + " Real higher-level beams and the character's invocations." });
            }
        if (cls == "cleric")
        {
            var sample = Sample(cls, new("cleric.toll_the_dead"), 1, rules, samples.Count + 1, realistic: true, woundedTarget: true);
            samples.Add(sample with { Id = "cleric.toll_the_dead.wounded", Name = "Toll the Dead (wounded)" });
        }
        if (cls == "druid")
            foreach (var form in new[] {"wolf", "bear", "dire_wolf", "elder_bear"})
                foreach (var action in new[] {"move", "attack", "revert"})
                    samples.Add(DruidFormSample(form, action, rules, samples.Count + 1));
        if (cls == "paladin")
        {
            foreach (var id in new[]{"paladin.smite_damage","paladin.smite_damage_2","paladin.smite_damage_3"})
            {
                var feature=Inventory(cls,rules).Single(f=>f.Id==id);
                samples.Add(ProcSample(cls,feature,rules,samples.Count+1,undeadTarget:true) with { Id=id+".undead",Name=feature.Name+" · undead" });
            }
            samples.Add(Sample(cls,new("paladin.elemental_weapon"),9,rules,samples.Count+1,realistic:true,weaponFollowThrough:true)
                with { Id="paladin.elemental_weapon.strike",Name="Elemental Weapon · actual attack" });
        }
        if(cls=="ranger")
            foreach(var action in new[]{"move","attack"})samples.Add(RangerCompanionSample(action,rules,samples.Count+1));
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
        if (id == "common.attack") return Sample(cls,RulesPack.CommonAttack.Id,1,rules,1);
        if (cls == "sorcerer" && id.Count(c => c == '.') == 2)
        {
            var split = id.LastIndexOf('.');
            return SurgeSample(Inventory(cls, rules).Single(f => f.Id == id[..split]), id[(split + 1)..], rules, 1);
        }
        if (cls == "warlock" && id.StartsWith("warlock.eldritch_blast.level", StringComparison.Ordinal))
        {
            var level = int.Parse(id["warlock.eldritch_blast.level".Length..]);
            return Sample(cls, new("warlock.eldritch_blast"), level, rules, 1, realistic: true) with
                { Id = id, Name = "Eldritch Blast " + (level == 5 ? "II" : "III") };
        }
        if (cls == "cleric" && id == "cleric.toll_the_dead.wounded")
            return Sample(cls, new("cleric.toll_the_dead"), 1, rules, 1, realistic: true, woundedTarget: true) with
                { Id = id, Name = "Toll the Dead (wounded)" };
        if (cls == "druid" && id.StartsWith("druid.form.", StringComparison.Ordinal))
        {
            var parts = id.Split('.');
            return DruidFormSample(parts[2],parts[3],rules,1);
        }
        if(cls=="paladin" && id.EndsWith(".undead",StringComparison.Ordinal))
            return ProcSample(cls,Inventory(cls,rules).Single(f=>f.Id==id[..^7]),rules,1,undeadTarget:true) with { Id=id };
        if(cls=="paladin" && id=="paladin.elemental_weapon.strike")
            return Sample(cls,new("paladin.elemental_weapon"),9,rules,1,realistic:true,weaponFollowThrough:true) with { Id=id };
        if(cls=="ranger" && id.StartsWith("ranger.companion.",StringComparison.Ordinal))return RangerCompanionSample(id.Split('.')[2],rules,1);
        var feature = Inventory(cls, rules).FirstOrDefault(r => r.Id == id);
        if (feature?.Kind == "static") throw new InvalidOperationException($"{id} is a static modifier, not a standalone visible proc.");
        return feature?.Kind == "proc" ? ProcSample(cls, feature, rules, 1)
            : Sample(cls, new(id), feature?.Level ?? 1, rules, 1, feature?.Subclass, realistic: true);
    }

    private static FxShowcaseSample ProcSample(string cls, FxShowcaseFeature feature, RulesPack rules, int sequence, bool undeadTarget=false)
    {
        if (feature.Id == "warlock.hunger_of_hadar.turn")
            return Sample(cls, new("warlock.hunger_of_hadar"), feature.Level, rules, sequence, realistic: true, zonePulse: true);
        if (feature.Id == "death_ward.save")
        {
            for (ulong seed = 7; seed < 80; seed++)
            {
                var sample = Sample(cls, new("cleric.death_ward"), feature.Level, rules, sequence, realistic: true, seed: seed, wardProc: true);
                if (sample.After.Beats.Single().Events.OfType<PassiveTriggered>().Any(p => p.Passive == feature.Id))
                    return sample with { Id = feature.Id, Name = feature.Name, Description = "Real Death Ward rescue; fixture seed " + seed + "." };
            }
            throw new InvalidOperationException("The real lethal hit did not trigger Death Ward.");
        }
        if (cls is "druid" or "paladin" or "bard" or "ranger" && !rules.Passives.ContainsKey(feature.Id))
            return Sample(cls, rules.Abilities.Single(pair=>pair.Value.Effects.OfType<ZoneEffect>().Any(z=>z.Passives.Any(p=>p.Id==feature.Id))).Key, feature.Level, rules, sequence,
                feature.Subclass, realistic: true, zoneProc: feature.Id) with { Id = feature.Id, Name = feature.Name };
        var passive = rules.Passives[feature.Id];
        if (passive.Hook is HookKind.OnBattleStart or HookKind.OnRoundStart)
            return OpeningProcSample(cls, feature, rules, sequence);
        if (cls=="paladin" && passive.Hook==HookKind.OnTurnEnd)
            return Sample(cls,RulesPack.CommonAttack.Id,feature.Level,rules,sequence,feature.Subclass,feature.Id,realistic:true,endTurnProc:true)
                with { Id=feature.Id,Name=feature.Name };
        if (passive.Hook is not (HookKind.OnAttackHit or HookKind.BeforeAttackRoll or HookKind.OnKill))
            throw new InvalidOperationException($"No trigger fixture yet for {passive.Hook}. It must not be skipped.");
        var trigger = cls == "warlock" && (passive.When?.IsSpell == true || passive.Hook == HookKind.OnKill)
            ? new AbilityId("warlock.eldritch_blast")
            : cls == "cleric" && passive.When?.IsSpell == true ? new AbilityId("cleric.guiding_bolt")
            : passive.When?.IsSpell == true ? new AbilityId(rules.Abilities.ContainsKey(new(cls + ".fire_bolt")) ? cls + ".fire_bolt" : "wizard.fire_bolt") : RulesPack.CommonAttack.Id;
        var subclass = cls == "warlock" ? feature.Subclass ?? "warlock.void" : feature.Subclass;
        for (ulong seed = 7; seed < 39; seed++)
        {
            var sample = Sample(cls, trigger, feature.Level, rules, sequence, subclass, feature.Id, realistic: true, seed: seed,undeadTarget:undeadTarget);
            if (sample.After.Beats.SelectMany(b => b.Events).OfType<PassiveTriggered>().Any(e => e.Passive == feature.Id))
                return sample with { Id = feature.Id, Name = feature.Name,
                    Description = $"Visible passive triggered during {sample.Name}; fixture seed {seed}. {sample.Description}" };
        }
        throw new InvalidOperationException("The real engine did not trigger this passive; supply its required conditions.");
    }

    private static FxShowcaseSample OpeningProcSample(string cls, FxShowcaseFeature feature, RulesPack rules, int sequence)
    {
        var map = rules.Maps["crypt_hall"]; var hero = new UnitId("hero1");
        var sheet = SheetBuilder.ForCharacter(SheetBuilder.DefaultCharacter("Ember", "human", cls, feature.Subclass, feature.Level, rules), rules)
            with { InitiativeBonus = 100, Sprite = "characters.class_" + cls, Passives = [feature.Id], Abilities = [] };
        var goblin = SheetBuilder.ForMonster("goblin", rules) with { InitiativeBonus = 0 };
        var placements=ImmutableArray.CreateBuilder<UnitPlacement>();
        placements.Add(new(hero,sheet,new PlayerControlled(new("preview")),new(3,5),[]));
        if(cls=="paladin")placements.Add(new(new("hero2"),sheet with { Name="Ally",InitiativeBonus=0,Passives=[] },new AiControlled("companion"),new(3,6),[]));
        placements.Add(new(new("target1"),goblin,new AiControlled("brute"),new(7,5),[]));
        var setup=new BattleSetup(map,placements.ToImmutable(),ObjectivesDef.Default,[],Difficulty.Normal,0,[]);
        var result = Battle.Start(setup, rules, 7);
        var state = result.State;
        BattleView before;
        if (rules.Passives[feature.Id].Hook == HookKind.OnBattleStart)
        {
            before = ShowcaseView(state, map, [], hideSummons: true);
            if(cls=="paladin")before=before with { Units=[.. before.Units.Select(u=>u with { Conditions=[] })] };
        }
        else
        {
            var resource=cls=="bard"?"inspiration":"pact";
            state = state with { Units = state.Units.SetItem(hero, state.Units[hero] with
                { Resources = state.Units[hero].Resources.SetItem(resource, Math.Max(0, state.Units[hero].Resources[resource] - 1)) }) };
            before = ShowcaseView(state, map, []);
            for (var step = 0; step < 12; step++)
            {
                before = ShowcaseView(state, map, []);
                result = Battle.Apply(state, new EndTurn(state.Active!.Value), rules);
                if (result.Rejected is not null) throw new InvalidOperationException("Round fixture could not end its turn.");
                state = result.State;
                if (result.Events.OfType<PassiveTriggered>().Any(p => p.Passive == feature.Id)) break;
            }
        }
        if (!result.Events.OfType<PassiveTriggered>().Any(p => p.Passive == feature.Id))
            throw new InvalidOperationException("The real opening/round hook did not fire.");
        return new(feature.Id, feature.Name, "Real " + rules.Passives[feature.Id].Hook + " hook; fixture seed 7.", feature.Level,
            before, ShowcaseView(result.State, map, [new(sequence, hero, result.Events)]));
    }

    private static BattleView ShowcaseView(BattleState s, BattleMap map, ImmutableArray<Beat> beats, bool hideSummons = false)
    {
        var units = s.Units.Values.Where(u => !hideSummons || u.SummonedBy is null).ToArray();
        return new("fx-showcase", map.Id, s.Round, [.. s.Order.Where(id => units.Any(u => u.Id == id))], s.Active,
            [.. units.Select(u => new UnitView(u.Id, u.Sheet.Name, u.Sheet.Side.ToString(), u.Sheet.Sprite, u.Pos,
                u.Hp, u.Sheet.MaxHp, u.TempHp, u.Life.ToString(), u.Countdown, [.. u.Conditions.Select(c => c.Id.Value)], null))],
            beats, null, BoardChanges.Of(s));
    }

    public static FxShowcaseSample Sample(string cls, AbilityId abilityId, int level, RulesPack rules, int sequence,
        string? subclass = null, string? passive = null, bool realistic = false, ulong seed = 7, bool zonePulse = false, bool woundedTarget = false, bool wardProc = false, string? zoneProc = null, string? formAction = null, bool undeadTarget=false, bool endTurnProc=false, bool weaponFollowThrough=false)
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
        var paladinPreparation=passive switch { "paladin.smite_damage"=>"paladin.divine_smite","paladin.smite_damage_2"=>"paladin.divine_smite_2",
            "paladin.smite_damage_3"=>"paladin.divine_smite_3","avenger.vengeful_strikes"=>"avenger.vow_of_enmity",_=>null };
        var needsHex = abilityId.Value == "warlock.maddening_hex" || passive is not null && rules.Passives[passive].When?.TargetHasCondition?.Value == "hex";
        var sheet = SheetBuilder.ForCharacter(SheetBuilder.DefaultCharacter("Ember", "human", cls, subclass, level, rules), rules)
            with { InitiativeBonus = 100, Sprite = "characters.class_" + cls, Abilities = [abilityId] };
        if (passive is not null) sheet = sheet with { Passives = [passive] };
        if (needsHex) sheet = sheet with { Abilities = [abilityId, new("warlock.hex")] };
        var rangerPreparation=passive switch { "ranger.hunters_mark_bonus"=>"ranger.hunters_mark", "beastmaster.ferocious_bond"=>"beastmaster.sic",_=>null };
        if(rangerPreparation is not null)sheet=sheet with { Abilities=[abilityId,new(rangerPreparation)] };
        if(paladinPreparation is not null)sheet=sheet with { Abilities=[abilityId,new(paladinPreparation)] };
        var friendly = (cls is "cleric" or "druid" or "paladin" or "bard" or "ranger") && (ability.Target.Kind is TargetKind.Ally or TargetKind.DownedAlly
            || abilityId.Value is "cleric.dispel_magic" or "paladin.dispel_magic" || cls is "druid" or "paladin" or "bard" or "ranger" && ability.Ai?.Tags.Any(t => t is "heal" or "cleanse") == true || zoneProc=="crusader.consecrated.tick");
        var from = Place(new(3, 5));
        var selfArea = ability.Target.Kind == TargetKind.Self && ability.Target.Area.Shape == AreaShape.Burst;
        var melee = weaponFollowThrough || formAction is "attack" or "revert" || !friendly && (selfArea || (ability.IsWeaponAttack ? !sheet.Weapon.Ranged : ability.Target.Range == 1));
        var distance = ability.IsWeaponAttack || ability.Target.Range == 0 ? 4 : Math.Min(4, ability.Target.Range);
        var target = Place(wardProc ? new(from.X + 2, from.Y) : friendly ? new(from.X + 4, from.Y) : melee ? new(from.X + 1, from.Y) : new(from.X + distance, from.Y));
        var enemyKind = undeadTarget || abilityId.Value is "cleric.turn_undead" or "cleric.sear_undead" ? "skeleton" : "goblin";
        var goblin = SheetBuilder.ForMonster(enemyKind, rules) with { MaxHp = realistic && abilityId.Value.EndsWith(".sleep", StringComparison.Ordinal) ? 5 : 200, InitiativeBonus = 0 };
        var ally = sheet with { Name = "Ally", InitiativeBonus = 0, Abilities = [] };
        var placements = ImmutableArray.CreateBuilder<UnitPlacement>();
        placements.Add(new(caster, sheet, new PlayerControlled(new("preview")), from, []));
        placements.Add(new(new("hero2"), ally, new AiControlled("companion"), Place(friendly ? new(from.X + 1, from.Y) : passive is null ? new(3, 7) : new(target.X, target.Y + 1)), []));
        if (friendly && ability.Target.Area.Shape != AreaShape.Single)
            placements.Add(new(new("hero3"), ally with { Name = "Second Ally" }, new AiControlled("companion"), Place(new(from.X, from.Y - 1)), []));
        placements.Add(new(new("target1"), goblin, new AiControlled("brute"), target, []));
        if (!realistic || ability.Target.Area.Shape != AreaShape.Single || abilityId.Value == "stormcaller.chain_tempest" || passive is not null && rules.Passives[passive].Hook == HookKind.OnKill)
        {
            placements.Add(new(new("target2"), goblin, new AiControlled("brute"), Place(selfArea ? new(from.X, from.Y + 1) : new(target.X + 1, target.Y)), []));
            placements.Add(new(new("target3"), goblin, new AiControlled("brute"), Place(selfArea ? new(from.X - 1, from.Y) : new(target.X, target.Y + 1)), []));
        }
        var setup = new BattleSetup(map, placements.ToImmutable(),
            ObjectivesDef.Default, [], Difficulty.Normal, 0, []);
        var state = Battle.Start(setup, rules, seed).State;
        if (realistic)
        {
            var revive = ability.Effects.Any(e => e is ReviveEffect) || ability.Target.Kind == TargetKind.DownedAlly;
            state = state with { Units = state.Units.ToImmutableSortedDictionary(kv => kv.Key, kv =>
                kv.Value.Sheet.Side != Side.Party ? kv.Value : kv.Value with
                { Hp = revive && kv.Key != caster ? 0 : Math.Max(1, kv.Value.Sheet.MaxHp / (cls is "cleric" or "druid" or "paladin" or "bard" or "ranger" ? 3 : 2)),
                    Life = revive && kv.Key != caster ? LifeState.Downed : LifeState.Up,
                    Countdown = revive && kv.Key != caster ? 2 : 0 }) };
            if (ability.Effects.Any(e => e is GrantResourceEffect))
                state = state with { Units = state.Units.SetItem(caster, state.Units[caster] with
                { Resources = state.Units[caster].Resources.ToImmutableSortedDictionary(p => p.Key,
                    p => ability.Effects.OfType<GrantResourceEffect>().Any(g => g.Resource == p.Key) ? p.Value / 2 : p.Value, StringComparer.Ordinal) }) };
        }
        if (passive is not null && rules.Passives[passive].Hook == HookKind.OnKill)
            state = state with { Units = state.Units.SetItem(new("target1"), state.Units[new("target1")] with { Hp = 1 }) };
        if (needsHex)
        {
            var preparation = Battle.Apply(state, new Use(caster, new("warlock.hex"), new Target(Unit: new("target1"))), rules);
            if (preparation.Rejected is not null || preparation.State.Pending is not null) throw new InvalidOperationException("Hex preparation failed.");
            state = preparation.State;
            if (ability.Cost.Bonus > 0)
                do { state = Battle.Apply(state, new EndTurn(state.Active!.Value), rules).State; } while (state.Active != caster);
        }
        if(paladinPreparation is not null)
        {
            var prepared=Battle.Apply(state,new Use(caster,new(paladinPreparation),new Target(Unit:new("target1"))),rules);
            if(prepared.Rejected is not null || prepared.State.Pending is not null)throw new InvalidOperationException("Paladin rider preparation failed.");
            state=prepared.State;
        }
        if(passive=="sharpshooter.high_ground_mastery")
            state=state with { HeightDelta=state.HeightDelta.SetItem(from,terrain.HeightAt(target)+1-terrain.HeightAt(from)) };
        if(rangerPreparation is not null)
        {
            var prepared=Battle.Apply(state,new Use(caster,new(rangerPreparation),new Target(Unit:new("target1"))),rules);
            if(prepared.Rejected is not null || prepared.State.Pending is not null)throw new InvalidOperationException("Ranger mark preparation failed.");
            state=prepared.State;
        }
        if (woundedTarget)
            state = state with { Units = state.Units.SetItem(new("target1"), state.Units[new("target1")] with { Hp = 100 }) };
        if (wardProc)
            state = state with { Units = state.Units.SetItem(new("hero2"), state.Units[new("hero2")] with { Hp = 1 }) };
        var conditions = abilityId.Value switch
        {
            "cleric.lesser_restoration" or "druid.lesser_restoration" or "paladin.lesser_restoration" or "bard.lesser_restoration" or "ranger.lesser_restoration" => new[] {"poisoned", "blinded", "paralysed"},
            "cleric.greater_restoration" or "druid.greater_restoration" or "cleric.divine_intervention" or "bard.greater_restoration" => new[] {"poisoned", "charmed", "frightened"},
            "bard.countercharm" => new[]{"charmed","frightened"},
            "bard.heroism" => new[]{"frightened"},
            _ => Array.Empty<string>()
        };
        if (conditions.Length > 0)
            state = state with { Units = state.Units.ToImmutableSortedDictionary(kv => kv.Key, kv =>
                kv.Key.Value.StartsWith("hero", StringComparison.Ordinal) && kv.Key != caster ? kv.Value with
                { Conditions = [.. conditions.Select(c => new ActiveCondition(new(c), new("target1"), null, false, 12, Stat.Wis, false))] } : kv.Value) };
        if (abilityId.Value is "cleric.dispel_magic" or "paladin.dispel_magic")
        {
            var helper = new UnitId("hero2");
            state = state with { Units = state.Units.SetItem(helper, state.Units[helper] with
                { Sheet = state.Units[helper].Sheet with { Abilities = [new(cls+".bless")] },
                    Conditions = [new ActiveCondition(new("charmed"), new("target1"), null, false, 12, Stat.Wis, false)] }) };
            for (var step = 0; step < 12 && state.Active != helper; step++)
                state = Battle.Apply(state, new EndTurn(state.Active!.Value), rules).State;
            var preparation = Battle.Apply(state, new Use(helper, new(cls+".bless"), new Target(Unit: helper)), rules);
            if (preparation.Rejected is not null) throw new InvalidOperationException("Dispel fixture could not cast its real Bless.");
            state = preparation.State;
            for (var step = 0; step < 12; step++)
            {
                state = Battle.Apply(state, new EndTurn(state.Active!.Value), rules).State;
                if (state.Active == caster) break;
            }
        }
        if (abilityId.Value is "druid.dispel_magic" or "bard.dispel_magic")
        {
            var zoneSpell=cls=="bard"?"bard.silence":"druid.entangle";
            state = state with { Units = state.Units.SetItem(caster, state.Units[caster] with
                { Sheet = state.Units[caster].Sheet with { Abilities = [abilityId,new(zoneSpell)] } }) };
            var prepared = Battle.Apply(state,new Use(caster,new(zoneSpell),new Target(Tile: target)),rules);
            if (prepared.Rejected is not null) throw new InvalidOperationException("Could not prepare the real Entangle zone.");
            state = prepared.State;
            do { state = Battle.Apply(state,new EndTurn(state.Active!.Value),rules).State; } while (state.Active != caster);
        }
        var choice = Choices.For(state, caster, rules).Abilities.FirstOrDefault(a => a.Ability == abilityId);
        var aim = choice?.Targets.OrderByDescending(t => t.Affected.Count(u => state.Units[u.Unit].Sheet.Side == (friendly ? Side.Party : Side.Enemy)))
            .ThenByDescending(t => friendly && t.Affected.Any(u => u.Unit.Value == "hero2"))
            .ThenBy(t => Reach.Distance(t.Aim, target)).FirstOrDefault();
        if (aim is null) throw new InvalidOperationException($"No legal demo target for {abilityId.Value}; add a scenario fixture.");
        var result = Battle.Apply(state, endTurnProc ? new EndTurn(caster) : new Use(caster, abilityId, aim.Target), rules);
        if (result.Rejected is { } rejected) throw new InvalidOperationException($"Demo {abilityId.Value}: {rejected.Code}");
        if (result.State.Pending is not null) throw new InvalidOperationException($"Demo {abilityId.Value} needs a reaction fixture.");
        BattleView View(BattleState s, ImmutableArray<Beat> beats) => new("fx-showcase", map.Id, s.Round, s.Order, s.Active,
            [.. s.Units.Values.Select(u => new UnitView(u.Id, u.Sheet.Name, u.Sheet.Side.ToString(), u.Sheet.Sprite, u.Pos,
                u.Hp, u.Sheet.MaxHp, u.TempHp, u.Life.ToString(), u.Countdown, [.. u.Conditions.Select(c => c.Id.Value)], null))],
            beats, null, BoardChanges.Of(s), Name: $"{ability.Name} · Level {level}");
        if (zoneProc is not null)
        {
            state = result.State;
            var enter = zoneProc.EndsWith(".step",StringComparison.Ordinal) || zoneProc.EndsWith(".enter",StringComparison.Ordinal) || zoneProc=="spike_growth.thorns";
            var victim = new UnitId(friendly ? "hero2" : "target1");
            for (var step = 0; step < 24; step++)
            {
                var old = state;
                if (enter && state.Active == victim)
                {
                    var tiles = state.Zones.SelectMany(z=>z.Tiles).ToHashSet();
                    var move = Choices.For(state,victim,rules).Moves.Where(m=>m.Path.Length==2 && tiles.Contains(m.To)).FirstOrDefault()
                        ?? throw new InvalidOperationException("Zone entry fixture needs a legal step inside the real zone.");
                    result = Battle.Apply(state,new Move(victim,move.Path),rules);
                }
                else result = Battle.Apply(state,new EndTurn(state.Active!.Value),rules);
                if (result.Rejected is not null || result.State.Pending is not null) throw new InvalidOperationException("Real zone trigger command failed.");
                state = result.State;
                if (result.Events.OfType<PassiveTriggered>().Any(p=>p.Passive==zoneProc) && (zoneProc!="crusader.consecrated.tick" || result.Events.OfType<Healed>().Any(h=>h.Amount>0)))
                    return new(zoneProc,zoneProc,"Real engine zone " + (enter ? "entry" : "turn-start") + " hook.",level,
                        View(old,[]),View(state,[new(sequence,old.Active,result.Events)]));
            }
            throw new InvalidOperationException("The real zone hook did not fire: " + zoneProc);
        }
        if(weaponFollowThrough)
        {
            state=result.State;
            do { state=Battle.Apply(state,new EndTurn(state.Active!.Value),rules).State; } while(state.Active!=caster);
            var old=state;result=Battle.Apply(state,new Use(caster,RulesPack.CommonAttack.Id,new Target(Unit:new("target1"))),rules);
            if(result.Rejected is not null || result.State.Pending is not null)throw new InvalidOperationException("Enchanted weapon follow-through failed.");
            return new("paladin.elemental_weapon.strike","Elemental Weapon · actual attack","Real attack while the actual weapon enchantment remains active.",level,View(old,[]),View(result.State,[new(sequence,caster,result.Events)]));
        }
        if (formAction is not null)
        {
            state = result.State;
            state = state with { Units = state.Units.SetItem(caster,state.Units[caster] with { Hp = state.Units[caster].Sheet.MaxHp,
                TempHp = formAction == "revert" ? 1 : state.Units[caster].TempHp }) };
            var actor = formAction == "revert" ? new UnitId("target1") : caster;
            do { state = Battle.Apply(state,new EndTurn(state.Active!.Value),rules).State; } while (state.Active != actor);
            var old = state;
            Command command = formAction == "move"
                ? new Move(caster,Choices.For(state,caster,rules).Moves.First(m=>m.Path.Length==5 && m.Path.All(t=>Reach.Distance(t,target)>1)).Path)
                : new Use(actor,RulesPack.CommonAttack.Id,new Target(Unit: formAction == "revert" ? caster : new("target1")));
            result = Battle.Apply(state,command,rules);
            if (result.Rejected is not null || result.State.Pending is not null) throw new InvalidOperationException("Form follow-through was not legal.");
            return new("form",formAction,"Actual transformed " + formAction + ".",level,View(old,[]),View(result.State,[new(sequence,actor,result.Events)]));
        }
        if (wardProc)
        {
            state = result.State;
            for (var step = 0; step < 12 && state.Active?.Value != "target1"; step++)
                state = Battle.Apply(state, new EndTurn(state.Active!.Value), rules).State;
            var old = state;
            result = Battle.Apply(state, new Use(new("target1"), RulesPack.CommonAttack.Id, new Target(Unit: new("hero2"))), rules);
            if (result.Rejected is not null || result.State.Pending is not null) throw new InvalidOperationException("Death Ward fixture attack was not legal.");
            return new("death_ward.save", "Death Ward Rescue", "Real protected ally survives a lethal hit.", level,
                View(old, []), View(result.State, [new Beat(sequence, new("target1"), result.Events)]));
        }
        if (zonePulse)
        {
            state = result.State;
            for (var turn = 0; turn < 16; turn++)
            {
                var old = state;
                result = Battle.Apply(state, new EndTurn(state.Active!.Value), rules);
                if (result.Rejected is not null) throw new InvalidOperationException("The zone pulse could not advance its real turn.");
                state = result.State;
                if (result.Events.OfType<PassiveTriggered>().Any(p => p.Passive == "warlock.hunger_of_hadar.turn"))
                    return new("warlock.hunger_of_hadar.turn", "Hadar Turn Pulse", "Real cold/acid zone pulse on turn start.", level,
                        View(old, []), View(state, [new Beat(sequence, old.Active, result.Events)]));
            }
            throw new InvalidOperationException("The real Hunger of Hadar zone did not pulse.");
        }
        return new(abilityId.Value, ability.Name, choice!.Tooltip, level, View(state, []),
            View(result.State, [new Beat(sequence, caster, result.Events)]));
    }

    private static FxShowcaseSample RangerCompanionSample(string action,RulesPack rules,int sequence)
    {
        var map=rules.Maps["crypt_hall"];var hero=new UnitId("hero1");var foe=new UnitId("target1");
        for(ulong seed=7;seed<40;seed++)
        {
            var ranger=SheetBuilder.ForCharacter(SheetBuilder.DefaultCharacter("Ember","human","ranger","ranger.beastmaster",3,rules),rules)
                with { InitiativeBonus=100,Sprite="characters.class_ranger",Passives=["beastmaster.companion"],Abilities=[] };
            var goblin=SheetBuilder.ForMonster("goblin",rules) with { InitiativeBonus=0,MaxHp=200 };
            var setup=new BattleSetup(map,[new(hero,ranger,new PlayerControlled(new("preview")),new(3,5),[]),new(foe,goblin,new AiControlled("brute"),new(7,5),[])],ObjectivesDef.Default,[],Difficulty.Normal,0,[]);
            var state=Battle.Start(setup,rules,seed).State;var hound=state.Units.Values.Single(u=>u.SummonedBy==hero).Id;
            for(var step=0;step<12 && state.Active!=hound;step++)state=Battle.Apply(state,new EndTurn(state.Active!.Value),rules).State;
            if(state.Active!=hound)throw new InvalidOperationException("Real companion did not receive its turn.");
            var move=Choices.For(state,hound,rules).Moves.Where(m=>action=="attack"?Reach.Distance(m.To,state.Units[foe].Pos)==1:m.Path.Length==4)
                .OrderBy(m=>Reach.Distance(m.To,state.Units[foe].Pos)).ThenBy(m=>m.Path.Length).First();
            var before=state;var result=Battle.Apply(state,new Move(hound,move.Path),rules);
            if(result.Rejected is not null || result.State.Pending is not null)throw new InvalidOperationException("Companion's real move was rejected.");
            if(action=="attack")
            {
                before=result.State;result=Battle.Apply(before,new Use(hound,RulesPack.CommonAttack.Id,new Target(Unit:foe)),rules);
                if(result.Rejected is not null || result.State.Pending is not null)throw new InvalidOperationException("Companion's real attack was rejected.");
                if(!result.Events.OfType<Damaged>().Any())continue;
            }
            return new("ranger.companion."+action,"Hound companion · "+action,"Actual summoned hound's own turn and "+action+"; fixture seed "+seed+".",3,
                ShowcaseView(before,map,[]),ShowcaseView(result.State,map,[new(sequence,hound,result.Events)]));
        }
        throw new InvalidOperationException("Could not obtain a real companion "+action+".");
    }

    private static FxShowcaseSample DruidFormSample(string form, string action, RulesPack rules, int sequence)
    {
        var ability = form == "elder_bear" ? "shapeshifter.elder_bear_form" : "druid.wild_shape_" + form;
        var level = form == "wolf" ? 2 : form == "bear" ? 4 : 6;
        for (ulong seed = 7; seed < 40; seed++)
        {
            var sample = Sample("druid",new(ability),level,rules,sequence,form=="elder_bear"?"druid.shapeshifter":null,
                realistic:true,seed:seed,formAction:action);
            if (action == "revert" && !sample.After.Beats.Single().Events.OfType<Transformed>().Any()) continue;
            if (action == "attack" && !sample.After.Beats.Single().Events.OfType<Damaged>().Any()) continue;
            if (action == "move" && sample.Before.Units.Single(u=>u.Id.Value=="hero1").Sprite != sample.After.Units.Single(u=>u.Id.Value=="hero1").Sprite) continue;
            return sample with { Id = "druid.form." + form + "." + action, Name = form.Replace('_',' ') + " · " + action,
                Description = sample.Description + " Fixture seed " + seed + "." };
        }
        throw new InvalidOperationException("Could not obtain the real " + form + " " + action);
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
        var attacker = SheetBuilder.ForCharacter(SheetBuilder.DefaultCharacter("Threat", "human", spell ? "wizard" : "fighter", null, spell ? 5 : cls is "warlock" or "cleric" or "paladin" or "bard" ? 3 : 10, rules), rules)
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
