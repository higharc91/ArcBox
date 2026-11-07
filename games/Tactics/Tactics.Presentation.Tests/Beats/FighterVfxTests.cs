using Tactics.Logic;
using Tactics.Presentation.Beats;
using Tactics.Presentation.Tests.Board;
namespace Tactics.Presentation.Tests.Beats;
public sealed class FighterVfxTests
{
 [Fact]
 public void Every_fighter_ability_and_hook_has_a_real_fixture_including_failure_and_recovery()
 {
  var rules=RulesPack.Load([Path.Combine(Pack.Root,"rules")]);var samples=FxShowcase.ForClass("fighter",rules);Assert.Equal(35,samples.Length);
  foreach(var f in FxShowcase.Inventory("fighter",rules).Where(f=>f.Kind!="static"))
  {
   var s=Assert.Single(samples,x=>x.Id==f.Id);var e=s.After.Beats.Single().Events;
   if(f.Kind=="proc")Assert.Contains(e.OfType<PassiveTriggered>(),p=>p.Passive==f.Id);else Assert.Contains(e.OfType<AbilityUsed>(),u=>u.Ability.Value==f.Id);
  }
  Assert.Contains(samples.Single(s=>s.Id=="fighter.second_wind").After.Beats.Single().Events.OfType<Healed>(),h=>h.Amount>0);
  Assert.Contains(samples.Single(s=>s.Id=="fighter.studied_attacks").After.Beats.Single().Events.OfType<AttackResolved>(),a=>!a.Hit);
  var save=samples.Single(s=>s.Id=="fighter.indomitable").After.Beats.Single().Events;
  Assert.Contains(save.OfType<Rolled>(),r=>r.Kind==RollKind.Save&&!r.Result.Success);Assert.Contains(save,e=>e is ConditionRemoved);
  var recover=samples.Single(s=>s.Id=="vanguard.unbreakable");Assert.Contains(recover.After.Beats.Single().Events,e=>e is Downed);Assert.Contains(recover.After.Beats.Single().Events,e=>e is Revived);Assert.True(recover.After.Units.Single(u=>u.Id.Value=="hero1").TempHp>0);
  Assert.True(samples.Single(s=>s.Id=="warlord.inspiring_presence").After.Units.Single(u=>u.Id.Value=="hero1").TempHp>0);
  Assert.Equal(2,samples.Single(s=>s.Id=="common.attack.level10").After.Beats.Single().Events.OfType<AttackResolved>().Count());
  Assert.True(samples.Single(s=>s.Id=="weaponmaster.blade_dance").After.Beats.Single().Events.OfType<AttackResolved>().Count()>=3);
  foreach(var s in samples.Where(s=>s.Id.EndsWith(".level10",StringComparison.Ordinal)))Assert.Equal(10,s.Level);
 }
 [Fact]
 public void Every_real_script_has_its_own_unlit_effect_and_fits_the_original_budgets()
 {
  var rules=RulesPack.Load([Path.Combine(Pack.Root,"rules")]);var fx=FxArt.Load(Pack.Root);var art=Tactics.Presentation.Board.UnitArt.Load(Pack.Root);var sounds=Tactics.Presentation.Audio.SoundBank.Load(Pack.Root);var failures=new List<string>();var keys=new HashSet<string>();
  foreach(var s in FxShowcase.ForClass("fighter",rules))
  {
   var variant=s.Id.EndsWith(".level10",StringComparison.Ordinal)||s.Id.EndsWith(".wounded",StringComparison.Ordinal);
   var a=rules.Abilities.GetValueOrDefault(new(variant?s.Id[..s.Id.LastIndexOf('.')]:s.Id));
   var ordinary=s.Id.StartsWith("common.attack",StringComparison.Ordinal)||s.Id is "fighter.studied_attacks" or "vanguard.hold_the_line" or "weaponmaster.keen_edge" or "weaponmaster.relentless";
   var cap=ordinary?.8:a is null?1.5:BattleCutIn.Tier(a)==CutInTier.Full?2.5:1.5;
   var script=BeatScript.Of(s.After.Beats.Single(),s.Before,s.After,fx,sounds);if(script.Length>cap+1e-6)failures.Add($"{s.Id}: {script.Length:F3}s > {cap:F1}s");
   foreach(var g in script.Cues.OfType<PopCue>().Where(p=>p.Style==PopStyle.Damage).GroupBy(p=>p.Unit).Where(g=>g.Count()>1))Assert.True(g.Count()==g.Select(p=>p.Row).Distinct().Count(),s.Id+": overlapping damage rows");
   if(s.Id is not "fighter.indomitable")Assert.DoesNotContain(script.Cues.OfType<SoundCue>(),c=>c.Id=="spell_cast");
   if(s.Id.StartsWith("common.attack",StringComparison.Ordinal))continue;
   var key=a?.Presentation?.Fx??fx.Passive(s.Id);Assert.NotNull(key);if(!variant)Assert.True(keys.Add(key!),s.Id);var clip=fx.Clip(key!)!;Assert.NotNull(clip);Assert.Null(clip.Emission);Assert.InRange(clip.Seconds,0,cap);
   if(a is not null)Assert.All(art.Sprite("characters.class_fighter")!.Anim(a.Presentation!.Anim!).Frames,f=>Assert.InRange(f,189,213));
  }
  Assert.Equal(27,keys.Count);Assert.True(failures.Count==0,string.Join("\n",failures));
 }
 [Fact]
 public void On_hit_and_miss_marks_land_on_the_actual_resolved_victim()
 {
  var rules=RulesPack.Load([Path.Combine(Pack.Root,"rules")]);var fx=FxArt.Load(Pack.Root);
  foreach(var id in new[]{"fighter.studied_attacks","vanguard.hold_the_line","weaponmaster.keen_edge"})
  {
   var s=FxShowcase.Specific("fighter",id,rules);var events=s.After.Beats.Single().Events;var at=Array.FindIndex(events.ToArray(),e=>e is PassiveTriggered p&&p.Passive==id);var victim=events.Take(at).OfType<AttackResolved>().Last().Target;
   var script=BeatScript.Of(s.After.Beats.Single(),s.Before,s.After,fx);Assert.Contains(script.Cues.OfType<BurstCue>(),b=>b.Clip.Path==fx.Clip(fx.Passive(id)!)!.Path&&b.Tile==s.Before.Units.Single(u=>u.Id==victim).At);
  }
 }
 [Fact]
 public void Two_real_basic_attacks_get_two_complete_nonoverlapping_physical_gestures()
 {
  var rules=RulesPack.Load([Path.Combine(Pack.Root,"rules")]);var fx=FxArt.Load(Pack.Root);var s=FxShowcase.Specific("fighter","common.attack.level10",rules);var script=BeatScript.Of(s.After.Beats.Single(),s.Before,s.After,fx);
  var lunges=script.Cues.OfType<LungeCue>().ToArray();Assert.Equal(2,lunges.Length);Assert.All(lunges,l=>Assert.Equal("characters.fighter_cut",l.Anim));Assert.True(lunges[1].At>=lunges[0].At+lunges[0].Length);Assert.All(lunges,l=>Assert.True(l.Length>=.2));
 }
 [Fact]
 public void Every_ability_retains_its_existing_sound_mapping_or_reuses_a_physical_clip()
 {
  var rules=RulesPack.Load([Path.Combine(Pack.Root,"rules")]);var sounds=Tactics.Presentation.Audio.SoundBank.Load(Pack.Root);
  using var old=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.GetFullPath(Path.Combine(Pack.Root,"../../../tools/tactics-assets/baseline/fighter-vfx.json"))));
  foreach(var a in old.RootElement.EnumerateObject())
  {
   var prior=a.Value.TryGetProperty("fx",out var key)?sounds.ForEffect(key.GetString()!):null;var current=sounds.ForEffect(rules.Abilities[new(a.Name)].Presentation!.Fx!);
   if(prior is not null)Assert.Equal(prior,current);else Assert.Contains(current,new[]{"swing_sword","block_shield","healed","buff_applied"});
  }
 }
 [Fact]
 public void Wounded_execution_keeps_the_conditional_damage_as_a_separate_result()
 {
  var rules=RulesPack.Load([Path.Combine(Pack.Root,"rules")]);var samples=FxShowcase.ForClass("fighter",rules);
  static int FirstSwing(FxShowcaseSample s)=>s.After.Beats.Single().Events.SkipWhile(e=>e is not AttackResolved).Skip(1).TakeWhile(e=>e is not Rolled { Kind:RollKind.Attack }).OfType<Damaged>().Count();
  Assert.True(FirstSwing(samples.Single(s=>s.Id=="weaponmaster.execution_strike.wounded"))>FirstSwing(samples.Single(s=>s.Id=="weaponmaster.execution_strike")));
 }
 [Fact]
 public void Charge_braces_then_reaches_the_foe_before_its_single_strike_and_impact()
 {
  var rules=RulesPack.Load([Path.Combine(Pack.Root,"rules")]);var fx=FxArt.Load(Pack.Root);var s=FxShowcase.Specific("fighter","vanguard.charge",rules);var script=BeatScript.Of(s.After.Beats.Single(),s.Before,s.After,fx);
  var used=s.After.Beats.Single().Events.OfType<AbilityUsed>().Single();var lunge=Assert.Single(script.Cues.OfType<LungeCue>());var walk=script.Cues.OfType<WalkCue>().Where(w=>w.Unit==used.Source).ToArray();Assert.NotEmpty(walk);
  Assert.True(lunge.At>=walk.Max(w=>w.At+w.Length));Assert.Equal(walk[^1].To,lunge.From);Assert.Equal("characters.fighter_cut",lunge.Anim);
  Assert.Single(script.Cues.OfType<AbilityCue>());Assert.Contains(script.Cues.OfType<PoseCue>(),p=>p.Unit==used.Source&&p.Anim=="characters.fighter_guard"&&p.At<walk[0].At);
  var impact=Assert.Single(script.Cues.OfType<BurstCue>(),b=>b.Clip.Path==fx.Clip("fx.fighter_charge.steel")!.Path);Assert.True(impact.At>=lunge.At+.1-1e-6);Assert.Equal(lunge.Toward,impact.Tile);
 }
 [Fact]
 public void Keen_edge_on_a_real_self_centered_cleave_marks_the_struck_enemy()
 {
  var rules=RulesPack.Load([Path.Combine(Pack.Root,"rules")]);var fx=FxArt.Load(Pack.Root);var s=FxShowcase.Specific("fighter","weaponmaster.cleave",rules);var events=s.After.Beats.Single().Events;
  var used=events.OfType<AbilityUsed>().Single();Assert.Equal(TargetKind.Self,rules.Abilities[used.Ability].Target.Kind);Assert.Null(used.Target.Unit);
  var at=Array.FindIndex(events.ToArray(),e=>e is PassiveTriggered { Passive:"weaponmaster.keen_edge" });Assert.True(at>=0);var target=events.Take(at).OfType<AttackResolved>().Last().Target;Assert.NotEqual(used.Source,target);
  var script=BeatScript.Of(s.After.Beats.Single(),s.Before,s.After,fx);var clip=fx.Clip(fx.Passive("weaponmaster.keen_edge")!)!;
  Assert.Contains(script.Cues.OfType<BurstCue>(),b=>b.Clip.Path==clip.Path&&b.Tile==s.Before.Units.Single(u=>u.Id==target).At);
 }
}
