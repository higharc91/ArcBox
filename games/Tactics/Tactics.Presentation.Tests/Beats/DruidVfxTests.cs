using Tactics.Logic;
using Tactics.Presentation.Beats;
using Tactics.Presentation.Tests.Board;
namespace Tactics.Presentation.Tests.Beats;
public sealed class DruidVfxTests
{
    [Fact]
    public void All_spells_zones_companion_and_four_beast_forms_have_real_engine_fixtures()
    {
        var rules=RulesPack.Load([Path.Combine(Pack.Root,"rules")]);
        var samples=FxShowcase.ForClass("druid",rules);
        Assert.Equal(53,samples.Length);
        Assert.Equal(samples.Single(s=>s.Id=="common.attack").Before.Units,FxShowcase.Specific("druid","common.attack",rules).Before.Units);
        foreach(var feature in FxShowcase.Inventory("druid",rules).Where(f=>f.Kind!="static"))
        {
            var sample=Assert.Single(samples,s=>s.Id==feature.Id);
            var events=sample.After.Beats.Single().Events;
            if(feature.Kind=="proc") Assert.Contains(events.OfType<PassiveTriggered>(),p=>p.Passive==feature.Id);
            else Assert.Contains(events.OfType<AbilityUsed>(),a=>a.Ability.Value==feature.Id);
        }
        foreach(var form in new[]{"wolf","bear","dire_wolf","elder_bear"})
        {
            var move=samples.Single(s=>s.Id=="druid.form."+form+".move");
            Assert.Contains(move.After.Beats.Single().Events,e=>e is Moved);
            var attack=samples.Single(s=>s.Id=="druid.form."+form+".attack");
            Assert.Contains(attack.After.Beats.Single().Events,e=>e is Damaged);
            var revert=samples.Single(s=>s.Id=="druid.form."+form+".revert");
            Assert.Contains(revert.After.Beats.Single().Events,e=>e is Transformed);
            Assert.NotEqual(revert.Before.Units.Single(u=>u.Id.Value=="hero1").Sprite,revert.After.Units.Single(u=>u.Id.Value=="hero1").Sprite);
        }
        foreach(var id in new[]{"druid.cure_wounds","druid.healing_word","druid.mass_cure_wounds","grovekeeper.bloom","grovekeeper.sanctuary_grove","grovekeeper.ancient_canopy"})
            Assert.Contains(samples.Single(s=>s.Id==id).After.Beats.Single().Events.OfType<Healed>(),h=>h.Amount>0);
        foreach(var id in new[]{"druid.lesser_restoration","druid.greater_restoration"})
            Assert.Contains(samples.Single(s=>s.Id==id).After.Beats.Single().Events,e=>e is ConditionRemoved);
        Assert.Contains(samples.Single(s=>s.Id=="druid.dispel_magic").After.Beats.Single().Events,e=>e is ZoneEnded);
        Assert.Equal(2,samples.Single(s=>s.Id=="druid.conjure_animals").After.Beats.Single().Events.OfType<Spawned>().Count());
    }
    [Fact]
    public void Every_real_beat_fits_its_budget_and_every_ability_or_proc_has_its_own_resolved_effect()
    {
        var rules = RulesPack.Load([Path.Combine(Pack.Root, "rules")]);
        var fx = FxArt.Load(Pack.Root);
        var units = Tactics.Presentation.Board.UnitArt.Load(Pack.Root);
        var failures = new List<string>();
        var effects = new HashSet<string>();
        foreach (var sample in FxShowcase.ForClass("druid", rules))
        {
            var script = BeatScript.Of(sample.After.Beats.Single(), sample.Before, sample.After, fx);
            var extra = sample.Id.StartsWith("druid.form.",StringComparison.Ordinal);
            var id = sample.Id;
            var ability = rules.Abilities.GetValueOrDefault(new(id));
            var key = ability?.Presentation?.Fx ?? fx.Passive(id);
            var cap = id == "common.attack" || extra && !id.EndsWith(".revert",StringComparison.Ordinal) ? .8 : ability is null ? 1.5
                : BattleCutIn.Tier(ability) == CutInTier.Full ? 2.5
                : ability.Tier > 0 || ability.Cost.Uses is not null || ability.Cost.Resource?.Any(p => p.Value > 0) == true ? 1.5 : .8;
            if (script.Length > cap + 1e-6) failures.Add($"{sample.Id}: {script.Length:F3}s > {cap:F1}s");
            if (id == "common.attack" || extra) continue;
            Assert.NotNull(key);
            var clip = fx.Clip(key!)!; Assert.NotNull(clip);
            if (clip.Seconds > cap) failures.Add($"{id} effect: {clip.Seconds:F3}s > {cap:F1}s");
            if (!extra) Assert.True(effects.Add(key!), id);
            if (ability is null) Assert.Contains(script.Cues.OfType<BurstCue>(), c => c.Clip.Path == clip.Path);
            else
            {
                Assert.StartsWith("characters.druid_", ability.Presentation!.Anim);
                Assert.All(units.Sprite("characters.class_druid")!.Anim(ability.Presentation.Anim!).Frames, f => Assert.InRange(f,189,213));
                if (ability.Presentation.Projectile is {} shot) Assert.NotNull(fx.Shot(shot));
            }
        }
        Assert.Empty(failures);
        Assert.Equal(40,effects.Count);
        foreach(var name in new[]{"thorn_whip","shillelagh","entangle","fog_cloud","hold_person","spike_growth","barkskin"})
            Assert.Null(fx.Clip(rules.Abilities[new("druid."+name)].Presentation!.Fx!)!.Emission);
        foreach(var name in new[]{"produce_flame","moonbeam","cure_wounds","call_lightning"})
            Assert.NotNull(fx.Clip(rules.Abilities[new("druid."+name)].Presentation!.Fx!)!.Emission);
    }

