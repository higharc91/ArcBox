using Tactics.Logic;
using Tactics.Presentation.Beats;
using Tactics.Presentation.Tests.Board;
namespace Tactics.Presentation.Tests.Beats;
public sealed class ClericVfxTests
{
    [Fact]
    public void The_complete_reel_uses_real_healing_restoration_revival_undead_and_death_ward_triggers()
    {
        var rules = RulesPack.Load([Path.Combine(Pack.Root,"rules")]);
        var samples = FxShowcase.ForClass("cleric", rules);
        Assert.Equal(44,samples.Length);
        foreach (var feature in FxShowcase.Inventory("cleric",rules).Where(f => f.Kind != "static"))
        {
            var sample = Assert.Single(samples,s => s.Id == feature.Id);
            if (feature.Kind == "proc") Assert.Contains(sample.After.Beats.Single().Events.OfType<PassiveTriggered>(),p => p.Passive == feature.Id);
            else Assert.Contains(sample.After.Beats.Single().Events.OfType<AbilityUsed>(),a => a.Ability.Value == feature.Id);
        }
        foreach (var id in new[] {"cleric.cure_wounds","cleric.healing_word","cleric.mass_healing_word","cleric.mass_cure_wounds","life.preserve_life","cleric.divine_intervention"})
        {
            var sample = samples.Single(s => s.Id == id);
            Assert.Contains(sample.After.Beats.Single().Events.OfType<Healed>(),h => h.Amount > 0);
        }
        foreach (var id in new[] {"life.word_of_renewal","life.mass_revival"})
        {
            var sample = samples.Single(s => s.Id == id);
            Assert.Contains(sample.Before.Units,u => u.Life == "Downed");
            Assert.Contains(sample.After.Beats.Single().Events,e => e is Revived);
        }
        foreach (var id in new[] {"cleric.lesser_restoration","cleric.greater_restoration","cleric.dispel_magic"})
            Assert.Contains(samples.Single(s => s.Id == id).After.Beats.Single().Events,e => e is ConditionRemoved);
        var ward = samples.Single(s => s.Id == "death_ward.save");
        Assert.Equal("Up",ward.After.Units.Single(u => u.Id.Value=="hero2").Life);
        Assert.Equal(1,ward.After.Units.Single(u => u.Id.Value=="hero2").Hp);
        Assert.DoesNotContain("death_ward",ward.After.Units.Single(u => u.Id.Value=="hero2").Conditions);
        foreach (var id in new[] {"cleric.turn_undead","cleric.sear_undead"})
        {
            var sample=samples.Single(s=>s.Id==id);
            Assert.Contains(sample.Before.Units,u=>u.Sprite?.Contains("skeleton") == true);
            Assert.Contains(sample.After.Beats.Single().Events.OfType<ConditionApplied>(),e=>e.Condition.Value=="frightened");
        }
        Assert.True(samples.Single(s=>s.Id=="cleric.toll_the_dead.wounded").Before.Units.Single(u=>u.Id.Value=="target1").Hp<200);
    }
    [Fact]
    public void Every_real_beat_fits_its_budget_and_every_ability_or_proc_has_its_own_resolved_effect()
    {
        var rules = RulesPack.Load([Path.Combine(Pack.Root, "rules")]);
        var fx = FxArt.Load(Pack.Root);
        var units = Tactics.Presentation.Board.UnitArt.Load(Pack.Root);
        var failures = new List<string>();
        var effects = new HashSet<string>();
        foreach (var sample in FxShowcase.ForClass("cleric", rules))
        {
            var script = BeatScript.Of(sample.After.Beats.Single(), sample.Before, sample.After, fx);
            var extra = sample.Id == "cleric.toll_the_dead.wounded";
            var id = extra ? "cleric.toll_the_dead" : sample.Id;
            var ability = rules.Abilities.GetValueOrDefault(new(id));
            var key = ability?.Presentation?.Fx ?? fx.Passive(id);
            var cap = id == "common.attack" ? .8 : ability is null ? 1.5
                : BattleCutIn.Tier(ability) == CutInTier.Full ? 2.5
                : ability.Tier > 0 || ability.Cost.Uses is not null || ability.Cost.Resource?.Any(p => p.Value > 0) == true ? 1.5 : .8;
            if (script.Length > cap + 1e-6) failures.Add($"{sample.Id}: {script.Length:F3}s > {cap:F1}s");
            if (id == "common.attack") continue;
            Assert.NotNull(key);
            var clip = fx.Clip(key!)!; Assert.NotNull(clip);
            if (clip.Seconds > cap) failures.Add($"{id} effect: {clip.Seconds:F3}s > {cap:F1}s");
            if (!extra) Assert.True(effects.Add(key!), id);
            if (ability is null) Assert.Contains(script.Cues.OfType<BurstCue>(), c => c.Clip.Path == clip.Path);
            else
            {
                Assert.StartsWith("characters.cleric_", ability.Presentation!.Anim);
                Assert.All(units.Sprite("characters.class_cleric")!.Anim(ability.Presentation.Anim!).Frames, f => Assert.InRange(f,189,208));
                if (ability.Presentation.Projectile is {} shot) Assert.NotNull(fx.Shot(shot));
            }
        }
        Assert.Empty(failures);
        Assert.Equal(42,effects.Count);
        foreach (var sample in FxShowcase.ForClass("cleric",rules).Where(s=>s.Id!="common.attack"))
        {
            var id = sample.Id == "cleric.toll_the_dead.wounded" ? "cleric.toll_the_dead" : sample.Id;
            var key = rules.Abilities.GetValueOrDefault(new(id))?.Presentation?.Fx ?? fx.Passive(id);
            Assert.NotNull(fx.Clip(key!)!.Emission);
        }
        Assert.Equal(Tactics.Presentation.Board.Rgb.Hex("#85d878"),fx.Clip("fx.cleric_cure_wounds.healing")!.Emission!.Colour);
        Assert.Equal(Tactics.Presentation.Board.Rgb.Hex("#ff8a32"),fx.Clip("fx.cleric_flame_strike.fire")!.Emission!.Colour);
    }

