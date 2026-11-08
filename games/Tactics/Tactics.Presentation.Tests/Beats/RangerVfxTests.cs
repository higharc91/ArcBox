using Tactics.Logic;
using Tactics.Presentation.Beats;
using Tactics.Presentation.Tests.Board;
namespace Tactics.Presentation.Tests.Beats;
public sealed class RangerVfxTests
{
 [Fact]
 public void Every_ranger_feature_and_the_real_hound_have_complete_engine_fixtures()
 {
  var rules=RulesPack.Load([Path.Combine(Pack.Root,"rules")]);var samples=FxShowcase.ForClass("ranger",rules);Assert.Equal(29,samples.Length);
  foreach(var f in FxShowcase.Inventory("ranger",rules).Where(f=>f.Kind!="static"))
  {
   var s=Assert.Single(samples,x=>x.Id==f.Id);var events=s.After.Beats.Single().Events;
   if(f.Kind=="proc")Assert.Contains(events.OfType<PassiveTriggered>(),e=>e.Passive==f.Id);else Assert.Contains(events.OfType<AbilityUsed>(),e=>e.Ability.Value==f.Id);
  }
  Assert.Contains(samples.Single(s=>s.Id=="ranger.hunters_mark_bonus").Before.Units,u=>u.Conditions.Contains("hunters_mark"));
  Assert.Contains(samples.Single(s=>s.Id=="beastmaster.ferocious_bond").Before.Units,u=>u.Conditions.Contains("marked"));
  Assert.Equal(2,samples.Single(s=>s.Id=="beastmaster.ferocious_bond").After.Beats.Single().Events.OfType<AttackResolved>().Count());
  Assert.Contains(samples.Single(s=>s.Id=="ranger.cure_wounds").After.Beats.Single().Events.OfType<Healed>(),e=>e.Amount>0);
  Assert.Contains(samples.Single(s=>s.Id=="ranger.lesser_restoration").After.Beats.Single().Events,e=>e is ConditionRemoved);
  Assert.Contains(samples.Single(s=>s.Id=="beastmaster.companion").After.Beats.Single().Events,e=>e is Spawned);
  Assert.Contains(samples.Single(s=>s.Id=="ranger.companion.move").After.Beats.Single().Events,e=>e is Moved);
  Assert.Contains(samples.Single(s=>s.Id=="ranger.companion.attack").After.Beats.Single().Events,e=>e is Damaged);
  Assert.Contains(samples.Single(s=>s.Id=="ensnared.thorns").After.Beats.Single().Events,e=>e is Damaged);
  Assert.Contains(samples.Single(s=>s.Id=="spike_growth.thorns").After.Beats.Single().Events,e=>e is Damaged);
 }

