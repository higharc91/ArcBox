using Tactics.Logic;
using Tactics.Presentation.Beats;
using Tactics.Presentation.Tests.Board;
namespace Tactics.Presentation.Tests.Beats;
public sealed class PaladinVfxTests
{
 [Fact]
 public void Every_ability_and_visible_proc_uses_real_combat_including_smite_undead_and_enchanted_hits()
 {
  var rules=RulesPack.Load([Path.Combine(Pack.Root,"rules")]);var samples=FxShowcase.ForClass("paladin",rules);
  Assert.Equal(37,samples.Length);
  foreach(var f in FxShowcase.Inventory("paladin",rules).Where(f=>f.Kind!="static"))
  {
   var s=Assert.Single(samples,s=>s.Id==f.Id);var events=s.After.Beats.Single().Events;
   if(f.Kind=="proc")Assert.Contains(events.OfType<PassiveTriggered>(),e=>e.Passive==f.Id);
   else Assert.Contains(events.OfType<AbilityUsed>(),e=>e.Ability.Value==f.Id);
  }
  foreach(var id in new[]{"paladin.smite_damage","paladin.smite_damage_2","paladin.smite_damage_3"})
  {
   var hit=samples.Single(s=>s.Id==id+".undead");var events=hit.After.Beats.Single().Events;
   Assert.Contains(events.OfType<PassiveTriggered>(),p=>p.Passive==id);
   Assert.True(events.OfType<Damaged>().Count(d=>d.Type=="radiant")>=2);
   Assert.Contains(events,e=>e is ConditionRemoved);
  }
  foreach(var id in new[]{"paladin.lay_on_hands","paladin.cure_wounds","crusader.consecrated.tick"})
   Assert.Contains(samples.Single(s=>s.Id==id).After.Beats.Single().Events.OfType<Healed>(),h=>h.Amount>0);
  Assert.Contains(samples.Single(s=>s.Id=="paladin.lesser_restoration").After.Beats.Single().Events,e=>e is ConditionRemoved);
  Assert.Contains(samples.Single(s=>s.Id=="paladin.dispel_magic").After.Beats.Single().Events,e=>e is ConcentrationBroken);
  var enchanted=samples.Single(s=>s.Id=="paladin.elemental_weapon.strike");Assert.Contains("elemental_weapon",enchanted.Before.Units.Single(u=>u.Id.Value=="hero1").Conditions);
  Assert.Contains(enchanted.After.Beats.Single().Events,e=>e is Damaged);
  foreach(var id in new[]{"paladin.aura_of_protection_start","paladin.aura_of_protection"})
   Assert.Contains(samples.Single(s=>s.Id==id).After.Beats.Single().Events.OfType<ConditionApplied>(),e=>e.Condition.Value=="aura_of_protection");
 }
 [Fact]
 public void Every_real_paladin_script_fits_its_budget_and_resolves_unique_effects_and_light()
 {
  var rules=RulesPack.Load([Path.Combine(Pack.Root,"rules")]);var fx=FxArt.Load(Pack.Root);var art=Tactics.Presentation.Board.UnitArt.Load(Pack.Root);var failures=new List<string>();var keys=new HashSet<string>();
  foreach(var sample in FxShowcase.ForClass("paladin",rules))
  {
   var id=sample.Id;var a=rules.Abilities.GetValueOrDefault(new(id));var script=BeatScript.Of(sample.After.Beats.Single(),sample.Before,sample.After,fx);
   var weapon=id=="common.attack"||id.StartsWith("paladin.smite_damage",StringComparison.Ordinal)||id is "crusader.radiant_blades" or "avenger.vengeful_strikes" or "paladin.elemental_weapon.strike";
   var cap=weapon ? .8 : a is not null&&BattleCutIn.Tier(a)==CutInTier.Full?2.5:1.5;
   if(script.Length>cap+1e-6)failures.Add($"{id}: {script.Length:F3}s > {cap:F1}s");
   if(id=="paladin.elemental_weapon.strike")Assert.Contains(script.Cues.OfType<BurstCue>(),b=>b.Clip.Path==fx.Clip("fx.paladin_elemental_strike.fire")!.Path);
   if(id=="common.attack"||id.EndsWith(".undead",StringComparison.Ordinal)||id.EndsWith(".strike",StringComparison.Ordinal))continue;
   var key=a?.Presentation?.Fx??fx.Passive(id);Assert.NotNull(key);Assert.True(keys.Add(key!),id);var clip=fx.Clip(key!)!;Assert.NotNull(clip);Assert.NotNull(clip.Emission);Assert.InRange(clip.Seconds,0,cap);
   if(a is not null)Assert.All(art.Sprite("characters.class_paladin")!.Anim(a.Presentation!.Anim!).Frames,f=>Assert.InRange(f,189,213));
  }
  Assert.Equal(32,keys.Count);Assert.True(failures.Count==0,string.Join("\n",failures));
 }
 [Fact]
 public void Smites_keep_separate_damage_numbers_and_place_the_real_rider_effect_on_the_victim()
 {
  var rules=RulesPack.Load([Path.Combine(Pack.Root,"rules")]);var fx=FxArt.Load(Pack.Root);
  foreach(var id in new[]{"paladin.smite_damage","paladin.smite_damage_2","paladin.smite_damage_3"})
  {
   var s=FxShowcase.Specific("paladin",id+".undead",rules);var script=BeatScript.Of(s.After.Beats.Single(),s.Before,s.After,fx);var target=s.Before.Units.Single(u=>u.Id.Value=="target1");
   var damage=script.Cues.OfType<PopCue>().Where(c=>c.Unit==target.Id&&c.Style==PopStyle.Damage).ToArray();Assert.True(damage.Length>=3);Assert.Equal(damage.Length,damage.Select(c=>c.Row).Distinct().Count());
   var path=fx.Clip(fx.Passive(id)!)!.Path;Assert.Contains(script.Cues.OfType<BurstCue>(),b=>b.Clip.Path==path&&b.Tile==target.At);
  }
 }
 [Fact]
 public void All_paladin_spells_keep_their_existing_sound()
 {
  var sounds=Tactics.Presentation.Audio.SoundBank.Load(Pack.Root);var rules=RulesPack.Load([Path.Combine(Pack.Root,"rules")]);
  using var old=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.GetFullPath(Path.Combine(Pack.Root,"../../../tools/tactics-assets/baseline/paladin-vfx.json"))));
  foreach(var a in old.RootElement.EnumerateObject())Assert.Equal(sounds.ForEffect(a.Value.GetProperty("fx").GetString()!),sounds.ForEffect(rules.Abilities[new(a.Name)].Presentation!.Fx!));
 }
}