    [Fact]
    public void All_renamed_effects_retain_their_existing_sounds()
    {
        var sounds = Tactics.Presentation.Audio.SoundBank.Load(Pack.Root);
        var rules = RulesPack.Load([Path.Combine(Pack.Root,"rules")]);
        using var old = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.GetFullPath(Path.Combine(Pack.Root,"../../../tools/tactics-assets/baseline/druid-vfx.json"))));
        foreach (var a in old.RootElement.EnumerateObject())
            Assert.Equal(sounds.ForEffect(a.Value.GetProperty("fx").GetString()!),sounds.ForEffect(rules.Abilities[new(a.Name)].Presentation!.Fx!));
    }
    [Fact]
    public void Four_forms_resolve_distinct_native_compass_atlases_and_all_weapon_rolls_keep_their_visual_hit()
    {
        var rules=RulesPack.Load([Path.Combine(Pack.Root,"rules")]);
        var art=Tactics.Presentation.Board.UnitArt.Load(Pack.Root);
        var fx=FxArt.Load(Pack.Root);
        var sounds=Tactics.Presentation.Audio.SoundBank.Load(Pack.Root);
        var paths=new HashSet<string>();
        foreach(var (id,key,count) in new[]{("wolf","beasts.wolf.druid",1),("bear","beasts.bear.druid",2),("dire_wolf","beasts.wolf.druid_dire",2),("elder_bear","beasts.bear.druid_elder",2)})
        {
            var sprite=art.Sprite(key)!;Assert.NotNull(sprite);Assert.True(paths.Add(sprite.Path));
            Assert.StartsWith("creatures/druid_",sprite.Path);Assert.Equal(8,sprite.Directions.Length);Assert.Equal(6,sprite.Columns);Assert.False(sprite.Unlit);
            Assert.Equal(id.Contains("bear")?"bear":"wolf",sounds.Gear(key).Family);
            Assert.Equal(id.Contains("bear")?"claws":"bite",sounds.Gear(key).Weapon);
            var sample=FxShowcase.Specific("druid","druid.form."+id+".attack",rules);
            var events=sample.After.Beats.Single().Events;
            Assert.Equal(count,events.OfType<AttackResolved>().Count());
            var script=BeatScript.Of(sample.After.Beats.Single(),sample.Before,sample.After,fx);
            Assert.Contains(script.Cues.OfType<BurstCue>(),c=>c.Clip.Path.StartsWith("fx/druid_form_"));
            Assert.All(script.Cues.OfType<LungeCue>(),c=>Assert.Equal(.35,c.Length));
        }
        Assert.True(art.Sprite("beasts.wolf.druid_dire")!.PixelScale>art.Sprite("beasts.wolf.druid")!.PixelScale);
        Assert.True(art.Sprite("beasts.bear.druid_elder")!.PixelScale>art.Sprite("beasts.bear.druid")!.PixelScale);
    }

    [Fact]
    public void Ice_storm_split_damage_keeps_separate_readable_numbers()
    {
        var rules=RulesPack.Load([Path.Combine(Pack.Root,"rules")]);var fx=FxArt.Load(Pack.Root);
        var sample=FxShowcase.Specific("druid","druid.ice_storm",rules);
        var script=BeatScript.Of(sample.After.Beats.Single(),sample.Before,sample.After,fx);
        var groups=script.Cues.OfType<PopCue>().Where(p=>p.Style==PopStyle.Damage&&p.Text.StartsWith('-')).GroupBy(p=>p.Unit).Where(g=>g.Count()>1).ToArray();
        Assert.NotEmpty(groups);
        foreach(var group in groups)Assert.Equal(group.Count(),group.Select(p=>p.Row).Distinct().Count());
    }
}