 [Fact]
 public void Every_real_script_has_its_own_effect_appropriate_light_and_fits_the_original_budgets()
 {
  var rules=RulesPack.Load([Path.Combine(Pack.Root,"rules")]);var fx=FxArt.Load(Pack.Root);var art=Tactics.Presentation.Board.UnitArt.Load(Pack.Root);var failures=new List<string>();var keys=new HashSet<string>();
  foreach(var s in FxShowcase.ForClass("ranger",rules))
  {
   var a=rules.Abilities.GetValueOrDefault(new(s.Id));var ordinary=s.Id=="common.attack"||s.Id.StartsWith("ranger.companion.",StringComparison.Ordinal);
   var cap=ordinary||s.Id is "ranger.hunters_mark_bonus" or "sharpshooter.high_ground_mastery" or "beastmaster.ferocious_bond" or "spike_growth.thorns" or "ensnared.thorns"?.8:a is null?1.5:BattleCutIn.Tier(a)==CutInTier.Full?2.5:1.5;
   var script=BeatScript.Of(s.After.Beats.Single(),s.Before,s.After,fx);if(script.Length>cap+1e-6)failures.Add($"{s.Id}: {script.Length:F3}s > {cap:F1}s");
   if(ordinary)continue;var key=a?.Presentation?.Fx??fx.Passive(s.Id);Assert.NotNull(key);Assert.True(keys.Add(key!),s.Id);var clip=fx.Clip(key!)!;Assert.NotNull(clip);Assert.InRange(clip.Seconds,0,cap);
   if(key!.EndsWith(".root",StringComparison.Ordinal)||key.EndsWith(".wood",StringComparison.Ordinal)||key.EndsWith(".steel",StringComparison.Ordinal)||key.EndsWith(".mist",StringComparison.Ordinal))Assert.Null(clip.Emission);else Assert.NotNull(clip.Emission);
   if(a is not null)
   {
    Assert.All(art.Sprite("characters.class_ranger")!.Anim(a.Presentation!.Anim!).Frames,f=>Assert.InRange(f,189,214));
    if(a.Presentation.Projectile is {} shot){Assert.NotNull(fx.Shot(shot));Assert.True(Tactics.Presentation.Audio.SoundChoice.IsArrow(shot));Assert.Equal("bow_release",Tactics.Presentation.Audio.SoundChoice.Launch(shot,"longbow"));}
   }
  }
  Assert.Equal(26,keys.Count);Assert.True(failures.Count==0,string.Join("\n",failures));
 }
 [Fact]
 public void On_hit_riders_land_on_the_real_victim_and_keep_the_damage_numbers_separate()
 {
  var rules=RulesPack.Load([Path.Combine(Pack.Root,"rules")]);var fx=FxArt.Load(Pack.Root);
  foreach(var id in new[]{"ranger.hunters_mark_bonus","sharpshooter.high_ground_mastery","beastmaster.ferocious_bond"})
  {
   var s=FxShowcase.Specific("ranger",id,rules);var events=s.After.Beats.Single().Events;var used=events.OfType<AbilityUsed>().Single();var script=BeatScript.Of(s.After.Beats.Single(),s.Before,s.After,fx);var target=s.Before.Units.Single(u=>u.Id==used.Target.Unit);
   Assert.Contains(script.Cues.OfType<BurstCue>(),b=>b.Clip.Path==fx.Clip(fx.Passive(id)!)!.Path&&b.Tile==target.At);
   foreach(var g in script.Cues.OfType<PopCue>().Where(p=>p.Style==PopStyle.Damage).GroupBy(p=>p.Unit).Where(g=>g.Count()>1))Assert.Equal(g.Count(),g.Select(p=>p.Row).Distinct().Count());
  }
 }
 [Fact]
 public void A_real_marked_volley_places_its_rider_on_the_marked_victim_instead_of_the_archer()
 {
  var rules=RulesPack.Load([Path.Combine(Pack.Root,"rules")]);var fx=FxArt.Load(Pack.Root);
  for(ulong seed=7;seed<40;seed++)
  {
   var s=FxShowcase.Sample("ranger",new("sharpshooter.volley"),10,rules,1,"ranger.sharpshooter","ranger.hunters_mark_bonus",realistic:true,seed:seed);
   if(!s.After.Beats.Single().Events.OfType<PassiveTriggered>().Any(p=>p.Passive=="ranger.hunters_mark_bonus"))continue;
   var used=s.After.Beats.Single().Events.OfType<AbilityUsed>().Single();Assert.Null(used.Target.Unit);
   var marked=s.Before.Units.Single(u=>u.Conditions.Contains("hunters_mark"));var script=BeatScript.Of(s.After.Beats.Single(),s.Before,s.After,fx);var clip=fx.Clip(fx.Passive("ranger.hunters_mark_bonus")!)!;
   var riders=script.Cues.OfType<BurstCue>().Where(c=>c.Clip.Path==clip.Path).ToArray();Assert.NotEmpty(riders);Assert.All(riders,c=>Assert.Equal(marked.At,c.Tile));
   return;
  }
  Assert.Fail("No real marked volley hit was found.");
 }
 [Fact]
 public void Every_ability_retains_its_existing_sound_mapping()
 {
  var rules=RulesPack.Load([Path.Combine(Pack.Root,"rules")]);var sounds=Tactics.Presentation.Audio.SoundBank.Load(Pack.Root);
  using var old=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.GetFullPath(Path.Combine(Pack.Root,"../../../tools/tactics-assets/baseline/ranger-vfx.json"))));
  foreach(var a in old.RootElement.EnumerateObject())Assert.Equal(sounds.ForEffect(a.Value.GetProperty("fx").GetString()!),sounds.ForEffect(rules.Abilities[new(a.Name)].Presentation!.Fx!));
 }

 [Fact]
 public void Refreshed_companion_bite_keeps_all_frames_and_real_save_results_inside_the_attack_budget()
 {
  var rules=RulesPack.Load([Path.Combine(Pack.Root,"rules")]);var fx=FxArt.Load(Pack.Root);var s=FxShowcase.Specific("ranger","ranger.companion.attack",rules);
  var script=BeatScript.Of(s.After.Beats.Single(),s.Before,s.After,fx);var lunge=Assert.Single(script.Cues.OfType<LungeCue>());
  Assert.Equal(.8,lunge.Length);Assert.True(script.Length>=lunge.At+lunge.Length);Assert.InRange(script.Length,0,.8+1e-6);
  Assert.Contains(s.After.Beats.Single().Events,e=>e is Damaged);Assert.NotEmpty(script.Cues.OfType<BadgeCue>());
  Assert.Contains(script.Cues.OfType<ApplyCue>(),c=>c.Event is ConditionApplied);
 }
}