    [Fact]
    public void All_renamed_effects_retain_their_existing_sounds()
    {
        var sounds = Tactics.Presentation.Audio.SoundBank.Load(Pack.Root);
        var rules = RulesPack.Load([Path.Combine(Pack.Root,"rules")]);
        using var old = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.GetFullPath(Path.Combine(Pack.Root,"../../../tools/tactics-assets/baseline/cleric-vfx.json"))));
        foreach (var a in old.RootElement.EnumerateObject())
            Assert.Equal(sounds.ForEffect(a.Value.GetProperty("fx").GetString()!),sounds.ForEffect(rules.Abilities[new(a.Name)].Presentation!.Fx!));
    }
    [Fact]
    public void Cleric_split_damage_and_visible_procs_keep_separate_number_rows()
    {
        var rules = RulesPack.Load([Path.Combine(Pack.Root,"rules")]);
        var fx = FxArt.Load(Pack.Root);
        foreach (var id in new[] {"cleric.flame_strike","war.war_priest","cleric.divine_strike","light.burning_luminescence"})
        {
            var sample = FxShowcase.Specific("cleric",id,rules);
            var script = BeatScript.Of(sample.After.Beats.Single(),sample.Before,sample.After,fx);
            var groups = script.Cues.OfType<PopCue>().Where(p => p.Style == PopStyle.Damage && p.Text.StartsWith('-')).GroupBy(p => p.Unit).Where(g=>g.Count()>1).ToArray();
            Assert.NotEmpty(groups);
            foreach (var group in groups)
            {
                Assert.Equal(group.Count(),group.Select(p=>p.Row).Distinct().Count());
                Assert.DoesNotContain(group,p=>p.Row==1); // keep the critical label's row available
            }
        }
    }
}
